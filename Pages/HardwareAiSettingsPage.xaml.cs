using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class HardwareAiSettingsPage : Page
{
    private bool _hardwareFitScreenInitializing;
    private bool _hardwareMultiDeviceNewLineInitializing;
    private bool _cpuzBusy;
    private bool _aiSettingsInitializing;
    private bool _aiTesting;

    private string? _pendingHighlightKey;

    private static readonly Dictionary<string, string> SettingKeyToCardName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HardwareFitScreen"] = "SettingsHardwareFitScreenCard",
        ["HardwareMultiDeviceNewLine"] = "SettingsHardwareMultiDeviceNewLineCard",
        ["MonitorDriver"] = "SettingsCpuzDataSourceCard",
        ["AiApiEndpoint"] = "SettingsAiEndpointCard",
        ["AiModelName"] = "SettingsAiEndpointCard",
        ["AiApiKey"] = "SettingsAiEndpointCard",
        ["SearchApiKey"] = "SettingsAiEndpointCard",
    };

    public HardwareAiSettingsPage()
    {
        InitializeComponent();
        InitHardwareFitScreenToggle();
        InitHardwareMultiDeviceNewLineToggle();
        InitCpuzDataSourceStatus();
        InitAiSettings();
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

    private void InitHardwareFitScreenToggle()
    {
        _hardwareFitScreenInitializing = true;
        HardwareFitScreenToggle.IsOn = AppSettings.GetBool("HardwareFitScreen", true);
        _hardwareFitScreenInitializing = false;
    }

    private void HardwareFitScreenToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_hardwareFitScreenInitializing) return;
        AppSettings.Set("HardwareFitScreen", HardwareFitScreenToggle.IsOn);
    }

    private void InitHardwareMultiDeviceNewLineToggle()
    {
        _hardwareMultiDeviceNewLineInitializing = true;
        HardwareMultiDeviceNewLineToggle.IsOn = AppSettings.GetBool("HardwareMultiDeviceNewLine", false);
        _hardwareMultiDeviceNewLineInitializing = false;
    }

    private void HardwareMultiDeviceNewLineToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_hardwareMultiDeviceNewLineInitializing) return;
        AppSettings.Set("HardwareMultiDeviceNewLine", HardwareMultiDeviceNewLineToggle.IsOn);
        HardwareInfoService.InvalidateCache();
    }

    private void InitCpuzDataSourceStatus()
    {
        UpdateCpuzDataSourceUI();
    }

    private void UpdateCpuzDataSourceUI()
    {
        var useCpuz = AppSettings.GetBool("UseCpuzDataSource", false);
        var cpuzAvailable = CpuzInfoService.FindCpuzExe() != null;

        if (useCpuz && CpuzInfoService.CachedInfo != null)
        {
            CpuzDataSourceStatusText.Text = "当前使用 CPU-Z 数据源（真实硬件读取）";
            CpuzDataSourceButtonText.Text = "切回默认";
            CpuzDataSourceIcon.Glyph = "\uE73E";
        }
        else if (useCpuz)
        {
            CpuzDataSourceStatusText.Text = cpuzAvailable
                ? "CPU-Z 数据源已启用，等待获取数据..."
                : "CPU-Z 数据源已启用，但未找到 CPU-Z";
            CpuzDataSourceButtonText.Text = "切回默认";
            CpuzDataSourceIcon.Glyph = "\uE950;";
        }
        else
        {
            CpuzDataSourceStatusText.Text = cpuzAvailable
                ? "当前使用 WMI 数据源，可切换为 CPU-Z 获取真实信息"
                : "当前使用 WMI 数据源（未找到 CPU-Z 工具）";
            CpuzDataSourceButtonText.Text = "切换";
            CpuzDataSourceIcon.Glyph = "\uE950";
        }
    }

    private async void CpuzDataSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cpuzBusy) return;

        var useCpuz = AppSettings.GetBool("UseCpuzDataSource", false);

        if (useCpuz)
        {
            AppSettings.Set("UseCpuzDataSource", false);
            UpdateCpuzDataSourceUI();
            return;
        }

        var cpuzExe = CpuzInfoService.FindCpuzExe();
        if (cpuzExe == null)
        {
            await ShowMessageAsync("未找到 CPU-Z", "在工具目录中未找到 CPU-Z 可执行文件，无法使用此功能。\n\n请确保 Tools/处理器工具/CPUZ/ 目录下存在 cpuz_x64.exe。");
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "切换硬件信息数据源",
            PrimaryButtonText = "确认切换",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(new TextBlock
        {
            Text = "当前硬件信息通过 WMI（Windows 管理规范）获取，数据来源于厂商在 SMBIOS/DMI 中填写的内容。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85
        });

        var problemBorder = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(40, 255, 185, 0)
                    : Color.FromArgb(30, 200, 130, 0)),
            BorderBrush = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(80, 255, 185, 0)
                    : Color.FromArgb(60, 200, 130, 0)),
            BorderThickness = new Thickness(1)
        };
        problemBorder.Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "⚠ WMI 数据可能被伪造",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 14
                },
                new TextBlock
                {
                    Text = "部分厂商或商家可能通过修改 BIOS/SMBIOS 信息来伪造 CPU 型号、内存品牌、主板型号等，导致 WMI 读取到的信息与实际硬件不符。",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85,
                    FontSize = 13
                }
            }
        };
        stack.Children.Add(problemBorder);

        var solutionBorder = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(40, 0, 200, 100)
                    : Color.FromArgb(25, 0, 160, 80)),
            BorderBrush = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(80, 0, 200, 100)
                    : Color.FromArgb(60, 0, 160, 80)),
            BorderThickness = new Thickness(1)
        };
        solutionBorder.Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "✓ CPU-Z 读取原理",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 14
                },
                new TextBlock
                {
                    Text = "CPU-Z 通过 CPUID 指令直接读取 CPU 硬件寄存器，通过 PCI 枚举直接扫描硬件，通过 SPD 芯片直接读取内存条信息——这些是底层硬件级别的数据，厂商无法通过修改 SMBIOS 来伪造。",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85,
                    FontSize = 13
                }
            }
        };
        stack.Children.Add(solutionBorder);

        var warnBorder = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(40, 100, 150, 255)
                    : Color.FromArgb(25, 60, 120, 255)),
            BorderBrush = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(80, 100, 150, 255)
                    : Color.FromArgb(60, 60, 120, 255)),
            BorderThickness = new Thickness(1)
        };
        warnBorder.Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "⏱ 注意事项",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 14
                },
                new TextBlock
                {
                    Text = "• 使用 CPU-Z 获取信息需要约 3~8 秒，期间会短暂启动 CPU-Z 进程\n• 获取完成后会自动关闭 CPU-Z 进程\n• 切换后可在设置中随时切回 WMI 数据源",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85,
                    FontSize = 13
                }
            }
        };
        stack.Children.Add(warnBorder);

        dialog.Content = new ScrollViewer
        {
            MaxHeight = 400,
            Content = stack
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        _cpuzBusy = true;
        CpuzDataSourceButton.IsEnabled = false;
        CpuzDataSourceStatusText.Text = "正在通过 CPU-Z 获取硬件信息，请稍候...";

        try
        {
            var cpuzInfo = await CpuzInfoService.FetchAsync(timeoutMs: 30000);

            if (cpuzInfo != null)
            {
                AppSettings.Set("UseCpuzDataSource", true);
                UpdateCpuzDataSourceUI();
            }
            else
            {
                CpuzInfoService.KillCpuzProcesses();
                await ShowMessageAsync("获取失败", "CPU-Z 未能成功获取硬件信息。\n\n可能原因：\n• CPU-Z 运行超时\n• CPU-Z 被安全软件拦截\n• 当前架构不支持此版本 CPU-Z");
                UpdateCpuzDataSourceUI();
            }
        }
        catch (Exception ex)
        {
            CpuzInfoService.KillCpuzProcesses();
            await ShowMessageAsync("获取失败", $"CPU-Z 获取过程中出现错误：\n{ex.Message}");
            UpdateCpuzDataSourceUI();
        }
        finally
        {
            _cpuzBusy = false;
            CpuzDataSourceButton.IsEnabled = true;
        }
    }

    private void InitAiSettings()
    {
        _aiSettingsInitializing = true;

        AiEndpointTextBox.Text = AppSettings.Get("AiApiEndpoint") ?? "";
        AiModelTextBox.Text = AppSettings.Get("AiModelName") ?? "";

        var apiKey = AppSettings.Get("AiApiKey") ?? "";
        AiApiKeyBox.Password = apiKey;

        SearchApiKeyBox.Password = AppSettings.Get("SearchApiKey") ?? "";

        UpdateAiConfigStatus();

        _aiSettingsInitializing = false;
    }

    private void UpdateAiConfigStatus()
    {
        if (AiService.IsConfigured)
        {
            AiConfigStatusText.Text = "AI 服务已配置，可在支持 AI 的功能中使用";
            AiConfigStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
        }
        else
        {
            AiConfigStatusText.Text = "未配置 — 配置 OpenAI 兼容 API 后可启用 AI 智能功能";
            AiConfigStatusText.Foreground = new SolidColorBrush(ThemeColors.DimText);
        }
    }

    private void AiEndpointTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_aiSettingsInitializing) return;
        AppSettings.Set("AiApiEndpoint", AiEndpointTextBox.Text.Trim());
        UpdateAiConfigStatus();
    }

    private void AiModelTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_aiSettingsInitializing) return;
        AppSettings.Set("AiModelName", AiModelTextBox.Text.Trim());
        UpdateAiConfigStatus();
    }

    private void AiApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_aiSettingsInitializing) return;
        AppSettings.Set("AiApiKey", AiApiKeyBox.Password);
        UpdateAiConfigStatus();
    }

    private void SearchApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_aiSettingsInitializing) return;
        AppSettings.Set("SearchApiKey", SearchApiKeyBox.Password);
    }

    private async void AiTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_aiTesting) return;
        _aiTesting = true;
        AiTestButton.IsEnabled = false;
        AiTestButtonText.Text = "测试中...";
        AiTestIcon.Glyph = "\uE950";

        try
        {
            var result = await AiService.TestConnectionAsync();

            if (result.Success)
            {
                AiTestIcon.Glyph = "\uE73E";
                AiTestButtonText.Text = "连接成功";
                AiConfigStatusText.Text = "AI 服务已配置，连接测试成功";
                AiConfigStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
            }
            else
            {
                AiTestIcon.Glyph = "\uE783";
                AiTestButtonText.Text = "连接失败";
                AiConfigStatusText.Text = $"连接失败：{result.Error}";
                AiConfigStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);

                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "AI 连接测试失败",
                    Content = new ScrollViewer
                    {
                        MaxHeight = 200,
                        Content = new TextBlock
                        {
                            Text = result.Error ?? "未知错误",
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 13
                        }
                    },
                    CloseButtonText = "确定",
                    RequestedTheme = ThemeService.CurrentElementTheme
                };
                await dialog.ShowAsync();
            }
        }
        finally
        {
            _aiTesting = false;
            AiTestButton.IsEnabled = true;

            await Task.Delay(2000);

            if (!_aiTesting)
            {
                AiTestIcon.Glyph = "\uE73E";
                AiTestButtonText.Text = "测试连接";
            }
        }
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
}
