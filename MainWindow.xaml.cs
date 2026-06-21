using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3;

public sealed partial class MainWindow : Window
{
    private CancellationTokenSource? _searchCts;
    private SearchResult? _pendingSearchResult;
    private bool _syncingNavSelection;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        AppWindow.SetIcon(iconPath);

        ApplyTitleBarTheme(ElementTheme.Default);

        BackdropService.ApplyBackdrop(this);
        BackdropService.BackdropChanged += OnBackdropChanged;

        WindowSizeService.ApplySavedWindowSize(this);

        Closed += MainWindow_Closed;
        AppWindow.Changed += AppWindow_Changed;

        PopulateCategories();
        NavigateToDefaultPage();

        DispatcherQueue.TryEnqueue(() => PopulateSearchSuggestions());
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        BackdropService.BackdropChanged -= OnBackdropChanged;
        AppWindow.Changed -= AppWindow_Changed;
        WindowSizeService.SaveWindowSize(this);
    }

    private void OnBackdropChanged()
    {
        DispatcherQueue.TryEnqueue(() => BackdropService.ApplyBackdrop(this));
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange) return;
        var size = sender.Size;
        var minWidth = 800;
        var minHeight = 600;
        var needsResize = false;
        var newW = size.Width;
        var newH = size.Height;

        if (size.Width < minWidth)
        {
            newW = minWidth;
            needsResize = true;
        }
        if (size.Height < minHeight)
        {
            newH = minHeight;
            needsResize = true;
        }

        if (needsResize)
        {
            sender.Resize(new Windows.Graphics.SizeInt32(newW, newH));
        }
    }

    public void ApplyTitleBarTheme(ElementTheme theme)
    {
        var tb = AppWindow.TitleBar;
        var isDark = theme == ElementTheme.Dark ||
                     (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        if (isDark)
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 50);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 180, 180, 180);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 30, 30, 30);

            tb.BackgroundColor = Color.FromArgb(255, 32, 32, 32);
            tb.InactiveBackgroundColor = Color.FromArgb(255, 32, 32, 32);
        }
        else
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 100, 100, 100);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);

            tb.BackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.InactiveBackgroundColor = Color.FromArgb(0, 255, 255, 255);
        }

        tb.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
    }

    private async void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingNavSelection) return;

        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "all":
                    NavFrame.Navigate(typeof(HomePage), null);
                    break;
                case "favorites":
                    NavFrame.Navigate(typeof(FavoritesPage));
                    break;
                case "hardware":
                    NavFrame.Navigate(typeof(HardwarePage));
                    break;
                case "builtin":
                    NavFrame.Navigate(typeof(BuiltinToolsPage));
                    break;
                case "community":
                    NavFrame.Navigate(typeof(CommunityToolsPage));
                    break;
                case string category:
                    NavFrame.Navigate(typeof(HomePage), category);
                    break;
            }
        }
    }

    private void NavigateToDefaultPage()
    {
        var defaultPage = AppSettings.Get("DefaultPage") ?? "all";
        NavigationViewItem? targetItem = null;

        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag is string tag && tag == defaultPage)
            {
                targetItem = navItem;
                break;
            }
        }

        if (targetItem is not null)
        {
            NavView.SelectedItem = targetItem;
        }
        else
        {
            NavFrame.Navigate(typeof(HomePage), null);
        }
    }

    private void PopulateCategories()
    {
        while (NavView.MenuItems.Count > 5)
        {
            NavView.MenuItems.RemoveAt(5);
        }

        var categories = ToolCatalog.GetCategories();
        var otherCategory = categories.FirstOrDefault(c => c.Contains("其他"));
        var restCategories = categories.Where(c => !c.Contains("其他"));

        foreach (var category in restCategories)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = category.Replace("工具", ""),
                Tag = category,
                Icon = new FontIcon { Glyph = GetCategoryGlyphStatic(category) }
            });
        }

        if (otherCategory != null)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = otherCategory.Replace("工具", ""),
                Tag = otherCategory,
                Icon = new FontIcon { Glyph = GetCategoryGlyphStatic(otherCategory) }
            });
        }
    }

    public static string GetCategoryGlyphStatic(string category)
    {
        var customGlyph = AppSettings.Get($"CategoryGlyph_{category}");
        if (!string.IsNullOrWhiteSpace(customGlyph))
            return customGlyph;

        if (category.Contains("处理器", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uEEA1";
        }

        if (category.Contains("显卡", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uF211";
        }

        if (category.Contains("显示器", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE7F4";
        }

        if (category.Contains("硬盘", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uEDA2";
        }

        if (category.Contains("内存", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uEEA0";
        }

        if (category.Contains("外设", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE962";
        }

        if (category.Contains("游戏", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE7FC";
        }

        if (category.Contains("声卡", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE7F5";
        }

        if (category.Contains("网卡", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uEDA3";
        }

        if (category.Contains("综合", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uEC4E";
        }

        if (category.Contains("其他", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE712";
        }

        return "\uE8B7";
    }

    public void RefreshToolCategories()
    {
        PopulateCategories();
    }

    public void NavigateToCommunity()
    {
        NavFrame.Navigate(typeof(CommunityToolsPage));
        SyncNavSelection("community");
    }

    private void PopulateSearchSuggestions()
    {
        var items = UnifiedSearchService.GetQuickPanelItems();
        GlobalSearchBox.ItemsSource = items;
    }

    private void GlobalSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        var query = sender.Text.Trim();

        if (query.Length == 0)
        {
            PopulateSearchSuggestions();
            return;
        }

        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;

        _ = SearchAsync(query, cts.Token);
    }

    private async Task SearchAsync(string query, CancellationToken ct)
    {
        try
        {
            var results = await Task.Run(() => UnifiedSearchService.Search(query), ct);
            ct.ThrowIfCancellationRequested();

            DispatcherQueue.TryEnqueue(() =>
            {
                GlobalSearchBox.ItemsSource = results;
            });
        }
        catch (OperationCanceledException) { }
    }

    private void GlobalSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SearchResult result)
        {
            _pendingSearchResult = result;
        }
    }

    private void GlobalSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_pendingSearchResult is not null)
        {
            var result = _pendingSearchResult;
            _pendingSearchResult = null;
            sender.Text = string.Empty;
            HandleSearchResult(result);
        }
        else if (!string.IsNullOrWhiteSpace(args.QueryText))
        {
            var first = (sender.ItemsSource as System.Collections.IList)?
                .Cast<SearchResult>().FirstOrDefault();
            if (first is not null)
            {
                sender.Text = string.Empty;
                HandleSearchResult(first);
            }
        }
    }

    private void GlobalSearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            GlobalSearchBox.Text = string.Empty;
            GlobalSearchBox.IsSuggestionListOpen = false;
            e.Handled = true;
        }
    }

    private void HandleSearchResult(SearchResult result)
    {
        switch (result.Kind)
        {
            case SearchItemKind.ExternalTool:
            case SearchItemKind.CustomTool:
                NavigateToTool(result.MatchKey);
                break;
            case SearchItemKind.BuiltinTool:
                NavFrame.Navigate(typeof(BuiltinToolsPage),
                    new SearchNavigationTarget { HighlightBuiltinId = result.MatchKey });
                SyncNavSelection("builtin");
                break;
            case SearchItemKind.CommunityTool:
                NavFrame.Navigate(typeof(CommunityToolsPage));
                SyncNavSelection("community");
                break;
            case SearchItemKind.Setting:
                NavFrame.Navigate(typeof(SettingsPage),
                    new SearchNavigationTarget { HighlightSettingKey = result.MatchKey });
                break;
            case SearchItemKind.QuickAction:
                HandleQuickAction(result.MatchKey);
                break;
        }
    }

    private void SyncNavSelection(string tag)
    {
        _syncingNavSelection = true;
        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag is string t && t == tag)
            {
                NavView.SelectedItem = navItem;
                break;
            }
        }
        _syncingNavSelection = false;
    }

    private void NavigateToTool(string toolPath)
    {
        try
        {
            var tools = ToolCatalog.GetAllToolsCached();
            var tool = tools.FirstOrDefault(t => t.Path.Equals(toolPath, StringComparison.OrdinalIgnoreCase));
            if (tool is not null)
            {
                NavFrame.Navigate(typeof(HomePage),
                    new SearchNavigationTarget { HighlightToolPath = toolPath });

                if (!string.IsNullOrEmpty(tool.Category))
                    SyncNavSelection(tool.Category);
            }
        }
        catch { }
    }

    private void HandleQuickAction(string action)
    {
        if (!action.StartsWith("navigate:")) return;
        var target = action["navigate:".Length..];

        switch (target)
        {
            case "hardware":
                NavFrame.Navigate(typeof(HardwarePage));
                SyncNavSelection("hardware");
                break;
            case "favorites":
                NavFrame.Navigate(typeof(FavoritesPage));
                SyncNavSelection("favorites");
                break;
            case "builtin":
                NavFrame.Navigate(typeof(BuiltinToolsPage));
                SyncNavSelection("builtin");
                break;
            case "community":
                NavFrame.Navigate(typeof(CommunityToolsPage));
                SyncNavSelection("community");
                break;
            case "settings":
                NavFrame.Navigate(typeof(SettingsPage));
                break;
        }
    }

}
