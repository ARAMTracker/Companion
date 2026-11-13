using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Win32;

namespace AramTrackerCompanion;

internal static partial class LinkProtocol
{
    public const string Scheme = "aramtracker";

    private const string ClassKey = @"Software\Classes\" + Scheme;

    public static void Register()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(ClassKey);
            key.SetValue("", $"URL:{AppInfo.Name}");
            key.SetValue("URL Protocol", "");

            using var command = key.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{Environment.ProcessPath}\" \"%1\"");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("The aramtracker link could not be registered", exception);
        }
    }

    public static string? ReadPairingCode(string? argument)
    {
        if (argument is null
            || !Uri.TryCreate(argument, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("pair", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var code = HttpUtility.ParseQueryString(uri.Query)["code"]?.Trim().ToUpperInvariant();
        return code is not null && PairingCode().IsMatch(code) ? code : null;
    }

    [GeneratedRegex("^[A-Z0-9]{4,16}$")]
    private static partial Regex PairingCode();
}
