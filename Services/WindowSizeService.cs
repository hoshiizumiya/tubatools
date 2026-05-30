using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace TubaWinUi3.Services;

public static class WindowSizeService
{
    private const string WidthKey = "WindowWidth";
    private const string HeightKey = "WindowHeight";
    private const string XKey = "WindowX";
    private const string YKey = "WindowY";
    private const string MaximizedKey = "WindowMaximized";

    public static (int Width, int Height, int? X, int? Y, bool Maximized) LoadWindowSize()
    {
        int width = AppSettings.GetInt(WidthKey, 1200);
        int height = AppSettings.GetInt(HeightKey, 800);
        width = Math.Max(800, width);
        height = Math.Max(600, height);

        int? x = AppSettings.Get(XKey) is string xs && int.TryParse(xs, out var xv) ? xv : null;
        int? y = AppSettings.Get(YKey) is string ys && int.TryParse(ys, out var yv) ? yv : null;
        bool maximized = AppSettings.GetBool(MaximizedKey);

        return (width, height, x, y, maximized);
    }

    public static void SaveWindowSize(MainWindow window)
    {
        if (window is null) return;

        var appWindow = window.AppWindow;
        if (appWindow is null) return;

        var presenter = appWindow.Presenter as OverlappedPresenter;
        var isMaximized = presenter?.State == OverlappedPresenterState.Maximized;

        if (isMaximized)
        {
            AppSettings.Set(MaximizedKey, true);
        }
        else
        {
            AppSettings.Set(MaximizedKey, false);
            AppSettings.Set(WidthKey, appWindow.Size.Width);
            AppSettings.Set(HeightKey, appWindow.Size.Height);
            AppSettings.Set(XKey, appWindow.Position.X);
            AppSettings.Set(YKey, appWindow.Position.Y);
        }
    }

    public static void ApplySavedWindowSize(MainWindow window)
    {
        if (window is null) return;

        var (width, height, x, y, maximized) = LoadWindowSize();
        var appWindow = window.AppWindow;

        appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

        if (x.HasValue && y.HasValue)
        {
            var displayArea = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(x.Value, y.Value), DisplayAreaFallback.Primary);
            if (displayArea is not null)
            {
                var workArea = displayArea.WorkArea;
                bool isVisible = x.Value + width > workArea.X &&
                                 x.Value < workArea.X + workArea.Width &&
                                 y.Value + height > workArea.Y &&
                                 y.Value < workArea.Y + workArea.Height;

                if (isVisible)
                {
                    appWindow.Move(new Windows.Graphics.PointInt32(x.Value, y.Value));
                }
            }
        }

        if (maximized)
        {
            var presenter = appWindow.Presenter as OverlappedPresenter;
            presenter?.Maximize();
        }
    }
}
