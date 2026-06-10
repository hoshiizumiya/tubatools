using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class BsodAnalysisTool : IBuiltinTool
{
    public string Id => "bsod-analysis";
    public string Name => "蓝屏分析";
    public string Description => "分析系统蓝屏（BSOD）历史记录，统计次数并提供常见错误类型的诊断建议。";
    public string Glyph => "\uE946";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    private static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color AccentRed = Color.FromArgb(255, 248, 113, 113);
    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);
    private static readonly Color AccentOrange = Color.FromArgb(255, 251, 146, 60);
    private static readonly Color AccentPurple = Color.FromArgb(255, 167, 139, 250);

    private List<BsodEntry>? _entries;
    private string _filter = "";

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var dialog = context.CreateDialog("蓝屏分析");
        dialog.Resources["ContentDialogMaxWidth"] = 960;
        dialog.Resources["ContentDialogMaxHeight"] = 760;

        var content = BuildDialogContent();
        dialog.Content = content;

        _ = LoadDataAsync(content);

        await dialog.ShowAsync();
    }

    private ScrollViewer BuildDialogContent()
    {
        var countText = new TextBlock { FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(AccentRed) };
        var recentText = new TextBlock { FontSize = 14, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var typeCountText = new TextBlock { FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(AccentOrange) };

        var countCard = MakeStatCard("蓝屏次数", countText, "\uE946", AccentRed);
        var recentCard = MakeStatCard("最近发生", recentText, "\uE823", AccentBlue);
        var typeCard = MakeStatCard("错误类型", typeCountText, "\uE9D9", AccentOrange);

        var statsGrid = new Grid { ColumnSpacing = 10 };
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.Children.Add(countCard); Grid.SetColumn(countCard, 0);
        statsGrid.Children.Add(recentCard); Grid.SetColumn(recentCard, 1);
        statsGrid.Children.Add(typeCard); Grid.SetColumn(typeCard, 2);

        var searchBox = new AutoSuggestBox
        {
            PlaceholderText = "搜索错误代码、驱动名...",
            MinWidth = 220,
            QueryIcon = new SymbolIcon(Symbol.Find)
        };
        searchBox.TextChanged += (s, e) =>
        {
            _filter = searchBox.Text;
            ApplyFilter(GetRoot(s));
        };

        var refreshBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 12 },
                    new TextBlock { Text = "刷新" }
                }
            }
        };

        var copyBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE8C8", FontSize = 12 },
                    new TextBlock { Text = "复制详情" }
                }
            }
        };

        var actionBar = new Grid { ColumnSpacing = 10 };
        actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionBar.Children.Add(searchBox);
        actionBar.Children.Add(copyBtn); Grid.SetColumn(copyBtn, 1);
        actionBar.Children.Add(refreshBtn); Grid.SetColumn(refreshBtn, 2);

        var eventList = new StackPanel { Spacing = 6 };
        var eventScroll = new ScrollViewer
        {
            Content = eventList,
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var eventBorder = new Border
        {
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = eventScroll
        };

        var insightList = new StackPanel { Spacing = 8 };
        var insightScroll = new ScrollViewer
        {
            Content = insightList,
            MaxHeight = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var insightBorder = new Border
        {
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = insightScroll
        };

        var loadingRing = new ProgressRing { Width = 40, Height = 40, IsActive = true };
        var loadingText = new TextBlock { Text = "正在读取蓝屏记录...", FontSize = 13, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var loadingPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Padding = new Thickness(0, 30, 0, 30),
            Children = { loadingRing, loadingText }
        };

        var noDataText = new TextBlock
        {
            Text = "未检测到蓝屏记录，系统运行良好！",
            FontSize = 14,
            Foreground = new SolidColorBrush(AccentGreen),
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(0, 20, 0, 20),
            Visibility = Visibility.Collapsed
        };

        var root = new StackPanel { Spacing = 14, MaxWidth = 920 };
        root.Children.Add(new TextBlock
        {
            Text = "通过 Windows 事件日志分析系统蓝屏（BSOD）历史记录，提供常见错误类型的诊断建议",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });
        root.Children.Add(statsGrid);
        root.Children.Add(actionBar);
        root.Children.Add(new TextBlock { Text = "蓝屏记录", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        root.Children.Add(eventBorder);
        root.Children.Add(noDataText);
        root.Children.Add(loadingPanel);
        root.Children.Add(new TextBlock { Text = "诊断分析", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        root.Children.Add(insightBorder);

        var scrollViewer = new ScrollViewer { Content = root, MaxWidth = 960 };
        scrollViewer.Tag = new BsodAnalysisState
        {
            CountText = countText,
            RecentText = recentText,
            TypeCountText = typeCountText,
            SearchBox = searchBox,
            CopyBtn = copyBtn,
            RefreshBtn = refreshBtn,
            EventList = eventList,
            EventScroll = eventScroll,
            EventBorder = eventBorder,
            InsightList = insightList,
            InsightScroll = insightScroll,
            InsightBorder = insightBorder,
            LoadingRing = loadingRing,
            LoadingPanel = loadingPanel,
            NoDataText = noDataText
        };

        refreshBtn.Click += async (_, _) =>
        {
            await LoadDataAsync(scrollViewer);
        };

        copyBtn.Click += (_, _) =>
        {
            CopyDetailsToClipboard(scrollViewer);
        };

        return scrollViewer;
    }

    private void CopyDetailsToClipboard(ScrollViewer root)
    {
        if (_entries is null || _entries.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== 蓝屏分析报告 ===");
        sb.AppendLine($"生成时间: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"蓝屏次数: {_entries.Count}");
        sb.AppendLine($"最近发生: {_entries[0].Time:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"错误类型数: {_entries.Select(e => e.BugCheckCode).Distinct().Count()}");
        sb.AppendLine();

        sb.AppendLine("=== 蓝屏记录 ===");
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var insight = BsodAnalysisService.GetInsight(entry.BugCheckCode);
            sb.AppendLine($"[{i + 1}] {entry.Time:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"    错误代码: {entry.BugCheckCode}");
            if (insight is not null)
                sb.AppendLine($"    类型: {insight.Title} (严重度: {insight.Severity})");
            if (!string.IsNullOrEmpty(entry.CausingDriver))
                sb.AppendLine($"    驱动: {entry.CausingDriver}");
            if (!string.IsNullOrEmpty(entry.BugCheckParameter))
                sb.AppendLine($"    参数: {entry.BugCheckParameter}");
            if (!string.IsNullOrEmpty(entry.Message))
                sb.AppendLine($"    信息: {entry.Message.ReplaceLineEndings(" ")}");
            sb.AppendLine();
        }

        var insights = BsodAnalysisService.GetInsightsForEntries(_entries);
        if (insights.Count > 0)
        {
            sb.AppendLine("=== 诊断分析 ===");
            foreach (var insight in insights)
            {
                sb.AppendLine($"[{insight.BugCheckCode}] {insight.Title} (严重度: {insight.Severity})");
                sb.AppendLine($"  描述: {insight.Description}");
                sb.AppendLine($"  建议措施:");
                sb.AppendLine($"  {insight.Suggestions.ReplaceLineEndings("\n  ")}");
                sb.AppendLine();
            }
        }

        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(sb.ToString());
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);

        var state = GetState(root);
        if (state is not null)
        {
            var originalContent = (StackPanel)state.CopyBtn.Content;
            var textBlock = originalContent.Children.OfType<TextBlock>().First();
            textBlock.Text = "已复制!";
            _ = Task.Delay(2000).ContinueWith(_ =>
            {
                root.DispatcherQueue.TryEnqueue(() =>
                {
                    if (state.CopyBtn.Content is StackPanel sp)
                    {
                        var tb = sp.Children.OfType<TextBlock>().FirstOrDefault();
                        if (tb is not null) tb.Text = "复制详情";
                    }
                });
            });
        }
    }

    private async Task LoadDataAsync(ScrollViewer root)
    {
        var state = GetState(root);
        if (state is null) return;

        state.LoadingPanel.Visibility = Visibility.Visible;
        state.LoadingRing.IsActive = true;
        state.EventBorder.Visibility = Visibility.Collapsed;
        state.InsightBorder.Visibility = Visibility.Collapsed;
        state.NoDataText.Visibility = Visibility.Collapsed;

        _entries = await BsodAnalysisService.GetBsodEventsAsync();

        state.LoadingPanel.Visibility = Visibility.Collapsed;
        state.LoadingRing.IsActive = false;

        if (_entries.Count == 0)
        {
            state.CountText.Text = "0";
            state.RecentText.Text = "无记录";
            state.TypeCountText.Text = "0";
            state.NoDataText.Visibility = Visibility.Visible;
            return;
        }

        state.EventBorder.Visibility = Visibility.Visible;
        state.InsightBorder.Visibility = Visibility.Visible;

        RefreshStats(root);
        ApplyFilter(root);
        RenderInsights(root);
    }

    private void RefreshStats(ScrollViewer root)
    {
        var state = GetState(root);
        if (state is null || _entries is null) return;

        state.CountText.Text = _entries.Count.ToString();
        state.RecentText.Text = _entries.Count > 0
            ? _entries[0].Time.ToString("yyyy-MM-dd HH:mm")
            : "无记录";
        state.TypeCountText.Text = _entries
            .Select(e => e.BugCheckCode)
            .Distinct()
            .Count()
            .ToString();
    }

    private void ApplyFilter(ScrollViewer root)
    {
        var state = GetState(root);
        if (state is null || _entries is null) return;

        var filtered = _entries.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_filter))
        {
            var f = _filter.Trim();
            filtered = filtered.Where(e =>
                e.BugCheckCode.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.CausingDriver.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.BugCheckParameter.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        RenderEvents(state, list);
    }

    private void RenderEvents(BsodAnalysisState state, List<BsodEntry> entries)
    {
        state.EventList.Children.Clear();

        if (entries.Count == 0)
        {
            state.EventList.Children.Add(new TextBlock
            {
                Text = "无匹配记录",
                FontSize = 13,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(0, 12, 0, 12)
            });
            return;
        }

        foreach (var entry in entries)
        {
            state.EventList.Children.Add(CreateEventRow(entry));
        }
    }

    private Border CreateEventRow(BsodEntry entry)
    {
        var insight = BsodAnalysisService.GetInsight(entry.BugCheckCode);
        var severityColor = insight is not null ? ParseHex(insight.SeverityColor) : ThemeColors.DimText;

        var codeBadge = new Border
        {
            Padding = new Thickness(8, 3, 8, 3),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(30, severityColor.R, severityColor.G, severityColor.B)),
            Child = new TextBlock
            {
                Text = entry.BugCheckCode,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(severityColor)
            }
        };

        var titleText = new TextBlock
        {
            Text = insight?.Title ?? "未知蓝屏",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };

        var timeText = new TextBlock
        {
            Text = entry.Time.ToString("yyyy-MM-dd HH:mm:ss"),
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var driverText = new TextBlock
        {
            Text = string.IsNullOrEmpty(entry.CausingDriver) ? "" : $"驱动: {entry.CausingDriver}",
            FontSize = 11,
            Foreground = new SolidColorBrush(AccentPurple),
            Visibility = string.IsNullOrEmpty(entry.CausingDriver) ? Visibility.Collapsed : Visibility.Visible
        };

        var paramText = new TextBlock
        {
            Text = string.IsNullOrEmpty(entry.BugCheckParameter) ? "" : entry.BugCheckParameter,
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            Visibility = string.IsNullOrEmpty(entry.BugCheckParameter) ? Visibility.Collapsed : Visibility.Visible
        };

        var severityBadge = insight is not null ? new Border
        {
            Padding = new Thickness(6, 2, 6, 2),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromArgb(30, severityColor.R, severityColor.G, severityColor.B)),
            Child = new TextBlock
            {
                Text = insight.Severity,
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(severityColor)
            }
        } : null;

        var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(titleText);
        infoPanel.Children.Add(timeText);
        if (!string.IsNullOrEmpty(entry.CausingDriver)) infoPanel.Children.Add(driverText);
        if (!string.IsNullOrEmpty(entry.BugCheckParameter)) infoPanel.Children.Add(paramText);

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (severityBadge is not null)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(codeBadge);
        grid.Children.Add(infoPanel); Grid.SetColumn(infoPanel, 1);
        if (severityBadge is not null)
        {
            grid.Children.Add(severityBadge); Grid.SetColumn(severityBadge, 2);
        }

        return new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = grid
        };
    }

    private void RenderInsights(ScrollViewer root)
    {
        var state = GetState(root);
        if (state is null || _entries is null) return;

        state.InsightList.Children.Clear();

        var insights = BsodAnalysisService.GetInsightsForEntries(_entries);
        if (insights.Count == 0)
        {
            state.InsightList.Children.Add(new TextBlock
            {
                Text = "暂无诊断建议",
                FontSize = 13,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(0, 12, 0, 12)
            });
            return;
        }

        foreach (var insight in insights)
        {
            state.InsightList.Children.Add(CreateInsightCard(insight, root));
        }
    }

    private Border CreateInsightCard(BsodInsight insight, ScrollViewer root)
    {
        var accent = ParseHex(insight.SeverityColor);
        var dimAccent = Color.FromArgb(26, accent.R, accent.G, accent.B);

        var iconBorder = new Border
        {
            Width = 40,
            Height = 40,
            Background = new SolidColorBrush(dimAccent),
            CornerRadius = new CornerRadius(8),
            Child = new FontIcon { FontSize = 18, Foreground = new SolidColorBrush(accent), Glyph = "\uE946" }
        };

        var codeText = new TextBlock
        {
            Text = insight.BugCheckCode,
            FontSize = 12,
            Foreground = new SolidColorBrush(accent)
        };

        var titleText = new TextBlock
        {
            Text = insight.Title,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };

        var severityBadge = new Border
        {
            Padding = new Thickness(6, 2, 6, 2),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromArgb(30, accent.R, accent.G, accent.B)),
            Child = new TextBlock
            {
                Text = $"严重度: {insight.Severity}",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accent)
            }
        };

        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        headerPanel.Children.Add(codeText);
        headerPanel.Children.Add(titleText);
        headerPanel.Children.Add(severityBadge);

        var descText = new TextBlock
        {
            Text = insight.Description,
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85
        };

        var suggestionsText = new TextBlock
        {
            Text = insight.Suggestions,
            FontSize = 12,
            Foreground = new SolidColorBrush(AccentGreen),
            TextWrapping = TextWrapping.Wrap
        };

        var suggestionsHeader = new TextBlock
        {
            Text = "建议措施：",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(AccentGreen)
        };

        var suggestionsPanel = new StackPanel { Spacing = 2 };
        suggestionsPanel.Children.Add(suggestionsHeader);
        suggestionsPanel.Children.Add(suggestionsText);

        var contentPanel = new StackPanel { Spacing = 6 };
        contentPanel.Children.Add(headerPanel);
        contentPanel.Children.Add(descText);
        contentPanel.Children.Add(suggestionsPanel);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(iconBorder);
        grid.Children.Add(contentPanel); Grid.SetColumn(contentPanel, 1);

        return new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    private static Color ParseHex(string hex)
    {
        var r = Convert.ToByte(hex[1..3], 16);
        var g = Convert.ToByte(hex[3..5], 16);
        var b = Convert.ToByte(hex[5..7], 16);
        return Color.FromArgb(255, r, g, b);
    }

    private static ScrollViewer GetRoot(object sender)
    {
        var child = sender as FrameworkElement;
        while (child is not null)
        {
            if (child is ScrollViewer sv) return sv;
            child = VisualTreeHelper.GetParent(child) as FrameworkElement;
        }
        return null!;
    }

    private static BsodAnalysisState? GetState(ScrollViewer root) => root?.Tag as BsodAnalysisState;

    private static Border MakeStatCard(string label, TextBlock value, string glyph, Color accent)
    {
        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(accent), Glyph = glyph }
        };
        var labelBlock = new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(labelBlock);
        stack.Children.Add(value);

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(iconBorder);
        grid.Children.Add(stack); Grid.SetColumn(stack, 1);

        return new Border
        {
            Padding = new Thickness(12),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    private sealed class BsodAnalysisState
    {
        public TextBlock CountText = null!;
        public TextBlock RecentText = null!;
        public TextBlock TypeCountText = null!;
        public AutoSuggestBox SearchBox = null!;
        public Button CopyBtn = null!;
        public Button RefreshBtn = null!;
        public StackPanel EventList = null!;
        public ScrollViewer EventScroll = null!;
        public Border EventBorder = null!;
        public StackPanel InsightList = null!;
        public ScrollViewer InsightScroll = null!;
        public Border InsightBorder = null!;
        public ProgressRing LoadingRing = null!;
        public StackPanel LoadingPanel = null!;
        public TextBlock NoDataText = null!;
    }
}
