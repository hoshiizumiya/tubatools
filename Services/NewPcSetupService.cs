using System.Diagnostics;
using System.Management;
using Microsoft.Win32;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class NewPcSetupService
{
    private static List<SetupPackage>? _commonCatalog;
    private static List<SetupPackage>? _devCommonCatalog;
    private static List<SetupPackage>? _aiToolsCatalog;
    private static List<DevLanguageGroup>? _devLanguages;
    private static List<SetupPackage>? _databaseTools;
    private static List<SetupPackage>? _apiTools;
    private static List<SetupPackage>? _manualTools;
    private static List<ServiceItem>? _optionalServices;

    public static bool IsAdmin
    {
        get
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    public static List<SetupPackage> GetCommonCatalog()
    {
        if (_commonCatalog is not null) return _commonCatalog;
        var catalog = LoadCatalogFromJson();
        _commonCatalog = catalog.CommonPackages;
        return _commonCatalog;
    }

    public static List<SetupPackage> GetDevCommonCatalog()
    {
        if (_devCommonCatalog is not null) return _devCommonCatalog;
        var catalog = LoadCatalogFromJson();
        _devCommonCatalog = catalog.DevCommonPackages;
        return _devCommonCatalog;
    }

    public static List<SetupPackage> GetAiToolsCatalog()
    {
        if (_aiToolsCatalog is not null) return _aiToolsCatalog;
        var catalog = LoadCatalogFromJson();
        _aiToolsCatalog = catalog.AiTools;
        return _aiToolsCatalog;
    }

    public static List<DevLanguageGroup> GetDevLanguages()
    {
        if (_devLanguages is not null) return _devLanguages;
        var catalog = LoadCatalogFromJson();
        _devLanguages = catalog.DevLanguages;
        return _devLanguages;
    }

    public static List<SetupPackage> GetDatabaseTools()
    {
        if (_databaseTools is not null) return _databaseTools;
        var catalog = LoadCatalogFromJson();
        _databaseTools = catalog.DatabaseTools;
        return _databaseTools;
    }

    public static List<SetupPackage> GetApiTools()
    {
        if (_apiTools is not null) return _apiTools;
        var catalog = LoadCatalogFromJson();
        _apiTools = catalog.ApiTools;
        return _apiTools;
    }

    public static List<SetupPackage> GetManualTools()
    {
        if (_manualTools is not null) return _manualTools;
        var catalog = LoadCatalogFromJson();
        _manualTools = catalog.ManualTools;
        return _manualTools;
    }

    public static List<ServiceItem> GetOptionalServices()
    {
        if (_optionalServices is not null) return _optionalServices;
        var catalog = LoadCatalogFromJson();
        _optionalServices = [];
        foreach (var s in catalog.OptionalServices)
        {
            uint startType = 4;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = $"qc {s.ServiceName}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    if (output.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase))
                        startType = 2;
                    else if (output.Contains("DEMAND_START", StringComparison.OrdinalIgnoreCase))
                        startType = 3;
                    else if (output.Contains("DISABLED", StringComparison.OrdinalIgnoreCase))
                        startType = 4;
                    else if (output.Contains("BOOT_START", StringComparison.OrdinalIgnoreCase))
                        startType = 0;
                    else if (output.Contains("SYSTEM_START", StringComparison.OrdinalIgnoreCase))
                        startType = 1;
                }
            }
            catch { }

            _optionalServices.Add(new ServiceItem
            {
                ServiceName = s.ServiceName,
                DisplayName = s.DisplayName,
                Description = s.Description,
                Recommendation = s.Recommendation,
                CurrentStartType = startType,
                WantDisable = s.Recommendation == "建议关闭"
            });
        }
        return _optionalServices;
    }

    public static List<StartupItem> GetStartupItems()
    {
        var items = new List<StartupItem>();

        foreach (var (hive, hiveName) in new[] { (Registry.CurrentUser, "HKCU"), (Registry.LocalMachine, "HKLM") })
        {
            foreach (var keyPath in new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
            })
            {
                try
                {
                    using var key = hive.OpenSubKey(keyPath);
                    if (key is null) continue;

                    foreach (var name in key.GetValueNames())
                    {
                        var cmd = key.GetValue(name) as string ?? "";
                        if (string.IsNullOrWhiteSpace(cmd)) continue;

                        var systemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            "SecurityHealth", "SysTrayApp", "HotKeysCmds", "Persistence",
                            "CTFMON", "cftmon.exe", "NvCplDaemon", "NvMediaCenter",
                            "RTHDCPL", "Skytel", "Alcmtr", "AGRSMMSG",
                            "System", "SystemInitialization", "WindowsDefender",
                            "OneDriveSetup", "DesktopCleanUP", "Load"
                        };

                        var isSystem = systemNames.Contains(name) ||
                            cmd.Contains(@"\Windows\System32\", StringComparison.OrdinalIgnoreCase) ||
                            cmd.Contains(@"\Windows\SysWOW64\", StringComparison.OrdinalIgnoreCase) ||
                            cmd.Contains("SecurityHealth", StringComparison.OrdinalIgnoreCase) ||
                            cmd.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase) ||
                            cmd.Contains("msiexec", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("System", StringComparison.OrdinalIgnoreCase);

                        items.Add(new StartupItem
                        {
                            Name = name,
                            Command = cmd,
                            Location = $"{hiveName}\\{keyPath}",
                            WantDisable = false
                        });
                    }
                }
                catch { }
            }
        }

        return items;
    }

    public static bool DisableStartupItem(StartupItem item)
    {
        try
        {
            var hive = item.Location.StartsWith("HKLM") ? Registry.LocalMachine : Registry.CurrentUser;
            var keyPath = item.Location.Contains('\\') ? item.Location[item.Location.IndexOf('\\')..] : "";

            using var key = hive.OpenSubKey(keyPath, true);
            if (key is null) return false;

            var cmd = key.GetValue(item.Name) as string;
            if (cmd is null) return false;

            key.DeleteValue(item.Name);
            var disabledKey = hive.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true);
            disabledKey?.SetValue(item.Name, new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

            return true;
        }
        catch { return false; }
    }

    public static bool SetServiceStartType(string serviceName, uint startType)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc",
                Arguments = $"config {serviceName} start= {startType}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    public static bool IsVisualEffectsDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
            var val = key?.GetValue("VisualFXSetting");
            return val is int i && i == 3;
        }
        catch { return false; }
    }

    public static bool DisableVisualEffects()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", true);
            key.SetValue("VisualFXSetting", 3, RegistryValueKind.DWord);

            using var advKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", true);
            advKey.SetValue("DragFullWindows", "0", RegistryValueKind.String);
            advKey.SetValue("FontSmoothing", "0", RegistryValueKind.String);
            advKey.SetValue("UserPreferencesMask", new byte[] { 0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00 }, RegistryValueKind.Binary);

            using var winMetrics = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop\WindowMetrics", true);
            winMetrics.SetValue("MinAnimate", "0", RegistryValueKind.String);

            return true;
        }
        catch { return false; }
    }

    public static bool AreSystemAdsDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager");
            if (key is null) return false;
            var sub = key.GetValue("SubscribedContent-338389Enabled");
            return sub is int i && i == 0;
        }
        catch { return false; }
    }

    public static bool DisableSystemAds()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", true);
            var values = new[] { "SubscribedContent-338389Enabled", "SubscribedContent-310093Enabled", "SubscribedContent-338388Enabled", "SubscribedContent-338393Enabled", "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled", "SystemPaneSuggestionsEnabled", "SoftLandingEnabled", "RotatingLockScreenEnabled", "RotatingLockScreenOverlayEnabled" };
            foreach (var v in values)
                key.SetValue(v, 0, RegistryValueKind.DWord);

            using var cloudKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CloudStore", true);
            try { Registry.CurrentUser.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CloudStore"); } catch { }

            return true;
        }
        catch { return false; }
    }

    public static bool AreTaskbarWidgetsDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            var val = key?.GetValue("TaskbarDa");
            return val is int i && i == 0;
        }
        catch { return false; }
    }

    public static bool DisableTaskbarWidgets()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
            key.SetValue("TaskbarDa", 0, RegistryValueKind.DWord);
            key.SetValue("TaskbarAl", 0, RegistryValueKind.DWord);
            key.SetValue("ShowCopilotButton", 0, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    public static bool AreFileExtensionsShown()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            var val = key?.GetValue("HideFileExt");
            return val is int i && i == 0;
        }
        catch { return false; }
    }

    public static bool ShowFileExtensions()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
            key.SetValue("HideFileExt", 0, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    public static bool AreHiddenFilesShown()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            var val = key?.GetValue("Hidden");
            return val is int i && i == 1;
        }
        catch { return false; }
    }

    public static bool ShowHiddenFiles()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
            key.SetValue("Hidden", 1, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    public static bool BlockMalwareCerts()
    {
        try
        {
            CertBlockService.LoadAsync();
            CertBlockService.BlockAll();
            return true;
        }
        catch { return false; }
    }

    public static bool IsDefenderRealtimeEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection");
            var val = key?.GetValue("DisableRealtimeMonitoring");
            return val is not int i || i == 0;
        }
        catch { return true; }
    }

    public static bool SetDefenderRealtime(bool enable)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection", true);
            key.SetValue("DisableRealtimeMonitoring", enable ? 0 : 1, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    public static bool IsAutoRunDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer");
            var val = key?.GetValue("NoDriveTypeAutoRun");
            return val is int i && i == 255;
        }
        catch { return false; }
    }

    public static bool DisableAutoRun()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", true);
            key.SetValue("NoDriveTypeAutoRun", 255, RegistryValueKind.DWord);
            using var lkey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", true);
            lkey.SetValue("NoDriveTypeAutoRun", 255, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    public static bool IsFirewallEnabled()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\StandardCimv2", "SELECT Enabled FROM MSFT_NetFirewallProfile");
            foreach (var obj in searcher.Get())
            {
                if (obj["Enabled"] is bool enabled && !enabled)
                    return false;
            }
            return true;
        }
        catch
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "advfirewall show allprofiles state",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var process = Process.Start(psi);
                if (process is null) return false;
                var output = process.StandardOutput.ReadToEnd();
                return output.Contains("ON", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    public static async Task ConfigureDevEnvironmentAsync(string[] languages, IProgress<string>? progress)
    {
        progress?.Report("配置 Git 基础设置...");
        await RunCommandAsync("git", "config --global core.autocrlf true");
        await RunCommandAsync("git", "config --global core.longpaths true");
        await RunCommandAsync("git", "config --global init.defaultBranch main");

        if (languages.Contains("Node.js/前端") || languages.Contains("AI"))
        {
            progress?.Report("配置 npm 国内镜像源...");
            await RunCommandAsync("npm", "config set registry https://registry.npmmirror.com");
        }

        if (languages.Contains("Python"))
        {
            progress?.Report("配置 pip 国内镜像源...");
            try
            {
                var pipDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pip");
                Directory.CreateDirectory(pipDir);
                var pipIni = Path.Combine(pipDir, "pip.ini");
                if (!File.Exists(pipIni))
                {
                    await File.WriteAllTextAsync(pipIni, "[global]\nindex-url = https://pypi.tuna.tsinghua.edu.cn/simple\n");
                }
            }
            catch { }
        }

        progress?.Report("开发环境配置完成");
    }

    public static async Task<WingetInstallResult> InstallPackageAsync(SetupPackage pkg, IProgress<WingetInstallProgress>? progress, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(pkg.InstallCommand))
        {
            return await InstallNpmPackageAsync(pkg, ct);
        }

        if (!string.IsNullOrEmpty(pkg.ManualUrl))
        {
            return new WingetInstallResult(false, "需手动下载");
        }

        var source = pkg.Source ?? "winget";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = $"install --id {pkg.Id} --accept-package-agreements --accept-source-agreements --source {source}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return new WingetInstallResult(false, "无法启动 winget 进程");

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var line = await process.StandardOutput.ReadLineAsync(ct);
                if (line is null) break;
                progress?.Report(new WingetInstallProgress(pkg.Id, line, ParseProgressFromLine(line)));
            }

            await process.WaitForExitAsync(ct);
            var success = process.ExitCode == 0;
            return new WingetInstallResult(success, success ? "安装成功" : $"安装失败 (退出代码: {process.ExitCode})");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new WingetInstallResult(false, ex.Message); }
    }

    private static async Task<WingetInstallResult> InstallNpmPackageAsync(SetupPackage pkg, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c {pkg.InstallCommand}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return new WingetInstallResult(false, "无法启动 npm 进程");

            await process.WaitForExitAsync(ct);
            var success = process.ExitCode == 0;
            return new WingetInstallResult(success, success ? "安装成功" : $"安装失败 (退出代码: {process.ExitCode})");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new WingetInstallResult(false, ex.Message); }
    }

    public static async Task<bool> IsPackageInstalledAsync(string packageId, CancellationToken ct = default)
    {
        if (packageId.Contains("openai-codex") || packageId.Contains("claude-code") || packageId.Contains("opencode-ai"))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c where {packageId.Replace("openai-codex", "codex").Replace("claude-code", "claude").Replace("opencode-ai", "opencode")}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var process = Process.Start(psi);
                if (process is null) return false;
                await process.WaitForExitAsync(ct);
                return process.ExitCode == 0;
            }
            catch { return false; }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = $"list --id {packageId} --accept-source-agreements",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return output.Contains(packageId, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static List<SetupPackage> BuildDevPackageList(string[] selectedLanguages)
    {
        var packages = new List<SetupPackage>();
        var seenIds = new HashSet<string>();

        foreach (var p in GetDevCommonCatalog())
        {
            if (seenIds.Add(p.Id))
                packages.Add(p);
        }

        foreach (var lang in GetDevLanguages())
        {
            if (!selectedLanguages.Contains(lang.Language)) continue;
            foreach (var p in lang.Packages)
            {
                if (p.Id == "Microsoft.VisualStudio.Community" && !seenIds.Add(p.Id))
                    continue;
                if (seenIds.Add(p.Id))
                    packages.Add(p);
            }
        }

        foreach (var p in GetDatabaseTools())
        {
            if (seenIds.Add(p.Id))
                packages.Add(p);
        }

        foreach (var p in GetApiTools())
        {
            if (seenIds.Add(p.Id))
                packages.Add(p);
        }

        foreach (var p in GetAiToolsCatalog())
        {
            if (seenIds.Add(p.Id))
                packages.Add(p);
        }

        return packages;
    }

    public static List<SetupPackage> ResolveDependencies(List<SetupPackage> selected)
    {
        var result = new List<SetupPackage>();
        var idMap = new Dictionary<string, SetupPackage>();
        var added = new HashSet<string>();

        foreach (var p in selected)
            idMap[p.Id] = p;

        foreach (var p in selected)
        {
            if (!string.IsNullOrEmpty(p.DependsOn) && !added.Contains(p.DependsOn))
            {
                if (idMap.TryGetValue(p.DependsOn, out var dep))
                {
                    result.Add(dep);
                    added.Add(dep.Id);
                }
                else
                {
                    var allPkgs = GetDevCommonCatalog().Concat(GetCommonCatalog()).Concat(GetAiToolsCatalog());
                    var found = allPkgs.FirstOrDefault(x => x.Id == p.DependsOn);
                    if (found is not null && added.Add(found.Id))
                    {
                        found.IsSelected = true;
                        result.Add(found);
                    }
                }
            }
        }

        foreach (var p in selected)
        {
            if (added.Add(p.Id))
                result.Add(p);
        }

        return result;
    }

    private static int ParseProgressFromLine(string line)
    {
        if (line.Contains("已成功安装", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Successfully installed", StringComparison.OrdinalIgnoreCase))
            return 100;
        if (line.Contains("正在下载", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Downloading", StringComparison.OrdinalIgnoreCase))
            return 30;
        if (line.Contains("正在安装", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Installing", StringComparison.OrdinalIgnoreCase))
            return 70;
        if (line.Contains("正在验证", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Verifying", StringComparison.OrdinalIgnoreCase))
            return 85;
        if (line.Contains("启动", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Starting", StringComparison.OrdinalIgnoreCase))
            return 10;
        return 0;
    }

    private static async Task RunCommandAsync(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var process = Process.Start(psi);
            if (process is not null)
                await process.WaitForExitAsync();
        }
        catch { }
    }

    private static NewPcSetupCatalog LoadCatalogFromJson()
    {
        var paths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Metadata", "newpc-setup-catalog.json"),
            Path.Combine(AppContext.BaseDirectory, "newpc-setup-catalog.json"),
        };

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return System.Text.Json.JsonSerializer.Deserialize<NewPcSetupCatalog>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
        }

        return new NewPcSetupCatalog();
    }

    public static void ReloadCatalog()
    {
        _commonCatalog = null;
        _devCommonCatalog = null;
        _aiToolsCatalog = null;
        _devLanguages = null;
        _databaseTools = null;
        _apiTools = null;
        _manualTools = null;
        _optionalServices = null;
    }
}

public sealed class NewPcSetupCatalog
{
    public List<SetupPackage> CommonPackages { get; set; } = [];
    public List<SetupPackage> DevCommonPackages { get; set; } = [];
    public List<SetupPackage> AiTools { get; set; } = [];
    public List<DevLanguageGroup> DevLanguages { get; set; } = [];
    public List<SetupPackage> DatabaseTools { get; set; } = [];
    public List<SetupPackage> ApiTools { get; set; } = [];
    public List<SetupPackage> ManualTools { get; set; } = [];
    public List<ServiceCatalogEntry> OptionalServices { get; set; } = [];
}

public sealed class ServiceCatalogEntry
{
    public required string ServiceName { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string Recommendation { get; init; }
}
