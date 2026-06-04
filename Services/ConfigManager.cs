using System.IO.Compression;
using System.Text.Json;

namespace TubaWinUi3.Services;

public enum ConfigLocation
{
    AppData,
    AppRoot
}

public static class ConfigManager
{
    private static readonly object _lock = new();
    private static string? _cachedDataDir;

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TubaWinUi3");

    private static readonly string AppRootDir = Path.Combine(
        ToolCatalog.AppDirectory, "Data");

    public static string GetDataDir()
    {
        lock (_lock)
        {
            if (_cachedDataDir is not null) return _cachedDataDir;
            _cachedDataDir = GetConfigLocation() == ConfigLocation.AppRoot ? AppRootDir : AppDataDir;
            return _cachedDataDir;
        }
    }

    public static ConfigLocation GetConfigLocation()
    {
        try
        {
            var markerPath = Path.Combine(AppRootDir, ".config_location");
            if (File.Exists(markerPath)) return ConfigLocation.AppRoot;
        }
        catch { }
        return ConfigLocation.AppData;
    }

    public static void SetConfigLocation(ConfigLocation location)
    {
        try
        {
            if (location == ConfigLocation.AppRoot)
            {
                Directory.CreateDirectory(AppRootDir);
                File.WriteAllText(Path.Combine(AppRootDir, ".config_location"), "AppRoot");
            }
            else
            {
                var markerPath = Path.Combine(AppRootDir, ".config_location");
                if (File.Exists(markerPath)) File.Delete(markerPath);
            }
        }
        catch { }
        lock (_lock) { _cachedDataDir = null; }
    }

    public static string GetSettingsPath() => Path.Combine(GetDataDir(), "settings.json");
    public static string GetFavoritesPath() => Path.Combine(GetDataDir(), "favorites.json");
    public static string GetLaunchHistoryPath() => Path.Combine(GetDataDir(), "launch_history.json");
    public static string GetPopupSettingsPath() => Path.Combine(GetDataDir(), "popup_settings.json");
    public static string GetSensorDumpPath() => Path.Combine(GetDataDir(), "sensor_dump.txt");
    public static string GetSkippedVersionPath() => Path.Combine(GetDataDir(), "skipped_version.txt");
    public static string GetIconCacheDir() => Path.Combine(GetDataDir(), "IconCache");
    public static string GetBackgroundsDir() => Path.Combine(GetDataDir(), "Backgrounds");
    public static string GetMetadataDir() => Path.Combine(GetDataDir(), "Metadata");

    public static string GetDataSize()
    {
        try
        {
            var dir = GetDataDir();
            if (!Directory.Exists(dir)) return "0 B";
            long size = 0;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
            if (size >= 1L << 30) return $"{(double)size / (1L << 30):F2} GB";
            if (size >= 1L << 20) return $"{(double)size / (1L << 20):F1} MB";
            if (size >= 1L << 10) return $"{(double)size / (1L << 10):F1} KB";
            return $"{size} B";
        }
        catch { return "未知"; }
    }

    public static bool MigrateData(ConfigLocation targetLocation, bool deleteOld)
    {
        var sourceDir = GetDataDir();
        var targetDir = targetLocation == ConfigLocation.AppRoot ? AppRootDir : AppDataDir;

        if (string.Equals(sourceDir, targetDir, StringComparison.OrdinalIgnoreCase)) return true;

        try
        {
            if (!Directory.Exists(sourceDir)) { SetConfigLocation(targetLocation); return true; }

            Directory.CreateDirectory(targetDir);

            var excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IconCache", "Metadata" };
            var excludeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sensor_dump.txt" };

            foreach (var file in Directory.EnumerateFiles(sourceDir))
            {
                var name = Path.GetFileName(file);
                if (excludeFiles.Contains(name)) continue;
                var dest = Path.Combine(targetDir, name);
                File.Copy(file, dest, true);
            }

            foreach (var dir in Directory.EnumerateDirectories(sourceDir))
            {
                var name = Path.GetFileName(dir);
                if (excludeDirs.Contains(name)) continue;
                var destDir = Path.Combine(targetDir, name);
                CopyDirectory(dir, destDir);
            }

            SetConfigLocation(targetLocation);

            if (deleteOld)
            {
                try { Directory.Delete(sourceDir, true); } catch { }
            }

            return true;
        }
        catch { return false; }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
    }

    public static async Task<bool> ExportConfigAsync(string outputPath)
    {
        try
        {
            var dataDir = GetDataDir();
            if (!Directory.Exists(dataDir)) return false;

            var excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IconCache", "Metadata" };
            var excludeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sensor_dump.txt" };

            if (File.Exists(outputPath)) File.Delete(outputPath);

            await Task.Run(() =>
            {
                using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);
                foreach (var file in Directory.EnumerateFiles(dataDir))
                {
                    var name = Path.GetFileName(file);
                    if (excludeFiles.Contains(name)) continue;
                    zip.CreateEntryFromFile(file, name, CompressionLevel.Optimal);
                }
                foreach (var dir in Directory.EnumerateDirectories(dataDir))
                {
                    var dirName = Path.GetFileName(dir);
                    if (excludeDirs.Contains(dirName)) continue;
                    foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        var relative = file.Substring(dataDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        zip.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
                    }
                }
            });

            return true;
        }
        catch { return false; }
    }

    public static async Task<bool> ImportConfigAsync(string zipPath)
    {
        try
        {
            var dataDir = GetDataDir();
            Directory.CreateDirectory(dataDir);

            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var entry in archive.Entries)
                {
                    var destPath = Path.Combine(dataDir, entry.FullName);
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destPath);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
                    entry.ExtractToFile(destPath);
                }
            });

            InvalidateAllCaches();
            return true;
        }
        catch { return false; }
    }

    public static void InvalidateAllCaches()
    {
        AppSettings.InvalidateCache();
        FavoritesService.InvalidateCache();
        LaunchHistoryService.InvalidateCache();
    }
}
