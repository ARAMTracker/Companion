using System.Text.Json;
using AramTrackerCompanion.Api;
using AramTrackerCompanion.Settings;

namespace AramTrackerCompanion.Tracking;

internal sealed class Outbox
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<MatchSubmission> _matches;

    public const string WaitingFile = "waiting.json";

    public Outbox(string directory, string fileName = "outbox.json")
    {
        _path = Path.Combine(directory, fileName);
        _matches = Load();
    }

    public IReadOnlyList<string> RemoveStartedBefore(DateTimeOffset cutoff)
    {
        var limit = cutoff.ToUnixTimeMilliseconds();

        lock (_gate)
        {
            var expired = _matches.Where(m => m.GameStart < limit).Select(m => m.MatchId).ToList();

            if (expired.Count > 0)
            {
                _matches.RemoveAll(m => m.GameStart < limit);
                Save();
            }

            return expired;
        }
    }

    public IReadOnlyList<MatchSubmission> TakeAll()
    {
        lock (_gate)
        {
            var all = _matches.ToList();

            if (all.Count > 0)
            {
                _matches.Clear();
                Save();
            }

            return all;
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _matches.Count;
            }
        }
    }

    public bool Add(MatchSubmission match)
    {
        lock (_gate)
        {
            if (_matches.Any(m => m.MatchId == match.MatchId))
            {
                return false;
            }

            _matches.Add(match);
            Save();
            return true;
        }
    }

    public IReadOnlyList<MatchSubmission> Peek(int count)
    {
        lock (_gate)
        {
            return _matches.Take(count).ToList();
        }
    }

    public void Remove(IEnumerable<string> matchIds)
    {
        var ids = matchIds.ToHashSet();

        lock (_gate)
        {
            if (_matches.RemoveAll(m => ids.Contains(m.MatchId)) > 0)
            {
                Save();
            }
        }
    }

    private List<MatchSubmission> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<List<MatchSubmission>>(File.ReadAllText(_path), AramTrackerApiService.Json) ?? [];
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Pending uploads could not be read", exception);
        }

        return [];
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_matches, AramTrackerApiService.Json));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Pending uploads could not be saved", exception);
        }
    }
}
