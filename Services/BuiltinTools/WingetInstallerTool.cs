using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class WingetInstallerTool : IBuiltinTool
{
    public string Id => "winget-installer";
    public string Name => "软件安装";
    public string Description => "通过 winget 一键安装常用软件，支持批量选择与进度显示。";
    public string Glyph => "\uE896";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var available = await WingetService.IsWingetAvailableAsync();
        if (!available)
        {
            var errDialog = context.CreateDialog("winget 不可用", "确定");
            errDialog.Content = "未检测到 winget，请确认系统已安装 App Installer 并更新至最新版本。";
            await errDialog.ShowAsync();
            return;
        }

        var window = new Window();
        var page = new WingetInstallerPage(window);
        page.RequestedTheme = ThemeService.CurrentElementTheme;

        window.Content = page;
        window.AppWindow.Title = "软件安装";
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 700));

        try
        {
            var mainPos = App.MainWindow?.AppWindow.Position;
            if (mainPos is not null)
            {
                window.AppWindow.Move(new Windows.Graphics.PointInt32(
                    mainPos.Value.X + 40,
                    mainPos.Value.Y + 40));
            }
        }
        catch { }

        window.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        ApplyTitleBarTheme(window);

        window.AppWindow.Closing += (_, args) =>
        {
            if (!page.TryCancelInstall())
            {
                args.Cancel = true;
            }
        };

        window.Activate();

        await page.CheckInstalledStatusAsync();
    }

    private static void ApplyTitleBarTheme(Window window)
    {
        var tb = window.AppWindow.TitleBar;
        var isDark = ThemeService.CurrentTheme == AppTheme.Dark ||
                     (ThemeService.CurrentTheme == AppTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        if (isDark)
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
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
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 100, 100, 100);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);
            tb.BackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.InactiveBackgroundColor = Color.FromArgb(0, 255, 255, 255);
        }

        tb.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
    }
}
