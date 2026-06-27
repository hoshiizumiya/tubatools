using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.Graphics;

namespace TubaWinUi3.Pages;

public sealed partial class UpdateWindow : Window
{
    private UpdateInfo? _updateInfo;
    private UpdateAsset? _portableAsset;
    private UpdateAsset? _installerAsset;
    private CancellationTokenSource? _cts;
    private bool _isDownloading;
    private bool _isPortableMode;
    private string? _downloadedFilePath;

    public bool SkipThisVersion { get; private set; }

    public UpdateWindow(UpdateInfo updateInfo)
    {
        InitializeComponent();

        AppWindow.Title = "图吧工具箱 - 发现新版本";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var screenArea = displayArea.WorkArea;
        var width = (int)(screenArea.Width * 0.42);
        var height = (int)(screenArea.Height * 0.65);
        width = Math.Max(width, 560);
        height = Math.Max(height, 520);
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            (screenArea.Width - width) / 2,
            (screenArea.Height - height) / 2));

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        _isPortableMode = !RuntimeHelper.IsMsixPackaged;

        SetupCardHover(GitCodeCard);
        SetupCardHover(GitHubCard);
        SetupCardHover(SkipCard);
        SetupCardHover(IgnoreCard);

        PopulateUpdateInfo(updateInfo);
    }

    private void SetupCardHover(Border card)
    {
        var hoverBrush = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"];
        var normalBrush = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];

        card.PointerEntered += (s, e) =>
        {
            if (!_isDownloading) card.Background = hoverBrush;
        };
        card.PointerExited += (s, e) =>
        {
            card.Background = normalBrush;
        };
    }

    private void PopulateUpdateInfo(UpdateInfo updateInfo)
    {
        _updateInfo = updateInfo;

        NewVersionText.Text = updateInfo.Version;
        PublishDateText.Text = updateInfo.PublishedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

        var body = updateInfo.Body ?? "暂无更新说明";
        MarkdownTextService.RenderToRichTextBlock(ChangelogText, body);

        if (_isPortableMode)
        {
            _portableAsset = UpdateService.FindBestPortableAsset(updateInfo.Assets);
            _installerAsset = UpdateService.FindBestInstallerAsset(updateInfo.Assets);
        }
        else
        {
            _portableAsset = null;
            _installerAsset = UpdateService.FindBestAsset(updateInfo.Assets);
        }

        var activeAsset = _isPortableMode ? _portableAsset ?? _installerAsset : _installerAsset;
        var hasGitCode = activeAsset is not null && !string.IsNullOrEmpty(activeAsset.GitCodeDownloadUrl);

        GitCodeCard.Visibility = hasGitCode ? Visibility.Visible : Visibility.Collapsed;

        if (activeAsset is null)
        {
            ErrorInfoBar.Message = $"未找到适用于 {UpdateService.CurrentArchitecture} 架构的更新文件";
            ErrorInfoBar.IsOpen = true;
            GitCodeCard.IsHitTestVisible = false;
            GitHubCard.IsHitTestVisible = false;
            GitCodeCard.Opacity = 0.4;
            GitHubCard.Opacity = 0.4;
        }
    }

    private async void OnGitCodeDownloadClick(object sender, TappedRoutedEventArgs e)
    {
        if (_isDownloading) return;

        var asset = _isPortableMode ? _portableAsset ?? _installerAsset : _installerAsset;
        if (asset is null) return;

        await StartDownloadAsync(asset, useGitCode: true);
    }

    private async void OnGitHubDownloadClick(object sender, TappedRoutedEventArgs e)
    {
        if (_isDownloading) return;

        var asset = _isPortableMode ? _portableAsset ?? _installerAsset : _installerAsset;
        if (asset is null) return;

        await StartDownloadAsync(asset, useGitCode: false);
    }

    private void OnSkipVersionClick(object sender, TappedRoutedEventArgs e)
    {
        Close();
    }

    private void OnIgnoreVersionClick(object sender, TappedRoutedEventArgs e)
    {
        SkipThisVersion = true;
        if (_updateInfo is not null)
            UpdateService.SetSkippedVersion(_updateInfo.Version);
        Close();
    }

    private async Task StartDownloadAsync(UpdateAsset asset, bool useGitCode)
    {
        _cts = new CancellationTokenSource();
        _isDownloading = true;
        ActionButtonsPanel.Visibility = Visibility.Collapsed;

        try
        {
            DownloadSection.Visibility = Visibility.Visible;
            DownloadTitleText.Text = useGitCode ? "正在从 GitCode 下载更新" : "正在从 GitHub 下载更新";
            StatusText.Text = "正在下载更新...";
            StatusIcon.Glyph = "\uE896";

            var downloadProgress = new Progress<DownloadProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() => UpdateDownloadProgress(p));
            });

            string filePath;
            if (useGitCode && !string.IsNullOrEmpty(asset.GitCodeDownloadUrl))
            {
                filePath = await UpdateService.DownloadFromGitCodeAsync(asset, downloadProgress, _cts.Token);
            }
            else
            {
                filePath = await UpdateService.DownloadUpdateAsync(asset, downloadProgress, _cts.Token);
            }

            _downloadedFilePath = filePath;
            ShowDownloadComplete(filePath);
        }
        catch (OperationCanceledException)
        {
            ResetToIdle();
        }
        catch (Exception ex)
        {
            ErrorInfoBar.Message = $"下载失败: {ex.Message}";
            ErrorInfoBar.IsOpen = true;
            ResetToIdle();
        }
        finally
        {
            _isDownloading = false;
        }
    }

    private void ResetToIdle()
    {
        DownloadSection.Visibility = Visibility.Collapsed;
        ActionButtonsPanel.Visibility = Visibility.Visible;
        StatusText.Text = "请选择下载源或跳过此版本";
        StatusIcon.Glyph = "\uE946";
    }

    private void UpdateDownloadProgress(DownloadProgress p)
    {
        DownloadProgressBar.Value = p.Percentage;
        DownloadPercentText.Text = $"{p.Percentage:F1}%";
        DownloadSpeedText.Text = UpdateService.FormatSpeed(p.SpeedMbps);
        DownloadSizeText.Text = $"{UpdateService.FormatSize(p.BytesReceived)} / {UpdateService.FormatSize(p.TotalBytes)}";
        DownloadTimeText.Text = UpdateService.FormatTime(p.EstimatedRemaining);
    }

    private void ShowDownloadComplete(string filePath)
    {
        DownloadSection.Visibility = Visibility.Collapsed;
        DownloadCompleteSection.Visibility = Visibility.Visible;

        var isZip = filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var isExe = filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        CompleteFileText.Text = $"文件: {Path.GetFileName(filePath)}";
        CompleteArchText.Text = $"架构: {UpdateService.CurrentArchitecture}";

        if (isZip && _isPortableMode)
        {
            CompleteTipText.Text = "便携版更新：请关闭本程序，将压缩包解压覆盖到当前程序目录即可完成更新";
        }
        else if (isExe)
        {
            CompleteTipText.Text = "点击「立即安装」将关闭本程序并启动安装程序";
        }
        else
        {
            CompleteTipText.Text = "请关闭本程序后解压/安装更新";
        }

        if (isExe)
        {
            ActionButtonText.Text = "立即安装";
            ActionButtonIcon.Glyph = "\uE896;";
        }
        else
        {
            ActionButtonText.Text = "打开文件夹";
            ActionButtonIcon.Glyph = "\uED25;";
        }
        ActionButton.Visibility = Visibility.Visible;

        StatusText.Text = "下载完成";
        StatusIcon.Glyph = "\uE73E";
    }

    private void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_downloadedFilePath)) return;

        var isExe = _downloadedFilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (isExe)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _downloadedFilePath,
                    UseShellExecute = true
                });
                Application.Current.Exit();
            }
            else
            {
                var folder = Path.GetDirectoryName(_downloadedFilePath)!;
                System.Diagnostics.Process.Start("explorer.exe", folder);
            }
        }
        catch { }
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
