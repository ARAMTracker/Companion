using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AramTrackerCompanion.Tests;

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("aramtracker-companion-tests-").FullName;

    public void Dispose() => Directory.Delete(Path, recursive: true);
}

internal sealed class StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path, string? Authorization, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), body));
        return respond(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal static class Fixtures
{
    public static JsonNode MayhemGameNode() =>
        JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "mayhem-game.json")))!;

    public static JsonElement MayhemGame() => ToElement(MayhemGameNode());

    public static JsonElement ToElement(JsonNode node) => JsonSerializer.SerializeToElement(node);
}
