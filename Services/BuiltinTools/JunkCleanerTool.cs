using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class JunkCleanerTool : IBuiltinTool
{
    public string Id => "junk-cleaner";
    public string Name => "垃圾清理";
    public string Description => "扫描并清理系统临时文件、浏览器缓存、回收站等垃圾文件。";
    public string Glyph => "\uE74D";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.ProgressTask;

    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);
    private static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color AccentRed = Color.FromArgb(255, 248, 113, 113);

    private List<JunkCategory>? _categories;
    private CancellationTokenSource? _cts;

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var dialog = new ContentDialog
        {
            Title = "垃圾清理",
            CloseButtonText = "关闭",
            XamlRoot = context.XamlRoot
        };
        dialog.Resources["ContentDialogMaxWidth"] = 900;
        dialog.Resources["ContentDialogMaxHeight"] = 700;
        dialog.Closing += (_, _) => _cts?.Cancel();

        var content = BuildDialogContent();
        dialog.Content = content;

        await dialog.ShowAsync();
    }

    private ScrollViewer BuildDialogContent()
    {
        var totalSizeText = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(AccentBlue) };
        var totalFilesText = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(AccentGreen) };
        var categoryCountText = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold };

        var sizeCard = MakeStatCard("总大小", totalSizeText, "\uEDA2", AccentBlue);
        var filesCard = MakeStatCard("文件数", totalFilesText, "\uE8C8", AccentGreen);
        var catCard = MakeStatCard("分类", categoryCountText, "\uE7F4", Color.FromArgb(255, 167, 139, 250));

        var statsGrid = new Grid { ColumnSpacing = 10 };
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.Children.Add(sizeCard); Grid.SetColumn(sizeCard, 0);
        statsGrid.Children.Add(filesCard); Grid.SetColumn(filesCard, 1);
        statsGrid.Children.Add(catCard); Grid.SetColumn(catCard, 2);

        var scanBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 12 },
                    new TextBlock { Text = "扫描" }
                }
            }
        };

        var cleanBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE74D", FontSize = 12 },
                    new TextBlock { Text = "清理" }
                }
            },
            IsEnabled = false
        };

        var selectAllBtn = new Button { Content = "全选", Padding = new Thickness(8, 4, 8, 4) };
        var deselectAllBtn = new Button { Content = "取消全选", Padding = new Thickness(8, 4, 8, 4) };

        var actionBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionBar.Children.Add(scanBtn);
        actionBar.Children.Add(cleanBtn);
        actionBar.Children.Add(selectAllBtn);
        actionBar.Children.Add(deselectAllBtn);

        var categoryList = new StackPanel { Spacing = 8 };
        var listScroll = new ScrollViewer
        {
            Content = categoryList,
            MaxHeight = 400,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var loadingRing = new ProgressRing { Width = 40, Height = 40, IsActive = true };
        var loadingText = new TextBlock { Text = "正在扫描垃圾文件...", FontSize = 13, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var loadingPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Padding = new Thickness(0, 30, 0, 30),
            Visibility = Visibility.Collapsed,
            Children = { loadingRing, loadingText }
        };

        var contentGrid = new Grid { RowSpacing = 14 };
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        contentGrid.Children.Add(statsGrid); Grid.SetRow(statsGrid, 0);
        contentGrid.Children.Add(actionBar); Grid.SetRow(actionBar, 1);
        contentGrid.Children.Add(listScroll); Grid.SetRow(listScroll, 2);
        contentGrid.Children.Add(loadingPanel); Grid.SetRow(loadingPanel, 3);

        var resultText = new TextBlock
        {
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(AccentGreen),
            Visibility = Visibility.Collapsed
        };

        var root = new StackPanel { Spacing = 14, MaxWidth = 860 };
        root.Children.Add(new TextBlock
        {
            Text = "扫描并清理系统临时文件、浏览器缓存、回收站等垃圾文件，释放磁盘空间",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });
        root.Children.Add(contentGrid);
        root.Children.Add(resultText);

        var scrollViewer = new ScrollViewer { Content = root, MaxWidth = 900 };
        scrollViewer.Tag = new JunkCleanerState
        {
            TotalSizeText = totalSizeText,
            TotalFilesText = totalFilesText,
            CategoryCountText = categoryCountText,
            ScanBtn = scanBtn,
            CleanBtn = cleanBtn,
            SelectAllBtn = selectAllBtn,
            DeselectAllBtn = deselectAllBtn,
            CategoryList = categoryList,
            ListScroll = listScroll,
            LoadingRing = loadingRing,
            LoadingPanel = loadingPanel,
            ResultText = resultText
        };

        scanBtn.Click += async (_, _) =>
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await ScanAsync(scrollViewer, _cts.Token);
        };

        cleanBtn.Click += async (_, _) =>
        {
            if (_categories is null) return;
            cleanBtn.IsEnabled = false;
            scanBtn.IsEnabled = false;
            resultText.Visibility = Visibility.Collapsed;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var cleaned = await Task.Run(() => JunkCleanerService.Clean(_categories, _cts.Token));

            resultText.Text = $"清理完成！释放了 {JunkCleanerService.FormatSize(cleaned)} 空间";
            resultText.Visibility = Visibility.Visible;

            RefreshUI(scrollViewer);
            cleanBtn.IsEnabled = false;
            scanBtn.IsEnabled = true;
        };

        selectAllBtn.Click += (_, _) =>
        {
            if (_categories is null) return;
            foreach (var c in _categories) c.Selected = true;
            RenderCategories(scrollViewer);
        };

        deselectAllBtn.Click += (_, _) =>
        {
            if (_categories is null) return;
            foreach (var c in _categories) c.Selected = false;
            RenderCategories(scrollViewer);
        };

        return scrollViewer;
    }

    private async Task ScanAsync(ScrollViewer root, CancellationToken ct)
    {
        var state = GetState(root);
        if (state is null) return;

        state.LoadingPanel.Visibility = Visibility.Visible;
        state.LoadingRing.IsActive = true;
        state.CategoryList.Children.Clear();
        state.CleanBtn.IsEnabled = false;
        state.ScanBtn.IsEnabled = false;
        state.ResultText.Visibility = Visibility.Collapsed;

        _categories = await JunkCleanerService.ScanAsync(ct);

        RefreshUI(root);
        RenderCategories(root);

        state.LoadingPanel.Visibility = Visibility.Collapsed;
        state.LoadingRing.IsActive = false;
        state.CleanBtn.IsEnabled = _categories.Any(c => c.SizeBytes > 0);
        state.ScanBtn.IsEnabled = true;
    }

    private void RefreshUI(ScrollViewer root)
    {
        var state = GetState(root);
        if (state is null || _categories is null) return;

        var totalSize = _categories.Sum(c => c.SizeBytes);
        var totalFiles = _categories.Sum(c => c.FileCount);
        state.TotalSizeText.Text = JunkCleanerService.FormatSize(totalSize);
        state.TotalFilesText.Text = totalFiles.ToString();
        state.CategoryCountText.Text = _categories.Count.ToString();
    }

    private void RenderCategories(ScrollViewer root)
    {
        var state = GetState(root);
        if (state is null || _categories is null) return;

        state.CategoryList.Children.Clear();

        foreach (var cat in _categories)
        {
            state.CategoryList.Children.Add(CreateCategoryRow(cat, root));
        }

        RefreshUI(root);
        state.CleanBtn.IsEnabled = _categories.Any(c => c.Selected && c.SizeBytes > 0);
    }

    private Border CreateCategoryRow(JunkCategory cat, ScrollViewer root)
    {
        var accent = ParseHex(cat.ColorHex);
        var dimAccent = Color.FromArgb(26, accent.R, accent.G, accent.B);

        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            Background = new SolidColorBrush(dimAccent),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(accent), Glyph = cat.Glyph }
        };

        var nameText = new TextBlock
        {
            Text = cat.Name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };

        var descText = new TextBlock
        {
            Text = cat.Description,
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var sizeText = new TextBlock
        {
            Text = cat.FileCount > 0 ? JunkCleanerService.FormatSize(cat.SizeBytes) : "无文件",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(cat.FileCount > 0 ? accent : ThemeColors.DimText)
        };

        var countText = new TextBlock
        {
            Text = cat.FileCount > 0 ? $"{cat.FileCount} 个文件" : "",
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var toggle = new ToggleSwitch
        {
            IsOn = cat.Selected,
            OnContent = "",
            OffContent = "",
            MinWidth = 76
        };
        toggle.Toggled += (_, _) =>
        {
            cat.Selected = toggle.IsOn;
            GetState(root)?.CleanBtn.IsEnabled = _categories?.Any(c => c.Selected && c.SizeBytes > 0) ?? false;
        };

        var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(nameText);
        infoPanel.Children.Add(descText);

        var sizePanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        sizePanel.Children.Add(sizeText);
        sizePanel.Children.Add(countText);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(iconBorder);
        grid.Children.Add(infoPanel); Grid.SetColumn(infoPanel, 1);
        grid.Children.Add(sizePanel); Grid.SetColumn(sizePanel, 2);
        grid.Children.Add(toggle); Grid.SetColumn(toggle, 3);

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

    private static JunkCleanerState? GetState(ScrollViewer root) => root?.Tag as JunkCleanerState;

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

    private sealed class JunkCleanerState
    {
        public TextBlock TotalSizeText = null!;
        public TextBlock TotalFilesText = null!;
        public TextBlock CategoryCountText = null!;
        public Button ScanBtn = null!;
        public Button CleanBtn = null!;
        public Button SelectAllBtn = null!;
        public Button DeselectAllBtn = null!;
        public StackPanel CategoryList = null!;
        public ScrollViewer ListScroll = null!;
        public ProgressRing LoadingRing = null!;
        public StackPanel LoadingPanel = null!;
        public TextBlock ResultText = null!;
    }
}