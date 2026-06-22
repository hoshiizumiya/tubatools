using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class PcSetupPage : Page
{
    private readonly Window _window;
    private int _currentStep = 1;
    private List<CatalogCategory> _categories = [];
    private List<PcSetupAction> _optimizeActions = [];
    private int _burnDurationMinutes = 10;
    private CancellationTokenSource? _burnCts;
    private CancellationTokenSource? _executeCts;
    private bool _executing;

    private static readonly Color ColorTemp = Color.FromArgb(255, 248, 113, 113);
    private static readonly Color ColorPower = Color.FromArgb(255, 251, 191, 36);
    private static readonly Color ColorClock = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color ColorLoad = Color.FromArgb(255, 74, 222, 128);

    public PcSetupPage(Window window)
    {
        _window = window;
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += (_, _) => RedrawBurnChart();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _categories = PcSetupCatalogService.GetCatalog();
        _optimizeActions = SystemOptimizer.GetAllOptimizeActions();
        PopulateCategoryFilter();
        BuildPackageList();
        RefreshStats();
        BuildVisualPresets();
        BuildOptimizeItems();
        BuildBurnStatCards();
        await CheckInstalledStatus();
    }

    #region Step Navigation

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep < 4)
        {
            _currentStep++;
            UpdateStepUI();
        }
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
        {
            _currentStep--;
            UpdateStepUI();
        }
    }

    private void UpdateStepUI()
    {
        Step1Content.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Content.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Content.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Content.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;

        PrevBtn.IsEnabled = _currentStep > 1;
        NextBtn.Content = _currentStep == 3 ? "查看执行清单" : "下一步";
        NextBtn.Visibility = _currentStep == 4 ? Visibility.Collapsed : Visibility.Visible;

        if (_currentStep == 4) BuildExecuteList();

        UpdateStepIndicator();
    }

    private void UpdateStepIndicator()
    {
        var circles = new[] { Step1Circle, Step2Circle, Step3Circle, Step4Circle };
        var texts = new[] { Step1Text, Step2Text, Step3Text, Step4Text };
        for (var i = 0; i < 4; i++)
        {
            var stepNum = i + 1;
            if (stepNum < _currentStep)
            {
                circles[i].Background = new SolidColorBrush(ThemeColors.AccentGreen);
                ((TextBlock)circles[i].Child).Text = "\uE73E";
                ((TextBlock)circles[i].Child).Foreground = new SolidColorBrush(Colors.White);
                texts[i].Foreground = new SolidColorBrush(ThemeColors.AccentGreen);
                texts[i].FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            }
            else if (stepNum == _currentStep)
            {
                circles[i].Background = Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush
                    ?? new SolidColorBrush(ThemeColors.AccentBlue);
                ((TextBlock)circles[i].Child).Text = stepNum.ToString();
                ((TextBlock)circles[i].Child).Foreground = new SolidColorBrush(Colors.White);
                texts[i].Foreground = Application.Current.Resources["AccentTextFillColorPrimaryBrush"] as Brush
                    ?? new SolidColorBrush(ThemeColors.PrimaryText);
                texts[i].FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            }
            else
            {
                circles[i].Background = Application.Current.Resources["ControlFillColorSecondaryBrush"] as Brush
                    ?? new SolidColorBrush(ThemeColors.SubtleBg);
                ((TextBlock)circles[i].Child).Text = stepNum.ToString();
                ((TextBlock)circles[i].Child).Foreground = Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush
                    ?? new SolidColorBrush(ThemeColors.DimText);
                texts[i].Foreground = Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush
                    ?? new SolidColorBrush(ThemeColors.DimText);
                texts[i].FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            }
        }
    }

    #endregion

    #region Step 1: Software Install

    private void PopulateCategoryFilter()
    {
        CategoryFilter.Items.Clear();
        var item = new ComboBoxItem { Content = "全部分类", Tag = "" };
        CategoryFilter.Items.Add(item);
        CategoryFilter.SelectedItem = item;
        foreach (var cat in _categories)
        {
            CategoryFilter.Items.Add(new ComboBoxItem { Content = cat.Name, Tag = cat.Name });
            foreach (var sub in cat.SubCategories)
                CategoryFilter.Items.Add(new ComboBoxItem { Content = $"  {cat.Name}/{sub.Name}", Tag = $"{cat.Name}/{sub.Name}" });
        }
    }

    private void BuildPackageList()
    {
        PackageList.Children.Clear();
        var filter = (CategoryFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        foreach (var cat in _categories)
        {
            if (!string.IsNullOrEmpty(filter) && filter != cat.Name && !filter.StartsWith(cat.Name + "/"))
                continue;

            var header = new TextBlock
            {
                Text = cat.Name,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
                Margin = new Thickness(0, 8, 0, 4)
            };
            PackageList.Children.Add(header);

            foreach (var pkg in cat.Packages)
            {
                if (!string.IsNullOrEmpty(filter) && filter != cat.Name)
                    continue;
                PackageList.Children.Add(CreatePackageRow(pkg, cat.Glyph));
            }

            foreach (var sub in cat.SubCategories)
            {
                if (!string.IsNullOrEmpty(filter) && filter != $"{cat.Name}/{sub.Name}" && filter != cat.Name)
                    continue;

                var subHeader = new TextBlock
                {
                    Text = $"  {sub.Name}",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(ThemeColors.AccentBlue),
                    Margin = new Thickness(8, 6, 0, 2)
                };
                PackageList.Children.Add(subHeader);

                foreach (var pkg in sub.Packages)
                    PackageList.Children.Add(CreatePackageRow(pkg, cat.Glyph));
            }
        }
    }

    private Border CreatePackageRow(CatalogPackage pkg, string categoryGlyph)
    {
        var cb = new CheckBox
        {
            IsChecked = pkg.IsSelected,
            Tag = pkg.Id,
            MinWidth = 0
        };
        cb.Checked += (_, _) => { pkg.IsSelected = true; RefreshStats(); };
        cb.Unchecked += (_, _) => { pkg.IsSelected = false; RefreshStats(); };

        var iconBorder = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(ThemeColors.SubtleBg),
            Child = new FontIcon
            {
                Glyph = categoryGlyph, FontSize = 13,
                Foreground = new SolidColorBrush(ThemeColors.AccentBlue)
            }
        };

        var nameBlock = new TextBlock
        {
            Text = pkg.Name, FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var descBlock = new TextBlock
        {
            Text = pkg.Desc ?? "", FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var nameStack = new StackPanel { Spacing = 1 };
        nameStack.Children.Add(nameBlock);
        nameStack.Children.Add(descBlock);

        var statusBlock = new TextBlock
        {
            Tag = $"status-{pkg.Id}", FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        if (pkg.State == WingetInstallState.Installed)
        {
            statusBlock.Text = "已安装";
            statusBlock.Foreground = new SolidColorBrush(ThemeColors.AccentGreen);
        }

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        Grid.SetColumn(cb, 0); Grid.SetColumn(iconBorder, 1);
        Grid.SetColumn(nameStack, 2); Grid.SetColumn(statusBlock, 3);
        grid.Children.Add(cb); grid.Children.Add(iconBorder);
        grid.Children.Add(nameStack); grid.Children.Add(statusBlock);

        var row = new Border
        {
            Tag = pkg.Id,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            Child = grid
        };
        return row;
    }

    private void RefreshStats()
    {
        var allPkgs = GetAllPackages();
        var total = allPkgs.Count;
        var installed = allPkgs.Count(p => p.State == WingetInstallState.Installed);
        var selected = allPkgs.Count(p => p.IsSelected && p.State != WingetInstallState.Installed);

        StatTotal.Child = BuildStatCard("可用软件", total, ThemeColors.AccentBlue);
        StatInstalled.Child = BuildStatCard("已安装", installed, ThemeColors.AccentGreen);
        StatSelected.Child = BuildStatCard("待安装", selected, ThemeColors.AccentOrange);
    }

    private StackPanel BuildStatCard(string label, int value, Color accent)
    {
        return new StackPanel
        {
            Children =
            {
                new TextBlock { Text = value.ToString(), FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(accent) },
                new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) }
            }
        };
    }

    private List<CatalogPackage> GetAllPackages()
    {
        var list = new List<CatalogPackage>();
        foreach (var cat in _categories)
        {
            list.AddRange(cat.Packages);
            foreach (var sub in cat.SubCategories) list.AddRange(sub.Packages);
        }
        return list;
    }

    private async Task CheckInstalledStatus()
    {
        var allPkgs = GetAllPackages();
        if (allPkgs.Count == 0) return;

        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingProgress.Value = 0;
        LoadingProgress.Maximum = allPkgs.Count;
        LoadingTitle.Text = "正在检测已安装软件...";
        LoadingDetail.Text = $"0 / {allPkgs.Count}";
        LoadingRing.IsActive = true;

        var progress = new Progress<(int Done, int Total, string Name)>(p =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LoadingProgress.Value = p.Done;
                LoadingDetail.Text = $"{p.Done} / {p.Total}  {p.Name}";
            });
        });

        try
        {
            await PcSetupCatalogService.CheckInstalledStatusAsync(_categories, progress);
            BuildPackageList();
            RefreshStats();
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = false;
        }
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BuildPackageList();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var pkg in GetAllPackages()) pkg.IsSelected = true;
        BuildPackageList(); RefreshStats();
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var pkg in GetAllPackages()) pkg.IsSelected = false;
        BuildPackageList(); RefreshStats();
    }

    private void SelectNotInstalled_Click(object sender, RoutedEventArgs e)
    {
        foreach (var pkg in GetAllPackages()) pkg.IsSelected = pkg.State != WingetInstallState.Installed;
        BuildPackageList(); RefreshStats();
    }

    #endregion

    #region Step 2: System Optimize

    private void BuildVisualPresets()
    {
        VisualPresetPanel.Children.Clear();
        var presets = SystemOptimizer.GetVisualPresets();
        foreach (var preset in presets)
        {
            var panel = new StackPanel { Spacing = 2 };
            panel.Children.Add(new TextBlock
            {
                Text = $"{preset.Glyph} {preset.Name}",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
            });
            panel.Children.Add(new TextBlock
            {
                Text = preset.Description,
                FontSize = 11,
                Foreground = new SolidColorBrush(ThemeColors.DimText)
            });
            var rb = new RadioButton { Content = panel, Tag = preset.Name, GroupName = "VisualPreset" };
            if (preset.Name == "平衡") rb.IsChecked = true;
            rb.Checked += VisualPresetRadio_Checked;
            VisualPresetPanel.Children.Add(rb);
        }
    }

    private void VisualPresetRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string presetName)
            SystemOptimizer.ApplyVisualPreset(_optimizeActions, presetName);
        BuildOptimizeItems();
    }

    private void BuildOptimizeItems()
    {
        OptimizeItemsList.Children.Clear();
        var groups = _optimizeActions.GroupBy(a => a.Group);
        foreach (var group in groups)
        {
            var header = new TextBlock
            {
                Text = group.Key,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
                Margin = new Thickness(0, 8, 0, 4)
            };
            OptimizeItemsList.Children.Add(header);

            foreach (var action in group)
                OptimizeItemsList.Children.Add(CreateOptimizeRow(action));
        }
    }

    private bool _suppressChecked;

    private Border CreateOptimizeRow(PcSetupAction action)
    {
        var cb = new CheckBox
        {
            IsChecked = action.IsSelected,
            Tag = action.Id,
            MinWidth = 0
        };
        cb.Checked += async (_, _) =>
        {
            if (_suppressChecked) return;
            if (action.IsDangerous)
            {
                _suppressChecked = true;
                cb.IsChecked = false;
                _suppressChecked = false;
                var dialog = new ContentDialog
                {
                    Title = "⚠ 高危操作确认",
                    Content = $"「{action.Name}」属于高危操作：\n\n{action.Description}\n\n确定要启用此操作吗？",
                    PrimaryButtonText = "确定启用",
                    CloseButtonText = "取消",
                    XamlRoot = XamlRoot,
                    RequestedTheme = ThemeService.CurrentElementTheme
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    action.IsSelected = true;
                    _suppressChecked = true;
                    cb.IsChecked = true;
                    _suppressChecked = false;
                }
            }
            else
            {
                action.IsSelected = true;
            }
        };
        cb.Unchecked += (_, _) =>
        {
            if (_suppressChecked) return;
            action.IsSelected = false;
        };

        var iconBorder = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(6),
            Background = action.IsDangerous
                ? new SolidColorBrush(Color.FromArgb(40, 248, 113, 113))
                : new SolidColorBrush(ThemeColors.SubtleBg),
            Child = new FontIcon
            {
                Glyph = action.Glyph, FontSize = 13,
                Foreground = action.IsDangerous
                    ? new SolidColorBrush(ThemeColors.AccentRed)
                    : new SolidColorBrush(ThemeColors.AccentBlue)
            }
        };

        var nameBlock = new TextBlock
        {
            Text = action.Name, FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var descBlock = new TextBlock
        {
            Text = action.Description, FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var nameStack = new StackPanel { Spacing = 1 };
        nameStack.Children.Add(nameBlock);
        nameStack.Children.Add(descBlock);

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(cb, 0); Grid.SetColumn(iconBorder, 1); Grid.SetColumn(nameStack, 2);
        grid.Children.Add(cb); grid.Children.Add(iconBorder); grid.Children.Add(nameStack);

        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = action.IsDangerous
                ? new SolidColorBrush(Color.FromArgb(80, 248, 113, 113))
                : new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            Child = grid
        };
    }

    #endregion

    #region Step 3: CPU Burn

    private void BuildBurnStatCards()
    {
        BurnTempCard.Child = BuildBurnStat("温度", "-- °C", ColorTemp);
        BurnPowerCard.Child = BuildBurnStat("功耗", "-- W", ColorPower);
        BurnClockCard.Child = BuildBurnStat("频率", "-- MHz", ColorClock);
        BurnLoadCard.Child = BuildBurnStat("占用", "-- %", ColorLoad);
    }

    private StackPanel BuildBurnStat(string label, string value, Color accent)
    {
        return new StackPanel
        {
            Children =
            {
                new TextBlock { Text = value, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(accent), Tag = "value" },
                new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) }
            }
        };
    }

    private void UpdateBurnStatCard(Border card, string value)
    {
        if (card.Child is StackPanel panel && panel.Children[0] is TextBlock tb)
            tb.Text = value;
    }

    private void BurnDuration_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BurnDurationCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out var min))
        {
            _burnDurationMinutes = min;
            BurnTimer.Text = $"00:00 / {min:D2}:00";
        }
    }

    private async void BurnStart_Click(object sender, RoutedEventArgs e)
    {
        BurnStartBtn.IsEnabled = false;
        BurnStopBtn.IsEnabled = true;
        BurnDurationCombo.IsEnabled = false;
        _burnCts = new CancellationTokenSource();
        var duration = TimeSpan.FromMinutes(_burnDurationMinutes);
        var startTime = DateTime.Now;

        var progress = new Progress<BurnSample>(sample =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var elapsed = DateTime.Now - startTime;
                var remaining = duration - elapsed;
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                BurnTimer.Text = $"{elapsed:mm\\:ss} / {_burnDurationMinutes:D2}:00";

                UpdateBurnStatCard(BurnTempCard, sample.Temp >= 0 ? $"{sample.Temp:F1} °C" : "-- °C");
                UpdateBurnStatCard(BurnPowerCard, sample.Power >= 0 ? $"{sample.Power:F1} W" : "-- W");
                UpdateBurnStatCard(BurnClockCard, sample.Clock >= 0 ? $"{sample.Clock:F0} MHz" : "-- MHz");
                UpdateBurnStatCard(BurnLoadCard, sample.Load >= 0 ? $"{sample.Load:F1} %" : "-- %");

                RedrawBurnChart();
            });
        });

        try
        {
            await CpuBurnService.RunBurnAsync(duration, progress, _burnCts.Token);
        }
        catch { }

        BurnStartBtn.IsEnabled = true;
        BurnStopBtn.IsEnabled = false;
        BurnDurationCombo.IsEnabled = true;
        BurnTimer.Text = $"00:00 / {_burnDurationMinutes:D2}:00";
    }

    private void BurnStop_Click(object sender, RoutedEventArgs e)
    {
        CpuBurnService.Stop();
        _burnCts?.Cancel();
        BurnStartBtn.IsEnabled = true;
        BurnStopBtn.IsEnabled = false;
        BurnDurationCombo.IsEnabled = true;
    }

    private void RedrawBurnChart()
    {
        var canvas = BurnChart;
        if (canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;

        var samples = CpuBurnService.GetSamples();
        if (samples.Count < 2) return;

        canvas.Children.Clear();
        var w = canvas.ActualWidth;
        var h = canvas.ActualHeight;
        var pad = new Thickness(40, 10, 10, 20);
        var plotW = w - pad.Left - pad.Right;
        var plotH = h - pad.Top - pad.Bottom;

        DrawAxis(canvas, pad, plotW, plotH, samples);

        var series = new (List<float> Data, Color Color, float Min, float Max, string Label)[]
        {
            (samples.Select(s => s.Temp).ToList(), ColorTemp, 0, 120, "温度"),
            (samples.Select(s => s.Power).ToList(), ColorPower, 0, 350, "功耗"),
            (samples.Select(s => s.Clock).ToList(), ColorClock, 0, 6000, "频率"),
            (samples.Select(s => s.Load).ToList(), ColorLoad, 0, 100, "占用"),
        };

        foreach (var s in series)
        {
            var validData = s.Data.Where(v => v >= 0).ToList();
            if (validData.Count < 2) continue;
            DrawLine(canvas, pad, plotW, plotH, s.Data, s.Color, s.Min, s.Max);
        }
    }

    private void DrawAxis(Canvas canvas, Thickness pad, double plotW, double plotH, List<BurnSample> samples)
    {
        var gridBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
        var textBrush = new SolidColorBrush(ThemeColors.DimText);

        for (var i = 0; i <= 4; i++)
        {
            var y = pad.Top + plotH * i / 4;
            var line = new Microsoft.UI.Xaml.Shapes.Line
            {
                X1 = pad.Left, Y1 = y, X2 = pad.Left + plotW, Y2 = y,
                Stroke = gridBrush, StrokeThickness = 0.5
            };
            canvas.Children.Add(line);
        }

        var timeStep = Math.Max(1, samples.Count / 6);
        for (var i = 0; i < samples.Count; i += timeStep)
        {
            var x = pad.Left + (double)i / (samples.Count - 1) * plotW;
            var label = new TextBlock
            {
                Text = samples[i].Time.ToString("mm:ss"),
                FontSize = 9,
                Foreground = textBrush
            };
            canvas.Children.Add(label);
            Canvas.SetLeft(label, x - 15);
            Canvas.SetTop(label, pad.Top + plotH + 2);
        }
    }

    private void DrawLine(Canvas canvas, Thickness pad, double plotW, double plotH,
        List<float> data, Color color, float minVal, float maxVal)
    {
        var range = maxVal - minVal;
        if (range <= 0) range = 1;
        var points = new PointCollection();
        for (var i = 0; i < data.Count; i++)
        {
            var x = pad.Left + (double)i / (data.Count - 1) * plotW;
            var val = data[i] < 0 ? minVal : data[i];
            val = Math.Clamp(val, minVal, maxVal);
            var y = pad.Top + plotH - (val - minVal) / range * plotH;
            points.Add(new Windows.Foundation.Point(x, y));
        }

        var line = new Microsoft.UI.Xaml.Shapes.Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };
        canvas.Children.Add(line);

        var fillPoints = new PointCollection();
        fillPoints.Add(new Windows.Foundation.Point(points[0].X, pad.Top + plotH));
        foreach (var p in points) fillPoints.Add(p);
        fillPoints.Add(new Windows.Foundation.Point(points[^1].X, pad.Top + plotH));

        var fill = new Microsoft.UI.Xaml.Shapes.Polygon
        {
            Points = fillPoints,
            Fill = new SolidColorBrush(Color.FromArgb(25, color.R, color.G, color.B))
        };
        canvas.Children.Add(fill);
    }

    #endregion

    #region Step 4: Execute

    private void BuildExecuteList()
    {
        ExecuteActionList.Children.Clear();
        var allActions = GetAllSelectedActions();
        var installActions = allActions.OfType<WingetInstallAction>().ToList();
        var optimizeActions = allActions.Where(a => a is not WingetInstallAction).ToList();

        ExecuteSummary.Text = $"共 {allActions.Count} 项操作：{installActions.Count} 个软件安装 + {optimizeActions.Count} 项系统优化";

        if (installActions.Count > 0)
        {
            ExecuteActionList.Children.Add(new TextBlock
            {
                Text = $"软件安装 ({installActions.Count})",
                FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
                Margin = new Thickness(0, 4, 0, 4)
            });
            foreach (var action in installActions)
                ExecuteActionList.Children.Add(CreateExecuteRow(action));
        }

        if (optimizeActions.Count > 0)
        {
            ExecuteActionList.Children.Add(new TextBlock
            {
                Text = $"系统优化 ({optimizeActions.Count})",
                FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
                Margin = new Thickness(0, 8, 0, 4)
            });
            foreach (var action in optimizeActions)
                ExecuteActionList.Children.Add(CreateExecuteRow(action));
        }

        if (allActions.Count == 0)
        {
            ExecuteActionList.Children.Add(new TextBlock
            {
                Text = "没有选中任何操作，请返回前几步选择需要执行的项目。",
                FontSize = 13, Foreground = new SolidColorBrush(ThemeColors.DimText)
            });
        }

        ExecuteBtn.IsEnabled = allActions.Count > 0;
        ExportBtn.IsEnabled = allActions.Count > 0;
    }

    private List<PcSetupAction> GetAllSelectedActions()
    {
        var list = new List<PcSetupAction>();
        list.AddRange(PcSetupCatalogService.ToInstallActions(_categories));
        list.AddRange(_optimizeActions.Where(a => a.IsSelected));
        return list;
    }

    private Border CreateExecuteRow(PcSetupAction action)
    {
        var isInstall = action is WingetInstallAction;
        var iconBorder = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(4),
            Background = isInstall
                ? new SolidColorBrush(Color.FromArgb(40, 96, 165, 250))
                : new SolidColorBrush(Color.FromArgb(40, 74, 222, 128)),
            Child = new FontIcon
            {
                Glyph = isInstall ? "\uE896" : "\uE90F", FontSize = 11,
                Foreground = isInstall
                    ? new SolidColorBrush(ThemeColors.AccentBlue)
                    : new SolidColorBrush(ThemeColors.AccentGreen)
            }
        };

        var nameBlock = new TextBlock
        {
            Text = action.Name, FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var statusBlock = new TextBlock
        {
            Text = "待执行", FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Tag = $"exec-status-{action.Id}"
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        Grid.SetColumn(iconBorder, 0); Grid.SetColumn(nameBlock, 1); Grid.SetColumn(statusBlock, 2);
        grid.Children.Add(iconBorder); grid.Children.Add(nameBlock); grid.Children.Add(statusBlock);

        return new Border
        {
            Tag = action.Id,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            Child = grid
        };
    }

    private async void Execute_Click(object sender, RoutedEventArgs e)
    {
        if (_executing) return;
        _executing = true;
        ExecuteBtn.IsEnabled = false;
        ExportBtn.IsEnabled = false;
        ExecuteProgressPanel.Visibility = Visibility.Visible;
        ExecuteLogBorder.Visibility = Visibility.Visible;
        ExecuteLogText.Text = "";
        _executeCts = new CancellationTokenSource();

        var actions = GetAllSelectedActions();
        var total = actions.Count;
        var completed = 0;
        var succeeded = 0;
        var failed = 0;

        var needsAdmin = actions.Any(a => a.RequiresAdmin);
        var isAdmin = IsRunningAsAdmin();

        if (needsAdmin && !isAdmin)
        {
            var adminDialog = new ContentDialog
            {
                Title = "需要管理员权限",
                Content = "部分操作需要管理员权限，但当前未以管理员身份运行。\n\n" +
                          "建议：\n" +
                          "1. 关闭本工具，右键以管理员身份重新运行\n" +
                          "2. 或继续执行，需要管理员权限的操作会弹出 UAC 确认框（逐一确认）\n\n" +
                          "继续执行？（需要管理员权限的操作将逐一请求 UAC 提权）",
                PrimaryButtonText = "继续执行",
                CloseButtonText = "取消",
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            var r = await adminDialog.ShowAsync();
            if (r != ContentDialogResult.Primary)
            {
                _executing = false;
                ExecuteBtn.IsEnabled = true;
                ExportBtn.IsEnabled = true;
                ExecuteProgressPanel.Visibility = Visibility.Collapsed;
                return;
            }
        }
        else if (needsAdmin && isAdmin)
        {
            var adminDialog = new ContentDialog
            {
                Title = "管理员模式",
                Content = "当前已以管理员身份运行，所有操作将直接执行，无需额外确认。",
                CloseButtonText = "开始执行",
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            await adminDialog.ShowAsync();
        }

        var progress = new Progress<string>(msg =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ExecuteLogText.Text += msg + "\n";
                ExecuteProgressBar.Value = (double)completed / total * 100;
                ExecuteProgressText.Text = $"{completed}/{total} 已完成 | 成功: {succeeded} 失败: {failed}";
            });
        });

        foreach (var action in actions)
        {
            if (_executeCts.Token.IsCancellationRequested) break;
            ((progress as IProgress<string>)!).Report($"[{completed + 1}/{total}] {action.Name}...");
            var result = await action.ExecuteAsync(progress, _executeCts.Token);
            completed++;
            if (result.Success) succeeded++; else failed++;

            DispatcherQueue.TryEnqueue(() =>
            {
                var row = ExecuteActionList.Children.OfType<Border>()
                    .FirstOrDefault(b => (string?)b.Tag == action.Id);
                if (row?.Child is Grid grid)
                {
                    var statusBlock = grid.Children.OfType<TextBlock>()
                        .FirstOrDefault(t => (string?)t.Tag == $"exec-status-{action.Id}");
                    if (statusBlock is not null)
                    {
                        statusBlock.Text = action.State switch
                        {
                            PcSetupActionState.Succeeded => "✓ 成功",
                            PcSetupActionState.Failed => "✗ 失败",
                            PcSetupActionState.Skipped => "- 跳过",
                            _ => action.StatusText ?? ""
                        };
                        statusBlock.Foreground = action.State == PcSetupActionState.Succeeded
                            ? new SolidColorBrush(ThemeColors.AccentGreen)
                            : action.State == PcSetupActionState.Failed
                                ? new SolidColorBrush(ThemeColors.AccentRed)
                                : new SolidColorBrush(ThemeColors.DimText);
                    }
                }
                ExecuteProgressBar.Value = (double)completed / total * 100;
                ExecuteProgressText.Text = $"{completed}/{total} 已完成 | 成功: {succeeded} 失败: {failed}";
            });
        }

        ((progress as IProgress<string>)!).Report($"\n========== 执行完毕 ==========");
        ((progress as IProgress<string>)!).Report($"成功: {succeeded} | 失败: {failed} | 跳过: {total - completed}");

        _executing = false;
        ExecuteBtn.IsEnabled = true;
        ExportBtn.IsEnabled = true;
        ExecuteBtn.Content = "重新执行";
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var actions = GetAllSelectedActions();
        if (actions.Count == 0) return;

        var customScript = "";

        var customDialog = new ContentDialog
        {
            Title = "自定义脚本（可选）",
            PrimaryButtonText = "导出",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var dialogStack = new StackPanel { Spacing = 8 };
        dialogStack.Children.Add(new TextBlock
        {
            Text = "可在生成的脚本中插入自定义 PowerShell 代码，留空则不插入。",
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap
        });

        var positionCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedIndex = 0
        };
        positionCombo.Items.Add(new ComboBoxItem { Content = "在软件安装之前执行", Tag = "before-install" });
        positionCombo.Items.Add(new ComboBoxItem { Content = "在软件安装与系统优化之间执行", Tag = "between" });
        positionCombo.Items.Add(new ComboBoxItem { Content = "在全部操作完成后执行", Tag = "after-all" });
        dialogStack.Children.Add(new TextBlock
        {
            Text = "插入位置：",
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        });
        dialogStack.Children.Add(positionCombo);

        var scriptBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 160,
            MaxHeight = 300,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 13,
            PlaceholderText = "# 输入自定义 PowerShell 脚本\n# 例如：\n# Set-ExecutionPolicy RemoteSigned -Scope CurrentUser\n# New-Item -Path 'C:\\MyFolder' -ItemType Directory"
        };
        dialogStack.Children.Add(scriptBox);

        customDialog.Content = dialogStack;

        var dlgResult = await customDialog.ShowAsync();
        if (dlgResult != ContentDialogResult.Primary) return;

        customScript = scriptBox.Text.Trim();
        var position = (positionCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "before-install";

        var script = PcSetupCatalogService.GeneratePowerShellScript(actions, customScript, position);
        var fileName = $"新机开荒_{DateTime.Now:yyyyMMdd_HHmmss}.ps1";

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            picker.FileTypeChoices.Add("PowerShell 脚本", new List<string> { ".ps1" });
            picker.SuggestedFileName = fileName;

            var file = await picker.PickSaveFileAsync();
            if (file is not null)
            {
                await WriteScriptWithBomAsync(file, script);
                var dialog = new ContentDialog
                {
                    Title = "导出成功",
                    Content = $"脚本已保存到：\n{file.Path}\n\n⚠ 请右键该文件 →「以管理员身份运行 PowerShell」执行，\n脚本需要管理员权限才能修改系统设置和安装软件。",
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot,
                    RequestedTheme = ThemeService.CurrentElementTheme
                };
                await dialog.ShowAsync();
            }
        }
        catch
        {
            try
            {
                var savePath = PickSaveFile("导出 PowerShell 脚本", "PowerShell 脚本\0*.ps1\0所有文件\0*.*\0\0", fileName, "ps1");
                if (savePath is not null)
                {
                    await WriteScriptWithBomViaPathAsync(savePath, script);
                    var dialog = new ContentDialog
                    {
                        Title = "导出成功",
                        Content = $"脚本已保存到：\n{savePath}\n\n⚠ 请右键该文件 →「以管理员身份运行 PowerShell」执行，\n脚本需要管理员权限才能修改系统设置和安装软件。",
                        CloseButtonText = "确定",
                        XamlRoot = XamlRoot,
                        RequestedTheme = ThemeService.CurrentElementTheme
                    };
                    await dialog.ShowAsync();
                }
            }
            catch (Exception ex2)
            {
                var errDialog = new ContentDialog
                {
                    Title = "导出失败",
                    Content = $"无法保存文件：\n{ex2.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot,
                    RequestedTheme = ThemeService.CurrentElementTheme
                };
                await errDialog.ShowAsync();
            }
        }
    }

    private static async Task WriteScriptWithBomAsync(Windows.Storage.StorageFile file, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(content))
            .ToArray();
        await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);
    }

    private static async Task WriteScriptWithBomViaPathAsync(string path, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(content))
            .ToArray();
        await File.WriteAllBytesAsync(path, bytes);
    }

    private static string? PickSaveFile(string title, string filter, string defaultFileName, string defaultExtension)
    {
        var buffer = defaultFileName + new string('\0', 1024 - defaultFileName.Length);
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
            lpstrFilter = filter,
            lpstrFile = buffer,
            nMaxFile = 1024,
            lpstrTitle = title,
            lpstrDefExt = defaultExtension,
            Flags = OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
            nFilterIndex = 1
        };

        return GetSaveFileName(ref ofn) ? ofn.lpstrFile.TrimEnd('\0') : null;
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetSaveFileName(ref OPENFILENAME ofn);

    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_NOCHANGEDIR = 0x00000008;

    #endregion
}
