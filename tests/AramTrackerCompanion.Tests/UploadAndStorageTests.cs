using System.Net;
using System.Text.Json;
using AramTrackerCompanion.Account;
using AramTrackerCompanion.Api;
using AramTrackerCompanion.Settings;
using AramTrackerCompanion.Tracking;
using Microsoft.Win32;

namespace AramTrackerCompanion.Tests;

public class UploadAndStorageTests
{
    private const string BaseUrl = "https://example.test/api/v1/companion";

    [Fact]
    public async Task Answered_games_leave_the_outbox_whatever_the_answer()
    {
        using var context = new UploadContext((_, body) =>
        {
            var ids = SentIds(body);
            return StubHandler.Json(HttpStatusCode.OK, $$"""
                {"results":[
                  {"match_id":"{{ids[0]}}","status":"accepted"},
                  {"match_id":"{{ids[1]}}","status":"duplicate"},
                  {"match_id":"{{ids[2]}}","status":"rejected","message":"The game failed validation.","errors":{"game_start":["Too old."]} }
                ]}
                """);
        });
        context.Queue("EUW1_1", "EUW1_2", "EUW1_3");

        var retryIn = await context.Uploads.FlushAsync(CancellationToken.None);

        Assert.Null(retryIn);
        Assert.Equal(0, context.Outbox.Count);
        Assert.Single(context.Handler.Requests);
        Assert.Equal("Bearer atc_token", context.Handler.Requests[0].Authorization);
        Assert.NotNull(context.Settings.Read(s => s.LastUploadAt));
        Assert.Equal("The game failed validation.", context.Settings.Read(s => s.LastUploadProblem));
    }

    [Fact]
    public async Task A_network_failure_keeps_every_game_and_backs_off()
    {
        var online = false;
        using var context = new UploadContext((_, body) => online
            ? StubHandler.Json(HttpStatusCode.OK, $$"""{"results":[{"match_id":"{{SentIds(body)[0]}}","status":"accepted"}]}""")
            : throw new HttpRequestException("offline"));
        context.Queue("EUW1_1");

        var first = await context.Uploads.FlushAsync(CancellationToken.None);
        var second = await context.Uploads.FlushAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(30), first);
        Assert.Equal(TimeSpan.FromMinutes(1), second);
        Assert.Equal(1, context.Outbox.Count);
        Assert.Equal(1, new Outbox(context.Directory.Path).Count);

        online = true;

