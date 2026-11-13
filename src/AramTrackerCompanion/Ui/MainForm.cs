using System.Globalization;
using AramTrackerCompanion.Account;
using AramTrackerCompanion.Settings;
using AramTrackerCompanion.Tracking;

namespace AramTrackerCompanion.Ui;

internal sealed class MainForm : Form
{
    private const int TileGap = 10;
    private const int HalfWidth = (Theme.ContentWidth - TileGap) / 2;

    private readonly SettingsService _settings;
    private readonly AuthenticationService _authentication;
    private readonly MatchTrackingService _tracking;
    private readonly Outbox _outbox;
    private readonly StartupService _startup;

    private readonly Label _headline = Theme.Text("", Theme.Headline, Theme.Ink, Theme.Margin - 3, 78, Theme.ContentWidth + 6, 38);
    private readonly Label _subline = Theme.Text("", Theme.Headline, Theme.Faint, Theme.Margin - 3, 114, Theme.ContentWidth + 6, 38);
    private readonly InfoTile _league = new("League client", Theme.Margin, 172, HalfWidth);
    private readonly InfoTile _server = new("ARAM Tracker", Theme.Margin + HalfWidth + TileGap, 172, HalfWidth);
    private readonly InfoTile _account = new("Account", Theme.Margin, 248, Theme.ContentWidth);
    private readonly InfoTile _lastMatch = new("Last tracked match", Theme.Margin, 324, HalfWidth);
    private readonly InfoTile _lastUpload = new("Last upload", Theme.Margin + HalfWidth + TileGap, 324, HalfWidth);
    private readonly TilePanel _startWithWindows;
    private readonly Label _problem = Theme.Text("", Theme.Small, Theme.Bad, Theme.Margin, 460, Theme.ContentWidth, 34);
    private readonly PillButton _toggleTracking = new("Resume tracking", primary: true);
    private readonly PillButton _toggleAccount = new("Disconnect account");
    private readonly PillButton _openSettings = new("Settings");

    public MainForm(SettingsService settings, AuthenticationService authentication, MatchTrackingService tracking, Outbox outbox, StartupService startup)
    {
        _settings = settings;
        _authentication = authentication;
        _tracking = tracking;
        _outbox = outbox;
        _startup = startup;

        Theme.Apply(this, AppInfo.Name, height: 566);
        Theme.AddBrand(this);

        _startWithWindows = TilePanel.Setting("Start with Windows", startup.IsEnabled, startup.SetEnabled, Theme.Margin, 400);

        _toggleTracking.Click += (_, _) => ToggleTrackingRequested?.Invoke();
        _toggleAccount.Click += (_, _) => (_authentication.IsLinked ? DisconnectRequested : LinkRequested)?.Invoke();
        _openSettings.Click += (_, _) => SettingsRequested?.Invoke();

        const int ButtonTop = 504;
        _toggleTracking.Location = new Point(Theme.Margin, ButtonTop);
        _toggleAccount.Location = new Point(_toggleTracking.Right + 8, ButtonTop);
        _openSettings.Location = new Point(Theme.Margin + Theme.ContentWidth - _openSettings.Width, ButtonTop);

        Controls.AddRange([_headline, _subline, _league, _server, _account, _lastMatch, _lastUpload, _startWithWindows, _problem, _toggleTracking, _toggleAccount, _openSettings]);

        Render();
        Theme.FitToDpi(this);
    }

    public event Action? ToggleTrackingRequested;

    public event Action? LinkRequested;

    public event Action? DisconnectRequested;

    public event Action? SettingsRequested;

    public void Render()
    {
        if (IsDisposed)
        {
            return;
        }

        var (accounts, lastMatchAt, lastUploadAt, problem) = _settings.Read(s =>
            (s.LinkedAccounts.ToList(), s.LastTrackedAt, s.LastUploadAt, s.LastUploadProblem));
        var pending = _outbox.Count;
        var linked = _authentication.IsLinked;
        var status = _tracking.Status;

        (_headline.Text, _subline.Text) = Headline(linked, status, pending);

        _league.Display(status switch
        {
            LeagueClientStatus.TrackingPaused => "Paused",
            LeagueClientStatus.NotRunning => "Not running",
            LeagueClientStatus.Connecting => "Starting",
            LeagueClientStatus.Connected => "Connected",
            _ => "In a game",
        }, status is LeagueClientStatus.Connected or LeagueClientStatus.InGame ? Theme.Good : Theme.Faint);

        _server.Display(_authentication.ServerStatus switch
        {
            ServerStatus.Online => "Online",
            ServerStatus.Unreachable => "Can't be reached",
            _ => linked ? "Checking" : "Not checked yet",
        }, _authentication.ServerStatus switch
        {
            ServerStatus.Online => Theme.Good,
            ServerStatus.Unreachable => Theme.Bad,
            _ => Theme.Faint,
        });

        _account.Display(
            !linked ? "Not connected" : accounts.Count == 0 ? "Connected" : string.Join(", ", accounts),
            linked ? Theme.Good : Theme.Faint);

        _lastMatch.Display(lastMatchAt is null ? "None yet" : Moment(lastMatchAt.Value));
        _lastUpload.Display((lastUploadAt is null ? "None yet" : Moment(lastUploadAt.Value)) + (pending > 0 ? $", {pending} waiting" : ""));

        _problem.Text = problem is null ? "" : $"Last problem: {problem}";

        if (_startWithWindows.Toggle is { } toggle)
        {
            toggle.Checked = _startup.IsEnabled;
        }

        _toggleTracking.Text = _tracking.IsRunning ? "Pause tracking" : "Resume tracking";
        _toggleTracking.Primary = !_tracking.IsRunning;
        _toggleAccount.Text = linked ? "Disconnect account" : "Connect account";
        _toggleAccount.Primary = !linked && _tracking.IsRunning;

        _toggleTracking.FitWidth();
        _toggleAccount.FitWidth();
        _toggleAccount.Left = _toggleTracking.Right + (int)(8 * Theme.ScaleOf(this));
    }

    private static (string Headline, string Subline) Headline(bool linked, LeagueClientStatus status, int pending)
    {
        if (!linked)
        {
            return ("Connect your account.", pending > 0 ? $"{pending} games are waiting." : "Games upload once you do.");
        }

        return status switch
        {
            LeagueClientStatus.TrackingPaused => ("Tracking is paused.", "Nothing is being read."),
            LeagueClientStatus.InGame => ("You're in a game.", "It uploads when it ends."),
            LeagueClientStatus.Connected => ("Tracking your games.", "League is open."),
            _ => ("Waiting for League.", "Tracking starts when it opens."),
        };
    }

    private static string Moment(DateTimeOffset moment)
    {
        var local = moment.ToLocalTime();
        var day = local.Date == DateTime.Today ? "Today" : local.ToString("d MMM", CultureInfo.CurrentCulture);
        return $"{day} {local:HH:mm}";
    }
}
