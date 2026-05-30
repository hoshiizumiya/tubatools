using Microsoft.UI.Xaml.Media.Imaging;

namespace TubaWinUi3.Services;

public static class BackgroundService
{
    private const string PathKey = "BackgroundImagePath";
    private const string OpacityKey = "BackgroundOpacity";

    public static string? GetBackgroundPath() => AppSettings.Get(PathKey);

    public static void SetBackgroundPath(string? path)
    {
        if (path is not null)
            AppSettings.Set(PathKey, path);
        else
            AppSettings.Remove(PathKey);
    }

    public static double GetBackgroundOpacity() => AppSettings.GetDouble(OpacityKey, 0.15);

    public static void SetBackgroundOpacity(double opacity) => AppSettings.Set(OpacityKey, opacity);

    public static BitmapImage? LoadBackgroundImage()
    {
        var path = GetBackgroundPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            return new BitmapImage(new Uri(path));
        }
        catch { return null; }
    }
}
