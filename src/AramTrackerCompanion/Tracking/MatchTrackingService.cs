using System.Net.WebSockets;
using System.Text.Json;
using AramTrackerCompanion.Lcu;
using AramTrackerCompanion.Settings;

namespace AramTrackerCompanion.Tracking;

internal enum LeagueClientStatus
{
    TrackingPaused,
    NotRunning,
    Connecting,
    Connected,
    InGame,
}

internal sealed class MatchTrackingService
{
    private const int RecentGameCount = 20;
    // A day short of the website's 14-day limit.
    private static readonly TimeSpan MaxGameAge = UploadService.MaxGameAge - TimeSpan.FromDays(1);

    private static readonly TimeSpan RescanInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan AfterGameRescanDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ClientSearchInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ClientReadyTimeout = TimeSpan.FromMinutes(2);

    // A finished game takes a moment to reach the match history.
    private static readonly TimeSpan[] CaptureDelays =
    [
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(7),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(2),
    ];

    private static readonly HashSet<string> InGamePhases = ["GameStart", "InProgress", "Reconnect"];

    private readonly SettingsService _settings;
    private readonly Outbox _outbox;
    private readonly UploadService _uploads;
    private readonly SemaphoreSlim _historyGate = new(1, 1);
    private readonly SemaphoreSlim _phaseGate = new(1, 1);

    private CancellationTokenSource? _cancellation;
    private Task _running = Task.CompletedTask;
    private LeagueClientService? _client;
    private (long GameId, long? QueueId)? _liveGame;

    public MatchTrackingService(SettingsService settings, Outbox outbox, UploadService uploads)
    {
        _settings = settings;
        _outbox = outbox;
        _uploads = uploads;
    }

    public event Action? StatusChanged;

    public LeagueClientStatus Status { get; private set; } = LeagueClientStatus.TrackingPaused;

    public bool IsRunning => _cancellation is not null;

    public void Start()
    {
        if (_cancellation is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        var cancellation = _cancellation.Token;
        _running = Task.Run(() => RunAsync(cancellation), cancellation);
        Log.Info("Tracking started");
    }

    public async Task StopAsync()
    {
        if (_cancellation is null)
        {
            return;
        }

        _cancellation.Cancel();

        try
        {
            await _running;
        }
        catch (OperationCanceledException)
        {
        }

        _cancellation.Dispose();
        _cancellation = null;
        SetStatus(LeagueClientStatus.TrackingPaused);
        Log.Info("Tracking stopped");
    }

    public async Task<bool> RescanAsync()
    {
        if (_client is not { } client || _cancellation is not { } cancellation)
        {
            return false;
        }

        _settings.Update(settings => settings.ProcessedMatchIds.Clear());
        await ImportRecentGamesAsync(client, cancellation.Token);
        return true;
    }

    private async Task RunAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            var connection = LeagueClientLocator.TryFind();

            if (connection is null)
            {
                SetStatus(LeagueClientStatus.NotRunning);
                await Task.Delay(ClientSearchInterval, cancellation);
                continue;
            }

            try
            {
                await TrackClientAsync(connection, cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is WebSocketException or HttpRequestException or IOException or InvalidDataException)
            {
                Log.Warn("League client connection lost", ("reason", exception.Message));
            }
            catch (Exception exception)
            {
                Log.Error("Tracking failed unexpectedly", exception);
            }

            SetStatus(LeagueClientStatus.NotRunning);
            await Task.Delay(ReconnectDelay, cancellation);
        }
    }

    private async Task TrackClientAsync(LeagueClientConnection connection, CancellationToken cancellation)
    {
        SetStatus(LeagueClientStatus.Connecting);

        using var client = new LeagueClientService(connection);
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellation);

        if (!await WaitUntilReadyAsync(client, cancellation))
        {
            return;
        }

        _client = client;
        _liveGame = null;
        Log.Info("League client connected", ("processId", connection.ProcessId));

