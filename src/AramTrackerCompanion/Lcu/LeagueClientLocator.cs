using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace AramTrackerCompanion.Lcu;

internal sealed record LeagueClientConnection(int ProcessId, int Port, string Password);

internal static class LeagueClientLocator
{
    private const string ProcessName = "LeagueClientUx";

    public static LeagueClientConnection? TryFind()
    {
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                var directory = InstallDirectory(process);
                if (directory is null)
                {
                    continue;
                }

                var connection = ParseLockfile(ReadLockfile(Path.Combine(directory, "lockfile")));
                if (connection is not null)
                {
                    return connection;
                }
            }
        }

        return null;
    }

    // name:pid:port:password:protocol
    internal static LeagueClientConnection? ParseLockfile(string? contents)
    {
        var parts = contents?.Trim().Split(':');

        if (parts is not { Length: 5 }
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var processId)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is <= 0 or > 65535
            || parts[3].Length == 0
            || parts[4] != "https")
        {
            return null;
        }

        return new LeagueClientConnection(processId, port, parts[3]);
    }

    private static string? InstallDirectory(Process process)
    {
        try
        {
            return Path.GetDirectoryName(process.MainModule?.FileName);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            // The client runs as administrator and the companion does not.
            return null;
        }
    }

    private static string? ReadLockfile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