        Assert.Null(await context.Uploads.FlushAsync(CancellationToken.None));
        Assert.Equal(0, context.Outbox.Count);
    }

    [Fact]
    public async Task A_server_error_keeps_every_game()
    {
        using var context = new UploadContext((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway));
        context.Queue("EUW1_1");

        Assert.NotNull(await context.Uploads.FlushAsync(CancellationToken.None));
        Assert.Equal(1, context.Outbox.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    public async Task A_refusal_the_website_did_not_judge_keeps_every_game(HttpStatusCode status)
    {
        using var context = new UploadContext((_, _) => new HttpResponseMessage(status) { Content = new StringContent("<html>Blocked</html>") });
        context.Queue("EUW1_1", "EUW1_2");

        Assert.Equal(TimeSpan.FromSeconds(30), await context.Uploads.FlushAsync(CancellationToken.None));
        Assert.Equal(2, context.Outbox.Count);
        Assert.NotNull(context.Settings.Read(s => s.LastUploadProblem));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task A_request_the_website_refused_is_dropped(HttpStatusCode status)
    {
        using var context = new UploadContext((_, _) => StubHandler.Json(status, """{"error":{"code":"invalid","message":"The matches field is required."}}"""));
        context.Queue("EUW1_1");

        Assert.Null(await context.Uploads.FlushAsync(CancellationToken.None));
        Assert.Equal(0, context.Outbox.Count);
    }

    [Fact]
    public async Task A_game_the_website_could_not_store_is_kept_for_another_try()
    {
        using var context = new UploadContext((_, body) => StubHandler.Json(HttpStatusCode.OK, $$"""
            {"results":[{"match_id":"{{SentIds(body)[0]}}","status":"retry","code":"store_failed","message":"The game could not be stored. Try again later."}]}
            """));
        context.Queue("EUW1_1");

        Assert.NotNull(await context.Uploads.FlushAsync(CancellationToken.None));
        Assert.Equal(1, context.Outbox.Count);
    }

    [Fact]
    public async Task A_game_from_an_account_that_is_not_linked_waits_until_one_is()
    {
        using var context = new UploadContext((_, body) => StubHandler.Json(HttpStatusCode.OK, $$"""
            {"results":[{"match_id":"{{SentIds(body)[0]}}","status":"rejected","code":"not_your_game","message":"None of the accounts linked to this companion played in this game."}]}
            """));
        context.Queue("EUW1_1");

        Assert.Null(await context.Uploads.FlushAsync(CancellationToken.None));
        Assert.Equal(0, context.Outbox.Count);
        Assert.Equal("EUW1_1", Assert.Single(new Outbox(context.Directory.Path, Outbox.WaitingFile).Peek(10)).MatchId);

        context.Uploads.ReleaseWaiting();

        Assert.Equal(0, context.Waiting.Count);
        Assert.Equal("EUW1_1", Assert.Single(context.Outbox.Peek(10)).MatchId);
    }

    [Fact]
    public async Task Games_too_old_for_the_website_expire_instead_of_being_sent()
    {
        using var context = new UploadContext((_, _) => throw new InvalidOperationException("Nothing should be sent."));
        context.Outbox.Add(Match("EUW1_1", DateTimeOffset.UtcNow.AddDays(-15)));
        context.Waiting.Add(Match("EUW1_2", DateTimeOffset.UtcNow.AddDays(-15)));

        Assert.Null(await context.Uploads.FlushAsync(CancellationToken.None));
        Assert.Equal(0, context.Outbox.Count);
        Assert.Equal(0, context.Waiting.Count);
        Assert.Empty(context.Handler.Requests);
    }

    [Fact]
    public async Task A_refused_token_stops_uploading_and_keeps_the_games_for_the_next_pairing()
    {
        using var context = new UploadContext((_, _) => StubHandler.Json(HttpStatusCode.Unauthorized, """{"error":{"code":"unauthenticated","message":"No."}}"""));
        context.Queue("EUW1_1");

        Assert.Null(await context.Uploads.FlushAsync(CancellationToken.None));
        Assert.False(context.Authentication.IsLinked);
        Assert.Equal(1, context.Outbox.Count);

        await context.Uploads.FlushAsync(CancellationToken.None);
        Assert.Single(context.Handler.Requests);
    }

    [Fact]
    public async Task More_than_ten_games_go_out_in_batches()
    {
        using var context = new UploadContext((_, body) =>
        {
            var results = string.Join(',', SentIds(body).Select(id => $$"""{"match_id":"{{id}}","status":"accepted"}"""));
            return StubHandler.Json(HttpStatusCode.OK, $$"""{"results":[{{results}}]}""");
        });
        context.Queue(Enumerable.Range(1, 23).Select(n => $"EUW1_{n}").ToArray());

        await context.Uploads.FlushAsync(CancellationToken.None);

        Assert.Equal([10, 10, 3], context.Handler.Requests.Select(r => SentIds(r.Body).Count));
        Assert.Equal(0, context.Outbox.Count);
    }

    [Fact]
    public async Task A_game_the_website_did_not_answer_for_stays_queued()
    {
        using var context = new UploadContext((_, _) => StubHandler.Json(HttpStatusCode.OK, """{"results":[{"match_id":"EUW1_1","status":"accepted"}]}"""));
        context.Queue("EUW1_1", "EUW1_2");

        Assert.NotNull(await context.Uploads.FlushAsync(CancellationToken.None));
        Assert.Equal("EUW1_2", Assert.Single(context.Outbox.Peek(10)).MatchId);
    }

    [Fact]
    public void The_outbox_holds_each_game_once_and_survives_a_restart()
    {
        using var directory = new TempDirectory();
        var outbox = new Outbox(directory.Path);

        Assert.True(outbox.Add(Match("EUW1_1")));
        Assert.False(outbox.Add(Match("EUW1_1")));
        Assert.True(outbox.Add(Match("EUW1_2")));

        var reopened = new Outbox(directory.Path);
        Assert.Equal(["EUW1_1", "EUW1_2"], reopened.Peek(10).Select(m => m.MatchId));
        Assert.Equal(10, reopened.Peek(10)[0].Participants.Count);
    }

    [Fact]
    public void Processed_games_are_remembered_across_restarts_and_capped()
    {
        using var directory = new TempDirectory();
        var settings = new SettingsService(directory.Path);

        for (var n = 1; n <= 320; n++)
        {
            settings.MarkProcessed($"EUW1_{n}", tracked: true);
        }

        var reopened = new SettingsService(directory.Path);
        Assert.True(reopened.IsProcessed("EUW1_320"));
        Assert.False(reopened.IsProcessed("EUW1_1"));
        Assert.Equal(300, reopened.Read(s => s.ProcessedMatchIds.Count));
        Assert.Equal("EUW1_320", reopened.Read(s => s.LastTrackedMatchId));
    }

    [Fact]
    public void Start_with_windows_can_be_turned_on_and_off_cleanly()
    {
        var valueName = $"ARAMTrackerCompanion.Test.{Guid.NewGuid():N}";
        var startup = new StartupService(valueName);

        try
        {
            Assert.False(startup.IsEnabled);

            startup.SetEnabled(true);
            Assert.True(startup.IsEnabled);

            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                var command = Assert.IsType<string>(key!.GetValue(valueName));
                Assert.Equal($"\"{Environment.ProcessPath}\" --minimized", command);
            }

            startup.SetEnabled(false);
            Assert.False(startup.IsEnabled);
        }
        finally
        {
            startup.SetEnabled(false);
        }
    }

    private static MatchSubmission Match(string matchId, DateTimeOffset? started = null)
    {
        var game = Fixtures.MayhemGameNode();
        game["gameId"] = long.Parse(matchId.Split('_')[1]);
        game["gameCreation"] = (started ?? DateTimeOffset.UtcNow.AddHours(-1)).ToUnixTimeMilliseconds();
        return MatchMapper.Map(Fixtures.ToElement(game)).Match!;
    }

    private static List<string> SentIds(string? body)
    {
        using var document = JsonDocument.Parse(body!);
        return document.RootElement.GetProperty("matches").EnumerateArray().Select(m => m.GetProperty("match_id").GetString()!).ToList();
    }

    private sealed class UploadContext : IDisposable
    {
        public UploadContext(Func<HttpRequestMessage, string?, HttpResponseMessage> respond)
        {
            new TokenStore(Directory.Path).Save("atc_token");
            Handler = new StubHandler(respond);
            Api = new AramTrackerApiService(BaseUrl, Handler);
            Settings = new SettingsService(Directory.Path);
            Authentication = new AuthenticationService(Api, Settings, new TokenStore(Directory.Path));
            Outbox = new Outbox(Directory.Path);
            Waiting = new Outbox(Directory.Path, Outbox.WaitingFile);
            Uploads = new UploadService(Api, Authentication, Outbox, Waiting, Settings);
        }

        public TempDirectory Directory { get; } = new();
        public StubHandler Handler { get; }
        public AramTrackerApiService Api { get; }
        public SettingsService Settings { get; }
        public AuthenticationService Authentication { get; }
        public Outbox Outbox { get; }
        public Outbox Waiting { get; }
        public UploadService Uploads { get; }

        public void Queue(params string[] matchIds)
        {
            foreach (var matchId in matchIds)
            {
                Outbox.Add(Match(matchId));
            }
        }

        public void Dispose()
        {
            Api.Dispose();
            Directory.Dispose();
        }
    }
}

public class TrackingLifecycleTests
{
    [Fact]
    public async Task Tracking_starts_and_stops_promptly_whether_or_not_a_client_is_running()
    {
        using var directory = new TempDirectory();
        using var api = new AramTrackerApiService("https://example.test/api/v1/companion", new StubHandler((_, _) => throw new HttpRequestException("offline")));
        var settings = new SettingsService(directory.Path);
        var outbox = new Outbox(directory.Path);
        var uploads = new UploadService(api, new AuthenticationService(api, settings, new TokenStore(directory.Path)), outbox, new Outbox(directory.Path, Outbox.WaitingFile), settings);
        var tracking = new MatchTrackingService(settings, outbox, uploads);

        tracking.Start();
        Assert.True(tracking.IsRunning);
        await Task.Delay(TimeSpan.FromSeconds(1));

        var stopping = tracking.StopAsync();
        Assert.Same(stopping, await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.False(tracking.IsRunning);
        Assert.Equal(LeagueClientStatus.TrackingPaused, tracking.Status);
    }
}
