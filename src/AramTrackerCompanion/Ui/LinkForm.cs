using AramTrackerCompanion.Account;
using AramTrackerCompanion.Settings;

namespace AramTrackerCompanion.Ui;

internal sealed class LinkForm : Form
{
    public LinkForm(AuthenticationService authentication, SettingsService settings, Uri websiteUrl)
    {
        Theme.Apply(this, "Connect your account", height: 70 + PairingControl.PreferredHeight + 16);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var pairing = new PairingControl(authentication, settings, websiteUrl) { Location = new Point(Theme.Margin, 70) };
        pairing.Linked += () =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(Theme.Text("Connect your account", Theme.Heading, Theme.Ink, Theme.Margin - 2, 22, Theme.ContentWidth, 30));
        Controls.Add(pairing);

        Theme.FitToDpi(this);
    }
}
