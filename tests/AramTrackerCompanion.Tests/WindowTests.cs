using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using AramTrackerCompanion.Account;
using AramTrackerCompanion.Api;
using AramTrackerCompanion.Settings;
using AramTrackerCompanion.Tracking;
using AramTrackerCompanion.Ui;

namespace AramTrackerCompanion.Tests;

public class WindowTests
{
    private const string PreviewDirectoryVariable = "ARAMTRACKER_COMPANION_PREVIEWS";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_window_builds_and_paints(bool linked)
    {
        RunOnStaThread(() =>
        {
            using var directory = new TempDirectory();
            using var api = new AramTrackerApiService("https://example.test/api/v1/companion", new StubHandler((_, _) => throw new HttpRequestException("offline")));
            var settings = new SettingsService(directory.Path);

            if (linked)
            {
                new TokenStore(directory.Path).Save("atc_preview");
                settings.Update(s =>
                {
                    s.LinkedAccounts = ["Player One#EUW", "Second Account#TEST"];
                    s.LastTrackedMatchId = "EUW1_7000000001";
                    s.LastTrackedAt = DateTimeOffset.UtcNow.AddMinutes(-12);
                    s.LastUploadAt = DateTimeOffset.UtcNow.AddMinutes(-11);
                });
            }

            var authentication = new AuthenticationService(api, settings, new TokenStore(directory.Path));
            var outbox = new Outbox(directory.Path);
            var tracking = new MatchTrackingService(settings, outbox, new UploadService(api, authentication, outbox, new Outbox(directory.Path, Outbox.WaitingFile), settings));
            var startup = new StartupService($"ARAMTrackerCompanion.Test.{Guid.NewGuid():N}");
            var website = new Uri("https://example.test/settings?tab=companion");
            var suffix = linked ? "linked" : "new";

            Paint(new MainForm(settings, authentication, tracking, outbox, startup), $"main-{suffix}");
            Paint(new SetupForm(authentication, settings, website), $"setup-{suffix}");
            Paint(new LinkForm(authentication, settings, website), $"link-{suffix}");
            Paint(new SettingsForm(settings, startup, tracking), $"settings-{suffix}");
        });
    }

    private static void Paint(Form form, string name)
    {
        using (form)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();

            using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(form.BackColor);
            }

            foreach (Control control in form.Controls.Cast<Control>().Reverse())
            {
                using var part = new Bitmap(control.Width, control.Height);
                control.DrawToBitmap(part, new Rectangle(Point.Empty, control.Size));
                using var graphics = Graphics.FromImage(bitmap);
                graphics.DrawImage(part, control.Location);
            }

            Assert.NotEmpty(form.Controls);

            if (Environment.GetEnvironmentVariable(PreviewDirectoryVariable) is { Length: > 0 } previews)
            {
                Directory.CreateDirectory(previews);
                bitmap.Save(Path.Combine(previews, name + ".png"), ImageFormat.Png);
            }

            form.Close();
        }
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }
}

public class LinkProtocolTests
{
    [Theory]
    [InlineData("aramtracker://pair?code=K7PX2MQ9", "K7PX2MQ9")]
    [InlineData("ARAMTRACKER://PAIR/?code=k7px2mq9", "K7PX2MQ9")]
    [InlineData("aramtracker://pair?other=1&code=ABCD1234", "ABCD1234")]
    public void Reads_the_code_from_a_pairing_link(string link, string expected)
    {
        Assert.Equal(expected, LinkProtocol.ReadPairingCode(link));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("--minimized")]
    [InlineData("show")]
    [InlineData("https://aramtracker.com/pair?code=K7PX2MQ9")]
    [InlineData("aramtracker://unpair?code=K7PX2MQ9")]
    [InlineData("aramtracker://pair")]
    [InlineData("aramtracker://pair?code=")]
    [InlineData("aramtracker://pair?code=abc")]
    [InlineData("aramtracker://pair?code=K7PX-2MQ9")]
    [InlineData("aramtracker://pair?code=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("aramtracker://pair?code=K7PX2MQ9%22%20--flag")]
    public void Ignores_anything_that_is_not_a_pairing_link(string? link)
    {
        Assert.Null(LinkProtocol.ReadPairingCode(link));
    }

    [Fact]
    public async Task A_second_launch_hands_its_link_to_the_running_companion()
    {
        var pipeName = $"ARAMTrackerCompanion.Test.{Guid.NewGuid():N}";
        using var channel = new InstanceChannel(pipeName);
        var received = new TaskCompletionSource<string>();
        channel.Listen(message => received.TrySetResult(message));

        var sent = false;
        for (var attempt = 0; attempt < 20 && !sent; attempt++)
        {
            sent = InstanceChannel.TrySend("aramtracker://pair?code=K7PX2MQ9", pipeName);
        }

        Assert.True(sent);
        Assert.Equal("aramtracker://pair?code=K7PX2MQ9", await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
