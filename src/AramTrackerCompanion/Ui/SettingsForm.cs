using System.Diagnostics;
using AramTrackerCompanion.Settings;
using AramTrackerCompanion.Tracking;

namespace AramTrackerCompanion.Ui;

internal sealed class SettingsForm : Form
{
    public SettingsForm(SettingsService settings, StartupService startup, MatchTrackingService tracking)
    {
        Theme.Apply(this, "Settings", height: 366);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;

        Controls.Add(Theme.Text("Settings", Theme.Heading, Theme.Ink, Theme.Margin - 2, 22, Theme.ContentWidth, 30));

        Controls.Add(TilePanel.Setting("Start with Windows", startup.IsEnabled, startup.SetEnabled, Theme.Margin, 70));
        Controls.Add(TilePanel.Setting(
            "Keep running in the tray when the window is closed",
            settings.Read(s => s.CloseToTray),
            on => settings.Update(s => s.CloseToTray = on),
            Theme.Margin,
            130));

        var status = Theme.Text("", Theme.Small, Theme.Muted, Theme.Margin, 250, Theme.ContentWidth, 36);

        var rescan = new PillButton("Check recent games again") { Location = new Point(Theme.Margin, 200) };
        rescan.Click += async (_, _) =>
        {
            rescan.Enabled = false;
            var done = await tracking.RescanAsync();

            if (!IsDisposed)
            {
                status.Text = done
                    ? "Done. Recent Mayhem games were queued again. Games the website already has are ignored."
                    : "The League client has to be open, and tracking on, to do this.";
                rescan.Enabled = true;
            }
        };

        var openLog = new PillButton("Open the log folder") { Location = new Point(rescan.Right + 8, 200) };
        openLog.Click += (_, _) => Process.Start(new ProcessStartInfo(AppInfo.DataDirectory) { UseShellExecute = true })?.Dispose();

        Controls.Add(rescan);
        Controls.Add(openLog);
        Controls.Add(status);
        Controls.Add(Theme.Text(
            $"Version {AppInfo.Version}. An independent project, not affiliated with or endorsed by Riot Games.",
            Theme.Small, Theme.Faint, Theme.Margin, 308, Theme.ContentWidth, 36));

        Theme.FitToDpi(this);
    }
}
