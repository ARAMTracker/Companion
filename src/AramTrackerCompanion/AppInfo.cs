using System.Reflection;

namespace AramTrackerCompanion;

internal static class AppInfo
{
    public const string Name = "ARAM Tracker Companion";
    public const string DefaultApiBaseUrl = "https://aramtracker.com/api/v1/companion";
    public const string DataDirectoryVariable = "ARAMTRACKER_COMPANION_DATA";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public static string UserAgent => $"ARAMTrackerCompanion/{Version}";

    public static string DataDirectory { get; } = ResolveDataDirectory();

    private static string ResolveDataDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        var directory = string.IsNullOrWhiteSpace(overridden)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ARAMTrackerCompanion")
            : Path.GetFullPath(overridden);

        Directory.CreateDirectory(directory);
        return directory;
    }
}
