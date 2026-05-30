using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TubaWinUi3.Pages;

public sealed partial class HardwarePage : Page
{
    private readonly ObservableCollection<HardwareMetricTile> _metrics = [];
    private readonly ObservableCollection<HardwareInfoItem> _profileItems = [];
    private readonly LiteMonitorService _monitor = LiteMonitorService.Instance;
    private readonly double[] _intervals = [0.5, 1, 2, 5];
    private DispatcherTimer? _timer;
    private bool _isReading;
    private bool _fpsPreparing;
    private bool _fpsProcessUpdating;
    private string _fpsProcessSnapshot = "";

    private HardwareMetricTile _cpu = null!;
    private HardwareMetricTile _gpu = null!;
    private HardwareMetricTile _memory = null!;
    private HardwareMetricTile _disk = null!;
    private HardwareMetricTile _network = null!;
    private HardwareMetricTile _fps = null!;
    private HardwareMetricTile _battery = null!;
    private HardwareMetricTile _uptime = null!;

    public HardwarePage()
    {
        InitializeComponent();
        BuildMetricTiles();

        MetricRepeater.ItemsSource = _metrics;
        ProfileRepeater.ItemsSource = _profileItems;

        Loaded += HardwarePage_Loaded;
        Unloaded += HardwarePage_Unloaded;
    }

    private void HardwarePage_Loaded(object sender, RoutedEventArgs e)
    {
        DriverWarning.IsOpen = !LiteMonitorService.IsDriverReady();
        StartTimer();
        _ = LoadProfileAsync();
        ReadLiveMetrics();
    }

