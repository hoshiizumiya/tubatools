using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices;
using TubaWinUi3;
using TubaWinUi3.Services;
using TubaWinUi3.Models;

namespace TubaWinUi3.Pages;

public sealed partial class ToolsCommunitySettingsPage : Page
{
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
    private static extern bool GetOpenFileName(ref OPENFILENAME ofn);

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetSaveFileName(ref OPENFILENAME ofn);

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_PATHMUSTEXIST = 0x00000800;

    private string? _pendingHighlightKey;

    private static readonly Dictionary<string, string> SettingKeyToCardName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ConfigManager"] = "SettingsConfigManagerCard",
        ["CustomToolManager"] = "SettingsCustomToolCard",
        ["ExportApp"] = "SettingsExportAppCard",
        ["CommunityTool"] = "SettingsCommunityCard",
    };

    public ToolsCommunitySettingsPage()
    {
        InitializeComponent();
        InitGitHubLoginStatus();
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

    private void ConfigManagerButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConfigManagerDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        _ = dialog.ShowAsync();
    }

    private void CustomToolManagerButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new CustomToolManagerWindow();
        window.Activate();
    }

    private async void ExportAppButton_Click(object sender, RoutedEventArgs e)
    {
        var exportPath = PickSaveFile("导出当前软件", "压缩包\0*.zip\0所有文件\0*.*\0\0", "TubaWinUi3-Custom.zip", "zip");
        if (string.IsNullOrWhiteSpace(exportPath))
            return;

        if (!exportPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            exportPath += ".zip";

        ExportAppButton.IsEnabled = false;
        ExportAppStatusText.Text = "正在打包当前软件...";

        try
        {
            await CustomToolPackageService.ExportCurrentAppAsync(exportPath);
            ExportAppStatusText.Text = $"已导出 {Path.GetFileName(exportPath)}";
            await ShowMessageAsync("导出完成", $"已保存到：\n{exportPath}");
        }
        catch (Exception ex)
        {
            ExportAppStatusText.Text = $"导出失败: {ex.Message}";
            await ShowMessageAsync("导出失败", ex.Message);
        }
        finally
        {
            ExportAppButton.IsEnabled = true;
        }
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

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = "确定",
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        await dialog.ShowAsync();
    }

    private async void InitGitHubLoginStatus()
    {
        try
        {
            if (GitHubAuthService.IsLoggedIn)
            {
                var user = await GitHubAuthService.GetCurrentUserAsync();
                if (user is not null)
                {
                    GitHubLoginStatusText.Text = $"已登录：{user.Name ?? user.Login}";
                    GitHubLoginButton.Visibility = Visibility.Collapsed;
                    GitHubLogoutButton.Visibility = Visibility.Visible;
                    GitHubAvatar.Visibility = Visibility.Visible;

                    if (!string.IsNullOrWhiteSpace(user.AvatarUrl))
                    {
                        GitHubAvatar.ProfilePicture = new BitmapImage(new Uri(user.AvatarUrl));
                    }
                    return;
                }
            }

            GitHubLoginStatusText.Text = "未登录";
            GitHubLoginButton.Visibility = Visibility.Visible;
            GitHubLogoutButton.Visibility = Visibility.Collapsed;
            GitHubAvatar.Visibility = Visibility.Collapsed;
        }
        catch
        {
            GitHubLoginStatusText.Text = "未登录";
        }
    }

    private async void GitHubLoginButton_Click(object sender, RoutedEventArgs e)
    {
        var token = await GitHubAuthService.StartDeviceFlowAsync(XamlRoot);
        InitGitHubLoginStatus();
    }

    private void GitHubLogoutButton_Click(object sender, RoutedEventArgs e)
    {
        GitHubAuthService.Logout();
        InitGitHubLoginStatus();
    }

    private async void CommunitySubmitButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.NavigateToCommunity();
        }
    }
}
