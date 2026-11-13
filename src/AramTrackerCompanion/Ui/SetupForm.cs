using AramTrackerCompanion.Account;
using AramTrackerCompanion.Settings;

namespace AramTrackerCompanion.Ui;

internal sealed class SetupForm : Form
{
    private readonly PillButton _startWithWindows = new("Yes, start with Windows", primary: true);
    private readonly PillButton _startManually = new("No, I'll start it manually");

    public SetupForm(AuthenticationService authentication, SettingsService settings, Uri websiteUrl)
    {
        Theme.Apply(this, $"{AppInfo.Name} setup", height: 664);
        Theme.AddBrand(this);

        Controls.Add(Theme.Text("Track your Mayhem games.", Theme.Headline, Theme.Ink, Theme.Margin - 3, 78, Theme.ContentWidth + 6, 38));
        Controls.Add(Theme.Text("Set up once, then forget it.", Theme.Headline, Theme.Faint, Theme.Margin - 3, 114, Theme.ContentWidth + 6, 38));
        Controls.Add(Theme.Text(
            "ARAM Tracker Companion automatically tracks your ARAM Mayhem matches and adds them to your ARAM Tracker profile. It never asks for your Riot password.",
            Theme.Body, Theme.Muted, Theme.Margin, 166, Theme.ContentWidth, 56));

        Controls.Add(Theme.Text(
            "Would you like ARAM Tracker Companion to start automatically when Windows starts?",
            Theme.Strong, Theme.Ink, Theme.Margin, 236, Theme.ContentWidth, 40));

        const int OptionWidth = (Theme.ContentWidth - 10) / 2;
        _startWithWindows.Bounds = new Rectangle(Theme.Margin, 284, OptionWidth, 38);
        _startManually.Bounds = new Rectangle(Theme.Margin + OptionWidth + 10, 284, OptionWidth, 38);
        _startWithWindows.Click += (_, _) => Choose(startWithWindows: true);
        _startManually.Click += (_, _) => Choose(startWithWindows: false);
        Controls.Add(_startWithWindows);
        Controls.Add(_startManually);

        Controls.Add(Theme.Text("Connect your account", Theme.Strong, Theme.Ink, Theme.Margin, 350, Theme.ContentWidth, 22));
        Controls.Add(new PairingControl(authentication, settings, websiteUrl) { Location = new Point(Theme.Margin, 378) });

        var finish = new PillButton("Finish", primary: true);
        finish.Width = 110;
        finish.Location = new Point(Theme.Margin + Theme.ContentWidth - finish.Width, 602);
        finish.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(finish);
        Controls.Add(Theme.Text("You can connect later from the main window.", Theme.Small, Theme.Faint, Theme.Margin, 612, 280, 18));

        Theme.FitToDpi(this);
    }

    public bool StartWithWindows { get; private set; } = true;

    private void Choose(bool startWithWindows)
    {
        StartWithWindows = startWithWindows;
        _startWithWindows.Primary = startWithWindows;
        _startManually.Primary = !startWithWindows;
    }
}
