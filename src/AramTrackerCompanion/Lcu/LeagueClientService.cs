using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AramTrackerCompanion.Lcu;

internal sealed class LeagueClientService : IDisposable
{
    private const string PhaseEvent = "OnJsonApiEvent_lol-gameflow_v1_gameflow-phase";
    private const int WampSubscribe = 5;
    private const int WampEvent = 8;
    private const int MaxMessageBytes = 1024 * 1024;

    private readonly LeagueClientConnection _connection;
    private readonly HttpClient _http;
    private readonly string _basicCredentials;

    public LeagueClientService(LeagueClientConnection connection)
    {
        _connection = connection;
        _basicCredentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{connection.Password}"));

        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            SslOptions = { RemoteCertificateValidationCallback = AcceptLocalCertificate },
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://127.0.0.1:{connection.Port}/"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", _basicCredentials);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<JsonElement?> GetCurrentSummonerAsync(CancellationToken cancellation) =>
        GetAsync("lol-summoner/v1/current-summoner", cancellation);

    public async Task<string?> GetGameflowPhaseAsync(CancellationToken cancellation) =>
        await GetAsync("lol-gameflow/v1/gameflow-phase", cancellation) is { ValueKind: JsonValueKind.String } phase
            ? phase.GetString()
            : null;

    public Task<JsonElement?> GetGameflowSessionAsync(CancellationToken cancellation) =>
        GetAsync("lol-gameflow/v1/session", cancellation);

    public Task<JsonElement?> GetRecentGamesAsync(int count, CancellationToken cancellation) =>
        GetAsync($"lol-match-history/v1/products/lol/current-summoner/matches?begIndex=0&endIndex={count - 1}", cancellation);

    public Task<JsonElement?> GetGameAsync(long gameId, CancellationToken cancellation) =>
        GetAsync($"lol-match-history/v1/games/{gameId}", cancellation);

    public async Task ListenForPhaseChangesAsync(Func<string, Task> onPhase, CancellationToken cancellation)
    {
        using var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = AcceptLocalCertificate;
        socket.Options.SetRequestHeader("Authorization", $"Basic {_basicCredentials}");
        socket.Options.Proxy = null;

        await socket.ConnectAsync(new Uri($"wss://127.0.0.1:{_connection.Port}/"), cancellation);

        var subscribe = JsonSerializer.SerializeToUtf8Bytes(new object[] { WampSubscribe, PhaseEvent });
        await socket.SendAsync(subscribe, WebSocketMessageType.Text, endOfMessage: true, cancellation);

        var buffer = new byte[8 * 1024];
        using var message = new MemoryStream();

        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancellation);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            message.Write(buffer, 0, result.Count);

            if (message.Length > MaxMessageBytes)
            {
                throw new InvalidDataException("The League client sent an oversized event.");
            }

            if (!result.EndOfMessage)
            {
                continue;
            }

            var phase = ReadPhase(message.GetBuffer().AsMemory(0, (int)message.Length));
            message.SetLength(0);

            if (phase is not null)
            {
                await onPhase(phase);
            }
        }
    }

    public void Dispose() => _http.Dispose();

    // [8, eventName, {data, eventType, uri}]
    internal static string? ReadPhase(ReadOnlyMemory<byte> frame)
    {
        if (frame.IsEmpty)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(frame);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Array
                || root.GetArrayLength() < 3
                || root[0].ValueKind != JsonValueKind.Number
                || root[0].GetInt32() != WampEvent
                || root[2].ValueKind != JsonValueKind.Object
                || !root[2].TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return data.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<JsonElement?> GetAsync(string path, CancellationToken cancellation)
    {
        try
        {
            using var response = await _http.GetAsync(path, cancellation);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellation);
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return null;
        }
    }

    // The client serves a Riot-signed certificate, and only on 127.0.0.1.
    private static bool AcceptLocalCertificate(object sender, object? certificate, object? chain, SslPolicyErrors errors) => true;
}