        try
        {
            var listening = client.ListenForPhaseChangesAsync(phase => OnPhaseAsync(client, phase, session.Token), session.Token);

            var phase = await client.GetGameflowPhaseAsync(session.Token);
            await OnPhaseAsync(client, phase ?? "None", session.Token);
            await ImportRecentGamesAsync(client, session.Token);
            _ = RescanPeriodicallyAsync(client, session.Token);

            await listening;
            Log.Info("League client closed");
        }
        finally
        {
            _client = null;
            _liveGame = null;
            session.Cancel();
        }
    }

    // The lockfile appears before the client's plugins answer.
    private static async Task<bool> WaitUntilReadyAsync(LeagueClientService client, CancellationToken cancellation)
    {
        var deadline = DateTimeOffset.UtcNow + ClientReadyTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await client.GetCurrentSummonerAsync(cancellation) is not null)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellation);
        }

        return false;
    }

    private async Task OnPhaseAsync(LeagueClientService client, string phase, CancellationToken cancellation)
    {
        await _phaseGate.WaitAsync(cancellation);

        try
        {
            await HandlePhaseAsync(client, phase, cancellation);
        }
        finally
        {
            _phaseGate.Release();
        }
    }

    private async Task HandlePhaseAsync(LeagueClientService client, string phase, CancellationToken cancellation)
    {
        Log.Info("Gameflow phase", ("phase", phase));

        if (InGamePhases.Contains(phase))
        {
            SetStatus(LeagueClientStatus.InGame);
            _liveGame ??= await ReadLiveGameAsync(client, cancellation);
            return;
        }

        SetStatus(LeagueClientStatus.Connected);

        if (_liveGame is not { } ended)
        {
            return;
        }

        _liveGame = null;

        if (ended.QueueId is not null && ended.QueueId != MatchMapper.MayhemQueueId)
        {
            Log.Info("Game ended, not Mayhem", ("queueId", ended.QueueId));
            return;
        }

        Log.Info("Mayhem game ended", ("gameId", ended.GameId));
        _ = CaptureAsync(client, ended.GameId, cancellation);
        _ = RescanAfterAsync(client, AfterGameRescanDelay, cancellation);
    }

    private async Task RescanPeriodicallyAsync(LeagueClientService client, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            if (!await RescanAfterAsync(client, RescanInterval, cancellation))
            {
                return;
            }
        }
    }

    private async Task<bool> RescanAfterAsync(LeagueClientService client, TimeSpan delay, CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(delay, cancellation);
            await ImportRecentGamesAsync(client, cancellation);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception) when (exception is WebSocketException or HttpRequestException or IOException or InvalidDataException)
        {
            Log.Warn("Checking recent games failed", ("reason", exception.Message));
            return true;
        }
        catch (Exception exception)
        {
            Log.Error("Checking recent games failed unexpectedly", exception);
            return true;
        }
    }

    private static async Task<(long, long?)?> ReadLiveGameAsync(LeagueClientService client, CancellationToken cancellation)
    {
        if (await client.GetGameflowSessionAsync(cancellation) is not { ValueKind: JsonValueKind.Object } session
            || !session.TryGetProperty("gameData", out var gameData)
            || gameData.ValueKind != JsonValueKind.Object
            || !gameData.TryGetProperty("gameId", out var gameId)
            || !gameId.TryGetInt64(out var id)
            || id <= 0)
        {
            return null;
        }

        long? queueId = gameData.TryGetProperty("queue", out var queue)
            && queue.ValueKind == JsonValueKind.Object
            && queue.TryGetProperty("id", out var queueIdElement)
            && queueIdElement.TryGetInt64(out var parsed)
                ? parsed
                : null;

        return (id, queueId);
    }

    private async Task CaptureAsync(LeagueClientService client, long gameId, CancellationToken cancellation)
    {
        try
        {
            foreach (var delay in CaptureDelays)
            {
                await Task.Delay(delay, cancellation);

                if (await client.GetGameAsync(gameId, cancellation) is { } game && Handle(game))
                {
                    return;
                }
            }

            Log.Warn("Finished game never appeared in the match history", ("gameId", gameId));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Log.Error("Capturing a finished game failed", exception, ("gameId", gameId));
        }
    }

    private async Task ImportRecentGamesAsync(LeagueClientService client, CancellationToken cancellation)
    {
        await _historyGate.WaitAsync(cancellation);

        try
        {
            if (await client.GetRecentGamesAsync(RecentGameCount, cancellation) is not { ValueKind: JsonValueKind.Object } history
                || !history.TryGetProperty("games", out var page)
                || page.ValueKind != JsonValueKind.Object
                || !page.TryGetProperty("games", out var games)
                || games.ValueKind != JsonValueKind.Array)
            {
                Log.Warn("Recent match history was not available");
                return;
            }

            var oldest = DateTimeOffset.UtcNow - MaxGameAge;

            foreach (var summary in games.EnumerateArray().Reverse())
            {
                if (!MatchMapper.IsMayhem(summary)
                    || MatchMapper.MatchId(summary) is not { } matchId
                    || _settings.IsProcessed(matchId)
                    || !summary.TryGetProperty("gameId", out var gameIdElement)
                    || !gameIdElement.TryGetInt64(out var gameId)
                    || !summary.TryGetProperty("gameCreation", out var created)
                    || !created.TryGetInt64(out var createdMs)
                    || DateTimeOffset.FromUnixTimeMilliseconds(createdMs) < oldest)
                {
                    continue;
                }

                if (await client.GetGameAsync(gameId, cancellation) is { } game)
                {
                    Handle(game);
                }
            }
        }
        finally
        {
            _historyGate.Release();
        }
    }

    private bool Handle(JsonElement game)
    {
        var result = MatchMapper.Map(game);
        var matchId = MatchMapper.MatchId(game);

        switch (result.Outcome)
        {
            case MapOutcome.Mapped when _settings.IsProcessed(result.Match!.MatchId):
                return true;

            case MapOutcome.Mapped:
                _outbox.Add(result.Match!);
                _settings.MarkProcessed(result.Match!.MatchId, tracked: true);
                Log.Info("Match queued", ("matchId", result.Match.MatchId), ("players", result.Match.Participants.Count));
                _uploads.Wake();
                return true;

            case MapOutcome.NotMayhem:
                return true;

            case MapOutcome.Unusable:
                if (matchId is not null)
                {
                    _settings.MarkProcessed(matchId, tracked: false);
                }

                Log.Info("Match skipped", ("matchId", matchId), ("reason", result.Reason));
                return true;

            default:
                Log.Info("Match not ready yet", ("matchId", matchId), ("reason", result.Reason));
                return false;
        }
    }

    private void SetStatus(LeagueClientStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        StatusChanged?.Invoke();
    }
}
