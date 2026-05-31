using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class WingetInstallerPage : Page
{
    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);
    private static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color AccentOrange = Color.FromArgb(255, 251, 191, 36);
    private static readonly Color AccentRed = Color.FromArgb(255, 248, 113, 113);

    private readonly Window _window;
    private List<WingetPackage>? _packages;
    private CancellationTokenSource? _cts;
    private bool _isInstalling;

    public WingetInstallerPage(Window window)
    {
        InitializeComponent();
        _window = window;

        CategoryFilter.Items.Add("全部分类");
        foreach (var cat in WingetService.GetCategories())
        {
            CategoryFilter.Items.Add(cat);
        }
        CategoryFilter.SelectedIndex = 0;

        CategoryFilter.SelectionChanged += CategoryFilter_SelectionChanged;
        InstallBtn.Click += InstallBtn_Click;
        SelectAllBtn.Click += SelectAllBtn_Click;
        DeselectAllBtn.Click += DeselectAllBtn_Click;
        SelectNotInstalledBtn.Click += SelectNotInstalledBtn_Click;

        LoadCatalog();
    }

    public async Task CheckInstalledStatusAsync()
    {
        if (_packages is null) return;

        LoadingPanel.Visibility = Visibility.Visible;
        LoadingRing.IsActive = true;
        LoadingText.Text = "正在检测已安装软件...";
        InstallBtn.IsEnabled = false;

        var packages = _packages.ToList();

        await Task.Run(async () =>
        {
            var tasks = packages.Select(async p =>
            {
                var installed = await WingetService.IsInstalledAsync(p.Id);
                p.State = installed ? WingetInstallState.Installed : WingetInstallState.NotInstalled;
                p.StatusText = installed ? "已安装" : "未安装";
            });
            await Task.WhenAll(tasks);
        });

        RefreshStats();
        RenderPackages(GetCurrentFilter());

        LoadingPanel.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
        InstallBtn.IsEnabled = true;
    }

    private void LoadCatalog()
    {
        _packages = WingetService.GetCatalog();
        RenderPackages(null);
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = CategoryFilter.SelectedItem as string;
        RenderPackages(selected == "全部分类" ? null : selected);
    }

    private async void InstallBtn_Click(object sender, RoutedEventArgs e)
    {
        await InstallSelectedAsync();
    }

    private void SelectAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_packages is null) return;
        foreach (var p in _packages) p.IsSelected = true;
        RenderPackages(GetCurrentFilter());
    }

    private void DeselectAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_packages is null) return;
        foreach (var p in _packages) p.IsSelected = false;
        RenderPackages(GetCurrentFilter());
    }

    private void SelectNotInstalledBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_packages is null) return;
        foreach (var p in _packages) p.IsSelected = p.State != WingetInstallState.Installed;
        RenderPackages(GetCurrentFilter());
    }

    private async Task InstallSelectedAsync()
    {
        if (_packages is null) return;

        var toInstall = _packages.Where(p => p.IsSelected && p.State != WingetInstallState.Installed).ToList();
        if (toInstall.Count == 0)
        {
            ResultText.Text = "没有需要安装的软件";
            ResultText.Foreground = new SolidColorBrush(AccentOrange);
            ResultText.Visibility = Visibility.Visible;
            return;
        }

        _isInstalling = true;
        InstallBtn.IsEnabled = false;
        SelectAllBtn.IsEnabled = false;
        DeselectAllBtn.IsEnabled = false;
        SelectNotInstalledBtn.IsEnabled = false;
        CategoryFilter.IsEnabled = false;
        ResultText.Visibility = Visibility.Collapsed;
        GlobalProgress.Visibility = Visibility.Visible;
        GlobalProgress.Minimum = 0;
        GlobalProgress.Maximum = toInstall.Count;
        GlobalProgress.Value = 0;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        var succeeded = 0;
        var failed = 0;

        foreach (var pkg in toInstall)
        {
            _cts.Token.ThrowIfCancellationRequested();

            pkg.State = WingetInstallState.Installing;
            pkg.StatusText = "正在安装...";
            pkg.Progress = 0;
            RefreshPackageRow(pkg);

            var progress = new Progress<WingetInstallProgress>(p =>
            {
                pkg.StatusText = p.StatusLine;
                pkg.Progress = p.Percent;
                RefreshPackageRow(pkg);
            });

            try
            {
                var result = await WingetService.InstallAsync(pkg.Id, progress, _cts.Token);
                pkg.State = result.Success ? WingetInstallState.Succeeded : WingetInstallState.Failed;
                pkg.StatusText = result.Message;
                pkg.Progress = result.Success ? 100 : 0;

                if (result.Success) succeeded++;
                else failed++;
            }
            catch (OperationCanceledException)
            {
                pkg.State = WingetInstallState.Failed;
                pkg.StatusText = "已取消";
                pkg.Progress = 0;
                failed++;
                break;
            }

            RefreshPackageRow(pkg);
            GlobalProgress.Value = succeeded + failed;
        }

        RefreshStats();

        ResultText.Text = failed == 0
            ? $"全部安装完成！成功安装 {succeeded} 个软件"
            : $"安装完成：成功 {succeeded} 个，失败 {failed} 个";
        ResultText.Foreground = new SolidColorBrush(failed == 0 ? AccentGreen : AccentOrange);
        ResultText.Visibility = Visibility.Visible;
        GlobalProgress.Visibility = Visibility.Collapsed;

        InstallBtn.IsEnabled = true;
        SelectAllBtn.IsEnabled = true;
        DeselectAllBtn.IsEnabled = true;
        SelectNotInstalledBtn.IsEnabled = true;
        CategoryFilter.IsEnabled = true;
        _isInstalling = false;
    }

    private void RenderPackages(string? category)
    {
        if (_packages is null) return;

        PackageList.Children.Clear();

        var filtered = category is null
            ? _packages
            : _packages.Where(p => p.Category == category).ToList();

        var currentCategory = "";
        foreach (var pkg in filtered)
        {
            if (pkg.Category != currentCategory)
            {
                currentCategory = pkg.Category;
                var header = new TextBlock
                {
                    Text = currentCategory,
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(ThemeColors.DimText),
                    Margin = new Thickness(0, 8, 0, 2)
                };
                PackageList.Children.Add(header);
            }

            PackageList.Children.Add(CreatePackageRow(pkg));
        }

        RefreshStats();
    }

    private Border CreatePackageRow(WingetPackage pkg)
    {
        var stateColor = pkg.State switch
        {
            WingetInstallState.Installed => AccentGreen,
            WingetInstallState.Succeeded => AccentGreen,
            WingetInstallState.Failed => AccentRed,
            WingetInstallState.Installing => AccentBlue,
            _ => ThemeColors.DimText
        };

        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            Background = new SolidColorBrush(Color.FromArgb(26, stateColor.R, stateColor.G, stateColor.B)),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(stateColor), Glyph = pkg.Glyph }
        };

        var nameText = new TextBlock
        {
            Text = pkg.Name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };

        var descText = new TextBlock
        {
            Text = pkg.Description ?? "",
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(nameText);
        infoPanel.Children.Add(descText);

        var statusText = new TextBlock
        {
            Text = pkg.StatusText ?? GetDefaultStatusText(pkg.State),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(stateColor),
            VerticalAlignment = VerticalAlignment.Center
        };

        var progressBar = new ProgressBar
        {
            Value = pkg.Progress,
            Minimum = 0,
            Maximum = 100,
            Width = 80,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = pkg.State == WingetInstallState.Installing ? Visibility.Visible : Visibility.Collapsed
        };

        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        statusPanel.Children.Add(progressBar);
        statusPanel.Children.Add(statusText);

        var checkbox = new CheckBox
        {
            IsChecked = pkg.IsSelected,
            MinWidth = 28,
            VerticalAlignment = VerticalAlignment.Center
        };
        checkbox.Checked += (_, _) =>
        {
            pkg.IsSelected = true;
            RefreshStats();
        };
        checkbox.Unchecked += (_, _) =>
        {
            pkg.IsSelected = false;
            RefreshStats();
        };

        if (pkg.State == WingetInstallState.Installed || pkg.State == WingetInstallState.Installing)
        {
            checkbox.IsEnabled = pkg.State != WingetInstallState.Installing;
        }

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(checkbox);
        grid.Children.Add(iconBorder); Grid.SetColumn(iconBorder, 1);
        grid.Children.Add(infoPanel); Grid.SetColumn(infoPanel, 2);
        grid.Children.Add(statusPanel); Grid.SetColumn(statusPanel, 3);

        return new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid,
            Tag = pkg.Id
        };
    }

    private void RefreshPackageRow(WingetPackage pkg)
    {
        var row = PackageList.Children.OfType<Border>().FirstOrDefault(b => (string?)b.Tag == pkg.Id);
        if (row is null) return;

        var index = PackageList.Children.IndexOf(row);
        var newRow = CreatePackageRow(pkg);
        PackageList.Children.RemoveAt(index);
        PackageList.Children.Insert(index, newRow);

        RefreshStats();
    }

    private void RefreshStats()
    {
        if (_packages is null) return;

        var category = GetCurrentFilter();
        var filtered = category is null
            ? _packages
            : _packages.Where(p => p.Category == category).ToList();

        var total = filtered.Count;
        var installed = filtered.Count(p => p.State is WingetInstallState.Installed or WingetInstallState.Succeeded);
        var selected = filtered.Count(p => p.IsSelected && p.State != WingetInstallState.Installed);

        TotalText.Text = total.ToString();
        InstalledText.Text = installed.ToString();
        SelectedText.Text = selected.ToString();
    }

    private string? GetCurrentFilter()
    {
        var selected = CategoryFilter.SelectedItem as string;
        return selected == "全部分类" ? null : selected;
    }

    private static string GetDefaultStatusText(WingetInstallState state) => state switch
    {
        WingetInstallState.NotInstalled => "未安装",
        WingetInstallState.Checking => "检测中...",
        WingetInstallState.Installed => "已安装",
        WingetInstallState.Installing => "安装中...",
        WingetInstallState.Succeeded => "安装成功",
        WingetInstallState.Failed => "安装失败",
        WingetInstallState.Skipped => "已跳过",
        _ => ""
    };

    public bool TryCancelInstall()
    {
        if (!_isInstalling) return true;
        _cts?.Cancel();
        return false;
    }
}
