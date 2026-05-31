using System.Text.Json;

namespace TubaWinUi3.Services;

public static class FavoritesService
{
    private const string Key = "FavoriteTools";
    private static readonly string _favoritesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TubaWinUi3", "favorites.json");
    private static List<string>? _cache;

    public static IReadOnlyList<string> GetFavorites()
    {
        if (_cache is not null)
            return _cache;

        try
        {
            if (File.Exists(_favoritesPath))
            {
                var json = File.ReadAllText(_favoritesPath);
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

    public static bool IsFavorite(string toolPath)
    {
        return GetFavorites().Contains(toolPath, StringComparer.OrdinalIgnoreCase);
    }

    public static void AddFavorite(string toolPath)
    {
        var list = GetFavorites()
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        if (list.Contains(toolPath, StringComparer.OrdinalIgnoreCase))
            return;

        list.Add(toolPath);
        _cache = list;
        Save(list);
    }

    public static void RemoveFavorite(string toolPath)
    {
        var list = GetFavorites()
            .Where(p => !p.Equals(toolPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _cache = list;
        Save(list);
    }

    public static void RemoveAll()
    {
        _cache = [];
        Save([]);
    }

    public static void ToggleFavorite(string toolPath)
    {
        if (IsFavorite(toolPath))
            RemoveFavorite(toolPath);
        else
            AddFavorite(toolPath);
    }

    private static void Save(List<string> favorites)
    {
        try
        {
            var dir = Path.GetDirectoryName(_favoritesPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(favorites);
            File.WriteAllText(_favoritesPath, json);
        }
        catch { }
    }
}
