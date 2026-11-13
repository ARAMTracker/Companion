using System.Globalization;
using System.Text;

namespace AramTrackerCompanion;

// Never log tokens, the lockfile password or player names.
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static string FilePath => _path ??= Path.Combine(AppInfo.DataDirectory, "companion.log");

    public static void Info(string message, params (string Key, object? Value)[] fields) => Write("INFO", message, fields);

    public static void Warn(string message, params (string Key, object? Value)[] fields) => Write("WARN", message, fields);

    public static void Error(string message, Exception exception, params (string Key, object? Value)[] fields) =>
        Write("ERROR", message, [.. fields, ("error", $"{exception.GetType().Name}: {exception.Message}")]);

    private static void Write(string level, string message, (string Key, object? Value)[] fields)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
            .Append(' ').Append(level)
            .Append(' ').Append(message);

        foreach (var (key, value) in fields)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            line.Append(' ').Append(key).Append('=');
            line.Append(text.Contains(' ') ? $"\"{text.Replace('"', '\'')}\"" : text);
        }

        line.AppendLine();

        try
        {
            lock (Gate)
            {
                Rotate();
                File.AppendAllText(FilePath, line.ToString());
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void Rotate()
    {
        var file = new FileInfo(FilePath);
        if (file.Exists && file.Length > MaxBytes)
        {
            File.Move(FilePath, FilePath + ".1", overwrite: true);
        }
    }
}