    private void HardwarePage_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
        _monitor.ClearFpsFocus();
    }

    private void BuildMetricTiles()
    {
        _cpu = AddMetric("处理器", "\uE950");
        _gpu = AddMetric("显卡", "\uE7F4");
        _memory = AddMetric("内存", "\uE965");
        _disk = AddMetric("磁盘", "\uEDA2");
        _network = AddMetric("网络", "\uE968");
        _fps = AddMetric("FPS", "\uE7FC");
        _battery = AddMetric("电池", "\uE85A");
        _uptime = AddMetric("运行时间", "\uE917");
    }

    private HardwareMetricTile AddMetric(string title, string glyph)
    {
        var tile = new HardwareMetricTile(title, glyph);
        _metrics.Add(tile);
        return tile;
    }

    private void StartTimer()
    {
        _timer?.Stop();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_intervals[Math.Clamp(RefreshIntervalBox.SelectedIndex, 0, _intervals.Length - 1)])
        };
        _timer.Tick += (_, _) => ReadLiveMetrics();
        _timer.Start();
    }

    private void RefreshIntervalBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_timer == null || RefreshIntervalBox.SelectedIndex < 0) return;
        _timer.Interval = TimeSpan.FromSeconds(_intervals[RefreshIntervalBox.SelectedIndex]);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadProfileAsync(forceRefresh: true);
        ReadLiveMetrics();
    }

    private async Task LoadProfileAsync(bool forceRefresh = false)
    {
        SetProfileLoading(true);

        try
        {
            var sections = await HardwareInfoService.LoadAsync(forceRefresh);
            _profileItems.Clear();

            foreach (var item in sections.SelectMany(section => section.Items))
            {
                if (_profileItems.Any(existing => existing.Label == item.Label && existing.Value == item.Value))
                    continue;

                _profileItems.Add(item);
            }

            ProfileStatusText.Text = $"已读取 {_profileItems.Count} 项硬件档案，点击任意项目可复制。";
            StatusBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            _profileItems.Clear();
            ProfileStatusText.Text = "硬件档案读取失败";
            ShowStatus("硬件档案读取失败", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetProfileLoading(false);
        }
    }

    private void SetProfileLoading(bool isLoading)
    {
        ProfileLoadingRing.IsActive = isLoading;
        ProfileLoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReadLiveMetrics()
    {
        if (_isReading) return;
        _isReading = true;

        try
        {
            var fpsEnabled = FpsToggle.IsOn;
            var sample = _monitor.Read(fpsEnabled);
            ApplySample(sample, fpsEnabled);
            DriverWarning.IsOpen = !LiteMonitorService.IsDriverReady();
        }
        catch (Exception ex)
        {
            MonitorStatusText.Text = "实时监控读取失败";
            ShowStatus("实时监控读取失败", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _isReading = false;
        }
    }

    private void ApplySample(MonitorSample s, bool fpsEnabled)
    {
        _cpu.Set(
            Percent(s.CpuLoad),
            $"温度 {Temperature(s.CpuTemp)}  频率 {CpuClock(s.CpuClock)}",
            $"功耗 {Power(s.CpuPower)}  {NameOrFallback(s.CpuName)}",
            ClampPercent(s.CpuLoad),
            "CPU 总占用");

        _gpu.Set(
            Percent(s.GpuLoad),
            $"温度 {Temperature(s.GpuTemp)}  频率 {GpuClock(s.GpuClock)}",
            $"功耗 {Power(s.GpuPower)}  显存 {Percent(s.GpuVramLoad)} {UsedGpuMemory(s.GpuVramUsedGB)}",
            ClampPercent(s.GpuLoad),
            NameOrFallback(s.GpuName, "GPU 总占用"));

        _memory.Set(
            Percent(s.MemLoad),
            s.MemUsedGB >= 0 && s.MemTotalGB > 0 ? $"{s.MemUsedGB:F1} / {s.MemTotalGB:F1} GB" : "容量 --",
            "系统物理内存占用",
            ClampPercent(s.MemLoad),
            "内存占用");

        _disk.Set(
            s.DiskReadMBs >= 0 || s.DiskWriteMBs >= 0 ? $"{Math.Max(0, s.DiskReadMBs + s.DiskWriteMBs):F1} MB/s" : "--",
            $"读取 {Speed(s.DiskReadMBs)}  写入 {Speed(s.DiskWriteMBs)}",
            $"硬盘温度 {Temperature(s.DiskTemp)}",
            0,
            "磁盘实时吞吐");

        _network.Set(
            s.NetDownMBs >= 0 || s.NetUpMBs >= 0 ? $"↓ {Speed(s.NetDownMBs)}" : "--",
            $"上传 {Speed(s.NetUpMBs)}",
            "物理网卡实时速率",
            0,
            "网络吞吐");

        _fps.Set(
            fpsEnabled ? (s.Fps >= 1 ? $"{s.Fps:0}" : "--") : "关闭",
            fpsEnabled ? NameOrFallback(s.FpsProcess, "等待渲染进程") : "开启后自动下载并启动 FPS 组件",
            IsRunningAsAdmin() ? "PresentMon 采集" : "FPS 需要管理员权限",
            fpsEnabled && s.Fps > 0 ? Math.Min(100, s.Fps / 1.44) : 0,
            fpsEnabled ? "当前帧率" : "未启用");

        _battery.Set(
            s.BatPercent >= 0 ? $"{s.BatPercent:0}%" : "未检测到",
            s.BatPower > 0 ? (s.BatCharging ? $"充电 {s.BatPower:0.0} W" : $"放电 {s.BatPower:0.0} W") : "电源状态 --",
            "笔记本电池",
            ClampPercent(s.BatPercent),
            "电量");

        _uptime.Set(
            FormatUptime(),
            "系统启动后经过的时间",
            $"当前时间 {DateTime.Now:HH:mm:ss}",
            0,
            "运行时间");

        MonitorStatusText.Text = $"实时数据已更新：{DateTime.Now:HH:mm:ss}";

        if (fpsEnabled)
        {
            UpdateFpsProcessBox();
        }
    }

    private async void InstallDriverButton_Click(object sender, RoutedEventArgs e)
    {
        InstallDriverButton.IsEnabled = false;
        InstallDriverButton.Content = "安装中...";

        try
        {
            var ok = await _monitor.EnsureDriverAsync(XamlRoot);
            if (ok)
            {
                DriverWarning.IsOpen = false;
                LiteMonitorService.ReinitLhm();
                ShowStatus("驱动已就绪", "传感器数据会在下一次刷新时更新。", InfoBarSeverity.Success);
                ReadLiveMetrics();
            }
            else
            {
                ShowStatus("驱动未安装", "已取消或安装失败，部分温度/功耗/频率数据可能不可用。", InfoBarSeverity.Warning);
            }
        }
        finally
        {
            InstallDriverButton.Content = "自动安装";
            InstallDriverButton.IsEnabled = true;
        }
    }

    private async void FpsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_fpsPreparing) return;

        if (!FpsToggle.IsOn)
        {
            FpsProcessBox.IsEnabled = false;
            FpsProcessBox.Items.Clear();
            _fpsProcessSnapshot = "";
            _monitor.ClearFpsFocus();
            ReadLiveMetrics();
            return;
        }

        _fpsPreparing = true;
        FpsToggle.IsEnabled = false;

        try
        {
            if (!IsRunningAsAdmin())
            {
                FpsToggle.IsOn = false;
                ShowStatus("需要管理员权限", "FPS 检测需要管理员权限。请以管理员身份运行本程序后再开启。", InfoBarSeverity.Warning);
                return;
            }

            var ok = await _monitor.EnsureFpsComponentAsync(XamlRoot);
            if (!ok)
            {
                FpsToggle.IsOn = false;
                ShowStatus("FPS 组件不可用", "已取消下载或组件校验失败。", InfoBarSeverity.Warning);
                return;
            }

            FpsProcessBox.IsEnabled = true;
            ShowStatus("FPS 已开启", "会自动选择当前有帧输出的游戏或渲染进程。", InfoBarSeverity.Success);
            ReadLiveMetrics();
        }
        finally
        {
            FpsToggle.IsEnabled = true;
            _fpsPreparing = false;
        }
    }

    private void UpdateFpsProcessBox()
    {
        var processes = _monitor.GetFpsProcessList();
        var snapshot = string.Join("|", processes.Select(process => $"{process.pid}:{process.name}:{process.fps:0}"));
        if (snapshot == _fpsProcessSnapshot) return;
        _fpsProcessSnapshot = snapshot;

        _fpsProcessUpdating = true;
        try
        {
            var previousIndex = FpsProcessBox.SelectedIndex;
            FpsProcessBox.Items.Clear();
            FpsProcessBox.Items.Add("自动选择");

            foreach (var process in processes)
            {
                FpsProcessBox.Items.Add($"{process.name} ({process.pid}) - {process.fps:0} FPS");
            }

            FpsProcessBox.SelectedIndex = previousIndex >= 0 && previousIndex < FpsProcessBox.Items.Count ? previousIndex : 0;
        }
        finally
        {
            _fpsProcessUpdating = false;
        }
    }

    private void FpsProcessBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fpsProcessUpdating) return;

        if (FpsProcessBox.SelectedIndex <= 0)
        {
            _monitor.ClearFpsFocus();
            return;
        }

        var processes = _monitor.GetFpsProcessList();
        var index = FpsProcessBox.SelectedIndex - 1;
        if (index >= 0 && index < processes.Count)
        {
            _monitor.SetFpsFocus(processes[index].pid);
        }
    }

    private void ProfileItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HardwareInfoItem item }) return;

        var package = new DataPackage();
        package.SetText(item.Value);
        Clipboard.SetContent(package);
        ShowStatus("已复制", item.Value.Length > 80 ? item.Value[..80] + "..." : item.Value, InfoBarSeverity.Success);
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private static string Percent(float value) => value >= 0 ? $"{value:0}%" : "--";
    private static string Temperature(float value) => value >= 0 ? $"{value:0} °C" : "--";
    private static string Power(float value) => value > 0 ? $"{value:0.0} W" : "--";
    private static string Speed(float value) => value >= 0 ? $"{value:F1} MB/s" : "--";
    private static string CpuClock(float value) => value > 0 ? $"{value / 1000f:0.00} GHz" : "--";
    private static string GpuClock(float value) => value > 0 ? $"{value:0} MHz" : "--";
    private static string UsedGpuMemory(float value) => value > 0 ? $"({value:F1} GB)" : "";
    private static double ClampPercent(float value) => value < 0 ? 0 : Math.Clamp(value, 0, 100);

    private static string FormatUptime()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return $"{uptime.Days}天 {uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";
    }

    private static string NameOrFallback(string value, string fallback = "--")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}

public sealed class HardwareMetricTile : INotifyPropertyChanged
{
    private string _primary = "--";
    private string _secondary = "--";
    private string _tertiary = "--";
    private double _progress;
    private string _progressText = "--";

    public HardwareMetricTile(string title, string glyph)
    {
        Title = title;
        Glyph = glyph;
    }

    public string Title { get; }
    public string Glyph { get; }

    public string Primary
    {
        get => _primary;
        private set => SetField(ref _primary, value);
    }

    public string Secondary
    {
        get => _secondary;
        private set => SetField(ref _secondary, value);
    }

    public string Tertiary
    {
        get => _tertiary;
        private set => SetField(ref _tertiary, value);
    }

    public double Progress
    {
        get => _progress;
        private set => SetField(ref _progress, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetField(ref _progressText, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Set(string primary, string secondary, string tertiary, double progress, string progressText)
    {
        Primary = primary;
        Secondary = secondary;
        Tertiary = tertiary;
        Progress = progress;
        ProgressText = progressText;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
