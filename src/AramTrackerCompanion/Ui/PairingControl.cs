using System.Diagnostics;
using AramTrackerCompanion.Account;
using AramTrackerCompanion.Api;
using AramTrackerCompanion.Settings;

namespace AramTrackerCompanion.Ui;

internal sealed class PairingControl : UserControl
{
    public const int PreferredHeight = 200;

    private readonly AuthenticationService _authentication;
    private readonly SettingsService _settings;
    private readonly Uri _websiteUrl;
    private readonly TextBox _code = new()
    {
        MaxLength = 16,
        CharacterCasing = CharacterCasing.Upper,
        Font = Theme.Code,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Tile,
        ForeColor = Theme.Ink,
        Bounds = new Rectangle(16, 10, 196, 22),
        AccessibleName = "Pairing code",
    };

    private readonly PillButton _connect = new("Connect");
    private readonly Label _status = Theme.Text("", Theme.Body, Theme.Muted, 0, 164, Theme.ContentWidth, 36);

    public PairingControl(AuthenticationService authentication, SettingsService settings, Uri websiteUrl)
    {
        _authentication = authentication;
        _settings = settings;
        _websiteUrl = websiteUrl;

        Size = new Size(Theme.ContentWidth, PreferredHeight);
        BackColor = Theme.Background;
        AutoScaleMode = AutoScaleMode.None;

        var openWebsite = new PillButton("Open ARAM Tracker", primary: true) { Location = new Point(0, 48) };
        openWebsite.Click += (_, _) => OpenWebsite();

        var codeBox = new TilePanel { Bounds = new Rectangle(0, 120, 228, 40) };
        codeBox.Controls.Add(_code);
        codeBox.Click += (_, _) => _code.Focus();

        _connect.Location = new Point(236, 121);
        _connect.Click += async (_, _) => await ConnectAsync();
        _code.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await ConnectAsync();
            }
        };

        Controls.Add(Theme.Text(
            "On ARAM Tracker, open Settings, then Companion, and press Connect the companion. Your browser asks to open this app.",
            Theme.Body, Theme.Muted, 0, 0, Theme.ContentWidth, 40));
        Controls.Add(openWebsite);
        Controls.Add(Theme.Text("Or type a pairing code from that page", Theme.Small, Theme.Faint, 0, 98, Theme.ContentWidth, 18));
        Controls.Add(codeBox);
        Controls.Add(_connect);
        Controls.Add(_status);

        _authentication.Changed += OnAccountChanged;
        ShowStatus(null);
    }

    public event Action? Linked;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _authentication.Changed -= OnAccountChanged;
        }

        base.Dispose(disposing);
    }

    private void OnAccountChanged()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        BeginInvoke(() =>
        {
            ShowStatus(null);

            if (_authentication.IsLinked)
            {
                Linked?.Invoke();
            }
        });
    }

    private void OpenWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_websiteUrl.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowStatus($"The browser could not be opened. Go to {_websiteUrl.AbsoluteUri}");
        }
    }

    private async Task ConnectAsync()
    {
        if (!_connect.Enabled)
        {
            return;
        }

        _connect.Enabled = false;
        _code.Enabled = false;
        ShowStatus("Connecting…", isProblem: false);

        try
        {
            await _authentication.PairAsync(_code.Text, CancellationToken.None);
            _code.Clear();
        }
        catch (ApiException exception)
        {
            ShowStatus(exception.Failure == ApiFailure.InvalidCode
                ? "That code is wrong, used or older than 10 minutes. Make a new one on the website."
                : exception.Message);
        }
        finally
        {
            if (!IsDisposed)
            {
                _connect.Enabled = true;
                _code.Enabled = true;
            }
        }
    }

    private void ShowStatus(string? message, bool isProblem = true)
    {
        if (IsDisposed)
        {
            return;
        }

        if (message is null && _authentication.IsLinked)
        {
            var accounts = _settings.Read(s => string.Join(", ", s.LinkedAccounts));
            _status.Text = accounts.Length == 0 ? "Connected to your ARAM Tracker account." : $"Connected: {accounts}";
            _status.ForeColor = Theme.Good;
            return;
        }

        _status.Text = message ?? "";
        _status.ForeColor = isProblem ? Theme.Bad : Theme.Muted;
    }
}
