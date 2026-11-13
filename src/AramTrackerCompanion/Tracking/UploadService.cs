using AramTrackerCompanion.Account;
using AramTrackerCompanion.Api;
using AramTrackerCompanion.Settings;

namespace AramTrackerCompanion.Tracking;

internal sealed class UploadService
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
    ];

    public static readonly TimeSpan MaxGameAge = TimeSpan.FromDays(14);

    private readonly AramTrackerApiService _api;
    private readonly AuthenticationService _authentication;
    private readonly Outbox _outbox;
    private readonly Outbox _waiting;
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private int _failures;

    public UploadService(AramTrackerApiService api, AuthenticationService authentication, Outbox outbox, Outbox waiting, SettingsService settings)
    {
        _api = api;
        _authentication = authentication;
        _outbox = outbox;
        _waiting = waiting;
        _settings = settings;
    }

    public void ReleaseWaiting()
    {
        var released = _waiting.TakeAll();

        foreach (var match in released)
        {
            _outbox.Add(match);
        }

        if (released.Count > 0)
        {
            Log.Info("A new account was linked, sending waiting games again", ("matches", released.Count));
            Wake();
        }
    }

    public void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public async Task RunAsync(CancellationToken cancellation)
    {
        try
        {
            while (true)
            {
                var retryIn = await FlushAsync(cancellation);

                if (retryIn is { } delay)
                {
                    await _wake.WaitAsync(delay, cancellation);
                }
                else
                {
                    await _wake.WaitAsync(cancellation);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    internal async Task<TimeSpan?> FlushAsync(CancellationToken cancellation)
    {
        DropExpired();

        while (_authentication.Token is { } token && _outbox.Peek(AramTrackerApiService.MaxMatchesPerRequest) is { Count: > 0 } batch)
        {
            SubmitResponse response;

            try
            {
                response = await _api.SubmitMatchesAsync(token, batch, cancellation);
            }
            catch (ApiException exception) when (exception.Failure == ApiFailure.Unauthorized)
            {
                _authentication.ReportUnauthorized();
                return null;
            }
            catch (ApiException exception) when (exception.IsTransient)
            {
                _authentication.ReportServerStatus(exception.Failure == ApiFailure.RateLimited ? ServerStatus.Online : ServerStatus.Unreachable);
                var delay = exception.RetryAfter ?? NextRetryDelay();
                Log.Warn("Upload failed, will retry", ("reason", exception.Message), ("retryInSeconds", (int)delay.TotalSeconds), ("pending", _outbox.Count));
                return delay;
            }
            catch (ApiException exception) when (exception.IsFinalRefusal)
            {
                // 400, 413 or 422: the website judged the request, so resending can't help.
                Log.Warn("Upload refused, dropping the batch", ("reason", exception.Message), ("status", exception.StatusCode), ("matches", string.Join(',', batch.Select(m => m.MatchId))));
                _outbox.Remove(batch.Select(m => m.MatchId));
                _settings.Update(s => s.LastUploadProblem = exception.Message);
                continue;
            }
            catch (ApiException exception)
            {
                // Other refusals can come from the host, such as its firewall or a deploy.
                var delay = NextRetryDelay();
                Log.Warn("Upload refused, keeping the games", ("reason", exception.Message), ("status", exception.StatusCode), ("retryInSeconds", (int)delay.TotalSeconds), ("pending", _outbox.Count));
                _settings.Update(s => s.LastUploadProblem = exception.Message);
                return delay;
            }

            _authentication.ReportServerStatus(ServerStatus.Online);

            if (!Apply(batch, response))
            {
                return NextRetryDelay();
            }

            _failures = 0;
        }

        return null;
    }

    private bool Apply(IReadOnlyList<MatchSubmission> batch, SubmitResponse response)
    {
        var sent = batch.Select(m => m.MatchId).ToHashSet();
        var answered = new List<string>();
        var stored = 0;
        string? problem = null;

        foreach (var result in response.Results ?? [])
        {
            if (result.MatchId is null || !sent.Contains(result.MatchId))
            {
                continue;
            }

            switch (result.Status)
            {
                case MatchResult.Accepted or MatchResult.Duplicate:
                    stored++;
                    answered.Add(result.MatchId);
                    Log.Info("Match uploaded", ("matchId", result.MatchId), ("status", result.Status));
                    break;

                case MatchResult.Rejected when result.Code == MatchResult.NotYourGame:
                    // Probably an account that isn't linked yet.
                    if (batch.FirstOrDefault(m => m.MatchId == result.MatchId) is { } waiting)
                    {
                        _waiting.Add(waiting);
                    }

                    problem = "A game was played on an account that isn't linked to your ARAM Tracker account. It will be sent when you link it.";
                    answered.Add(result.MatchId);
                    Log.Info("Match waiting for a linked account", ("matchId", result.MatchId));
                    break;

                case MatchResult.Rejected:
                    problem = result.Message ?? "The website rejected a game.";
                    answered.Add(result.MatchId);
                    Log.Warn("Match rejected", ("matchId", result.MatchId), ("code", result.Code), ("reason", problem), ("fields", result.Errors?.GetRawText()));
                    break;

                case MatchResult.Retry:
                    Log.Info("Match will be sent again", ("matchId", result.MatchId), ("code", result.Code), ("reason", result.Message));
                    break;

                default:
                    Log.Warn("Unknown upload status, keeping the match", ("matchId", result.MatchId), ("status", result.Status));
                    break;
            }
        }

        _outbox.Remove(answered);
        _settings.Update(settings =>
        {
            if (stored > 0)
            {
                settings.LastUploadAt = DateTimeOffset.UtcNow;
            }

            if (problem is not null || stored > 0)
            {
                settings.LastUploadProblem = problem;
            }
        });

        return answered.Count == batch.Count;
    }

    private void DropExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - MaxGameAge;

        foreach (var matchId in _outbox.RemoveStartedBefore(cutoff).Concat(_waiting.RemoveStartedBefore(cutoff)))
        {
            Log.Info("Match expired before it could be sent", ("matchId", matchId));
        }
    }

    private TimeSpan NextRetryDelay()
    {
        var delay = RetryDelays[Math.Min(_failures, RetryDelays.Length - 1)];
        _failures++;
        return delay;
    }
}
