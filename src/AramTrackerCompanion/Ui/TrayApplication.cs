using System.Diagnostics;
using AramTrackerCompanion.Account;
using AramTrackerCompanion.Api;
using AramTrackerCompanion.Settings;
using AramTrackerCompanion.Tracking;

namespace AramTrackerCompanion.Ui;

internal sealed class TrayApplication : ApplicationContext
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

    private readonly SettingsService _settings;
    private readonly AramTrackerApiService _api;
    private readonly AuthenticationService _authentication;
    private readonly Outbox _outbox;
    private readonly UploadService _uploads;
    private readonly MatchTrackingService _tracking;
    private readonly StartupService _startup = new();

    private readonly CancellationTokenSource _shutdown = new();
    private readonly Control _invoker = new();
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _toggleTrackingItem = new("Pause tracking");
    private readonly Task _uploading;

    private MainForm? _window;
    private bool _trayHintShown;
    private bool _exiting;
    private bool _pairingFromLink;

    public TrayApplication(bool startMinimized, InstanceChannel channel, string? pairingCode)
    {
        _ = _invoker.Handle;

        _settings = new SettingsService(AppInfo.DataDirectory);
        _api = CreateApi(_settings.Read(s => s.ApiBaseUrl));
        _authentication = new AuthenticationService(_api, _settings, new TokenStore(AppInfo.DataDirectory));
        _outbox = new Outbox(AppInfo.DataDirectory);
        _uploads = new UploadService(_api, _authentication, _outbox, new Outbox(AppInfo.DataDirectory, Outbox.WaitingFile), _settings);
        _tracking = new MatchTrackingService(_settings, _outbox, _uploads);
        _authentication.AccountsAdded += _uploads.ReleaseWaiting;

        _trayIcon = CreateTrayIcon();

        _settings.Changed += RefreshOnUiThread;
        _tracking.StatusChanged += RefreshOnUiThread;
        _authentication.Changed += () =>
        {
            _uploads.Wake();
            RefreshOnUiThread();
        };

        channel.Listen(message => OnUiThread(() => OnMessageFromSecondLaunch(message)));

        TryRefreshStartupPath();
        LinkProtocol.Register();

        _uploading = Task.Run(() => _uploads.RunAsync(_shutdown.Token));
        _ = Task.Run(() => _authentication.VerifyAsync(_shutdown.Token));

        if (_settings.Read(s => s.TrackingEnabled))
        {
            _tracking.Start();
        }

        Refresh();

        if (!startMinimized || pairingCode is not null)
        {
            OnUiThread(async () =>
            {
                if (pairingCode is not null)
                {
                    await PairFromLinkAsync(pairingCode);
                }

                RunSetupIfNeeded();
                ShowWindow();
            });
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
            _window?.Dispose();
            _invoker.Dispose();
            _api.Dispose();
            _shutdown.Dispose();
        }

        base.Dispose(disposing);
    }

    private static AramTrackerApiService CreateApi(string baseUrl)
    {
        try
        {
            return new AramTrackerApiService(baseUrl);
        }
        catch (ArgumentException exception)
        {
            Log.Error("The configured API address is not usable, using the default", exception);
            return new AramTrackerApiService(AppInfo.DefaultApiBaseUrl);
        }
    }

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowWindow());
        menu.Items.Add(_toggleTrackingItem);
        menu.Items.Add("Open ARAM Tracker", null, (_, _) => OpenWebsite());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());
        _toggleTrackingItem.Click += async (_, _) => await ToggleTrackingAsync();

        var icon = new NotifyIcon
        {
            Icon = Theme.AppIcon,
            Text = AppInfo.Name,
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.DoubleClick += (_, _) => ShowWindow();
        return icon;
    }

    private void RunSetupIfNeeded()
    {
        if (_settings.Read(s => s.SetupCompleted))
        {
            return;
        }

        using var setup = new SetupForm(_authentication, _settings, _api.WebsiteSettingsUrl);

        if (setup.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        TrySetStartup(setup.StartWithWindows);
        _settings.Update(s => s.SetupCompleted = true);
    }

    private void ShowWindow()
    {
        if (_exiting)
        {
            return;
        }

        if (_window is null || _window.IsDisposed)
        {
            _window = new MainForm(_settings, _authentication, _tracking, _outbox, _startup);
            _window.ToggleTrackingRequested += async () => await ToggleTrackingAsync();
            _window.LinkRequested += LinkAccount;
            _window.DisconnectRequested += async () => await DisconnectAsync();
            _window.SettingsRequested += OpenSettings;
            _window.FormClosing += OnWindowClosing;
        }

        _window.Render();
        _window.Show();
        _window.WindowState = FormWindowState.Normal;
        _window.Activate();
    }

    private async void OnWindowClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exiting || e.CloseReason != CloseReason.UserClosing)
        {
            return;
        }

        e.Cancel = true;

        if (!_settings.Read(s => s.CloseToTray))
        {
            await ExitAsync();
            return;
        }

        _window?.Hide();

        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _trayIcon.ShowBalloonTip(4000, AppInfo.Name, "Still running here, tracking your Mayhem games.", ToolTipIcon.None);
        }
    }

    private async Task ToggleTrackingAsync()
    {
        if (_tracking.IsRunning)
        {
            await _tracking.StopAsync();
            _settings.Update(s => s.TrackingEnabled = false);
        }
        else
        {
            _tracking.Start();
            _settings.Update(s => s.TrackingEnabled = true);
        }

        Refresh();
    }

    private void LinkAccount()
    {
        using var link = new LinkForm(_authentication, _settings, _api.WebsiteSettingsUrl);
        link.ShowDialog(_window);
        Refresh();
    }

    private async void OnMessageFromSecondLaunch(string message)
    {
        if (LinkProtocol.ReadPairingCode(message) is { } code)
        {
            await PairFromLinkAsync(code);
        }

        if (_settings.Read(s => s.SetupCompleted))
        {
            ShowWindow();
        }
    }

    // Any web page can open the link, so ask first.
    private async Task PairFromLinkAsync(string code)
    {
        if (_exiting || _pairingFromLink)
        {
            return;
        }

        _pairingFromLink = true;

        try
        {
            var owner = Form.ActiveForm;
            owner?.Activate();

            var question = _authentication.IsLinked
                ? "This companion is already connected to an ARAM Tracker account. Replace that with the account that just asked to connect?"
                : "Connect this companion to your ARAM Tracker account?";

            var answer = MessageBox.Show(
                owner,
                question + "\n\nOnly continue if you just pressed Connect the companion on aramtracker.com.",
                AppInfo.Name,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);

            if (answer != DialogResult.OK)
            {
                Log.Info("Pairing from a link was declined");
                return;
            }

            if (_authentication.IsLinked)
            {
                await _authentication.DisconnectAsync(_shutdown.Token);
            }

            await _authentication.PairAsync(code, _shutdown.Token);
        }
        catch (ApiException exception)
        {
            MessageBox.Show(
                Form.ActiveForm,
                exception.Failure == ApiFailure.InvalidCode
                    ? "That link has already been used or is older than 10 minutes. Press Connect the companion on the website again."
                    : exception.Message,
                AppInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _pairingFromLink = false;
            Refresh();
        }
    }

    private async Task DisconnectAsync()
    {
        var answer = MessageBox.Show(
            _window,
            "Disconnect this companion from your ARAM Tracker account? Games will stop being uploaded until you connect again.",
            AppInfo.Name,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);

        if (answer != DialogResult.OK)
        {
            return;
        }

        var revoked = await _authentication.DisconnectAsync(_shutdown.Token);

        if (!revoked)
        {
            MessageBox.Show(
                _window,
                "The account was removed from this computer, but the website could not be reached. Remove this companion under Settings, Companion on the website as well.",
                AppInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        Refresh();
    }

    private void OpenSettings()
    {
        using var settings = new SettingsForm(_settings, _startup, _tracking);
        settings.ShowDialog(_window);
        Refresh();
    }

    private void OpenWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo(new Uri(_api.WebsiteSettingsUrl, "/").AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("The browser could not be opened", exception);
        }
    }

    private async Task ExitAsync()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _trayIcon.Visible = false;
        _window?.Hide();
        _shutdown.Cancel();

        var stopping = Task.WhenAll(_tracking.StopAsync(), _uploading);
        await Task.WhenAny(stopping, Task.Delay(ShutdownTimeout));

        ExitThread();
    }

    private void TrySetStartup(bool enabled)
    {
        try
        {
            _startup.SetEnabled(enabled);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("Start with Windows could not be changed", exception);
        }
    }

    private void TryRefreshStartupPath()
    {
        try
        {
            _startup.RefreshPath();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("The start-up entry could not be refreshed", exception);
        }
    }

    private void RefreshOnUiThread() => OnUiThread(Refresh);

    private void OnUiThread(Action action)
    {
        if (_exiting || _invoker.IsDisposed)
        {
            return;
        }

        try
        {
            _invoker.BeginInvoke(action);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    private void Refresh()
    {
        if (_exiting)
        {
            return;
        }

        _toggleTrackingItem.Text = _tracking.IsRunning ? "Pause tracking" : "Resume tracking";

        var status = _tracking.Status switch
        {
            LeagueClientStatus.TrackingPaused => "tracking paused",
            LeagueClientStatus.Connected => "League client connected",
            LeagueClientStatus.InGame => "in a game",
            _ => "waiting for the League client",
        };
        _trayIcon.Text = $"{AppInfo.Name}: {status}";

        _window?.Render();
    }
}
