using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class KeyboardTestTool : IBuiltinTool
{
    public string Id => "keyboard-test";
    public string Name => "键盘测试";
    public string Description => "检测键盘按键是否正常，按键后高亮显示，支持全键盘可视化。";
    public string Glyph => "\uE92E";
    public string Category => "外设工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    private static readonly Color KeyPressed = Color.FromArgb(255, 66, 133, 244);
    private static readonly Color KeyVisited = Color.FromArgb(60, 66, 133, 244);
    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);

    private readonly Dictionary<VirtualKey, Border> _keyMap = [];
    private readonly HashSet<VirtualKey> _visitedKeys = [];
    private Grid? _keyGrid;
    private int _totalPressed;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private ContentDialog? _currentDialog;
    private TextBlock? _countText;
    private TextBlock? _lastKeyText;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _currentDialog != null)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            var key = (VirtualKey)vkCode;
            bool isDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
            bool isUp = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;

            if (isDown)
            {
                _currentDialog.DispatcherQueue.TryEnqueue(() =>
                {
                    HandleKeyDown(key);
                });
            }
            else if (isUp)
            {
                _currentDialog.DispatcherQueue.TryEnqueue(() =>
                {
                    HandleKeyUp(key);
                });
            }

            if (ShouldBlockKey(key))
            {
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool ShouldBlockKey(VirtualKey key)
    {
        return key is VirtualKey.LeftWindows or VirtualKey.RightWindows
            or VirtualKey.Menu or VirtualKey.F4
            or VirtualKey.Tab or VirtualKey.Escape;
    }

    private void InstallHook()
    {
        _hookProc = HookCallback;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private void RemoveHook()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _hookProc = null;
    }

    private void HandleKeyDown(VirtualKey key)
    {
        if (_keyMap.TryGetValue(key, out var keyBorder))
        {
            keyBorder.Background = new SolidColorBrush(KeyPressed);
            var tb = FindTextBlock(keyBorder);
            if (tb is not null) tb.Foreground = new SolidColorBrush(ThemeColors.PrimaryText);
        }
        if (_visitedKeys.Add(key))
        {
            _totalPressed++;
        }
        if (_countText is not null) _countText.Text = _totalPressed.ToString();
        if (_lastKeyText is not null) _lastKeyText.Text = KeyDisplayName(key);
    }

    private void HandleKeyUp(VirtualKey key)
    {
        if (_keyMap.TryGetValue(key, out var keyBorder))
        {
            if (_visitedKeys.Contains(key))
            {
                keyBorder.Background = new SolidColorBrush(KeyVisited);
                var tb = FindTextBlock(keyBorder);
                if (tb is not null) tb.Foreground = new SolidColorBrush(ThemeColors.KeyText);
            }
            else
            {
                keyBorder.Background = new SolidColorBrush(ThemeColors.KeyDefault);
            }
        }
    }

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        _keyGrid = null;
        _countText = null;
        _lastKeyText = null;
        _visitedKeys.Clear();
        _totalPressed = 0;

        var dialog = context.CreateDialog("键盘测试");
        dialog.Resources["ContentDialogMaxWidth"] = 1080;
        dialog.Resources["ContentDialogMaxHeight"] = 680;

        var content = BuildDialogContent();
        dialog.Content = content;
        _currentDialog = dialog;

        dialog.Opened += (_, _) =>
        {
            _keyGrid?.Focus(FocusState.Programmatic);
            InstallHook();
        };

        dialog.Closing += (_, e) =>
        {
            RemoveHook();
            _currentDialog = null;
        };

        await dialog.ShowAsync();
    }

    private ScrollViewer BuildDialogContent()
    {
        var countText = new TextBlock
        {
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(AccentGreen),
            Text = "0"
        };
        _countText = countText;
        var lastKeyText = new TextBlock
        {
            FontSize = 14,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        _lastKeyText = lastKeyText;

        var statsBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        statsBar.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new FontIcon { Glyph = "\uE92E", FontSize = 14, Foreground = new SolidColorBrush(AccentGreen) },
                new TextBlock { Text = "已检测按键:", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(ThemeColors.DimText) },
                countText
            }
        });
        statsBar.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "最后按键:", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(ThemeColors.DimText) },
                lastKeyText
            }
        });

        var keyGrid = new Grid
        {
            Background = new SolidColorBrush(ThemeColors.KeyboardBg),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            IsTabStop = true
        };
        _keyGrid = keyGrid;

        BuildKeyboardLayout(keyGrid);

        keyGrid.KeyDown += (s, e) =>
        {
            HandleKeyDown(e.Key);
            e.Handled = true;
        };

        keyGrid.KeyUp += (s, e) =>
        {
            HandleKeyUp(e.Key);
            e.Handled = true;
        };

        keyGrid.PointerPressed += (s, e) =>
        {
            keyGrid.Focus(FocusState.Programmatic);
            e.Handled = true;
        };

        var resetBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 12 },
                    new TextBlock { Text = "重置" }
                }
            }
        };
        resetBtn.Click += (_, _) =>
        {
            _totalPressed = 0;
            _visitedKeys.Clear();
            countText.Text = "0";
            lastKeyText.Text = "";
            foreach (var border in _keyMap.Values)
            {
                border.Background = new SolidColorBrush(ThemeColors.KeyDefault);
                var tb = FindTextBlock(border);
                if (tb is not null) tb.Foreground = new SolidColorBrush(ThemeColors.KeyText);
            }
            keyGrid.Focus(FocusState.Programmatic);
        };

        var tipText = new TextBlock
        {
            Text = "点击下方键盘区域后开始按键测试，按键会高亮显示，已按过的键会留有浅色标记",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var actionBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionBar.Children.Add(statsBar);
        actionBar.Children.Add(resetBtn);

        var root = new StackPanel { Spacing = 14, MaxWidth = 1040 };
        root.Children.Add(tipText);
        root.Children.Add(actionBar);
        root.Children.Add(keyGrid);

        return new ScrollViewer { Content = root, MaxWidth = 1080, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void BuildKeyboardLayout(Grid rootGrid)
    {
        _keyMap.Clear();
        var rows = GetKeyboardRows();
        var rowDefs = rows.Select(_ => new RowDefinition { Height = GridLength.Auto }).ToList();
        rowDefs.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        foreach (var rd in rowDefs) rootGrid.RowDefinitions.Add(rd);

        for (int r = 0; r < rows.Count; r++)
        {
            var rowPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(0, r == 0 ? 0 : 4, 0, 0)
            };

            foreach (var (key, label, width) in rows[r])
            {
                var keyBorder = MakeKeyBorder(label, width);
                _keyMap[key] = keyBorder;
                rowPanel.Children.Add(keyBorder);
            }

            rootGrid.Children.Add(rowPanel);
            Grid.SetRow(rowPanel, r);
        }
    }

    private Border MakeKeyBorder(string label, double width)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = label.Length > 3 ? 10 : 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.KeyText),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        return new Border
        {
            Width = width,
            Height = 42,
            Background = new SolidColorBrush(ThemeColors.KeyDefault),
            BorderBrush = new SolidColorBrush(ThemeColors.KeyBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = text
        };
    }

    private static TextBlock? FindTextBlock(Border border)
    {
        return border.Child as TextBlock;
    }

    private static string KeyDisplayName(VirtualKey key)
    {
        return key switch
        {
            VirtualKey.Space => "Space",
            VirtualKey.Enter => "Enter",
            VirtualKey.Back => "Backspace",
            VirtualKey.Tab => "Tab",
            VirtualKey.CapitalLock => "CapsLock",
            VirtualKey.Shift => "Shift",
            VirtualKey.Control => "Ctrl",
            VirtualKey.Menu => "Alt",
            VirtualKey.LeftWindows => "Win",
            VirtualKey.RightWindows => "Win",
            VirtualKey.Escape => "Esc",
            VirtualKey.Delete => "Delete",
            VirtualKey.Insert => "Insert",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            VirtualKey.PageUp => "PgUp",
            VirtualKey.PageDown => "PgDn",
            VirtualKey.Left => "←",
            VirtualKey.Right => "→",
            VirtualKey.Up => "↑",
            VirtualKey.Down => "↓",
            VirtualKey.Number0 => "0",
            VirtualKey.Number1 => "1",
            VirtualKey.Number2 => "2",
            VirtualKey.Number3 => "3",
            VirtualKey.Number4 => "4",
            VirtualKey.Number5 => "5",
            VirtualKey.Number6 => "6",
            VirtualKey.Number7 => "7",
            VirtualKey.Number8 => "8",
            VirtualKey.Number9 => "9",
            VirtualKey.NumberPad0 => "Num0",
            VirtualKey.NumberPad1 => "Num1",
            VirtualKey.NumberPad2 => "Num2",
            VirtualKey.NumberPad3 => "Num3",
            VirtualKey.NumberPad4 => "Num4",
            VirtualKey.NumberPad5 => "Num5",
            VirtualKey.NumberPad6 => "Num6",
            VirtualKey.NumberPad7 => "Num7",
            VirtualKey.NumberPad8 => "Num8",
            VirtualKey.NumberPad9 => "Num9",
            VirtualKey.Snapshot => "PrtSc",
            VirtualKey.Scroll => "ScrLk",
            VirtualKey.Pause => "Pause",
            VirtualKey.F1 => "F1",
            VirtualKey.F2 => "F2",
            VirtualKey.F3 => "F3",
            VirtualKey.F4 => "F4",
            VirtualKey.F5 => "F5",
            VirtualKey.F6 => "F6",
            VirtualKey.F7 => "F7",
            VirtualKey.F8 => "F8",
            VirtualKey.F9 => "F9",
            VirtualKey.F10 => "F10",
            VirtualKey.F11 => "F11",
            VirtualKey.F12 => "F12",
            VirtualKey.Multiply => "Num*",
            VirtualKey.Add => "Num+",
            VirtualKey.Subtract => "Num-",
            VirtualKey.Divide => "Num/",
            VirtualKey.Decimal => "Num.",
            VirtualKey.NumberKeyLock => "NumLk",
            VirtualKey.Application => "Menu",
            _ => key.ToString()
        };
    }

    private static List<List<(VirtualKey Key, string Label, double Width)>> GetKeyboardRows()
    {
        var rows = new List<List<(VirtualKey, string, double)>>();

        rows.Add([
            (VirtualKey.Escape, "Esc", 46),
            (VirtualKey.F1, "F1", 40), (VirtualKey.F2, "F2", 40), (VirtualKey.F3, "F3", 40), (VirtualKey.F4, "F4", 40),
            (VirtualKey.F5, "F5", 40), (VirtualKey.F6, "F6", 40), (VirtualKey.F7, "F7", 40), (VirtualKey.F8, "F8", 40),
            (VirtualKey.F9, "F9", 40), (VirtualKey.F10, "F10", 40), (VirtualKey.F11, "F11", 40), (VirtualKey.F12, "F12", 40),
            (VirtualKey.Snapshot, "PrtSc", 46), (VirtualKey.Scroll, "ScrLk", 46), (VirtualKey.Pause, "Pause", 46),
        ]);

        rows.Add([
            ((VirtualKey)0xC0, "`", 40),
            (VirtualKey.Number1, "1", 40), (VirtualKey.Number2, "2", 40), (VirtualKey.Number3, "3", 40),
            (VirtualKey.Number4, "4", 40), (VirtualKey.Number5, "5", 40), (VirtualKey.Number6, "6", 40),
            (VirtualKey.Number7, "7", 40), (VirtualKey.Number8, "8", 40), (VirtualKey.Number9, "9", 40),
            (VirtualKey.Number0, "0", 40), ((VirtualKey)0xBD, "-", 40), ((VirtualKey)0xBB, "=", 40),
            (VirtualKey.Back, "⌫", 72),
            (VirtualKey.Insert, "Ins", 46), (VirtualKey.Home, "Home", 46), (VirtualKey.PageUp, "PgUp", 46),
        ]);

        rows.Add([
            (VirtualKey.Tab, "Tab", 58),
            (VirtualKey.Q, "Q", 40), (VirtualKey.W, "W", 40), (VirtualKey.E, "E", 40), (VirtualKey.R, "R", 40),
            (VirtualKey.T, "T", 40), (VirtualKey.Y, "Y", 40), (VirtualKey.U, "U", 40), (VirtualKey.I, "I", 40),
            (VirtualKey.O, "O", 40), (VirtualKey.P, "P", 40),
            ((VirtualKey)0xDB, "[", 40), ((VirtualKey)0xDD, "]", 40), ((VirtualKey)0xDC, "\\", 58),
            (VirtualKey.Delete, "Del", 46), (VirtualKey.End, "End", 46), (VirtualKey.PageDown, "PgDn", 46),
        ]);

        rows.Add([
            (VirtualKey.CapitalLock, "Caps", 70),
            (VirtualKey.A, "A", 40), (VirtualKey.S, "S", 40), (VirtualKey.D, "D", 40), (VirtualKey.F, "F", 40),
            (VirtualKey.G, "G", 40), (VirtualKey.H, "H", 40), (VirtualKey.J, "J", 40), (VirtualKey.K, "K", 40),
            (VirtualKey.L, "L", 40),
            ((VirtualKey)0xBA, ";", 40), ((VirtualKey)0xDE, "'", 40), (VirtualKey.Enter, "Enter", 86),
        ]);

        rows.Add([
            (VirtualKey.Shift, "Shift", 90),
            (VirtualKey.Z, "Z", 40), (VirtualKey.X, "X", 40), (VirtualKey.C, "C", 40), (VirtualKey.V, "V", 40),
            (VirtualKey.B, "B", 40), (VirtualKey.N, "N", 40), (VirtualKey.M, "M", 40),
            ((VirtualKey)0xBC, ",", 40), ((VirtualKey)0xBE, ".", 40), ((VirtualKey)0xBF, "/", 40),
            (VirtualKey.Shift, "Shift", 110),
            (VirtualKey.Up, "↑", 46),
        ]);

        rows.Add([
            (VirtualKey.Control, "Ctrl", 58),
            (VirtualKey.LeftWindows, "Win", 46),
            (VirtualKey.Menu, "Alt", 52),
            (VirtualKey.Space, "Space", 250),
            (VirtualKey.Menu, "Alt", 52),
            (VirtualKey.RightWindows, "Win", 46),
            (VirtualKey.Control, "Ctrl", 58),
            (VirtualKey.Left, "←", 46), (VirtualKey.Down, "↓", 46), (VirtualKey.Right, "→", 46),
        ]);

        return rows;
    }
}
