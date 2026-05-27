using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class HostsEditorTool : IBuiltinTool
{
    public string Id => "hosts-editor";
    public string Name => "Hosts 编辑";
    public string Description => "可视化编辑系统 Hosts 文件，支持启用/禁用规则和 DNS 刷新。";
    public string Glyph => "\uE779";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    private static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);
    private static readonly Color AccentRed = Color.FromArgb(255, 248, 113, 113);
    private static readonly Color AccentOrange = Color.FromArgb(255, 251, 146, 60);
    private static readonly Color AccentPurple = Color.FromArgb(255, 167, 139, 250);

    private List<HostsEntry>? _entries;
    private bool _dirty;

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var dialog = new ContentDialog
        {
            Title = "Hosts 编辑",
            CloseButtonText = "关闭",
            XamlRoot = context.XamlRoot
        };
        dialog.Resources["ContentDialogMaxWidth"] = 900;
        dialog.Resources["ContentDialogMaxHeight"] = 720;

        var content = BuildDialogContent();
        dialog.Content = content;
        dialog.Closing += (s, e) =>
        {
            if (_dirty)
            {
                e.Cancel = true;
                _ = ShowUnsavedWarning(s as ContentDialog);
            }
        };

        _ = LoadEntriesAsync(content);

        await dialog.ShowAsync();
    }

    private async Task ShowUnsavedWarning(ContentDialog? dialog)
    {
        if (dialog is null) return;
        var warn = new ContentDialog
        {
            Title = "未保存更改",
            Content = "Hosts 文件已修改但未保存，是否放弃更改？",
            PrimaryButtonText = "放弃",
            CloseButtonText = "继续编辑",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = dialog.XamlRoot
        };
        var result = await warn.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _dirty = false;
            dialog.Hide();
        }
    }

    private async Task LoadEntriesAsync(ScrollViewer root)
    {
        var state = GetState(root);
        if (state is null) return;

        state.LoadingRing.IsActive = true;
        state.LoadingPanel.Visibility = Visibility.Visible;
        state.ListPanel.Visibility = Visibility.Collapsed;

        _entries = await Task.Run(() => HostsEditorService.Load());
        RenderEntries(root);

        state.LoadingRing.IsActive = false;
        state.LoadingPanel.Visibility = Visibility.Collapsed;
        state.ListPanel.Visibility = Visibility.Visible;
    }

    private void RenderEntries(ScrollViewer root)
    {
        var state = GetState(root);
        if (state is null || _entries is null) return;

        state.ListContainer.Children.Clear();

        var activeCount = _entries.Count(e => e.Enabled && !e.IsComment && !string.IsNullOrEmpty(e.Address));
        var disabledCount = _entries.Count(e => !e.Enabled && !e.IsComment && !string.IsNullOrEmpty(e.Address));
        state.ActiveCountText.Text = activeCount.ToString();
        state.DisabledCountText.Text = disabledCount.ToString();
        state.AdminText.Text = HostsEditorService.IsAdmin ? "是" : "否";
        state.AdminText.Foreground = HostsEditorService.IsAdmin
            ? new SolidColorBrush(AccentGreen)
            : new SolidColorBrush(AccentRed);

        foreach (var entry in _entries)
        {
            var row = CreateEntryRow(entry, root);
            state.ListContainer.Children.Add(row);
        }
    }

    private Border CreateEntryRow(HostsEntry entry, ScrollViewer root)
    {
        if (entry.IsComment)
        {
            if (string.IsNullOrEmpty(entry.Comment))
            {
                return new Border
                {
                    Height = 6,
                    Background = new SolidColorBrush(ThemeColors.Separator)
                };
            }

            return new Border
            {
                Padding = new Thickness(10, 6, 10, 6),
                BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new TextBlock
                {
                    Text = entry.Comment,
                    FontSize = 12,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(ThemeColors.DimText),
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
        }

        var toggle = new ToggleSwitch
        {
            IsOn = entry.Enabled,
            OnContent = "",
            OffContent = "",
            MinWidth = 80
        };

        var addrText = new TextBlock
        {
            Text = entry.Address,
            FontSize = 13,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(entry.Enabled ? AccentBlue : ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var hostText = new TextBlock
        {
            Text = entry.Hostname,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(entry.Enabled ? ThemeColors.PrimaryText : ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var commentText = new TextBlock
        {
            Text = string.IsNullOrEmpty(entry.Comment) ? "" : $"# {entry.Comment}",
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = string.IsNullOrEmpty(entry.Comment) ? 0 : 0.8
        };

        var deleteBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
            Padding = new Thickness(6, 2, 6, 2),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            Tag = entry
        };
        deleteBtn.Click += (_, _) =>
        {
            _entries?.Remove(entry);
            _dirty = true;
            RenderEntries(root);
        };

        var editBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE70F", FontSize = 12 },
            Padding = new Thickness(6, 2, 6, 2),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            Tag = entry
        };
        editBtn.Click += async (_, _) =>
        {
            await EditEntryDialog(root, entry);
        };

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(toggle); Grid.SetColumn(toggle, 0);
        grid.Children.Add(addrText); Grid.SetColumn(addrText, 1);
        grid.Children.Add(hostText); Grid.SetColumn(hostText, 2);
        grid.Children.Add(commentText); Grid.SetColumn(commentText, 3);
        grid.Children.Add(editBtn); Grid.SetColumn(editBtn, 4);
        grid.Children.Add(deleteBtn); Grid.SetColumn(deleteBtn, 5);

        var border = new Border
        {
            Padding = new Thickness(10, 6, 10, 6),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
            Tag = entry
        };

        toggle.Toggled += (_, _) =>
        {
            entry.Enabled = toggle.IsOn;
            _dirty = true;
            UpdateEntryVisual(border, entry);
        };

        return border;
    }

    private static void UpdateEntryVisual(Border border, HostsEntry entry)
    {
        var grid = border.Child as Grid;
        if (grid is null) return;

        foreach (var child in grid.Children)
        {
            if (child is TextBlock { FontFamily: not null } tb)
            {
                if (tb.FontSize == 13 && tb.Text == entry.Address)
                    tb.Foreground = new SolidColorBrush(entry.Enabled ? AccentBlue : ThemeColors.DimText);
                else if (tb.FontSize == 13 && tb.Text == entry.Hostname)
                    tb.Foreground = new SolidColorBrush(entry.Enabled ? ThemeColors.PrimaryText : ThemeColors.DimText);
            }
        }

        border.Background = new SolidColorBrush(entry.Enabled
            ? Color.FromArgb(0, 0, 0, 0)
            : Color.FromArgb(30, 0, 0, 0));
    }

    private async Task EditEntryDialog(ScrollViewer root, HostsEntry entry)
    {
        var state = GetState(root);
        if (state is null) return;

        var addrBox = new TextBox
        {
            Text = entry.Address,
            PlaceholderText = "IP 地址，如 127.0.0.1",
            FontFamily = new FontFamily("Consolas"),
            Header = "IP 地址"
        };
        var hostBox = new TextBox
        {
            Text = entry.Hostname,
            PlaceholderText = "主机名，如 example.com",
            FontFamily = new FontFamily("Consolas"),
            Header = "主机名"
        };
        var commentBox = new TextBox
        {
            Text = entry.Comment,
            PlaceholderText = "可选备注",
            Header = "备注"
        };
        var enabledCheck = new CheckBox { Content = "启用此规则", IsChecked = entry.Enabled };

        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(addrBox);
        panel.Children.Add(hostBox);
        panel.Children.Add(commentBox);
        panel.Children.Add(enabledCheck);

        var dlg = new ContentDialog
        {
            Title = "编辑 Hosts 规则",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = state.XamlRoot
        };

        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            entry.Address = addrBox.Text.Trim();
            entry.Hostname = hostBox.Text.Trim();
            entry.Comment = commentBox.Text.Trim();
            entry.Enabled = enabledCheck.IsChecked ?? true;
            _dirty = true;
            RenderEntries(root);
        }
    }

    private ScrollViewer BuildDialogContent()
    {
        var activeCountText = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(AccentGreen) };
        var disabledCountText = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(AccentOrange) };
        var adminText = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold };

        var activeCard = MakeStatCard("启用规则", activeCountText, "\uE73E", AccentGreen);
        var disabledCard = MakeStatCard("禁用规则", disabledCountText, "\uE894", AccentOrange);
        var adminCard = MakeStatCard("管理员", adminText, "\uE77B", AccentPurple);

        var statsGrid = new Grid { ColumnSpacing = 10 };
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.Children.Add(activeCard); Grid.SetColumn(activeCard, 0);
        statsGrid.Children.Add(disabledCard); Grid.SetColumn(disabledCard, 1);
        statsGrid.Children.Add(adminCard); Grid.SetColumn(adminCard, 2);

        var addBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE710", FontSize = 12 },
                    new TextBlock { Text = "添加规则" }
                }
            }
        };
        var saveBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE74E", FontSize = 12 },
                    new TextBlock { Text = "保存" }
                }
            }
        };
        var backupBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE787", FontSize = 12 },
                    new TextBlock { Text = "备份" }
                }
            }
        };
        var flushBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 12 },
                    new TextBlock { Text = "刷新DNS" }
                }
            }
        };
        var reloadBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE777", FontSize = 12 },
                    new TextBlock { Text = "重载" }
                }
            }
        };

        var actionBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionBar.Children.Add(addBtn);
        actionBar.Children.Add(saveBtn);
        actionBar.Children.Add(backupBtn);
        actionBar.Children.Add(flushBtn);
        actionBar.Children.Add(reloadBtn);

        var headerGrid = new Grid { ColumnSpacing = 12, Padding = new Thickness(12, 6, 12, 6) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        AddHeader(headerGrid, "状态", 0);
        AddHeader(headerGrid, "IP 地址", 1);
        AddHeader(headerGrid, "主机名", 2);
        AddHeader(headerGrid, "备注", 3);
        AddHeader(headerGrid, "操作", 4);

        var headerBorder = new Border
        {
            Background = new SolidColorBrush(ThemeColors.HeaderBg),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Child = headerGrid
        };

        var listContainer = new StackPanel();
        var listScroll = new ScrollViewer
        {
            Content = listContainer,
            MaxHeight = 380,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var listBorder = new Border
        {
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0, 0, 6, 6),
            Child = listScroll
        };

        var pathText = new TextBlock
        {
            Text = HostsEditorService.HostsPath,
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            IsTextSelectionEnabled = true
        };

        var loadingRing = new ProgressRing { Width = 36, Height = 36, IsActive = true };
        var loadingText = new TextBlock { Text = "正在加载 Hosts 文件...", FontSize = 13, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var loadingPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Padding = new Thickness(0, 30, 0, 30),
            Children = { loadingRing, loadingText }
        };

        var contentGrid = new Grid { RowSpacing = 14 };
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        contentGrid.Children.Add(statsGrid); Grid.SetRow(statsGrid, 0);
        contentGrid.Children.Add(actionBar); Grid.SetRow(actionBar, 1);
        contentGrid.Children.Add(headerBorder); Grid.SetRow(headerBorder, 2);
        contentGrid.Children.Add(listBorder); Grid.SetRow(listBorder, 3);
        contentGrid.Children.Add(loadingPanel); Grid.SetRow(loadingPanel, 4);

        var root = new StackPanel { Spacing = 14, MaxWidth = 860 };
        root.Children.Add(new TextBlock
        {
            Text = "可视化编辑系统 Hosts 文件，支持启用/禁用规则、添加/删除/编辑条目",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });
        root.Children.Add(pathText);
        root.Children.Add(contentGrid);

        var scrollViewer = new ScrollViewer { Content = root, MaxWidth = 900 };
        scrollViewer.Tag = new HostsEditorState
        {
            AddBtn = addBtn,
            SaveBtn = saveBtn,
            BackupBtn = backupBtn,
            FlushBtn = flushBtn,
            ReloadBtn = reloadBtn,
            ActiveCountText = activeCountText,
            DisabledCountText = disabledCountText,
            AdminText = adminText,
            ListContainer = listContainer,
            ListScroll = listScroll,
            LoadingRing = loadingRing,
            LoadingPanel = loadingPanel,
            ListPanel = listBorder
        };

        addBtn.Click += async (_, _) =>
        {
            var newEntry = new HostsEntry { Enabled = true, Address = "127.0.0.1", Hostname = "", Comment = "" };
            await EditEntryDialog(scrollViewer, newEntry);
            if (!string.IsNullOrEmpty(newEntry.Hostname))
            {
                _entries ??= [];
                _entries.Add(newEntry);
                _dirty = true;
                RenderEntries(scrollViewer);
            }
        };

        saveBtn.Click += async (_, _) =>
        {
            if (_entries is null) return;
            try
            {
                await Task.Run(() => HostsEditorService.Save(_entries));
                _dirty = false;
                ShowToast(scrollViewer, "已保存", "Hosts 文件保存成功", AccentGreen);
            }
            catch (UnauthorizedAccessException)
            {
                ShowToast(scrollViewer, "权限不足", "请以管理员身份运行本程序", AccentRed);
            }
            catch (Exception ex)
            {
                ShowToast(scrollViewer, "保存失败", ex.Message, AccentRed);
            }
        };

        backupBtn.Click += (_, _) =>
        {
            try
            {
                HostsEditorService.Backup();
                ShowToast(scrollViewer, "备份成功", "已创建 Hosts 备份文件", AccentGreen);
            }
            catch (Exception ex)
            {
                ShowToast(scrollViewer, "备份失败", ex.Message, AccentRed);
            }
        };

        flushBtn.Click += (_, _) =>
        {
            try
            {
                HostsEditorService.FlushDns();
                ShowToast(scrollViewer, "DNS 已刷新", "DNS 缓存已清除", AccentBlue);
            }
            catch (Exception ex)
            {
                ShowToast(scrollViewer, "刷新失败", ex.Message, AccentRed);
            }
        };

        reloadBtn.Click += async (_, _) =>
        {
            _dirty = false;
            await LoadEntriesAsync(scrollViewer);
        };

        return scrollViewer;
    }

    private static void ShowToast(ScrollViewer root, string title, string message, Color accent)
    {
        var state = root?.Tag as HostsEditorState;
        if (state is null) return;

        var infoBar = new InfoBar
        {
            Title = title,
            Message = message,
            Severity = accent == AccentGreen ? InfoBarSeverity.Success
                     : accent == AccentRed ? InfoBarSeverity.Error
                     : InfoBarSeverity.Informational,
            IsOpen = true,
            IsClosable = true
        };

        var parent = VisualTreeHelper.GetParent(root!);
        while (parent is not null && parent is not Panel)
            parent = VisualTreeHelper.GetParent(parent);

        if (parent is Panel panel)
        {
            panel.Children.Add(infoBar);
        }
    }

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

    private static void AddHeader(Grid grid, string text, int column)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        grid.Children.Add(tb);
        Grid.SetColumn(tb, column);
    }

    private static HostsEditorState? GetState(ScrollViewer root) => root?.Tag as HostsEditorState;

    private sealed class HostsEditorState
    {
        public Button AddBtn = null!;
        public Button SaveBtn = null!;
        public Button BackupBtn = null!;
        public Button FlushBtn = null!;
        public Button ReloadBtn = null!;
        public TextBlock ActiveCountText = null!;
        public TextBlock DisabledCountText = null!;
        public TextBlock AdminText = null!;
        public StackPanel ListContainer = null!;
        public ScrollViewer ListScroll = null!;
        public ProgressRing LoadingRing = null!;
        public StackPanel LoadingPanel = null!;
        public Border ListPanel = null!;
        public XamlRoot XamlRoot => AddBtn.XamlRoot;
    }
}
