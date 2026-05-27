using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Services;

internal static class AppColors
{
    public static readonly Color Transparent = Color.FromArgb(0, 0, 0, 0);
    public static readonly Color DodgerBlue = Color.FromArgb(255, 30, 144, 255);
    public static readonly Color CpuAccent = Color.FromArgb(255, 66, 133, 244);
    public static readonly Color GpuAccent = Color.FromArgb(255, 234, 67, 53);
    public static readonly Color BatAccent = Color.FromArgb(255, 52, 168, 83);
}

public sealed class PowerMonitorTool : IBuiltinTool
{
    public string Id => "power-monitor";
    public string Name => "硬件监测";
    public string Description => "实时显示 CPU/GPU 负载和电池放电功耗，支持弹出悬窗置顶显示。";
    public string Glyph => "\uE945";
    public string Category => "监测工具";
    public BuiltinToolKind Kind => BuiltinToolKind.BackgroundTask;

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var dialog = new ContentDialog
        {
            Title = "硬件监测",
            CloseButtonText = "关闭",
            XamlRoot = context.XamlRoot
        };
        dialog.Resources["ContentDialogMaxWidth"] = 700;
        dialog.Content = BuildDialogContent();
        await dialog.ShowAsync();
    }

    private ScrollViewer BuildDialogContent()
    {
        var monitor = new PowerMonitorService();
        var refreshIntervals = new double[] { 0.5, 1, 2, 5 };

        var cpuLoadText = new TextBlock { FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        var gpuLoadText = new TextBlock { FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        var batteryText = new TextBlock { FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        var cpuNameText = new TextBlock { FontSize = 11, Opacity = 0.68, Text = "CPU 负载" };
        var gpuNameText = new TextBlock { FontSize = 11, Opacity = 0.68, Text = "GPU 负载" };
        var batteryNameText = new TextBlock { FontSize = 11, Opacity = 0.68, Text = "电池放电" };
        var refreshCombo = new ComboBox { MinWidth = 90, SelectedIndex = 1 };
        refreshCombo.Items.Add("0.5秒");
        refreshCombo.Items.Add("1秒");
        refreshCombo.Items.Add("2秒");
        refreshCombo.Items.Add("5秒");
        var popupBtn = new Button { Content = "弹出悬窗", Padding = new Thickness(10, 4, 10, 4) };

        DispatcherTimer? liveTimer = null;
        bool ticking = false;

        void StartTimer()
        {
            StopTimer();
            var interval = refreshIntervals[refreshCombo.SelectedIndex];
            liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            liveTimer.Tick += OnTick;
            liveTimer.Start();
        }

        void StopTimer()
        {
            if (liveTimer is not null) { liveTimer.Tick -= OnTick; liveTimer.Stop(); liveTimer = null; }
        }

        async void OnTick(object? s, object e)
        {
            if (ticking) return;
            ticking = true;
            try
            {
                var sample = await monitor.ReadAsync();
                cpuNameText.Text = sample.CpuName;
                gpuNameText.Text = sample.GpuName;
                cpuLoadText.Text = $"{sample.CpuLoad:0}%";
                gpuLoadText.Text = $"{sample.GpuLoad:0}%";
                if (sample.BatteryPercent >= 0)
                {
                    batteryText.Text = $"{sample.BatteryDischargeWatts:0.0} W";
                    batteryNameText.Text = $"电池放电 ({sample.BatteryPercent}%)";
                }
                else
                {
                    batteryText.Text = "—";
                    batteryNameText.Text = "电池放电（未检测到）";
                }
            }
            finally { ticking = false; }
        }

        double?[] sharedInterval = { null };
        refreshCombo.SelectionChanged += (s, e) =>
        {
            if (liveTimer is not null) StartTimer();
            sharedInterval[0] = refreshIntervals[refreshCombo.SelectedIndex];
        };
        popupBtn.Click += (s, e) => OpenPopupWindow(refreshIntervals, refreshCombo.SelectedIndex, sharedInterval);

        var cardsGrid = new Grid { ColumnSpacing = 10 };
        cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cpuCard = MakeCard(cpuNameText, cpuLoadText, "#4285F4", "\uE950");
        var gpuCard = MakeCard(gpuNameText, gpuLoadText, "#EA4335", "\uE7F4");
        var batCard = MakeCard(batteryNameText, batteryText, "#34A853", "\uE85A");
        cardsGrid.Children.Add(cpuCard); Grid.SetColumn(cpuCard, 0);
        cardsGrid.Children.Add(gpuCard); Grid.SetColumn(gpuCard, 1);
        cardsGrid.Children.Add(batCard); Grid.SetColumn(batCard, 2);

        var bar = new Grid { ColumnSpacing = 10 };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(new TextBlock { Opacity = 0.68, Text = "实时刷新中", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(refreshCombo, 1); bar.Children.Add(refreshCombo);
        Grid.SetColumn(popupBtn, 2); bar.Children.Add(popupBtn);

        var root = new StackPanel { Spacing = 14 };
        root.Children.Add(cardsGrid);
        root.Children.Add(bar);

        StartTimer();

        return new ScrollViewer { Content = root, MaxWidth = 680 };
    }

    private static void OpenPopupWindow(double[] refreshIntervals, int selectedIndex, double?[] sharedInterval)
    {
        const int maxHistory = 60;
        var cpuHistory = new List<float>(maxHistory);
        var gpuHistory = new List<float>(maxHistory);
        var batHistory = new List<float>(maxHistory);

        var monitor = new PowerMonitorService();
        var isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        var fgColor = isDark ? ThemeColors.PrimaryText : Color.FromArgb(255, 30, 30, 30);
        var dimColor = isDark ? ThemeColors.DimText : Color.FromArgb(255, 120, 120, 120);
        var chartBg = isDark ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0);
        var bgColor = isDark ? Color.FromArgb(230, 30, 30, 30) : Color.FromArgb(240, 243, 243, 243);
        var borderColor = isDark ? Color.FromArgb(255, 60, 60, 60) : Color.FromArgb(255, 220, 220, 220);

        var cpuLoadText = new TextBlock { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(fgColor) };
        var gpuLoadText = new TextBlock { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(fgColor) };
        var batLoadText = new TextBlock { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(fgColor) };
        var cpuLabel = new TextBlock { FontSize = 10, Foreground = new SolidColorBrush(dimColor), Text = "CPU" };
        var gpuLabel = new TextBlock { FontSize = 10, Foreground = new SolidColorBrush(dimColor), Text = "GPU" };
        var batLabel = new TextBlock { FontSize = 10, Foreground = new SolidColorBrush(dimColor), Text = "BAT" };

        var cpuChart = new Canvas { Width = 200, Height = 40, Background = new SolidColorBrush(chartBg) };
        var gpuChart = new Canvas { Width = 200, Height = 40, Background = new SolidColorBrush(chartBg) };
        var batChart = new Canvas { Width = 200, Height = 40, Background = new SolidColorBrush(chartBg) };

        var topmostBtn = new Button
        {
            Content = new FontIcon { FontSize = 14, Glyph = "\uE840" },
            Padding = new Thickness(4),
            Background = new SolidColorBrush(AppColors.Transparent),
            Foreground = new SolidColorBrush(dimColor)
        };

        var topRow = new Grid();
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleText = new TextBlock
        {
            Text = "硬件监测",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fgColor),
            VerticalAlignment = VerticalAlignment.Center
        };
        topRow.Children.Add(titleText); Grid.SetColumn(titleText, 0);
        topRow.Children.Add(topmostBtn); Grid.SetColumn(topmostBtn, 1);
        topRow.Padding = new Thickness(0, 0, 46, 0);

        var row1 = MakePopupRow("\uE950", AppColors.CpuAccent, cpuLabel, cpuLoadText, cpuChart);
        var row2 = MakePopupRow("\uE7F4", AppColors.GpuAccent, gpuLabel, gpuLoadText, gpuChart);
        var row3 = MakePopupRow("\uE85A", AppColors.BatAccent, batLabel, batLoadText, batChart);

        var stack = new StackPanel { Spacing = 6, Padding = new Thickness(12, 8, 12, 12) };
        stack.Children.Add(topRow);
        stack.Children.Add(row1);
        stack.Children.Add(row2);
        stack.Children.Add(row3);

        var bg = new Border
        {
            Background = new SolidColorBrush(bgColor),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(1),
            Child = stack
        };

        var page = new Page { Content = bg };
        page.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;

        var window = new Window { Content = page };
        window.AppWindow.Title = "硬件监测";
        window.AppWindow.Resize(new SizeInt32(340, 420));
        window.AppWindow.Move(new PointInt32(100, 100));
        window.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        window.AppWindow.TitleBar.ButtonBackgroundColor = AppColors.Transparent;
        window.AppWindow.TitleBar.ButtonInactiveBackgroundColor = AppColors.Transparent;
        if (isDark)
        {
            window.AppWindow.TitleBar.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            window.AppWindow.TitleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            window.AppWindow.TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 50);
            window.AppWindow.TitleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 180, 180, 180);
            window.AppWindow.TitleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 30, 30, 30);
        }
        else
        {
            window.AppWindow.TitleBar.ButtonForegroundColor = Color.FromArgb(255, 30, 30, 30);
            window.AppWindow.TitleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 30, 30, 30);
            window.AppWindow.TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
            window.AppWindow.TitleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 100, 100, 100);
            window.AppWindow.TitleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);
        }

        var isTopmost = false;
        DispatcherTimer? timer = null;
        bool ticking = false;

        async void PopupTick(object? s, object e)
        {
            if (ticking) return;
            ticking = true;
            try
            {
                var sample = await monitor.ReadAsync();
                cpuLabel.Text = sample.CpuName.Length > 16 ? sample.CpuName[..16] + "…" : sample.CpuName;
                gpuLabel.Text = sample.GpuName.Length > 16 ? sample.GpuName[..16] + "…" : sample.GpuName;
                cpuLoadText.Text = $"{sample.CpuLoad:0}%";
                gpuLoadText.Text = $"{sample.GpuLoad:0}%";

                AddHistory(cpuHistory, sample.CpuLoad, maxHistory);
                AddHistory(gpuHistory, sample.GpuLoad, maxHistory);

                if (sample.BatteryPercent >= 0)
                {
                    batLoadText.Text = $"{sample.BatteryDischargeWatts:0.0}W";
                    batLabel.Text = $"BAT {sample.BatteryPercent}%";
                    AddHistory(batHistory, sample.BatteryDischargeWatts, maxHistory);
                }
                else
                {
                    batLoadText.Text = "—";
                    batLabel.Text = "BAT";
                }

                DrawSparkline(cpuChart, cpuHistory, AppColors.CpuAccent, 0, 100);
                DrawSparkline(gpuChart, gpuHistory, AppColors.GpuAccent, 0, 100);
                if (batHistory.Count > 0) DrawSparkline(batChart, batHistory, AppColors.BatAccent, 0, Math.Max(10, batHistory.Max() * 1.2f));
            }
            finally { ticking = false; }
        }

        topmostBtn.Click += (s, e) =>
        {
            isTopmost = !isTopmost;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var after = isTopmost ? HWND_TOPMOST : HWND_NOTOPMOST;
            SetWindowPos(hwnd, after, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            topmostBtn.Foreground = isTopmost ? new SolidColorBrush(AppColors.DodgerBlue) : new SolidColorBrush(dimColor);
        };

        var interval = refreshIntervals[selectedIndex];
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
        timer.Tick += PopupTick;
        timer.Start();
        PopupTick(null, null);

        var syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        double lastApplied = interval;
        syncTimer.Tick += (s, e) =>
        {
            var ext = sharedInterval[0];
            if (ext.HasValue && Math.Abs(ext.Value - lastApplied) > 0.001)
            {
                lastApplied = ext.Value;
                if (timer is not null) { timer.Tick -= PopupTick; timer.Stop(); }
                timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(lastApplied) };
                timer.Tick += PopupTick;
                timer.Start();
            }
        };
        syncTimer.Start();

        window.Closed += (s, e) =>
        {
            syncTimer.Stop();
            sharedInterval[0] = null;
            if (timer is not null) { timer.Tick -= PopupTick; timer.Stop(); }
        };
        window.Activate();

        HideSystemButtons(window);
    }

    private static void HideSystemButtons(Window window)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var style = GetWindowLongPtr(hwnd, GWL_STYLE);
        style = new IntPtr(style.ToInt64() & ~(WS_MINIMIZEBOX | WS_MAXIMIZEBOX));
        SetWindowLongPtr(hwnd, GWL_STYLE, style);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    private static void AddHistory(List<float> history, float value, int max)
    {
        history.Add(value);
        if (history.Count > max) history.RemoveAt(0);
    }

    private static void DrawSparkline(Canvas canvas, List<float> data, Color color, float minVal, float maxVal)
    {
        canvas.Children.Clear();
        if (data.Count < 2) return;

        var w = canvas.Width;
        var h = canvas.Height;
        var range = Math.Max(maxVal - minVal, 0.001f);
        var step = w / Math.Max(data.Count - 1, 1);

        var pc = new PointCollection();
        for (int i = 0; i < data.Count; i++)
        {
            var x = i * step;
            var y = h - ((data[i] - minVal) / range) * h;
            y = Math.Max(0, Math.Min(h, y));
            pc.Add(new Point(x, y));
        }

        if (pc.Count >= 2)
        {
            var line = new Polyline
            {
                Points = pc,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1.5
            };
            canvas.Children.Add(line);
        }

        var fc = new PointCollection();
        for (int i = 0; i < data.Count; i++)
        {
            var x = i * step;
            var y = h - ((data[i] - minVal) / range) * h;
            y = Math.Max(0, Math.Min(h, y));
            fc.Add(new Point(x, y));
        }
        fc.Add(new Point((data.Count - 1) * step, h));
        fc.Add(new Point(0, h));
        var fill = new Polygon
        {
            Points = fc,
            Fill = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B))
        };
        canvas.Children.Add(fill);
    }

    private static StackPanel MakePopupRow(string glyph, Color accent, TextBlock label, TextBlock value, Canvas chart)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        header.Children.Add(new FontIcon { FontSize = 12, Foreground = new SolidColorBrush(accent), Glyph = glyph });
        header.Children.Add(label);
        header.Children.Add(value);

        var row = new StackPanel { Spacing = 2 };
        row.Children.Add(header);
        row.Children.Add(chart);
        return row;
    }

    private static SolidColorBrush MakeBrush(string hexColor)
    {
        var r = Convert.ToByte(hexColor[1..3], 16);
        var g = Convert.ToByte(hexColor[3..5], 16);
        var b = Convert.ToByte(hexColor[5..7], 16);
        return new SolidColorBrush(Color.FromArgb(255, r, g, b));
    }

    private static Border MakeCard(TextBlock label, TextBlock value, string accentColor, string glyph)
    {
        var accent = Color.FromArgb(255, Convert.ToByte(accentColor[1..3], 16), Convert.ToByte(accentColor[3..5], 16), Convert.ToByte(accentColor[5..7], 16));
        var bg = Color.FromArgb(26, accent.R, accent.G, accent.B);
        var iconBorder = new Border { Width = 36, Height = 36, Background = new SolidColorBrush(bg), CornerRadius = new CornerRadius(6), Child = new FontIcon { FontSize = 18, Foreground = new SolidColorBrush(accent), Glyph = glyph } };
        var stack = new StackPanel { Spacing = 2 }; stack.Children.Add(label); stack.Children.Add(value);
        var grid = new Grid { ColumnSpacing = 10 }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.Children.Add(iconBorder); Grid.SetColumn(stack, 1); grid.Children.Add(stack);
        return new Border { Padding = new Thickness(12), BorderBrush = new SolidColorBrush(ThemeColors.BorderColor), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = grid };
    }

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const int GWL_STYLE = -16;
    private const long WS_MINIMIZEBOX = 0x00020000L;
    private const long WS_MAXIMIZEBOX = 0x00010000L;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
