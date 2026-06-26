using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using TubaWinUi3;
using TubaWinUi3.Services;
using TubaWinUi3.Models;
using Windows.UI;
using static TubaWinUi3.Services.ConfigManager;

namespace TubaWinUi3.Pages;

public sealed partial class AppearanceSettingsPage : Page
{
    private bool _backdropInitializing;
    private bool _opacityChanging;
    private bool _brandLogoInitializing;
    private bool _watermarkInitializing;
    private bool _watermarkTextInitializing;
    private bool _watermarkFontInitializing;
    private Border[] _backdropOptions = [];

    private string? _pendingHighlightKey;

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

    private static readonly Dictionary<string, string> SettingKeyToCardName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Background"] = "SettingsBackgroundCard",
        ["Backdrop"] = "SettingsBackdropCard",
        ["BrandLogo"] = "SettingsBrandLogoCard",
        ["Watermark"] = "SettingsWatermarkCard",
    };

    public AppearanceSettingsPage()
    {
        InitializeComponent();

        InitBackdropSettings();
        LoadBackgroundSettings();
        InitBrandLogoToggle();
        InitWatermarkSettings();
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

    private void InitBackdropSettings()
    {
        _backdropInitializing = true;

        _backdropOptions = [BackdropMicaOption, BackdropMicaAltOption, BackdropAcrylicOption];

        var currentType = BackdropService.GetBackdropType();
        UpdateBackdropOptionSelection(currentType);

        _backdropInitializing = false;
    }

    private void UpdateBackdropOptionSelection(BackdropType selected)
    {
        foreach (var border in _backdropOptions)
        {
            if (border is null) continue;
            var tag = border.Tag?.ToString();
            var isSelected = tag == selected.ToString();
            border.BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 215))
                : (Brush)App.Current.Resources["SubtleFillColorSecondaryBrush"];
        }
    }

    private void BackdropOption_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_backdropInitializing) return;
        if (sender is not Border border) return;
        if (!Enum.TryParse<BackdropType>(border.Tag?.ToString(), out var type)) return;

        BackdropService.SetBackdropType(type);
        UpdateBackdropOptionSelection(type);
    }

    private void BackdropOption_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Opacity = 0.85;
        }
    }

    private void BackdropOption_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Opacity = 1.0;
        }
    }

    private void LoadBackgroundSettings()
    {
        _opacityChanging = true;
        BgOpacitySlider.Minimum = 5;
        BgOpacitySlider.Maximum = 80;
        BgOpacitySlider.StepFrequency = 5;

        var path = BackgroundService.GetBackgroundPath();
        if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
        {
            ShowBgPreview(path);
        }

        var opacity = BackgroundService.GetBackgroundOpacity();
        BgOpacitySlider.Value = (int)(opacity * 100);
        _opacityChanging = false;
        BgOpacityText.Text = $"{(int)(opacity * 100)}%";

        PopulateBgList();
    }

    private void PopulateBgList()
    {
        var entries = BackgroundService.GetImportedBackgrounds();

        BgListPanel.Children.Clear();

        if (entries.Count == 0)
        {
            BgListEmptyText.Visibility = Visibility.Visible;
            BgListScrollViewer.Visibility = Visibility.Collapsed;
            BgHistoryCountText.Text = "";
            BgHistoryExpander.Visibility = Visibility.Collapsed;
            return;
        }

        BgListEmptyText.Visibility = Visibility.Collapsed;
        BgListScrollViewer.Visibility = Visibility.Visible;
        BgHistoryCountText.Text = $"({entries.Count})";
        BgHistoryExpander.Visibility = Visibility.Visible;

        foreach (var entry in entries)
        {
            var item = CreateBgListItem(entry);
            BgListPanel.Children.Add(item);
        }
    }

    private Border CreateBgListItem(BackgroundImageEntry entry)
    {
        var isSelected = entry.IsSelected;
        var accentBrush = (Brush)App.Current.Resources["AccentFillColorDefaultBrush"];

        var thumbnailBorder = new Border
        {
            Width = 140,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            BorderBrush = isSelected ? accentBrush : (Brush)App.Current.Resources["CardStrokeColorDefaultBrush"],
            Tag = entry.Path,
            Padding = new Thickness(0),
        };

        var grid = new Grid
        {
            RowSpacing = 0
        };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(80) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            Source = new BitmapImage(new Uri(entry.Path)),
        };
        Grid.SetRow(image, 0);
        grid.Children.Add(image);

        var infoPanel = new Grid
        {
            Padding = new Thickness(6, 4, 6, 4),
            ColumnSpacing = 4,
        };
        Grid.SetRow(infoPanel, 1);
        infoPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameText = new TextBlock
        {
            Text = entry.FileName,
            FontSize = 11,
            Opacity = 0.72,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(nameText, 0);
        infoPanel.Children.Add(nameText);

        var deleteButton = new Button
        {
            Padding = new Thickness(2),
            MinWidth = 0,
            MinHeight = 0,
            Width = 22,
            Height = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = entry.Path,
        };
        var deleteIcon = new FontIcon
        {
            Glyph = "\uE74D",
            FontSize = 10,
            Foreground = (Brush)App.Current.Resources["TextFillColorSecondaryBrush"],
        };
        deleteButton.Content = deleteIcon;
        deleteButton.Click += BgDeleteItem_Click;
        Grid.SetColumn(deleteButton, 1);
        infoPanel.Children.Add(deleteButton);

        grid.Children.Add(infoPanel);
        thumbnailBorder.Child = grid;

        if (isSelected)
        {
            var checkBadge = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = accentBrush,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 4, 0),
            };
            var checkIcon = new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = 10,
                Foreground = (Brush)App.Current.Resources["TextOnAccentFillColorPrimaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            checkBadge.Child = checkIcon;
            grid.Children.Add(checkBadge);
        }

        thumbnailBorder.PointerPressed += (s, e) =>
        {
            BgListItem_Tapped(entry.Path);
        };

        return thumbnailBorder;
    }

    private void BgListItem_Tapped(string path)
    {
        if (!System.IO.File.Exists(path)) return;

        BackgroundService.SelectBackground(path);
        ShowBgPreview(path);
        PopulateBgList();
    }

    private void BgDeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string path) return;

        BackgroundService.DeleteBackground(path);

        var currentPath = BackgroundService.GetBackgroundPath();
        if (string.IsNullOrWhiteSpace(currentPath))
            HideBgPreview();
        else
            ShowBgPreview(currentPath);

        PopulateBgList();
    }

    private void ShowBgPreview(string path)
    {
        try
        {
            BgPreviewImage.Source = new BitmapImage(new Uri(path));
            BgFileNameText.Text = System.IO.Path.GetFileName(path);
            BgPreviewPanel.Visibility = Visibility.Visible;
            BgPreviewBorder.Visibility = Visibility.Visible;
            ClearBgButton.Visibility = Visibility.Visible;
        }
        catch { }
    }

    private void HideBgPreview()
    {
        BgPreviewImage.Source = null;
        BgFileNameText.Text = string.Empty;
        BgPreviewPanel.Visibility = Visibility.Collapsed;
        BgPreviewBorder.Visibility = Visibility.Collapsed;
        ClearBgButton.Visibility = Visibility.Collapsed;
    }

    private async void ImportBgButton_Click(object sender, RoutedEventArgs e)
    {
        var ofn = new OPENFILENAME();
        ofn.lStructSize = Marshal.SizeOf(ofn);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        ofn.hwndOwner = hwnd;
        ofn.lpstrFilter = "图片文件\0*.jpg;*.jpeg;*.png;*.bmp\0所有文件\0*.*\0\0";
        ofn.lpstrFile = new string(new char[260]);
        ofn.nMaxFile = 260;
        ofn.lpstrTitle = "选择背景图片";
        ofn.Flags = OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR;
        ofn.nFilterIndex = 1;

        if (!GetOpenFileName(ref ofn))
            return;

        var sourcePath = ofn.lpstrFile.TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(sourcePath) || !System.IO.File.Exists(sourcePath))
            return;

        try
        {
            var bgDir = ConfigManager.GetBackgroundsDir();
            System.IO.Directory.CreateDirectory(bgDir);

            var destName = $"bg_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{System.IO.Path.GetExtension(sourcePath)}";
            var destPath = System.IO.Path.Combine(bgDir, destName);
            System.IO.File.Copy(sourcePath, destPath, true);

            BackgroundService.SetBackgroundPath(destPath);
            ShowBgPreview(destPath);
        }
        catch
        {
            BackgroundService.SetBackgroundPath(sourcePath);
            ShowBgPreview(sourcePath);
        }

        PopulateBgList();
    }

    private void ClearBgButton_Click(object sender, RoutedEventArgs e)
    {
        BackgroundService.SetBackgroundPath(null);
        HideBgPreview();
        PopulateBgList();
    }

    private void BgOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_opacityChanging) return;
        var percent = e.NewValue;
        BackgroundService.SetBackgroundOpacity(percent / 100.0);
        BgOpacityText.Text = $"{(int)percent}%";
    }

    private void BrandLogoToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_brandLogoInitializing) return;
        AppSettings.Set("ShowBrandLogo", BrandLogoToggle.IsOn);
    }

    private void InitBrandLogoToggle()
    {
        _brandLogoInitializing = true;
        BrandLogoToggle.IsOn = AppSettings.GetBool("ShowBrandLogo", true);
        _brandLogoInitializing = false;
    }

    private void InitWatermarkSettings()
    {
        _watermarkInitializing = true;
        var watermarkOn = AppSettings.GetBool("ScreenshotWatermark", true);
        WatermarkToggle.IsOn = watermarkOn;
        _watermarkInitializing = false;

        UpdateWatermarkDetailVisibility(watermarkOn);

        _watermarkTextInitializing = true;
        WatermarkTextBox.Text = AppSettings.Get("ScreenshotWatermarkText") ?? "图吧工具箱";
        _watermarkTextInitializing = false;

        _watermarkFontInitializing = true;
        InitWatermarkFontComboBox();
        _watermarkFontInitializing = false;
    }

    private void InitWatermarkFontComboBox()
    {
        WatermarkFontComboBox.Items.Clear();
        var savedFont = AppSettings.Get("ScreenshotWatermarkFont") ?? "微软雅黑";

        using var fc = new InstalledFontCollection();
        var preferredFonts = new[] { "微软雅黑", "宋体", "黑体", "楷体", "仿宋", "Arial", "Segoe UI" };
        var allFonts = new List<string>();

        foreach (var preferred in preferredFonts)
        {
            if (fc.Families.Any(f => f.Name == preferred) && !allFonts.Contains(preferred))
                allFonts.Add(preferred);
        }

        foreach (var family in fc.Families.OrderBy(f => f.Name))
        {
            if (!allFonts.Contains(family.Name))
                allFonts.Add(family.Name);
        }

        var selectedIndex = 0;
        for (var i = 0; i < allFonts.Count; i++)
        {
            WatermarkFontComboBox.Items.Add(allFonts[i]);
            if (allFonts[i] == savedFont)
                selectedIndex = i;
        }

        WatermarkFontComboBox.SelectedIndex = Math.Min(selectedIndex, allFonts.Count - 1);
    }

    private void UpdateWatermarkDetailVisibility(bool watermarkOn)
    {
        WatermarkDivider.Visibility = watermarkOn ? Visibility.Visible : Visibility.Collapsed;
        WatermarkDetailPanel.Visibility = watermarkOn ? Visibility.Visible : Visibility.Collapsed;
        WatermarkFontPanel.Visibility = watermarkOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void WatermarkToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_watermarkInitializing) return;
        var enabled = WatermarkToggle.IsOn;
        AppSettings.Set("ScreenshotWatermark", enabled);
        UpdateWatermarkDetailVisibility(enabled);
    }

    private void WatermarkTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_watermarkTextInitializing) return;
        var text = WatermarkTextBox.Text.Trim();
        AppSettings.Set("ScreenshotWatermarkText", string.IsNullOrEmpty(text) ? "图吧工具箱" : text);
    }

    private void WatermarkFontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_watermarkFontInitializing) return;
        if (WatermarkFontComboBox.SelectedItem is string font)
            AppSettings.Set("ScreenshotWatermarkFont", font);
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
}
