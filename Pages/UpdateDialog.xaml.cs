using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class UpdateDialog : ContentDialog
{
    private UpdateInfo? _updateInfo;
    private UpdateAsset? _portableAsset;
    private UpdateAsset? _installerAsset;
    private CancellationTokenSource? _cts;
    private bool _isDownloading;
    private bool _isPortableMode;

    public bool SkipThisVersion { get; private set; }

    public UpdateDialog()
    {
        InitializeComponent();
        XamlRoot = App.MainWindow?.Content?.XamlRoot;
        _isPortableMode = !RuntimeHelper.IsMsixPackaged;

        SetupCardHover(GitCodeCard);
        SetupCardHover(GitHubCard);
        SetupCardHover(SkipCard);
        SetupCardHover(IgnoreCard);
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

    public async Task ShowUpdateAsync(UpdateInfo updateInfo)
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

        await ShowAsync();
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
        Hide();
    }

    private void OnIgnoreVersionClick(object sender, TappedRoutedEventArgs e)
    {
        SkipThisVersion = true;
        if (_updateInfo is not null)
            UpdateService.SetSkippedVersion(_updateInfo.Version);
        Hide();
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

            Hide();
            await ShowDownloadCompleteDialog(filePath);
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
    }

    private void UpdateDownloadProgress(DownloadProgress p)
    {
        DownloadProgressBar.Value = p.Percentage;
        DownloadPercentText.Text = $"{p.Percentage:F1}%";
        DownloadSpeedText.Text = UpdateService.FormatSpeed(p.SpeedMbps);
        DownloadSizeText.Text = $"{UpdateService.FormatSize(p.BytesReceived)} / {UpdateService.FormatSize(p.TotalBytes)}";
        DownloadTimeText.Text = UpdateService.FormatTime(p.EstimatedRemaining);
    }

    private async Task ShowDownloadCompleteDialog(string filePath)
    {
        var isZip = filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var isExe = filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        var dialog = new ContentDialog
        {
            Title = "下载完成",
            XamlRoot = XamlRoot,
            PrimaryButtonText = isExe ? "立即安装" : "打开文件夹",
            SecondaryButtonText = "稍后手动处理",
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var stack = new StackPanel { Spacing = 12 };

        var successBorder = new Border
        {
            Padding = new Thickness(24, 20, 24, 20),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };

        var grid = new Grid { ColumnSpacing = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconBorder = new Border
        {
            Width = 52,
            Height = 52,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Green),
            CornerRadius = new CornerRadius(14)
        };
        var checkIcon = new FontIcon
        {
            Glyph = "\uE73E",
            FontSize = 26,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        };
        iconBorder.Child = checkIcon;
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        var infoStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4
        };
        infoStack.Children.Add(new TextBlock
        {
            Text = "更新已下载完成",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = $"文件: {Path.GetFileName(filePath)}",
            FontSize =12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = $"架构: {UpdateService.CurrentArchitecture}",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        string tipText;
        if (isZip && _isPortableMode)
        {
            tipText = "便携版更新：请关闭本程序，将压缩包解压覆盖到当前程序目录即可完成更新";
        }
        else if (isExe)
        {
            tipText = "点击「立即安装」将关闭本程序并启动安装程序";
        }
        else
        {
            tipText = "请关闭本程序后解压/安装更新";
        }

        infoStack.Children.Add(new TextBlock
        {
            Text = tipText,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(infoStack, 1);
        grid.Children.Add(infoStack);

        successBorder.Child = grid;
        stack.Children.Add(successBorder);
        dialog.Content = stack;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                if (isExe)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                    Application.Current.Exit();
                }
                else
                {
                    var folder = Path.GetDirectoryName(filePath)!;
                    System.Diagnostics.Process.Start("explorer.exe", folder);
                }
            }
            catch
            {
            }
        }
    }
}
