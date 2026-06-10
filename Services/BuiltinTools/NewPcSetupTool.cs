using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class NewPcSetupTool : IBuiltinTool
{
    public string Id => "new-pc-setup";
    public string Name => "新机开荒";
    public string Description => "逐步引导优化新电脑系统设置、安装常用软件、配置安全策略与烤机测试。";
    public string Glyph => "\uE9F5";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var window = new Window();
        var content = BuildMainContent(window);

        var page = new Page { Content = content };
        page.RequestedTheme = ThemeService.CurrentElementTheme;

        window.Content = page;
        window.AppWindow.Title = "新机开荒";
        window.AppWindow.Resize(new SizeInt32(1060, 740));

        try
        {
            var mainPos = App.MainWindow?.AppWindow.Position;
            if (mainPos is not null)
                window.AppWindow.Move(new PointInt32(mainPos.Value.X + 30, mainPos.Value.Y + 30));
        }
        catch { }

        window.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        ApplyTitleBarTheme(window);
        BackdropService.ApplyBackdrop(window);

        window.Activate();
    }

    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);
    private static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color AccentOrange = Color.FromArgb(255, 251, 191, 36);
    private static readonly Color AccentRed = Color.FromArgb(255, 248, 113, 113);
    private static readonly Color AccentPurple = Color.FromArgb(255, 167, 139, 250);

    private const int StepWelcome = 0;
    private const int StepOptimize = 1;
    private const int StepSoftware = 2;
    private const int StepSecurity = 3;
    private const int StepStressTest = 4;
    private const int StepFinish = 5;

    private static readonly string[] StepNames = ["欢迎", "系统优化", "软件安装", "安全设置", "烤机测试", "完成"];
    private static readonly string[] StepGlyphs = ["\uE8A3", "\uE90F", "\uE896", "\uE72E", "\uE8A3", "\uE73E"];

    private Grid BuildMainContent(Window window)
    {
        var state = new SetupWindowState { Window = window };

        var stepList = new StackPanel { Spacing = 2 };
        for (int i = 0; i < StepNames.Length; i++)
        {
            var row = CreateStepRow(i, StepNames[i], StepGlyphs[i], i == 0);
            stepList.Children.Add(row);
            state.StepRows.Add(row);
        }

        var sidebar = new StackPanel
        {
            Spacing = 14,
            Padding = new Thickness(20, 48, 20, 16),
            Width = 180,
            Background = new SolidColorBrush(ThemeColors.HeaderBg),
            Children =
            {
                new TextBlock { Text = "新机开荒", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(ThemeColors.PrimaryText) },
                new Border { Height = 1, Background = new SolidColorBrush(ThemeColors.Separator) },
                stepList
            }
        };

        var contentPanel = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Enabled,
            Content = new StackPanel { Padding = new Thickness(32, 48, 32, 16) }
        };
        state.ContentHost = contentPanel;

        var prevBtn = new Button
        {
            Content = "上一步",
            MinWidth = 90,
            Visibility = Visibility.Collapsed
        };
        var nextBtn = new Button
        {
            Content = "下一步",
            MinWidth = 90,
            Style = Application.Current.Resources["AccentButtonStyle"] as Style
        };
        state.NextBtn = nextBtn;
        state.PrevBtn = prevBtn;

        var actionBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(32, 8, 32, 16)
        };
        actionBar.Children.Add(prevBtn);
        actionBar.Children.Add(nextBtn);

        var rightPanel = new Grid { RowSpacing = 0 };
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightPanel.Children.Add(contentPanel); Grid.SetRow(contentPanel, 0);
        rightPanel.Children.Add(actionBar); Grid.SetRow(actionBar, 1);

        var rootGrid = new Grid { ColumnSpacing = 0 };
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rootGrid.Children.Add(sidebar);
        rootGrid.Children.Add(rightPanel); Grid.SetColumn(rightPanel, 1);

        state.Root = rootGrid;

        prevBtn.Click += (_, _) => NavigateTo(state, state.CurrentStep - 1);
        nextBtn.Click += async (_, _) =>
        {
            if (state.CurrentStep == StepFinish)
            {
                window.Close();
                return;
            }
            if (state.CurrentStep == StepStressTest)
            {
                StressTestWindow.Show();
                NavigateTo(state, StepFinish);
                return;
            }
            if (state.CurrentStep == StepSoftware && state.SoftwareInstalling)
                return;
            if (state.CurrentStep == StepSecurity)
            {
                await ApplySecuritySettingsAsync(state);
                NavigateTo(state, StepStressTest);
                return;
            }
            if (state.CurrentStep == StepOptimize)
            {
                await ApplyOptimizeSettingsAsync(state);
                NavigateTo(state, StepSoftware);
                return;
            }
            NavigateTo(state, state.CurrentStep + 1);
        };

        NavigateTo(state, StepWelcome);

        return rootGrid;
    }

    private Border CreateStepRow(int index, string name, string glyph, bool isActive)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 14,
            Foreground = new SolidColorBrush(isActive ? AccentBlue : ThemeColors.DimText)
        };
        var text = new TextBlock
        {
            Text = name,
            FontSize = 13,
            FontWeight = isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = new SolidColorBrush(isActive ? ThemeColors.PrimaryText : ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Padding = new Thickness(8, 6, 8, 6)
        };
        panel.Children.Add(icon);
        panel.Children.Add(text);

        return new Border
        {
            Child = panel,
            Background = isActive ? new SolidColorBrush(Color.FromArgb(40, AccentBlue.R, AccentBlue.G, AccentBlue.B)) : new SolidColorBrush(Colors.Transparent),
            CornerRadius = new CornerRadius(4),
            Tag = index
        };
    }

    private void NavigateTo(SetupWindowState state, int step)
    {
        if (step < 0 || step >= StepNames.Length) return;
        state.CurrentStep = step;

        foreach (var row in state.StepRows)
        {
            var idx = (int)row.Tag;
            var isActive = idx == step;
            var isDone = idx < step;
            var panel = (StackPanel)row.Child;
            var icon = (FontIcon)panel.Children[0];
            var text = (TextBlock)panel.Children[1];

            if (isDone)
            {
                icon.Glyph = "\uE73E";
                icon.Foreground = new SolidColorBrush(AccentGreen);
                text.Foreground = new SolidColorBrush(ThemeColors.PrimaryText);
                row.Background = new SolidColorBrush(Colors.Transparent);
            }
            else if (isActive)
            {
                icon.Glyph = StepGlyphs[idx];
                icon.Foreground = new SolidColorBrush(AccentBlue);
                text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                text.Foreground = new SolidColorBrush(ThemeColors.PrimaryText);
                row.Background = new SolidColorBrush(Color.FromArgb(40, AccentBlue.R, AccentBlue.G, AccentBlue.B));
            }
            else
            {
                icon.Glyph = StepGlyphs[idx];
                icon.Foreground = new SolidColorBrush(ThemeColors.DimText);
                text.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
                text.Foreground = new SolidColorBrush(ThemeColors.DimText);
                row.Background = new SolidColorBrush(Colors.Transparent);
            }
        }

        state.PrevBtn.Visibility = step > StepWelcome ? Visibility.Visible : Visibility.Collapsed;

        if (step == StepFinish)
        {
            state.NextBtn.Content = "完成";
        }
        else if (step == StepStressTest)
        {
            state.NextBtn.Content = "开始烤机";
        }
        else if (step == StepOptimize)
        {
            state.NextBtn.Content = "应用并继续";
        }
        else if (step == StepSecurity)
        {
            state.NextBtn.Content = "应用并继续";
        }
        else
        {
            state.NextBtn.Content = "下一步";
        }

        RenderStepContent(state, step);
    }

    private void RenderStepContent(SetupWindowState state, int step)
    {
        var host = state.ContentHost;
        if (host.Content is not StackPanel panel) return;
        panel.Children.Clear();

        switch (step)
        {
            case StepWelcome: RenderWelcomeStep(panel, state); break;
            case StepOptimize: RenderOptimizeStep(panel, state); break;
            case StepSoftware: RenderSoftwareStep(panel, state); break;
            case StepSecurity: RenderSecurityStep(panel, state); break;
            case StepStressTest: RenderStressTestStep(panel, state); break;
            case StepFinish: RenderFinishStep(panel, state); break;
        }
    }

    #region Step 1: Welcome

    private void RenderWelcomeStep(StackPanel panel, SetupWindowState state)
    {
        panel.Spacing = 20;

        panel.Children.Add(new TextBlock
        {
            Text = "欢迎使用新机开荒向导",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "本向导将逐步引导您完成新电脑的初始化设置，包括系统优化、软件安装、安全配置和烤机测试。",
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap
        });

        if (!NewPcSetupService.IsAdmin)
        {
            panel.Children.Add(new InfoBar
            {
                Title = "建议以管理员身份运行",
                Message = "部分优化和安全设置需要管理员权限。请右键程序选择「以管理员身份运行」后重试。",
                Severity = InfoBarSeverity.Warning,
                IsOpen = true,
                IsClosable = false
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = "这台电脑的主要用途？",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            Margin = new Thickness(0, 8, 0, 0)
        });

        var roles = new (SetupUserRole Role, string Name, string Glyph, string Desc)[]
        {
            (SetupUserRole.Daily, "日常办公", "\uE8BD", "浏览网页、办公文档、影音娱乐"),
            (SetupUserRole.Developer, "程序员开发", "\uE943", "编程开发、AI 工具、开发环境"),
            (SetupUserRole.Gaming, "游戏娱乐", "\uE768", "游戏、直播、性能优化"),
            (SetupUserRole.Design, "设计创作", "\uEB9F", "图像编辑、视频剪辑、UI 设计")
        };

        var rolePanel = new StackPanel { Spacing = 8 };
        foreach (var role in roles)
        {
            var rb = new RadioButton
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        new FontIcon { Glyph = role.Glyph, FontSize = 18, Foreground = new SolidColorBrush(AccentBlue) },
                        new StackPanel
                        {
                            Spacing = 2,
                            Children =
                            {
                                new TextBlock { Text = role.Name, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(ThemeColors.PrimaryText) },
                                new TextBlock { Text = role.Desc, FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) }
                            }
                        }
                    }
                },
                Tag = role.Role,
                IsChecked = role.Role == SetupUserRole.Daily
            };
            rb.Checked += (_, _) => { state.Role = role.Role; UpdateLanguageSection(state); };
            rolePanel.Children.Add(rb);
        }
        panel.Children.Add(rolePanel);

        var langSection = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
        langSection.Children.Add(new TextBlock
        {
            Text = "主要使用的编程语言（可多选）：",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        });

        var langGrid = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        for (int c = 0; c < 2; c++)
            langGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var languages = NewPcSetupService.GetDevLanguages();
        for (int idx = 0; idx < languages.Count; idx++)
        {
            var col = idx % 2;
            var row = idx / 2;
            if (col == 0) langGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lang = languages[idx].Language;
            var glyph = languages[idx].Glyph;
            var cb = new CheckBox
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = glyph, FontSize = 14 },
                        new TextBlock { Text = lang, FontSize = 13 }
                    }
                },
                Tag = lang
            };
            cb.Checked += (_, _) => { if (!state.SelectedLanguages.Contains(lang)) state.SelectedLanguages.Add(lang); };
            cb.Unchecked += (_, _) => { state.SelectedLanguages.Remove(lang); };

            langGrid.Children.Add(cb); Grid.SetColumn(cb, col); Grid.SetRow(cb, row);
        }
        langSection.Children.Add(langGrid);
        panel.Children.Add(langSection);
        state.LanguageSection = langSection;
    }

    private void UpdateLanguageSection(SetupWindowState state)
    {
        if (state.LanguageSection is null) return;
        state.LanguageSection.Visibility = state.Role == SetupUserRole.Developer ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region Step 2: Optimize

    private void RenderOptimizeStep(StackPanel panel, SetupWindowState state)
    {
        panel.Spacing = 16;

        panel.Children.Add(new TextBlock
        {
            Text = "系统优化",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "根据您的需求调整系统设置，勾选要应用的优化项。",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });

        var startupItems = NewPcSetupService.GetStartupItems();
        if (startupItems.Count > 0)
        {
            panel.Children.Add(MakeSectionHeader("\uE8A3", "启动项管理", $"检测到 {startupItems.Count} 个启动项"));
            var startupList = new StackPanel { Spacing = 4 };
            foreach (var item in startupItems)
            {
                startupList.Children.Add(CreateStartupRow(item, state));
            }
            panel.Children.Add(startupList);
            state.StartupItems = startupItems;
        }

        var services = NewPcSetupService.GetOptionalServices();
        if (services.Count > 0)
        {
            panel.Children.Add(MakeSectionHeader("\uE90F", "系统服务", $"可优化的系统服务"));
            var svcList = new StackPanel { Spacing = 4 };
            foreach (var svc in services)
            {
                svcList.Children.Add(CreateServiceRow(svc, state));
            }
            panel.Children.Add(svcList);
            state.ServiceItems = services;
        }

        panel.Children.Add(MakeSectionHeader("\uE90F", "系统设置", "调整系统行为和显示"));

        var optItems = new (string Id, string Name, string Desc, string Glyph, Func<bool> GetCurrent)[]
        {
            ("visual-effects", "关闭视觉效果", "调整为最佳性能，关闭动画和透明效果", "\uE790", () => NewPcSetupService.IsVisualEffectsDisabled()),
            ("system-ads", "关闭系统广告与推荐", "禁用开始菜单建议、锁屏广告、设置推荐", "\uE72E", () => NewPcSetupService.AreSystemAdsDisabled()),
            ("taskbar-widgets", "关闭任务栏小组件和 Copilot", "移除任务栏上的小组件按钮和 Copilot", "\uE756", () => NewPcSetupService.AreTaskbarWidgetsDisabled()),
            ("file-ext", "显示文件扩展名", "在资源管理器中显示已知文件类型的扩展名", "\uE8AC", () => NewPcSetupService.AreFileExtensionsShown()),
            ("hidden-files", "显示隐藏文件", "在资源管理器中显示隐藏的文件和文件夹", "\uE721", () => NewPcSetupService.AreHiddenFilesShown()),
        };

        var optList = new StackPanel { Spacing = 4 };
        foreach (var opt in optItems)
        {
            var current = opt.GetCurrent();
            var item = new OptimizeItem
            {
                Id = opt.Id,
                Name = opt.Name,
                Description = opt.Desc,
                Glyph = opt.Glyph,
                CurrentState = current,
                WantApply = !current
            };
            state.OptimizeItems.Add(item);
            optList.Children.Add(CreateOptimizeRow(item, state));
        }
        panel.Children.Add(optList);
    }

    private Border CreateStartupRow(StartupItem item, SetupWindowState state)
    {
        var toggle = new ToggleSwitch
        {
            IsOn = item.WantDisable,
            OnContent = "禁用",
            OffContent = "保留",
            MinWidth = 80
        };
        toggle.Toggled += (_, _) => item.WantDisable = toggle.IsOn;

        var nameText = new TextBlock
        {
            Text = item.Name,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };
        var cmdText = new TextBlock
        {
            Text = TruncatePath(item.Command, 60),
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(nameText);
        infoPanel.Children.Add(cmdText);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(infoPanel);
        grid.Children.Add(toggle); Grid.SetColumn(toggle, 1);

        return new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    private Border CreateServiceRow(ServiceItem item, SetupWindowState state)
    {
        var recColor = item.Recommendation switch
        {
            "建议关闭" => AccentGreen,
            "可选关闭" => AccentOrange,
            _ => ThemeColors.DimText
        };

        var recBadge = new Border
        {
            Padding = new Thickness(6, 2, 6, 2),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromArgb(30, recColor.R, recColor.G, recColor.B)),
            Child = new TextBlock { Text = item.Recommendation, FontSize = 10, Foreground = new SolidColorBrush(recColor), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
        };

        var toggle = new ToggleSwitch
        {
            IsOn = item.WantDisable,
            OnContent = "禁用",
            OffContent = "保留",
            MinWidth = 80
        };
        toggle.Toggled += (_, _) => item.WantDisable = toggle.IsOn;

        var nameText = new TextBlock
        {
            Text = item.DisplayName,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };
        var descText = new TextBlock
        {
            Text = item.Description,
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap
        };
        var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(nameText);
        infoPanel.Children.Add(descText);

        var rightPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        rightPanel.Children.Add(recBadge);
        rightPanel.Children.Add(toggle);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(infoPanel);
        grid.Children.Add(rightPanel); Grid.SetColumn(rightPanel, 1);

        return new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    private Border CreateOptimizeRow(OptimizeItem item, SetupWindowState state)
    {
        var statusText = new TextBlock
        {
            Text = item.CurrentState ? "已开启" : "未开启",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(item.CurrentState ? AccentGreen : ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var toggle = new ToggleSwitch
        {
            IsOn = item.WantApply,
            OnContent = "应用",
            OffContent = "跳过",
            MinWidth = 80
        };
        toggle.Toggled += (_, _) => item.WantApply = toggle.IsOn;

        var iconBorder = new Border
        {
            Width = 32,
            Height = 32,
            Background = new SolidColorBrush(Color.FromArgb(26, AccentBlue.R, AccentBlue.G, AccentBlue.B)),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { Glyph = item.Glyph, FontSize = 14, Foreground = new SolidColorBrush(AccentBlue) }
        };

        var nameText = new TextBlock
        {
            Text = item.Name,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };
        var descText = new TextBlock
        {
            Text = item.Description,
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(nameText);
        infoPanel.Children.Add(descText);

        var rightPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        rightPanel.Children.Add(statusText);
        rightPanel.Children.Add(toggle);

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(iconBorder);
        grid.Children.Add(infoPanel); Grid.SetColumn(infoPanel, 1);
        grid.Children.Add(rightPanel); Grid.SetColumn(rightPanel, 2);

        return new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    #endregion

    #region Step 3: Software

    private void RenderSoftwareStep(StackPanel panel, SetupWindowState state)
    {
        panel.Spacing = 16;

        panel.Children.Add(new TextBlock
        {
            Text = "软件安装",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        });

        var wingetError = new InfoBar
        {
            Title = "winget 不可用",
            Message = "未检测到 winget，软件安装功能不可用。请确认系统已安装 App Installer 并更新至最新版本。",
            Severity = InfoBarSeverity.Error,
            IsOpen = false,
            IsClosable = false
        };
        panel.Children.Add(wingetError);
        _ = Task.Run(async () =>
        {
            var available = await WingetService.IsWingetAvailableAsync();
            _ = state.Window.DispatcherQueue.TryEnqueue(() => { wingetError.IsOpen = !available; });
        });

        panel.Children.Add(new TextBlock
        {
            Text = "勾选要安装的软件，点击「下一步」开始批量安装。",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });

        var allPackages = new List<SetupPackage>();
        var seenIds = new HashSet<string>();

        var packageListPanel = new StackPanel { Spacing = 4 };
        state.PackageListPanel = packageListPanel;

        var commonPkgs = NewPcSetupService.GetCommonCatalog();
        packageListPanel.Children.Add(MakeSectionHeader("\uE896", "常用软件", $"{commonPkgs.Count} 个"));
        var commonList = new StackPanel { Spacing = 4 };
        foreach (var p in commonPkgs)
        {
            if (seenIds.Add(p.Id))
            {
                commonList.Children.Add(CreatePackageRow(p, state));
                allPackages.Add(p);
            }
        }
        packageListPanel.Children.Add(commonList);

        if (state.Role == SetupUserRole.Developer)
        {
            var devPkgs = NewPcSetupService.BuildDevPackageList(state.SelectedLanguages.ToArray());
            packageListPanel.Children.Add(MakeSectionHeader("\uE943", "开发工具", $"{devPkgs.Count} 个"));
            var devList = new StackPanel { Spacing = 4 };
            foreach (var p in devPkgs)
            {
                if (seenIds.Add(p.Id))
                {
                    devList.Children.Add(CreatePackageRow(p, state));
                    allPackages.Add(p);
                }
            }
            packageListPanel.Children.Add(devList);
        }

        var manualPkgs = NewPcSetupService.GetManualTools();
        if (manualPkgs.Count > 0)
        {
            packageListPanel.Children.Add(MakeSectionHeader("\uE72E", "需手动下载", $"{manualPkgs.Count} 个"));
            var manualList = new StackPanel { Spacing = 4 };
            foreach (var p in manualPkgs)
            {
                manualList.Children.Add(CreateManualToolRow(p));
            }
            packageListPanel.Children.Add(manualList);
        }

        panel.Children.Add(packageListPanel);

        state.AllPackages = allPackages;

        var selectAllBtn = new Button { Content = "全选", Padding = new Thickness(8, 4, 8, 4) };
        var deselectAllBtn = new Button { Content = "取消全选", Padding = new Thickness(8, 4, 8, 4) };
        selectAllBtn.Click += (_, _) => { foreach (var p in allPackages) { p.IsSelected = true; } RefreshSoftwareStep(state); };
        deselectAllBtn.Click += (_, _) => { foreach (var p in allPackages) { p.IsSelected = false; } RefreshSoftwareStep(state); };

        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionRow.Children.Add(selectAllBtn);
        actionRow.Children.Add(deselectAllBtn);
        panel.Children.Add(actionRow);

        var progressPanel = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        var progressText = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(ThemeColors.PrimaryText) };
        var progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Width = 400 };
        progressPanel.Children.Add(progressText);
        progressPanel.Children.Add(progressBar);
        panel.Children.Add(progressPanel);
        state.InstallProgressPanel = progressPanel;
        state.InstallProgressText = progressText;
        state.InstallProgressBar = progressBar;

        _ = CheckInstalledStatusAsync(state);
    }

    private void RefreshSoftwareStep(SetupWindowState state)
    {
        if (state.AllPackages is null || state.PackageListPanel is null) return;

        var list = state.PackageListPanel;
        list.Children.Clear();

        var grouped = state.AllPackages.GroupBy(p => p.Category);
        foreach (var group in grouped)
        {
            var label = group.Key;
            var pkgGlyph = "\uE896";
            list.Children.Add(MakeSectionHeader(pkgGlyph, label, $"{group.Count()} 个"));
            var sub = new StackPanel { Spacing = 4 };
            foreach (var p in group)
            {
                if (string.IsNullOrEmpty(p.ManualUrl))
                    sub.Children.Add(CreatePackageRow(p, state));
                else
                    sub.Children.Add(CreateManualToolRow(p));
            }
            list.Children.Add(sub);
        }
    }

    private Border CreatePackageRow(SetupPackage pkg, SetupWindowState state)
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
            Width = 32,
            Height = 32,
            Background = new SolidColorBrush(Color.FromArgb(26, stateColor.R, stateColor.G, stateColor.B)),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 14, Foreground = new SolidColorBrush(stateColor), Glyph = pkg.Glyph }
        };

        var nameText = new TextBlock
        {
            Text = pkg.Name,
            FontSize = 13,
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
            Text = pkg.StatusText ?? (pkg.State == WingetInstallState.NotInstalled ? "未安装" : "已安装"),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(stateColor),
            VerticalAlignment = VerticalAlignment.Center
        };

        var depBadge = !string.IsNullOrEmpty(pkg.DependsOn)
            ? new Border
            {
                Padding = new Thickness(4, 1, 4, 1),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromArgb(30, AccentOrange.R, AccentOrange.G, AccentOrange.B)),
                Child = new TextBlock { Text = "需 Node.js", FontSize = 9, Foreground = new SolidColorBrush(AccentOrange) }
            }
            : null;

        var checkbox = new CheckBox
        {
            IsChecked = pkg.IsSelected,
            MinWidth = 28,
            VerticalAlignment = VerticalAlignment.Center
        };
        checkbox.Checked += (_, _) => pkg.IsSelected = true;
        checkbox.Unchecked += (_, _) => pkg.IsSelected = false;

        if (pkg.State == WingetInstallState.Installed || pkg.State == WingetInstallState.Installing)
            checkbox.IsEnabled = pkg.State != WingetInstallState.Installing;

        var rightPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (depBadge is not null) rightPanel.Children.Add(depBadge);
        rightPanel.Children.Add(statusText);

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(checkbox);
        grid.Children.Add(iconBorder); Grid.SetColumn(iconBorder, 1);
        grid.Children.Add(infoPanel); Grid.SetColumn(infoPanel, 2);
        grid.Children.Add(rightPanel); Grid.SetColumn(rightPanel, 3);

        return new Border
        {
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Tag = pkg.Id
        };
    }

    private Border CreateManualToolRow(SetupPackage pkg)
    {
        var linkBtn = new HyperlinkButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { Glyph = "\uE71B", FontSize = 12 },
                    new TextBlock { Text = "前往下载", FontSize = 12 }
                }
            },
            NavigateUri = !string.IsNullOrEmpty(pkg.ManualUrl) ? new Uri(pkg.ManualUrl) : null
        };

        var nameText = new TextBlock
        {
            Text = pkg.Name,
            FontSize = 13,
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

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(infoPanel);
        grid.Children.Add(linkBtn); Grid.SetColumn(linkBtn, 1);

        return new Border
        {
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };
    }

    private async Task CheckInstalledStatusAsync(SetupWindowState state)
    {
        if (state.AllPackages is null) return;

        foreach (var pkg in state.AllPackages)
        {
            if (!string.IsNullOrEmpty(pkg.ManualUrl)) continue;
            pkg.State = WingetInstallState.Checking;
            pkg.StatusText = "检测中...";
        }

        RefreshSoftwareStep(state);

        await Task.Run(async () =>
        {
            var tasks = state.AllPackages.Where(p => string.IsNullOrEmpty(p.ManualUrl)).Select(async p =>
            {
                var installed = await NewPcSetupService.IsPackageInstalledAsync(p.Id);
                p.State = installed ? WingetInstallState.Installed : WingetInstallState.NotInstalled;
                p.StatusText = installed ? "已安装" : "未安装";
            });
            await Task.WhenAll(tasks);
        });

        RefreshSoftwareStep(state);
    }

    #endregion

    #region Step 4: Security

    private void RenderSecurityStep(StackPanel panel, SetupWindowState state)
    {
        panel.Spacing = 16;

        panel.Children.Add(new TextBlock
        {
            Text = "安全设置",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "配置系统安全策略，保护电脑免受威胁。",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });

        if (!NewPcSetupService.IsAdmin)
        {
            panel.Children.Add(new InfoBar
            {
                Title = "需要管理员权限",
                Message = "安全设置需要管理员权限才能修改。请以管理员身份运行后重试。",
                Severity = InfoBarSeverity.Error,
                IsOpen = true,
                IsClosable = false
            });
        }

        var items = new (SecurityItem Item, Func<bool> GetCurrent)[]
        {
            (new SecurityItem { Id = "cert-block", Name = "封锁流氓软件证书", Description = "将常见流氓软件厂商证书加入系统不信任列表，阻止安装", Glyph = "\uE72E", RequiresAdmin = true, IsDangerous = false }, () => false),
            (new SecurityItem { Id = "disable-autorun", Name = "禁用 USB 自动运行", Description = "防止 U 盘病毒通过自动运行传播", Glyph = "\uE88E", RequiresAdmin = true, IsDangerous = false }, () => NewPcSetupService.IsAutoRunDisabled()),
            (new SecurityItem { Id = "firewall-check", Name = "确认防火墙已开启", Description = "确保 Windows 防火墙处于启用状态", Glyph = "\uE72E", RequiresAdmin = false, IsDangerous = false }, () => NewPcSetupService.IsFirewallEnabled()),
            (new SecurityItem { Id = "disable-defender", Name = "关闭 Windows Defender 实时保护", Description = "⚠ 此操作会降低系统安全性，仅建议安装第三方杀毒软件时使用", Glyph = "\uE72E", RequiresAdmin = true, IsDangerous = true }, () => !NewPcSetupService.IsDefenderRealtimeEnabled()),
        };

        foreach (var (item, getCurrent) in items)
        {
            item.CurrentState = getCurrent();
            item.WantApply = !item.CurrentState && !item.IsDangerous;
            state.SecurityItems.Add(item);
            panel.Children.Add(CreateSecurityRow(item, state));
        }
    }

    private Border CreateSecurityRow(SecurityItem item, SetupWindowState state)
    {
        var iconColor = item.IsDangerous ? AccentRed : AccentBlue;
        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            Background = new SolidColorBrush(Color.FromArgb(26, iconColor.R, iconColor.G, iconColor.B)),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { Glyph = item.Glyph, FontSize = 16, Foreground = new SolidColorBrush(iconColor) }
        };

        var nameText = new TextBlock
        {
            Text = item.Name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };
        var descText = new TextBlock
        {
            Text = item.Description,
            FontSize = 11,
            Foreground = new SolidColorBrush(item.IsDangerous ? AccentRed : ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap
        };
        var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(nameText);
        infoPanel.Children.Add(descText);

        var statusText = new TextBlock
        {
            Text = item.CurrentState ? "已启用" : "未启用",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(item.CurrentState ? AccentGreen : ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var toggle = new ToggleSwitch
        {
            IsOn = item.WantApply,
            OnContent = "应用",
            OffContent = "跳过",
            MinWidth = 80,
            IsEnabled = !item.RequiresAdmin || NewPcSetupService.IsAdmin
        };
        toggle.Toggled += (_, _) => item.WantApply = toggle.IsOn;

        var rightPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        rightPanel.Children.Add(statusText);
        rightPanel.Children.Add(toggle);

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(iconBorder);
        grid.Children.Add(infoPanel); Grid.SetColumn(infoPanel, 1);
        grid.Children.Add(rightPanel); Grid.SetColumn(rightPanel, 2);

        return new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(item.IsDangerous ? AccentRed : ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    #endregion

    #region Step 5: Stress Test

    private void RenderStressTestStep(StackPanel panel, SetupWindowState state)
    {
        panel.Spacing = 16;

        panel.Children.Add(new TextBlock
        {
            Text = "烤机测试",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "新电脑建议进行烤机测试，验证硬件稳定性和散热性能。点击「开始烤机」将打开烤机测试窗口，支持 CPU/GPU 单烤和双烤模式，实时监控温度、占用率、频率和功耗。",
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap
        });

        var features = new[]
        {
            ("CPU 单烤", "\uE950", "对 CPU 施加满载压力，测试稳定性"),
            ("GPU 单烤", "\uE950", "对 GPU 施加满载压力，测试显卡稳定性"),
            ("双烤模式", "\uE950", "同时烤 CPU + GPU，测试整机散热"),
            ("实时监控", "\uE928", "温度、占用率、频率、功耗实时显示"),
        };

        foreach (var (name, glyph, desc) in features)
        {
            var iconBorder = new Border
            {
                Width = 32,
                Height = 32,
                Background = new SolidColorBrush(Color.FromArgb(26, AccentOrange.R, AccentOrange.G, AccentOrange.B)),
                CornerRadius = new CornerRadius(6),
                Child = new FontIcon { Glyph = glyph, FontSize = 14, Foreground = new SolidColorBrush(AccentOrange) }
            };
            var nameText = new TextBlock
            {
                Text = name,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
            };
            var descText = new TextBlock
            {
                Text = desc,
                FontSize = 11,
                Foreground = new SolidColorBrush(ThemeColors.DimText)
            };
            var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            infoPanel.Children.Add(nameText);
            infoPanel.Children.Add(descText);

            var grid = new Grid { ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(iconBorder);
            grid.Children.Add(infoPanel); Grid.SetColumn(infoPanel, 1);

            panel.Children.Add(new Border
            {
                Padding = new Thickness(12, 8, 12, 8),
                Background = new SolidColorBrush(ThemeColors.CardBg),
                BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = grid
            });
        }

        panel.Children.Add(new InfoBar
        {
            Title = "提示",
            Message = "烤机测试将持续对硬件施加高负载，请确保散热良好。建议至少运行 15 分钟以上验证稳定性。",
            Severity = InfoBarSeverity.Informational,
            IsOpen = true,
            IsClosable = true
        });
    }

    #endregion

    #region Step 6: Finish

    private void RenderFinishStep(StackPanel panel, SetupWindowState state)
    {
        panel.Spacing = 16;

        panel.Children.Add(new TextBlock
        {
            Text = "开荒完成！",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(AccentGreen)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "您的新电脑已完成初始化设置，以下是本次操作的总结：",
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });

        var result = state.Result;

        var summaryGrid = new Grid { ColumnSpacing = 10 };
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var card1 = MakeStatCard("启动项优化", result.DisabledStartupItems.ToString(), "\uE8A3", AccentBlue);
        var card2 = MakeStatCard("系统设置", result.OptimizedSettings.ToString(), "\uE90F", AccentGreen);
        var card3 = MakeStatCard("软件安装", result.InstalledSoftware.ToString(), "\uE896", AccentPurple);
        var card4 = MakeStatCard("安全配置", result.SecuritySettings.ToString(), "\uE72E", AccentOrange);

        summaryGrid.Children.Add(card1); Grid.SetColumn(card1, 0);
        summaryGrid.Children.Add(card2); Grid.SetColumn(card2, 1);
        summaryGrid.Children.Add(card3); Grid.SetColumn(card3, 2);
        summaryGrid.Children.Add(card4); Grid.SetColumn(card4, 3);

        panel.Children.Add(summaryGrid);

        if (result.DevConfigResults.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "开发环境配置：",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
                Margin = new Thickness(0, 8, 0, 0)
            });
            foreach (var msg in result.DevConfigResults)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"  ✓ {msg}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(AccentGreen)
                });
            }
        }

        panel.Children.Add(new InfoBar
        {
            Title = "建议重启",
            Message = "部分系统设置需要重启后才能生效，建议重启电脑以完成所有优化。",
            Severity = InfoBarSeverity.Informational,
            IsOpen = true,
            IsClosable = true
        });
    }

    #endregion

    #region Apply Actions

    private async Task ApplyOptimizeSettingsAsync(SetupWindowState state)
    {
        state.NextBtn.IsEnabled = false;
        state.PrevBtn.IsEnabled = false;

        var result = state.Result;

        if (state.StartupItems is not null)
        {
            foreach (var item in state.StartupItems.Where(i => i.WantDisable))
            {
                await Task.Run(() => NewPcSetupService.DisableStartupItem(item));
                result.DisabledStartupItems++;
            }
        }

        if (state.ServiceItems is not null)
        {
            foreach (var svc in state.ServiceItems.Where(s => s.WantDisable))
            {
                await Task.Run(() => NewPcSetupService.SetServiceStartType(svc.ServiceName, 4));
                result.OptimizedSettings++;
            }
        }

        foreach (var opt in state.OptimizeItems.Where(o => o.WantApply && !o.CurrentState))
        {
            var success = opt.Id switch
            {
                "visual-effects" => await Task.Run(NewPcSetupService.DisableVisualEffects),
                "system-ads" => await Task.Run(NewPcSetupService.DisableSystemAds),
                "taskbar-widgets" => await Task.Run(NewPcSetupService.DisableTaskbarWidgets),
                "file-ext" => await Task.Run(NewPcSetupService.ShowFileExtensions),
                "hidden-files" => await Task.Run(NewPcSetupService.ShowHiddenFiles),
                _ => false
            };
            if (success) result.OptimizedSettings++;
        }

        if (state.Role == SetupUserRole.Developer && state.SelectedLanguages.Count > 0)
        {
            var progress = new Progress<string>(msg => state.Result.DevConfigResults.Add(msg));
            await NewPcSetupService.ConfigureDevEnvironmentAsync(state.SelectedLanguages.ToArray(), progress);
        }

        state.NextBtn.IsEnabled = true;
        state.PrevBtn.IsEnabled = true;
    }

    private async Task ApplySecuritySettingsAsync(SetupWindowState state)
    {
        state.NextBtn.IsEnabled = false;
        state.PrevBtn.IsEnabled = false;

        var result = state.Result;

        foreach (var item in state.SecurityItems.Where(i => i.WantApply && !i.CurrentState))
        {
            var success = item.Id switch
            {
                "cert-block" => await Task.Run(NewPcSetupService.BlockMalwareCerts),
                "disable-autorun" => await Task.Run(NewPcSetupService.DisableAutoRun),
                "firewall-check" => item.CurrentState,
                "disable-defender" => await Task.Run(() => NewPcSetupService.SetDefenderRealtime(false)),
                _ => false
            };
            if (success) result.SecuritySettings++;
        }

        if (state.AllPackages is not null)
        {
            var selected = state.AllPackages.Where(p => p.IsSelected && p.State != WingetInstallState.Installed).ToList();
            if (selected.Count > 0)
            {
                var resolved = NewPcSetupService.ResolveDependencies(selected);
                state.SoftwareInstalling = true;
                state.InstallProgressPanel!.Visibility = Visibility.Visible;

                var cts = new CancellationTokenSource();
                var succeeded = 0;
                var failed = 0;

                foreach (var pkg in resolved)
                {
                    try
                    {
                        pkg.State = WingetInstallState.Installing;
                        pkg.StatusText = "正在安装...";
                        pkg.Progress = 0;

                        var progress = new Progress<WingetInstallProgress>(p =>
                        {
                            pkg.StatusText = p.StatusLine;
                            pkg.Progress = p.Percent;
                            state.InstallProgressText!.Text = $"正在安装 {pkg.Name}... ({succeeded + failed + 1}/{resolved.Count})";
                            state.InstallProgressBar!.Value = (double)(succeeded + failed) / resolved.Count * 100 + (double)p.Percent / resolved.Count;
                        });

                        var installResult = await NewPcSetupService.InstallPackageAsync(pkg, progress, cts.Token);
                        pkg.State = installResult.Success ? WingetInstallState.Succeeded : WingetInstallState.Failed;
                        pkg.StatusText = installResult.Message;
                        pkg.Progress = installResult.Success ? 100 : 0;

                        if (installResult.Success) succeeded++;
                        else failed++;
                    }
                    catch (OperationCanceledException)
                    {
                        pkg.State = WingetInstallState.Failed;
                        pkg.StatusText = "已取消";
                        failed++;
                        break;
                    }
                }

                result.InstalledSoftware = succeeded;
                state.SoftwareInstalling = false;
                state.InstallProgressPanel!.Visibility = Visibility.Collapsed;
            }
        }

        state.NextBtn.IsEnabled = true;
        state.PrevBtn.IsEnabled = true;
    }

    #endregion

    #region Helpers

    private static StackPanel MakeSectionHeader(string glyph, string title, string subtitle)
    {
        var icon = new FontIcon { Glyph = glyph, FontSize = 14, Foreground = new SolidColorBrush(AccentBlue) };
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center
        };
        var subText = new TextBlock
        {
            Text = subtitle,
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 4)
        };
        panel.Children.Add(icon);
        panel.Children.Add(titleText);
        panel.Children.Add(subText);
        return panel;
    }

    private static Border MakeStatCard(string label, string value, string glyph, Color accent)
    {
        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(accent), Glyph = glyph }
        };
        var labelBlock = new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var valueBlock = new TextBlock { Text = value, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(accent) };
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(labelBlock);
        stack.Children.Add(valueBlock);

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(iconBorder);
        grid.Children.Add(stack); Grid.SetColumn(stack, 1);

        return new Border
        {
            Padding = new Thickness(12),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    private static string TruncatePath(string path, int maxLen)
    {
        if (path.Length <= maxLen) return path;
        return "..." + path[^maxLen..];
    }

    private static void ApplyTitleBarTheme(Window window)
    {
        var tb = window.AppWindow.TitleBar;
        var isDark = ThemeService.CurrentTheme == AppTheme.Dark ||
                     (ThemeService.CurrentTheme == AppTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        if (isDark)
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
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
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 100, 100, 100);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);
            tb.BackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.InactiveBackgroundColor = Color.FromArgb(0, 255, 255, 255);
        }

        tb.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
    }

    #endregion

    private sealed class SetupWindowState
    {
        public Window Window = null!;
        public Grid Root = null!;
        public ScrollViewer ContentHost = null!;
        public Button NextBtn = null!;
        public Button PrevBtn = null!;
        public List<Border> StepRows = [];
        public int CurrentStep;
        public SetupUserRole Role = SetupUserRole.Daily;
        public List<string> SelectedLanguages = [];
        public StackPanel? LanguageSection;
        public List<StartupItem>? StartupItems;
        public List<ServiceItem>? ServiceItems;
        public List<OptimizeItem> OptimizeItems = [];
        public List<SecurityItem> SecurityItems = [];
        public List<SetupPackage>? AllPackages;
        public StackPanel? PackageListPanel;
        public bool SoftwareInstalling;
        public StackPanel? InstallProgressPanel;
        public TextBlock? InstallProgressText;
        public ProgressBar? InstallProgressBar;
        public SetupStepResult Result = new();
    }
}
