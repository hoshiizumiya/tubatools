using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;
using TubaWinUi3.Models;
using System.Reflection;

namespace TubaWinUi3.Pages;

public sealed partial class GeneralSettingsPage : Page
{
    private bool _isCheckingUpdate;
    private bool _isCheckingToolsBundle;
    private bool _compactModeInitializing;
    private bool _fastModeInitializing;
    private bool _rememberWindowInitializing;
    private bool _defaultPageInitializing;

    private static readonly (string Tag, string DisplayName)[] DefaultPageOptions =
    [
        ("all", "全部工具"),
        ("favorites", "常用"),
        ("hardware", "硬件信息"),
        ("builtin", "内置工具"),
        ("community", "社区工具"),
    ];

    private string? _pendingHighlightKey;

    private static readonly Dictionary<string, string> SettingKeyToCardName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Theme"] = "SettingsThemeCard",
        ["CompactMode"] = "SettingsCompactModeCard",
        ["DefaultPage"] = "SettingsDefaultPageCard",
        ["FastMode"] = "SettingsFastModeCard",
        ["RememberWindow"] = "SettingsRememberWindowCard",
        ["Update"] = "SettingsUpdateCard",
        ["ToolsBundle"] = "SettingsToolsBundleCard",
    };

    public GeneralSettingsPage()
    {
        InitializeComponent();

        InitThemeComboBox();
        InitCompactModeToggle();
        InitDefaultPageComboBox();
        InitFastModeToggle();
        InitRememberWindowToggle();
        InitUpdateSection();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is SearchNavigationTarget target && target.HighlightSettingKey is not null)
        {
            _pendingHighlightKey = target.HighlightSettingKey;
        }

        if (_pendingHighlightKey is not null)
        {
            _ = HighlightSettingAsync(_pendingHighlightKey);
            _pendingHighlightKey = null;
        }
    }

    private async Task HighlightSettingAsync(string settingKey)
    {
        if (!SettingKeyToCardName.TryGetValue(settingKey, out var cardName)) return;

        await Task.Delay(300);

        var border = FindName(cardName) as Border;
        if (border is null) return;

        border.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalAlignmentRatio = 0.5
        });

        await Task.Delay(500);
        SearchHighlightService.HighlightBorder(border);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.GoBack();
    }

    private void InitThemeComboBox()
    {
        ThemeComboBox.Items.Add("跟随系统");
        ThemeComboBox.Items.Add("浅色");
        ThemeComboBox.Items.Add("深色");
        ThemeComboBox.SelectedIndex = ThemeService.CurrentTheme switch
        {
            AppTheme.Light => 1,
            AppTheme.Dark => 2,
            _ => 0
        };
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var theme = ThemeComboBox.SelectedIndex switch
        {
            1 => AppTheme.Light,
            2 => AppTheme.Dark,
            _ => AppTheme.Default
        };
        ThemeService.SetTheme(theme);
    }

    private void InitCompactModeToggle()
    {
        _compactModeInitializing = true;
        CompactModeToggle.IsOn = CompactModeService.IsCompactModeEnabled();
        _compactModeInitializing = false;
    }

    private void CompactModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_compactModeInitializing) return;
        CompactModeService.SetCompactModeEnabled(CompactModeToggle.IsOn);
    }

    private void InitDefaultPageComboBox()
    {
        _defaultPageInitializing = true;
        DefaultPageComboBox.Items.Clear();
        var saved = AppSettings.Get("DefaultPage") ?? "all";

        for (var i = 0; i < DefaultPageOptions.Length; i++)
        {
            DefaultPageComboBox.Items.Add(DefaultPageOptions[i].DisplayName);
            if (DefaultPageOptions[i].Tag == saved)
                DefaultPageComboBox.SelectedIndex = i;
        }

        if (DefaultPageComboBox.SelectedIndex < 0)
            DefaultPageComboBox.SelectedIndex = 0;

        _defaultPageInitializing = false;
    }

    private void DefaultPageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_defaultPageInitializing) return;
        if (DefaultPageComboBox.SelectedIndex >= 0 && DefaultPageComboBox.SelectedIndex < DefaultPageOptions.Length)
            AppSettings.Set("DefaultPage", DefaultPageOptions[DefaultPageComboBox.SelectedIndex].Tag);
    }

    private void InitFastModeToggle()
    {
        _fastModeInitializing = true;
        FastModeToggle.IsOn = FastModeService.IsFastModeEnabled();
        _fastModeInitializing = false;
    }

    private void FastModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_fastModeInitializing) return;
        FastModeService.SetFastModeEnabled(FastModeToggle.IsOn);
    }

    private void InitRememberWindowToggle()
    {
        _rememberWindowInitializing = true;
        RememberWindowToggle.IsOn = WindowSizeService.IsRememberEnabled();
        _rememberWindowInitializing = false;
    }

    private void RememberWindowToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_rememberWindowInitializing) return;
        WindowSizeService.SetRememberEnabled(RememberWindowToggle.IsOn);
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCheckingUpdate) return;
        _isCheckingUpdate = true;
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查更新...";

        try
        {
            var update = await UpdateService.CheckForUpdateAsync();

            if (update is not null)
            {
                UpdateStatusText.Text = $"发现新版本 v{update.Version}";
                var window = new UpdateWindow(update);
                window.Activate();
            }
            else
            {
                UpdateStatusText.Text = "已是最新版本";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"检查失败: {ex.Message}";
        }
        finally
        {
            _isCheckingUpdate = false;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void InitUpdateSection()
    {
        if (RuntimeHelper.IsMsixPackaged)
        {
            SettingsUpdateCard.Visibility = Visibility.Collapsed;
            SettingsToolsBundleCard.Visibility = Visibility.Visible;

            var currentVersion = ToolsBundleService.GetCurrentVersion();
            if (currentVersion is not null)
            {
                ToolsBundleStatusText.Text = $"当前工具包版本 v{currentVersion}";
            }
            else if (!ToolsBundleService.IsToolsBundleReady())
            {
                ToolsBundleStatusText.Text = "工具包未下载";
            }
            else
            {
                ToolsBundleStatusText.Text = "工具包已就绪（版本未知）";
            }
        }
        else
        {
            SettingsUpdateCard.Visibility = Visibility.Visible;
            SettingsToolsBundleCard.Visibility = Visibility.Collapsed;
        }
    }

    private async void CheckToolsBundleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCheckingToolsBundle) return;
        _isCheckingToolsBundle = true;
        CheckToolsBundleButton.IsEnabled = false;
        ToolsBundleStatusText.Text = "正在检查工具包更新...";

        try
        {
            if (!ToolsBundleService.IsToolsBundleReady())
            {
                var dialog = new ToolsBundleDownloadDialog
                {
                    XamlRoot = XamlRoot,
                    RequestedTheme = ThemeService.CurrentElementTheme
                };
                await dialog.ShowDownloadAsync();

                if (dialog.DownloadSucceeded)
                {
                    var v = ToolsBundleService.GetCurrentVersion();
                    ToolsBundleStatusText.Text = v is not null
                        ? $"当前工具包版本 v{v}"
                        : "工具包已就绪";
                }
                else
                {
                    ToolsBundleStatusText.Text = "工具包未下载";
                }
                return;
            }

            var info = await ToolsBundleService.CheckForToolsUpdateAsync();

            if (info is not null && info.HasUpdate)
            {
                ToolsBundleStatusText.Text = $"发现新版本 v{info.Version}";
                var dialog = new ToolsBundleDownloadDialog
                {
                    XamlRoot = XamlRoot,
                    RequestedTheme = ThemeService.CurrentElementTheme
                };
                await dialog.ShowDownloadAsync(info);

                if (dialog.DownloadSucceeded)
                {
                    var v = ToolsBundleService.GetCurrentVersion();
                    ToolsBundleStatusText.Text = v is not null
                        ? $"当前工具包版本 v{v}"
                        : "工具包已就绪";
                }
                else
                {
                    ToolsBundleStatusText.Text = "点击检查工具包是否有新版本";
                }
            }
            else if (info is not null)
            {
                ToolsBundleStatusText.Text = $"当前工具包已是最新版本 (v{info.Version})";
            }
            else
            {
                var currentVersion = ToolsBundleService.GetCurrentVersion();
                ToolsBundleStatusText.Text = currentVersion is not null
                    ? $"当前工具包版本 v{currentVersion}"
                    : "检查失败，请稍后重试";
            }
        }
        catch (Exception ex)
        {
            ToolsBundleStatusText.Text = $"检查失败: {ex.Message}";
        }
        finally
        {
            _isCheckingToolsBundle = false;
            CheckToolsBundleButton.IsEnabled = true;
        }
    }
}
