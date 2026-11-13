using System.Text.Json;

namespace AramTrackerCompanion.Settings;

internal sealed class SettingsService
{
    private const int ProcessedMatchLimit = 300;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _gate = new();
    private AppSettings _settings;

    public SettingsService(string directory)
    {
        _path = Path.Combine(directory, "settings.json");
        _settings = Load();
    }

    public event Action? Changed;

    public T Read<T>(Func<AppSettings, T> read)
    {
        lock (_gate)
        {
            return read(_settings);
        }
    }

    public void Update(Action<AppSettings> change)
    {
        lock (_gate)
        {
            change(_settings);
            Save();
        }

        Changed?.Invoke();
    }

    public bool IsProcessed(string matchId) => Read(s => s.ProcessedMatchIds.Contains(matchId));

    public void MarkProcessed(string matchId, bool tracked)
    {
        Update(settings =>
        {
            if (!settings.ProcessedMatchIds.Contains(matchId))
            {
                settings.ProcessedMatchIds.Add(matchId);
            }

            var overflow = settings.ProcessedMatchIds.Count - ProcessedMatchLimit;
            if (overflow > 0)
            {
                settings.ProcessedMatchIds.RemoveRange(0, overflow);
            }

            if (tracked)
            {
                settings.LastTrackedMatchId = matchId;
                settings.LastTrackedAt = DateTimeOffset.UtcNow;
            }
        });
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Json) ?? new AppSettings();
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Settings could not be read, starting with defaults", exception);
        }

        return new AppSettings();
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_settings, Json));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Settings could not be saved", exception);
        }
    }
}
