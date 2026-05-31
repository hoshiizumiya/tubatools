using System.Text.Json;

namespace TubaWinUi3.Services;

public static class LaunchHistoryService
{
    private const int MaxEntries = 5;
    private static readonly string _historyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TubaWinUi3", "launch_history.json");
    private static List<string>? _cache;

    public static IReadOnlyList<string> GetHistory()
    {
        if (_cache is not null)
            return _cache;

        try
        {
            if (File.Exists(_historyPath))
            {
                var json = File.ReadAllText(_historyPath);
                _cache = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            }
            else
            {
                _cache = [];
            }
        }
        catch
        {
            _cache = [];
        }

        return _cache;
    }

    public static void RecordLaunch(string toolPath)
    {
        var list = GetHistory()
            .Where(p => !p.Equals(toolPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        list.Insert(0, toolPath);

        if (list.Count > MaxEntries)
            list = list.Take(MaxEntries).ToList();

        _cache = list;
        Save(list);
    }

    public static void Clear()
    {
        _cache = [];
        Save([]);
    }

    private static void Save(List<string> history)
    {
        try
        {
            var dir = Path.GetDirectoryName(_historyPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(history);
            File.WriteAllText(_historyPath, json);
        }
        catch { }
    }
}
