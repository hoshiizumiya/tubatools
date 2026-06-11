using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TubaWinUi3.Services;
using Windows.Foundation;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed class BatteryAnalyzerPage : Page
{
    private readonly Window _window;
    private DispatcherTimer? _realtimeTimer;
    private DispatcherTimer? _chartAnimTimer;
    private CancellationTokenSource? _loadCts;

    private List<BatteryTrendPoint> _trendData = [];
    private List<BatteryTrendPoint> _visibleTrendData = [];
    private List<ProcessPowerEntry> _processData = [];
    private BatteryInfo? _batteryInfo;
    private BatteryRealtimeStatus? _lastRealtime;
    private readonly List<double> _powerHistory = [];
    private int _selectedDays = 7;
    private int _chartAnimProgress;
    private bool _isReloading;

    private TextBlock _powerValueText = null!;
    private Canvas _powerSparkline = null!;
    private TextBlock _chargeValueText = null!;
    private ProgressBar _chargeBar = null!;
    private TextBlock _timeValueText = null!;
    private TextBlock _healthValueText = null!;
    private ProgressBar _healthBar = null!;
    private TextBlock _healthStatusText = null!;

    private ComboBox _timeRangeCombo = null!;
    private Canvas _trendChart = null!;
    private Canvas _trendOverlay = null!;
    private Border _chartTooltip = null!;
    private TextBlock _chartTooltipText = null!;
    private ProgressBar _chartLoading = null!;

    private StackPanel _processList = null!;
    private TextBlock _processStatusText = null!;

    private TextBlock _designCapText = null!;
    private TextBlock _fullCapText = null!;
    private TextBlock _cycleCountText = null!;
    private TextBlock _manufacturerText = null!;
    private TextBlock _manufactureDateText = null!;
    private TextBlock _voltageText = null!;
    private TextBlock _uniqueIdText = null!;
    private TextBlock _batteryTypeText = null!;

    private InfoBar _infoBar = null!;

    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);
    private static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color AccentOrange = Color.FromArgb(255, 251, 191, 36);
    private static readonly Color AccentRed = Color.FromArgb(255, 248, 113, 113);
    private static readonly Color AccentPurple = Color.FromArgb(255, 167, 139, 250);
    private static readonly Color ChartGreen = Color.FromArgb(255, 52, 211, 153);
    private static readonly Color ChartBlue = Color.FromArgb(255, 96, 165, 250);

    public BatteryAnalyzerPage(Window window)
    {
        _window = window;
        _window.Closed += OnWindowClosed;
        Content = BuildUI();
        _ = LoadAllDataAsync();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _loadCts?.Cancel();
        _realtimeTimer?.Stop();
        _chartAnimTimer?.Stop();
    }

    private ScrollViewer BuildUI()
    {
        var mainStack = new StackPanel { Spacing = 16, Padding = new Thickness(28, 48, 28, 20) };

        mainStack.Children.Add(BuildHeader());

        _chartLoading = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed };
        _infoBar = new InfoBar { Severity = InfoBarSeverity.Error, IsOpen = false, IsClosable = true };

        mainStack.Children.Add(_chartLoading);
        mainStack.Children.Add(_infoBar);
        mainStack.Children.Add(BuildOverviewCards());
        mainStack.Children.Add(BuildTrendSection());
        mainStack.Children.Add(BuildProcessSection());
        mainStack.Children.Add(BuildDetailsSection());

        return new ScrollViewer { Content = mainStack };
    }

    private StackPanel BuildHeader()
    {
        var title = new TextBlock
        {
            Text = "电池消耗分析",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };
        var subtitle = new TextBlock
        {
            Text = "分析电池消耗趋势、高耗电进程排行，比 Windows 设置更强大的电池分析工具",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            Margin = new Thickness(0, 4, 0, 0)
        };

        var refreshBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 13 },
                    new TextBlock { Text = "刷新", FontSize = 13 }
                }
            },
            Background = new SolidColorBrush(ThemeColors.SubtleBg),
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            Padding = new Thickness(14, 6, 14, 6),
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center
        };
        refreshBtn.Click += async (_, _) => await ReloadTrendAsync();

        var exportBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE8A5", FontSize = 13 },
                    new TextBlock { Text = "导出报告", FontSize = 13 }
                }
            },
            Background = new SolidColorBrush(ThemeColors.SubtleBg),
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            Padding = new Thickness(14, 6, 14, 6),
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center
        };
        exportBtn.Click += async (_, _) => await ExportReportAsync();

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        btnPanel.Children.Add(refreshBtn);
        btnPanel.Children.Add(exportBtn);

        var headerGrid = new Grid { ColumnSpacing = 12 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(new StackPanel { Spacing = 2, Children = { title, subtitle } });
        headerGrid.Children.Add(btnPanel);
        Grid.SetColumn(btnPanel, 1);

        return new StackPanel { Spacing = 10, Children = { headerGrid } };
    }

    private Grid BuildOverviewCards()
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var powerCard = BuildStatCard("实时功率", "-- W", "\uE946", AccentPurple, out _powerValueText, out _powerSparkline);
        grid.Children.Add(powerCard);

        var chargeCard = BuildStatCardWithBar("当前电量", "--%", "\uE85A", AccentBlue, out _chargeValueText, out _chargeBar);
        grid.Children.Add(chargeCard);
        Grid.SetColumn(chargeCard, 1);

        _timeValueText = new TextBlock
        {
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(AccentGreen)
        };
        var timeIconBorder = new Border
        {
            Width = 36, Height = 36,
            Background = new SolidColorBrush(Color.FromArgb(26, AccentGreen.R, AccentGreen.G, AccentGreen.B)),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(AccentGreen), Glyph = "\uE823" }
        };
        var timeLabel = new TextBlock { Text = "预估时间", FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var timeStack = new StackPanel { Spacing = 2 };
        timeStack.Children.Add(timeLabel);
        timeStack.Children.Add(_timeValueText);
        var timeInnerGrid = new Grid { ColumnSpacing = 10 };
        timeInnerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        timeInnerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeInnerGrid.Children.Add(timeIconBorder);
        timeInnerGrid.Children.Add(timeStack);
        Grid.SetColumn(timeStack, 1);
        var timeCard = MakeCardBorder(timeInnerGrid);
        grid.Children.Add(timeCard);
        Grid.SetColumn(timeCard, 2);

        _healthValueText = new TextBlock
        {
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(AccentGreen)
        };
        _healthStatusText = new TextBlock
        {
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(AccentGreen)
        };
        _healthBar = new ProgressBar { Minimum = 0, Maximum = 100, HorizontalAlignment = HorizontalAlignment.Stretch };
        var healthIconBorder = new Border
        {
            Width = 36, Height = 36,
            Background = new SolidColorBrush(Color.FromArgb(26, AccentGreen.R, AccentGreen.G, AccentGreen.B)),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(AccentGreen), Glyph = "\uE95E" }
        };
        var healthLabel = new TextBlock { Text = "电池健康", FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var healthStack = new StackPanel { Spacing = 2 };
        healthStack.Children.Add(healthLabel);
        healthStack.Children.Add(_healthValueText);
        healthStack.Children.Add(_healthStatusText);
        healthStack.Children.Add(_healthBar);
        var healthInnerGrid = new Grid { ColumnSpacing = 10 };
        healthInnerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        healthInnerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        healthInnerGrid.Children.Add(healthIconBorder);
        healthInnerGrid.Children.Add(healthStack);
        Grid.SetColumn(healthStack, 1);
        var healthCard = MakeCardBorder(healthInnerGrid);
        grid.Children.Add(healthCard);
        Grid.SetColumn(healthCard, 3);

        return grid;
    }

    private Border BuildStatCard(string label, string initial, string glyph, Color accent, out TextBlock valueText, out Canvas sparkline)
    {
        var iconBorder = new Border
        {
            Width = 36, Height = 36,
            Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(accent), Glyph = glyph }
        };
        valueText = new TextBlock
        {
            Text = initial,
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(accent)
        };
        var labelBlock = new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        sparkline = new Canvas { Width = 120, Height = 30, HorizontalAlignment = HorizontalAlignment.Left };

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(labelBlock);
        stack.Children.Add(valueText);
        stack.Children.Add(sparkline);

        var innerGrid = new Grid { ColumnSpacing = 10 };
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        innerGrid.Children.Add(iconBorder);
        innerGrid.Children.Add(stack);
        Grid.SetColumn(stack, 1);

        return MakeCardBorder(innerGrid);
    }

    private Border BuildStatCardWithBar(string label, string initial, string glyph, Color accent, out TextBlock valueText, out ProgressBar bar)
    {
        var iconBorder = new Border
        {
            Width = 36, Height = 36,
            Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(accent), Glyph = glyph }
        };
        valueText = new TextBlock
        {
            Text = initial,
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(accent)
        };
        bar = new ProgressBar { Minimum = 0, Maximum = 100, HorizontalAlignment = HorizontalAlignment.Stretch };
        var labelBlock = new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) };

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(labelBlock);
        stack.Children.Add(valueText);
        stack.Children.Add(bar);

        var innerGrid = new Grid { ColumnSpacing = 10 };
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        innerGrid.Children.Add(iconBorder);
        innerGrid.Children.Add(stack);
        Grid.SetColumn(stack, 1);

        return MakeCardBorder(innerGrid);
    }

    private StackPanel BuildTrendSection()
    {
        var label = new TextBlock
        {
            Text = "电量变化趋势",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };

        _timeRangeCombo = new ComboBox
        {
            MinWidth = 100,
            SelectedIndex = 2,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _timeRangeCombo.Items.Add("最近 1 天");
        _timeRangeCombo.Items.Add("最近 3 天");
        _timeRangeCombo.Items.Add("最近 7 天");
        _timeRangeCombo.Items.Add("最近 14 天");
        _timeRangeCombo.SelectionChanged += async (_, _) =>
        {
            var days = new[] { 1, 3, 7, 14 };
            _selectedDays = days[_timeRangeCombo.SelectedIndex];
            await ReloadTrendAsync();
        };

        var headerGrid = new Grid { ColumnSpacing = 12 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(label);
        headerGrid.Children.Add(_timeRangeCombo);
        Grid.SetColumn(_timeRangeCombo, 1);

        var legendAc = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        legendAc.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(ChartGreen) });
        legendAc.Children.Add(new TextBlock { Text = "充电", FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) });
        var legendDc = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        legendDc.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(ChartBlue) });
        legendDc.Children.Add(new TextBlock { Text = "放电", FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) });
        var legendPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 4, 0, 0) };
        legendPanel.Children.Add(legendAc);
        legendPanel.Children.Add(legendDc);

        _chartTooltipText = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.PrimaryText) };
        _chartTooltip = new Border
        {
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Child = _chartTooltipText,
            Visibility = Visibility.Collapsed
        };

        _trendOverlay = new Canvas { IsHitTestVisible = false };
        _trendOverlay.Children.Add(_chartTooltip);

        _trendChart = new Canvas { Background = new SolidColorBrush(ThemeColors.CardBg) };
        _trendChart.SizeChanged += (_, _) => DrawTrendChart();
        _trendChart.PointerMoved += OnChartPointerMoved;
        _trendChart.PointerExited += OnChartPointerExited;

        var chartWrapper = new Grid();
        chartWrapper.Children.Add(_trendChart);
        chartWrapper.Children.Add(_trendOverlay);

        var chartBorder = new Border
        {
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(50, 24, 16, 36),
            Height = 280,
            Child = chartWrapper
        };

        var section = new StackPanel { Spacing = 10 };
        section.Children.Add(headerGrid);
        section.Children.Add(legendPanel);
        section.Children.Add(chartBorder);
        return section;
    }

    private StackPanel BuildProcessSection()
    {
        var label = new TextBlock
        {
            Text = "高耗电进程排行",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };

        _processStatusText = new TextBlock
        {
            Text = "采样中...",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var headerGrid = new Grid { ColumnSpacing = 12 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(label);
        headerGrid.Children.Add(_processStatusText);
        Grid.SetColumn(_processStatusText, 1);

        _processList = new StackPanel { Spacing = 2 };

        var listBorder = new Border
        {
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = _processList
        };

        var section = new StackPanel { Spacing = 10 };
        section.Children.Add(headerGrid);
        section.Children.Add(listBorder);
        return section;
    }

    private StackPanel BuildDetailsSection()
    {
        var label = new TextBlock
        {
            Text = "电池详细信息",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };

        _designCapText = MakeDetailValue();
        _fullCapText = MakeDetailValue();
        _cycleCountText = MakeDetailValue();
        _manufacturerText = MakeDetailValue();
        _manufactureDateText = MakeDetailValue();
        _voltageText = MakeDetailValue();
        _uniqueIdText = MakeDetailValue();
        _batteryTypeText = MakeDetailValue();

        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddDetailRow(grid, 0, "设计容量", _designCapText, "\uEDA2", "充满容量", _fullCapText, "\uEDA2");
        AddDetailRow(grid, 1, "循环次数", _cycleCountText, "\uE8C8", "制造商", _manufacturerText, "\uE7F4");
        AddDetailRow(grid, 2, "制造日期", _manufactureDateText, "\uE787", "当前电压", _voltageText, "\uE946");
        AddDetailRow(grid, 3, "序列号", _uniqueIdText, "\uE945", "电池类型", _batteryTypeText, "\uE85A");

        var detailBorder = new Border
        {
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = grid
        };

        var section = new StackPanel { Spacing = 10 };
        section.Children.Add(label);
        section.Children.Add(detailBorder);
        return section;
    }

    private static TextBlock MakeDetailValue() => new() { FontSize = 14, Foreground = new SolidColorBrush(ThemeColors.PrimaryText) };

    private static void AddDetailRow(Grid grid, int row, string label1, TextBlock value1, string glyph1, string label2, TextBlock value2, string glyph2)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var left = MakeDetailCell(label1, value1, glyph1);
        var right = MakeDetailCell(label2, value2, glyph2);
        grid.Children.Add(left); Grid.SetRow(left, row); Grid.SetColumn(left, 0);
        grid.Children.Add(right); Grid.SetRow(right, row); Grid.SetColumn(right, 1);
    }

    private static Border MakeDetailCell(string label, TextBlock value, string glyph)
    {
        var icon = new FontIcon { FontSize = 14, Glyph = glyph, Foreground = new SolidColorBrush(ThemeColors.DimText), VerticalAlignment = VerticalAlignment.Center };
        var labelBlock = new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText), VerticalAlignment = VerticalAlignment.Center };

        var inner = new Grid { ColumnSpacing = 8 };
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.Children.Add(icon);
        inner.Children.Add(labelBlock); Grid.SetColumn(labelBlock, 1);
        inner.Children.Add(value); Grid.SetColumn(value, 2);

        return new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(ThemeColors.SubtleBg),
            CornerRadius = new CornerRadius(6),
            Child = inner
        };
    }

    private static Border MakeCardBorder(UIElement child) => new()
    {
        Padding = new Thickness(14),
        Background = new SolidColorBrush(ThemeColors.CardBg),
        BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Child = child
    };

    private async Task LoadAllDataAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        _chartLoading.Visibility = Visibility.Visible;
        _chartLoading.IsIndeterminate = true;

        var batteryTask = BatteryReportService.GetBatteryInfoAsync();
        var trendTask = BatteryAnalyzerService.GetTrendAsync(_selectedDays);

        await Task.WhenAll(batteryTask, trendTask);

        if (ct.IsCancellationRequested) return;

        _batteryInfo = batteryTask.Result;
        _trendData = trendTask.Result;

        DownsampleTrend();

        _chartLoading.Visibility = Visibility.Collapsed;
        _chartLoading.IsIndeterminate = false;

        UpdateBatteryInfoUI();
        AnimateTrendChart();

        StartRealtimeTimer();
    }

    private async Task ReloadTrendAsync()
    {
        if (_isReloading) return;
        _isReloading = true;
        _chartLoading.Visibility = Visibility.Visible;
        _chartLoading.IsIndeterminate = true;

        try
        {
            _trendData = await BatteryAnalyzerService.GetTrendAsync(_selectedDays);
            DownsampleTrend();
            AnimateTrendChart();
        }
        catch { }

        _chartLoading.Visibility = Visibility.Collapsed;
        _chartLoading.IsIndeterminate = false;
        _isReloading = false;
    }

    private void DownsampleTrend()
    {
        const int maxPoints = 500;
        if (_trendData.Count <= maxPoints)
        {
            _visibleTrendData = _trendData;
            return;
        }
        var step = (double)_trendData.Count / maxPoints;
        var result = new List<BatteryTrendPoint>();
        for (var i = 0.0; i < _trendData.Count; i += step)
        {
            var idx = (int)i;
            if (idx < _trendData.Count) result.Add(_trendData[idx]);
        }
        _visibleTrendData = result;
    }

    private void UpdateBatteryInfoUI()
    {
        var info = _batteryInfo;
        if (info is null || !info.BatteryPresent) return;

        var healthColor = info.HealthPercent >= 80 ? AccentGreen : info.HealthPercent >= 60 ? AccentOrange : AccentRed;

        _healthValueText.Text = $"{info.HealthPercent}%";
        _healthValueText.Foreground = new SolidColorBrush(healthColor);
        _healthBar.Value = info.HealthPercent;
        _healthBar.Foreground = new SolidColorBrush(healthColor);
        _healthStatusText.Text = info.HealthStatus;
        _healthStatusText.Foreground = new SolidColorBrush(healthColor);

        _chargeValueText.Text = $"{info.EstimatedChargeRemaining}%";
        _chargeBar.Value = info.EstimatedChargeRemaining;

        _designCapText.Text = info.DesignedCapacityText;
        _fullCapText.Text = info.FullChargedCapacityText;
        _cycleCountText.Text = info.CycleCount > 0 ? info.CycleCount.ToString() : "未知";
        _manufacturerText.Text = string.IsNullOrEmpty(info.ManufactureName) ? "未知" : info.ManufactureName;
        _manufactureDateText.Text = string.IsNullOrEmpty(info.ManufactureDate) ? "未知" : info.ManufactureDate;
        _uniqueIdText.Text = string.IsNullOrEmpty(info.UniqueId) ? "未知" : info.UniqueId;
    }

    private void StartRealtimeTimer()
    {
        _realtimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _realtimeTimer.Tick += async (_, _) =>
        {
            try
            {
                var statusTask = BatteryAnalyzerService.GetRealtimeStatusAsync();
                var processTask = BatteryAnalyzerService.GetTopProcessesAsync(12);

                await Task.WhenAll(statusTask, processTask);

                var status = statusTask.Result;
                if (status != null)
                {
                    _lastRealtime = status;
                    DispatcherQueue.TryEnqueue(() => UpdateRealtimeUI(status));
                }

                var procs = processTask.Result;
                if (procs.Count > 0)
                {
                    _processData = procs;
                    DispatcherQueue.TryEnqueue(() => UpdateProcessUI());
                }
            }
            catch { }
        };
        _realtimeTimer.Start();

        _ = BatteryAnalyzerService.GetTopProcessesAsync(12);
        _ = UpdateRealtimeOnceAsync();
    }

    private async Task UpdateRealtimeOnceAsync()
    {
        var status = await BatteryAnalyzerService.GetRealtimeStatusAsync();
        if (status != null)
        {
            _lastRealtime = status;
            DispatcherQueue.TryEnqueue(() => UpdateRealtimeUI(status));
        }
    }

    private void UpdateRealtimeUI(BatteryRealtimeStatus s)
    {
        if (s.PowerOnline && !s.Discharging)
        {
            _powerValueText.Text = s.Charging ? $"+{s.ChargeRateMw / 1000.0:F1} W" : "已接电源";
            _powerValueText.Foreground = new SolidColorBrush(AccentGreen);
            _timeValueText.Text = s.Charging ? "充电中" : "交流电源";
            _timeValueText.Foreground = new SolidColorBrush(AccentGreen);
        }
        else
        {
            var watts = s.DischargeRateMw / 1000.0;
            _powerValueText.Text = $"{watts:F1} W";
            _powerValueText.Foreground = new SolidColorBrush(AccentPurple);

            if (s.RemainingCapacityMwh > 0 && s.DischargeRateMw > 0)
            {
                var remainHours = (double)s.RemainingCapacityMwh / s.DischargeRateMw;
                var h = (int)remainHours;
                var m = (int)((remainHours - h) * 60);
                _timeValueText.Text = $"{h}h {m}m";
            }
            else
            {
                _timeValueText.Text = "计算中...";
            }
            _timeValueText.Foreground = new SolidColorBrush(AccentGreen);
        }

        if (_batteryInfo != null && _batteryInfo.BatteryPresent)
        {
            _chargeValueText.Text = $"{_batteryInfo.EstimatedChargeRemaining}%";
            _chargeBar.Value = _batteryInfo.EstimatedChargeRemaining;
        }

        _voltageText.Text = s.VoltageMv > 0 ? $"{s.VoltageMv / 1000.0:F2} V" : "未知";

        _powerHistory.Add(s.DischargeRateMw / 1000.0);
        if (_powerHistory.Count > 30) _powerHistory.RemoveAt(0);
        DrawPowerSparkline();
    }

    private void DrawPowerSparkline()
    {
        var c = _powerSparkline;
        c.Children.Clear();
        if (_powerHistory.Count < 2) return;

        var w = c.ActualWidth > 0 ? c.ActualWidth : c.Width;
        var h = c.ActualHeight > 0 ? c.ActualHeight : c.Height;
        if (w <= 0 || h <= 0) return;

        var max = _powerHistory.Max();
        var min = _powerHistory.Min();
        var range = Math.Max(max - min, 0.1);
        var step = w / (_powerHistory.Count - 1);

        var pc = new PointCollection();
        var fc = new PointCollection();
        for (var i = 0; i < _powerHistory.Count; i++)
        {
            var x = i * step;
            var y = h - ((_powerHistory[i] - min) / range) * (h - 4) - 2;
            pc.Add(new Point(x, y));
            fc.Add(new Point(x, y));
        }
        fc.Add(new Point((_powerHistory.Count - 1) * step, h));
        fc.Add(new Point(0, h));

        c.Children.Add(new Polygon { Points = fc, Fill = new SolidColorBrush(Color.FromArgb(30, AccentPurple.R, AccentPurple.G, AccentPurple.B)) });
        c.Children.Add(new Polyline { Points = pc, Stroke = new SolidColorBrush(AccentPurple), StrokeThickness = 1.5 });
    }

    private void AnimateTrendChart()
    {
        _chartAnimTimer?.Stop();
        _chartAnimProgress = 0;

        var totalSteps = FastModeService.IsFastModeEnabled() ? 1 : 20;
        _chartAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        _chartAnimTimer.Tick += (_, _) =>
        {
            _chartAnimProgress++;
            DrawTrendChart();
            if (_chartAnimProgress >= totalSteps)
            {
                _chartAnimTimer.Stop();
                _chartAnimProgress = totalSteps;
                DrawTrendChart();
            }
        };
        _chartAnimTimer.Start();
    }

    private void DrawTrendChart()
    {
        var c = _trendChart;
        c.Children.Clear();
        if (_visibleTrendData.Count < 2) return;

        var W = c.ActualWidth;
        var H = c.ActualHeight;
        if (W <= 0 || H <= 0) return;

        var padL = 0.0;
        var padR = 0.0;
        var padT = 8.0;
        var padB = 8.0;
        var chartW = W - padL - padR;
        var chartH = H - padT - padB;

        for (var pct = 0; pct <= 100; pct += 25)
        {
            var y = padT + chartH * (1 - pct / 100.0);
            c.Children.Add(new Line
            {
                X1 = padL, Y1 = y, X2 = W - padR, Y2 = y,
                Stroke = new SolidColorBrush(ThemeColors.BorderColor),
                StrokeThickness = 0.5,
                StrokeDashArray = new DoubleCollection { 4, 4 }
            });
            c.Children.Add(new TextBlock
            {
                Text = $"{pct}%",
                FontSize = 10,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                RenderTransform = new TranslateTransform { X = -42, Y = y - 7 }
            });
        }

        var totalSteps = FastModeService.IsFastModeEnabled() ? 1 : 20;
        var animFrac = Math.Min(1.0, (double)_chartAnimProgress / totalSteps);
        var visibleCount = Math.Max(2, (int)(_visibleTrendData.Count * animFrac));
        var data = _visibleTrendData.Take(visibleCount).ToList();

        var stepX = chartW / (data.Count - 1);

        for (var i = 1; i < data.Count; i++)
        {
            var prev = data[i - 1];
            var curr = data[i];
            var x1 = padL + (i - 1) * stepX;
            var x2 = padL + i * stepX;
            var y1 = padT + chartH * (1 - prev.ChargePercent / 100.0);
            var y2 = padT + chartH * (1 - curr.ChargePercent / 100.0);

            var color = prev.OnAcPower ? ChartGreen : ChartBlue;
            c.Children.Add(new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 2
            });
        }

        var fillPoints = new PointCollection();
        for (var i = 0; i < data.Count; i++)
        {
            var x = padL + i * stepX;
            var y = padT + chartH * (1 - data[i].ChargePercent / 100.0);
            fillPoints.Add(new Point(x, y));
        }
        var lastX = padL + (data.Count - 1) * stepX;
        fillPoints.Add(new Point(lastX, padT + chartH));
        fillPoints.Add(new Point(padL, padT + chartH));

        c.Children.Add(new Polygon
        {
            Points = fillPoints,
            Fill = new SolidColorBrush(Color.FromArgb(18, 96, 165, 250))
        });

        var timeAxisCount = Math.Min(8, data.Count);
        var timeStep = Math.Max(1, data.Count / timeAxisCount);
        for (var i = 0; i < data.Count; i += timeStep)
        {
            var x = padL + i * stepX;
            var ts = data[i].Timestamp;
            var label = _selectedDays <= 1 ? ts.ToString("HH:mm") : ts.ToString("M/d HH:mm");
            c.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 9,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                RenderTransform = new TranslateTransform { X = x - 20, Y = padT + chartH + 4 }
            });
        }

        if (data.Count > 0 && animFrac >= 1.0)
        {
            var last = data[^1];
            var lx = padL + (data.Count - 1) * stepX;
            var ly = padT + chartH * (1 - last.ChargePercent / 100.0);
            c.Children.Add(new Ellipse
            {
                Width = 8, Height = 8,
                Fill = new SolidColorBrush(last.OnAcPower ? ChartGreen : ChartBlue),
                RenderTransform = new TranslateTransform { X = lx - 4, Y = ly - 4 }
            });
        }
    }

    private void OnChartPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_visibleTrendData.Count < 2) return;
        var pos = e.GetCurrentPoint(_trendChart).Position;
        var W = _trendChart.ActualWidth;
        var H = _trendChart.ActualHeight;
        if (W <= 0 || H <= 0) return;

        var padL = 0.0;
        var padT = 8.0;
        var chartW = W;
        var chartH = H - padT - 8;

        var stepX = chartW / (_visibleTrendData.Count - 1);
        var idx = (int)((pos.X - padL) / stepX);
        if (idx < 0 || idx >= _visibleTrendData.Count)
        {
            _chartTooltip.Visibility = Visibility.Collapsed;
            return;
        }

        var point = _visibleTrendData[idx];
        var px = padL + idx * stepX;
        var py = padT + chartH * (1 - point.ChargePercent / 100.0);

        _chartTooltipText.Text = $"{point.Timestamp:yyyy/M/d HH:mm}\n电量 {point.ChargePercent}% · {(point.OnAcPower ? "充电" : "放电")} · {point.CapacityMwh} mWh";
        _chartTooltip.Visibility = Visibility.Visible;

        var tipX = Math.Max(4, Math.Min(px + 14, W - 200));
        var tipY = Math.Max(4, Math.Min(py - 40, H - 60));
        Canvas.SetLeft(_chartTooltip, tipX);
        Canvas.SetTop(_chartTooltip, tipY);
    }

    private void OnChartPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _chartTooltip.Visibility = Visibility.Collapsed;
    }

    private void UpdateProcessUI()
    {
        _processList.Children.Clear();

        if (_processData.Count == 0)
        {
            _processStatusText.Text = "采样中，请稍候...";
            var tip = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 8,
                Padding = new Thickness(0, 16, 0, 16)
            };
            tip.Children.Add(new ProgressRing { Width = 28, Height = 28, IsIndeterminate = true });
            tip.Children.Add(new TextBlock
            {
                Text = "正在采集进程 CPU 数据，下次刷新后显示排行...",
                FontSize = 12,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            _processList.Children.Add(tip);
            return;
        }

        _processStatusText.Text = $"CPU 占用 Top {_processData.Count}";
        var maxCpu = _processData.Max(p => p.CpuPercent);

        for (var i = 0; i < _processData.Count; i++)
        {
            var entry = _processData[i];
            var ratio = maxCpu > 0 ? entry.CpuPercent / maxCpu : 0;
            var barColor = ratio > 0.6 ? AccentRed : ratio > 0.3 ? AccentOrange : AccentGreen;

            var rankText = new TextBlock
            {
                Text = $"{i + 1}",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = i < 3 ? new SolidColorBrush(barColor) : new SolidColorBrush(ThemeColors.DimText),
                Width = 24,
                VerticalAlignment = VerticalAlignment.Center
            };
            var nameText = new TextBlock
            {
                Text = entry.ProcessName,
                FontSize = 13,
                Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var bar = new ProgressBar
            {
                Value = ratio * 100,
                Minimum = 0,
                Maximum = 100,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = new SolidColorBrush(barColor)
            };
            var cpuText = new TextBlock
            {
                Text = $"{entry.CpuPercent:0.0}%",
                FontSize = 12,
                Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 52,
                TextAlignment = TextAlignment.Right
            };
            var memText = new TextBlock
            {
                Text = FormatBytes(entry.MemoryBytes),
                FontSize = 11,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 64,
                TextAlignment = TextAlignment.Right
            };

            var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(4, 5, 4, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            row.Children.Add(rankText);
            row.Children.Add(nameText); Grid.SetColumn(nameText, 1);
            row.Children.Add(bar); Grid.SetColumn(bar, 2);
            row.Children.Add(cpuText); Grid.SetColumn(cpuText, 3);
            row.Children.Add(memText); Grid.SetColumn(memText, 4);

            _processList.Children.Add(row);
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] u = ["B", "KB", "MB", "GB"];
        double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{s:0.#}{u[i]}";
    }

    private async Task ExportReportAsync()
    {
        var path = await BatteryReportService.ExportHtmlReportAsync();
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }
        else
        {
            _infoBar.Title = "导出失败";
            _infoBar.Message = "无法生成电池报告，请检查是否为笔记本电脑。";
            _infoBar.Severity = InfoBarSeverity.Error;
            _infoBar.IsOpen = true;
        }
    }
}
