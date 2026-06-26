using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Reflection;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class SettingsPage : Page
{
    private string? _pendingNavigateSubPage;
    private string? _pendingHighlightKey;

    private static readonly Dictionary<string, string> SettingKeyToSubPage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Theme"] = "general",
        ["CompactMode"] = "general",
        ["DefaultPage"] = "general",
        ["FastMode"] = "general",
        ["RememberWindow"] = "general",
        ["Update"] = "general",
        ["ToolsBundle"] = "general",
        ["Background"] = "appearance",
        ["Backdrop"] = "appearance",
        ["BrandLogo"] = "appearance",
        ["Watermark"] = "appearance",
        ["HardwareFitScreen"] = "hardware",
        ["HardwareMultiDeviceNewLine"] = "hardware",
        ["MonitorDriver"] = "hardware",
        ["AiApiEndpoint"] = "hardware",
        ["AiModelName"] = "hardware",
        ["AiApiKey"] = "hardware",
        ["ConfigManager"] = "tools",
        ["CustomToolManager"] = "tools",
        ["ExportApp"] = "tools",
        ["CommunityTool"] = "tools",
    };

    public SettingsPage()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is not null
            ? $"版本 {version.Major}.{version.Minor}.{version.Build}"
            : "版本 1.0.0";

        LoadSettingsGif();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is SearchNavigationTarget target && target.HighlightSettingKey is not null)
        {
            _pendingHighlightKey = target.HighlightSettingKey;
        }

        if (_pendingHighlightKey is not null && SettingKeyToSubPage.TryGetValue(_pendingHighlightKey, out var subPage))
        {
            _pendingNavigateSubPage = subPage;
            var key = _pendingHighlightKey;
            _pendingHighlightKey = null;
            _ = NavigateToSubPageAsync(subPage, key);
        }
    }

    private void LoadSettingsGif()
    {
        try
        {
            var gifPath = Path.Combine(AppContext.BaseDirectory, "Assets", "settings.gif");
            if (File.Exists(gifPath))
            {
                var bitmap = new BitmapImage(new Uri(gifPath)) { AutoPlay = true };
                SettingsGifImage.Source = bitmap;
            }
        }
        catch
        {
        }
    }

    private void NavGeneral_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        => Frame.Navigate(typeof(GeneralSettingsPage));

    private void NavAppearance_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        => Frame.Navigate(typeof(AppearanceSettingsPage));

    private void NavHardwareAi_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        => Frame.Navigate(typeof(HardwareAiSettingsPage));

    private void NavToolsCommunity_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        => Frame.Navigate(typeof(ToolsCommunitySettingsPage));

    private void NavCredits_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        => Frame.Navigate(typeof(CreditsSettingsPage));

    private void NavWhatsNew_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        var window = new WhatsNewWindow();
        window.Activate();
    }

    private async Task NavigateToSubPageAsync(string subPage, string highlightKey)
    {
        Type pageType = subPage switch
        {
            "general" => typeof(GeneralSettingsPage),
            "appearance" => typeof(AppearanceSettingsPage),
            "hardware" => typeof(HardwareAiSettingsPage),
            "tools" => typeof(ToolsCommunitySettingsPage),
            _ => typeof(GeneralSettingsPage)
        };

        await Task.Delay(100);
        Frame.Navigate(pageType, new SearchNavigationTarget { HighlightSettingKey = highlightKey });
    }

    public static Type? ResolveSubPage(string settingKey)
    {
        if (!SettingKeyToSubPage.TryGetValue(settingKey, out var subPage)) return null;
        return subPage switch
        {
            "general" => typeof(GeneralSettingsPage),
            "appearance" => typeof(AppearanceSettingsPage),
            "hardware" => typeof(HardwareAiSettingsPage),
            "tools" => typeof(ToolsCommunitySettingsPage),
            _ => null
        };
    }
}
