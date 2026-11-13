using System.Net;
using System.Text.Json;
using AramTrackerCompanion.Account;
using AramTrackerCompanion.Api;
using AramTrackerCompanion.Settings;

namespace AramTrackerCompanion.Tests;

public class ApiAndAccountTests
{
    private const string BaseUrl = "https://example.test/api/v1/companion";

    private const string PairAnswer = """
        {"token":"atc_test-token","installation":{"id":12,"name":"TEST-PC","created_at":"2026-11-11T12:00:00+00:00"},
         "players":[{"puuid":"x","game_name":"Player 1","tag_line":"TEST","region":"EUW","platform":"EUW1"}]}
        """;

    [Fact]
    public async Task Pairing_stores_an_encrypted_token_and_the_linked_accounts()
    {
        using var directory = new TempDirectory();
        var handler = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.Created, PairAnswer));
        using var api = new AramTrackerApiService(BaseUrl, handler);
        var settings = new SettingsService(directory.Path);
        var authentication = new AuthenticationService(api, settings, new TokenStore(directory.Path));

        await authentication.PairAsync(" k7px 2mq9 ", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/companion/pair", request.Path);
        Assert.Null(request.Authorization);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("K7PX2MQ9", body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("name").GetString()));

        Assert.True(authentication.IsLinked);
        Assert.Equal(["Player 1#TEST"], settings.Read(s => s.LinkedAccounts));
        Assert.Equal(12, settings.Read(s => s.InstallationId));

        var stored = File.ReadAllBytes(Path.Combine(directory.Path, "token.bin"));
        Assert.DoesNotContain("atc_test-token", System.Text.Encoding.UTF8.GetString(stored));
        Assert.DoesNotContain("atc_test-token", File.ReadAllText(Path.Combine(directory.Path, "settings.json")));
        Assert.Equal("atc_test-token", new TokenStore(directory.Path).Load());
    }

    [Fact]
    public async Task A_newly_linked_account_is_announced_once()
    {
        using var directory = new TempDirectory();
        var players = """[{"puuid":"x","game_name":"Player 1","tag_line":"TEST","region":"EUW","platform":"EUW1"}]""";
        var handler = new StubHandler((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/pair")
            ? StubHandler.Json(HttpStatusCode.Created, PairAnswer)
            : StubHandler.Json(HttpStatusCode.OK, $$"""{"installation":{"id":12,"name":"TEST-PC"},"players":{{players}}}"""));
        using var api = new AramTrackerApiService(BaseUrl, handler);
        var authentication = new AuthenticationService(api, new SettingsService(directory.Path), new TokenStore(directory.Path));
        var announced = 0;
        authentication.AccountsAdded += () => announced++;

        await authentication.PairAsync("K7PX2MQ9", CancellationToken.None);
        Assert.Equal(1, announced);

        await authentication.VerifyAsync(CancellationToken.None);
        Assert.Equal(1, announced);

        players = """[{"puuid":"x","game_name":"Player 1","tag_line":"TEST"},{"puuid":"y","game_name":"Smurf","tag_line":"EUW"}]""";
        await authentication.VerifyAsync(CancellationToken.None);
        Assert.Equal(2, announced);
    }

    [Fact]
    public async Task A_wrong_pairing_code_is_reported_and_nothing_is_stored()
    {
        using var directory = new TempDirectory();
        var handler = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.BadRequest, """{"error":{"code":"invalid_code","message":"That code is wrong."}}"""));
        using var api = new AramTrackerApiService(BaseUrl, handler);
        var authentication = new AuthenticationService(api, new SettingsService(directory.Path), new TokenStore(directory.Path));

        var exception = await Assert.ThrowsAsync<ApiException>(() => authentication.PairAsync("WRONG123", CancellationToken.None));

        Assert.Equal(ApiFailure.InvalidCode, exception.Failure);
        Assert.False(authentication.IsLinked);
        Assert.False(File.Exists(Path.Combine(directory.Path, "token.bin")));
    }

    [Fact]
    public async Task A_revoked_token_is_forgotten_on_start_up()
    {
        using var directory = new TempDirectory();
        new TokenStore(directory.Path).Save("atc_revoked");
        var handler = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.Unauthorized, """{"error":{"code":"unauthenticated","message":"No."}}"""));
        using var api = new AramTrackerApiService(BaseUrl, handler);
        var authentication = new AuthenticationService(api, new SettingsService(directory.Path), new TokenStore(directory.Path));

        await authentication.VerifyAsync(CancellationToken.None);

        Assert.Equal("Bearer atc_revoked", handler.Requests[0].Authorization);
        Assert.False(authentication.IsLinked);
        Assert.Null(new TokenStore(directory.Path).Load());
    }

    [Fact]
    public async Task A_network_failure_on_start_up_keeps_the_token()
    {
        using var directory = new TempDirectory();
        new TokenStore(directory.Path).Save("atc_kept");
        var handler = new StubHandler((_, _) => throw new HttpRequestException("offline"));
        using var api = new AramTrackerApiService(BaseUrl, handler);
        var authentication = new AuthenticationService(api, new SettingsService(directory.Path), new TokenStore(directory.Path));

        await authentication.VerifyAsync(CancellationToken.None);

        Assert.True(authentication.IsLinked);
        Assert.Equal(ServerStatus.Unreachable, authentication.ServerStatus);
    }

    [Fact]
    public async Task Disconnecting_revokes_the_token_and_forgets_it()
    {
        using var directory = new TempDirectory();
        new TokenStore(directory.Path).Save("atc_mine");
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var api = new AramTrackerApiService(BaseUrl, handler);
        var authentication = new AuthenticationService(api, new SettingsService(directory.Path), new TokenStore(directory.Path));

        var revoked = await authentication.DisconnectAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Delete, "/api/v1/companion/installation", "Bearer atc_mine"), (request.Method, request.Path, request.Authorization));
        Assert.True(revoked);
        Assert.False(authentication.IsLinked);
        Assert.False(File.Exists(Path.Combine(directory.Path, "token.bin")));
    }

    [Fact]
    public async Task Disconnecting_offline_still_forgets_the_token_locally()
    {
        using var directory = new TempDirectory();
        new TokenStore(directory.Path).Save("atc_mine");
        var handler = new StubHandler((_, _) => throw new HttpRequestException("offline"));
        using var api = new AramTrackerApiService(BaseUrl, handler);
        var authentication = new AuthenticationService(api, new SettingsService(directory.Path), new TokenStore(directory.Path));

        Assert.False(await authentication.DisconnectAsync(CancellationToken.None));
        Assert.False(authentication.IsLinked);
    }

    [Fact]
    public async Task A_rate_limit_carries_the_retry_after_delay()
    {
        var handler = new StubHandler((_, _) =>
        {
            var response = StubHandler.Json(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
            return response;
        });
        using var api = new AramTrackerApiService(BaseUrl, handler);

        var exception = await Assert.ThrowsAsync<ApiException>(() => api.GetMeAsync("atc_x", CancellationToken.None));

        Assert.Equal(ApiFailure.RateLimited, exception.Failure);
        Assert.Equal(TimeSpan.FromSeconds(42), exception.RetryAfter);
    }

    [Theory]
    [InlineData("http://aramtracker.com/api/v1/companion")]
    [InlineData("ftp://aramtracker.com")]
    [InlineData("not a url")]
    public void Refuses_an_api_address_that_is_not_https(string baseUrl)
    {
        Assert.Throws<ArgumentException>(() => AramTrackerApiService.ValidateBaseUrl(baseUrl));
    }

    [Fact]
    public void Allows_plain_http_only_for_a_local_development_server()
    {
        Assert.Equal("http://localhost:8000/api/v1/companion/", AramTrackerApiService.ValidateBaseUrl("http://localhost:8000/api/v1/companion").AbsoluteUri);
    }
}
