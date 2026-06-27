using System.Diagnostics;
using System.Security.Principal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;

namespace TubaWinUi3;

public partial class App : Application
{
    private Window? _window;
    public static Window? MainWindow => ((App)Current)?._window;

    public App()
    {
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
        InitializeComponent();
        BuiltinToolRegistry.RegisterDefaults();

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        UnhandledException += OnWinUIUnhandledException;
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void ElevateAndRestart()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

        try
        {
            Process.Start(new ProcessStartInfo(exePath)
            {
                Verb = "runas",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!RuntimeHelper.IsMsixPackaged && !IsRunningAsAdmin())
        {
            ElevateAndRestart();
            Exit();
            return;
        }

        _window = new MainWindow();
        _window.Activate();
        ThemeService.ApplySavedTheme();
        _ = Task.Run(() => ToolIconService.CleanExpiredCache());
        HardwareInfoService.Preload();

        _ = RunStartupSequenceAsync();
    }

    private static async Task RunStartupSequenceAsync()
    {
        try
        {
            if (AppSettings.Get("SetupCompleted") == null)
            {
                await Task.Delay(500);

                if (MainWindow?.Content is FrameworkElement root)
                {
                    var wizard = new SetupWizardDialog
                    {
                        XamlRoot = root.XamlRoot,
                        RequestedTheme = ThemeService.CurrentElementTheme
                    };
                    await wizard.ShowAsync();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Setup] Wizard failed: {ex.Message}");
        }
        finally
        {
            if (AppSettings.Get("SetupCompleted") == null)
                AppSettings.Set("SetupCompleted", true);
        }

        if (RuntimeHelper.IsMsixPackaged)
        {
            if (!ToolsBundleService.IsToolsBundleReady())
            {
                await ShowToolsBundleDownloadDialogAsync();
            }
            _ = CheckForToolsUpdateSilentAsync();
        }
        else
        {
            _ = CheckForUpdateSilentAsync();
        }
    }

    private static async Task ShowToolsBundleDownloadDialogAsync()
    {
        try
        {
            if (MainWindow?.Content is FrameworkElement root)
            {
                var dialog = new ToolsBundleDownloadDialog
                {
                    XamlRoot = root.XamlRoot,
                    RequestedTheme = ThemeService.CurrentElementTheme
                };
                await dialog.ShowDownloadAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ToolsBundle] Download dialog failed: {ex.Message}");
        }
    }

    private static async Task CheckForToolsUpdateSilentAsync()
    {
        try
        {
            if (!ToolsBundleService.IsToolsBundleReady()) return;

            var currentVersion = ToolsBundleService.GetCurrentVersion();
            if (currentVersion is null) return;

            var info = await ToolsBundleService.CheckForToolsUpdateAsync();
            if (info is null || !info.HasUpdate) return;

            if (MainWindow?.DispatcherQueue is null) return;

            MainWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                if (MainWindow?.Content is not FrameworkElement root) return;
                var dialog = new ToolsBundleDownloadDialog
                {
                    XamlRoot = root.XamlRoot,
                    RequestedTheme = ThemeService.CurrentElementTheme
                };
                dialog.SetDescription("发现工具包新版本，建议更新以获取最新工具。");
                await dialog.ShowDownloadAsync(info);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ToolsBundle] Update check failed: {ex.Message}");
        }
    }

    private static async Task CheckForUpdateSilentAsync()
    {
        try
        {
            var update = await UpdateService.CheckForUpdateAsync();
            if (update is null) return;

            var skipped = UpdateService.GetSkippedVersion();
            if (skipped == update.Version) return;

            if (MainWindow?.DispatcherQueue is null) return;

            MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                var window = new UpdateWindow(update);
                window.Activate();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Update] Silent check failed: {ex.Message}");
        }
    }

    private static Exception? _pendingException;

    private void OnUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        _pendingException = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "未知错误");
        NavigateToErrorPage();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _pendingException = e.Exception;
        NavigateToErrorPage();
        e.SetObserved();
    }

    private void OnWinUIUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _pendingException = e.Exception ?? new Exception(e.Message);
        NavigateToErrorPage();
        e.Handled = true;
    }

    public static Exception? ConsumePendingException()
    {
        var ex = _pendingException;
        _pendingException = null;
        return ex;
    }

    private void NavigateToErrorPage()
    {
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            var errorWindow = new Pages.ErrorWindow();
            errorWindow.Activate();
        });
    }
}
