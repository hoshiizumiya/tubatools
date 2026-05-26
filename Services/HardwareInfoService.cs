using System.Management;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class HardwareInfoService
{
    public static Task<IReadOnlyList<HardwareInfoSection>> LoadAsync()
    {
        return Task.Run<IReadOnlyList<HardwareInfoSection>>(() =>
        {
            var sections = CreateEmptySections();

            FillSummary(sections[0]);
            FillSystem(sections[1]);
            FillDetails(sections[2]);

            return sections;
        });
    }

    private static List<HardwareInfoSection> CreateEmptySections()
    {
        return
        [
            new HardwareInfoSection { Title = "型号信息", Glyph = "\uE7F4" },
            new HardwareInfoSection { Title = "系统信息", Glyph = "\uE782" },
            new HardwareInfoSection { Title = "详细信息", Glyph = "\uE9D9" }
        ];
    }

    private static void FillSummary(HardwareInfoSection section)
    {
        var computer = First("Win32_ComputerSystem");
        var board = First("Win32_BaseBoard");
        var bios = First("Win32_BIOS");

        section.Items.Add(Item("设备型号", Join(Get(computer, "Manufacturer"), Get(computer, "Model"))));
        section.Items.Add(Item("主板", Join(Get(board, "Manufacturer"), Get(board, "Product"))));
        section.Items.Add(Item("BIOS", Join(Get(bios, "Manufacturer"), Get(bios, "SMBIOSBIOSVersion"))));
    }

    private static void FillSystem(HardwareInfoSection section)
    {
        var os = First("Win32_OperatingSystem");

        section.Items.Add(Item("系统", Join(Get(os, "Caption"), Get(os, "OSArchitecture"))));
        section.Items.Add(Item("版本", Get(os, "Version")));
        section.Items.Add(Item("运行时间", FormatUptime()));
    }

    private static void FillDetails(HardwareInfoSection section)
    {
        section.Items.Add(Item("处理器", FirstName("Win32_Processor")));
        section.Items.Add(Item("内存", FormatMemory()));
        section.Items.Add(Item("显卡", JoinNames("Win32_VideoController")));
        section.Items.Add(Item("显示器", FormatDisplays()));
        section.Items.Add(Item("磁盘", FormatDisks()));
        section.Items.Add(Item("声卡", JoinNames("Win32_SoundDevice")));
        section.Items.Add(Item("网卡", JoinNames("Win32_NetworkAdapter", item =>
            IsTrue(item, "PhysicalAdapter") &&
            !ContainsAny(Get(item, "Name"), "Virtual", "Bluetooth", "WAN Miniport"))));
    }

    private static HardwareInfoItem Item(string label, string? value)
    {
        return new HardwareInfoItem
        {
            Label = label,
            Value = string.IsNullOrWhiteSpace(value) ? "未知" : value
        };
    }

    private static string FormatMemory()
    {
        var modules = Query("Win32_PhysicalMemory").ToList();
        if (modules.Count == 0)
        {
            return "未知";
        }

        var totalBytes = modules.Sum(item => ToLong(Get(item, "Capacity")));
        var manufacturer = modules.Select(item => Get(item, "Manufacturer")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var speed = modules.Select(item => Get(item, "Speed")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return $"{manufacturer} {totalBytes / 1024d / 1024d / 1024d:0.#}GB {speed}MHz ({modules.Count} 条)";
    }

    private static string FormatDisks()
    {
        var disks = Query("Win32_DiskDrive")
            .Select(item =>
            {
                var model = Get(item, "Model");
                var size = ToLong(Get(item, "Size")) / 1024d / 1024d / 1024d;
                return string.IsNullOrWhiteSpace(model) ? null : $"{model} ({size:0.#}GB)";
            })
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return string.Join(" / ", disks);
    }

    private static string FormatDisplays()
    {
        var monitors = QueryWmiNamespace("root\\WMI", "WmiMonitorID")
            .Select(item =>
            {
                var mfr = GetManufacturerName(Get(item, "ManufacturerName"));
                var product = DecodeWmiString(Get(item, "ProductName"));
                var serial = DecodeWmiString(Get(item, "SerialNumberID"));
                var pnpId = Get(item, "InstanceName")?.Split('\\').FirstOrDefault() ?? "";

                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(mfr)) parts.Add(mfr);
                if (!string.IsNullOrWhiteSpace(product)) parts.Add(product);
                if (parts.Count == 0 && !string.IsNullOrWhiteSpace(pnpId)) parts.Add(DecodePnpManufacturer(pnpId));

                var label = string.Join(" ", parts.Distinct());
                if (!string.IsNullOrWhiteSpace(serial) && serial != "0") label += $" (SN:{serial})";
                return string.IsNullOrWhiteSpace(label) ? null : label;
            })
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct()
            .ToList();

        var resolutions = Query("Win32_VideoController")
            .Select(item =>
            {
                var width = Get(item, "CurrentHorizontalResolution");
                var height = Get(item, "CurrentVerticalResolution");
                return string.IsNullOrWhiteSpace(width) || string.IsNullOrWhiteSpace(height)
                    ? null
                    : $"{width} x {height}";
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct()
            .ToList();

        if (monitors.Count == 0 && resolutions.Count == 0) return "未知";
        if (monitors.Count == 0) return string.Join(" / ", resolutions);
        if (resolutions.Count == 0) return string.Join(" / ", monitors);

        return string.Join(" / ", monitors.Select((name, i) =>
        {
            var res = resolutions[Math.Min(i, resolutions.Count - 1)];
            return $"{name} [{res}]";
        }));
    }

    private static IEnumerable<ManagementBaseObject> QueryWmiNamespace(string ns, string className)
    {
        ManagementObjectCollection? collection = null;
        try
        {
            using var searcher = new ManagementObjectSearcher($"{ns}", $"SELECT * FROM {className}");
            collection = searcher.Get();
        }
        catch { yield break; }

        foreach (ManagementBaseObject item in collection) yield return item;
    }

    private static string? DecodeWmiString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim('{', '}', ' ');
        var chars = trimmed.Split(',')
            .Select(s => int.TryParse(s.Trim(), out var c) ? c : 0)
            .TakeWhile(c => c > 0)
            .Select(c => (char)c)
            .ToArray();
        return chars.Length > 0 ? new string(chars).Trim() : null;
    }

    private static string? GetManufacturerName(string? raw)
    {
        var decoded = DecodeWmiString(raw);
        if (string.IsNullOrWhiteSpace(decoded)) return null;
        return decoded switch
        {
            "ACI" or "ACR" => "Acer",
            "AUO" or "AUO_" => "AU Optronics",
            "BOE" or "BOE_" => "京东方",
            "CMN" => "奇美",
            "CSO" => "华星光电",
            "IVO" => "天马",
            "INL" => "Innolux",
            "LGD" or "LPL" => "LG Display",
            "LEN" or "LEN_" => "联想",
            "SEC" or "SDC" => "三星",
            "SHV" => "Sharp",
            _ => decoded
        };
    }

    private static string DecodePnpManufacturer(string pnpId)
    {
        if (pnpId.Length < 3) return pnpId;
        var code = pnpId.Substring(0, 3).ToUpperInvariant();
        return code switch
        {
            "ACI" or "ACR" => "Acer",
            "AUO" => "AU Optronics",
            "BOE" => "京东方",
            "CMN" => "奇美",
            "CSO" => "华星光电",
            "DEL" => "Dell",
            "HSD" => "瀚宇彩晶",
            "HKC" => "HKC",
            "IVO" => "天马",
            "INL" => "Innolux",
            "LGD" or "LPL" => "LG Display",
            "LEN" => "联想",
            "SAM" or "SDC" or "SEC" => "三星",
            "SHV" => "Sharp",
            _ => code
        };
    }

    private static string FormatUptime()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return $"{uptime.Days}天{uptime.Hours}小时{uptime.Minutes}分钟{uptime.Seconds}秒";
    }

    private static string FirstName(string className)
    {
        return Query(className).Select(item => Get(item, "Name")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "未知";
    }

    private static string JoinNames(string className, Func<ManagementBaseObject, bool>? filter = null)
    {
        var names = Query(className)
            .Where(item => filter?.Invoke(item) ?? true)
            .Select(item => Get(item, "Name"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct();

        return string.Join(" / ", names);
    }

    private static ManagementBaseObject? First(string className)
    {
        return Query(className).FirstOrDefault();
    }

    private static IEnumerable<ManagementBaseObject> Query(string className)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {className}");
            foreach (ManagementBaseObject item in searcher.Get())
            {
                yield return item;
            }
        }
        finally
        {
        }
    }

    private static string? Get(ManagementBaseObject? item, string propertyName)
    {
        try
        {
            return item?[propertyName]?.ToString()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsTrue(ManagementBaseObject item, string propertyName)
    {
        return bool.TryParse(Get(item, propertyName), out var value) && value;
    }

    private static bool ContainsAny(string? value, params string[] needles)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static long ToLong(string? value)
    {
        return long.TryParse(value, out var number) ? number : 0;
    }

    private static string Join(params string?[] values)
    {
        return string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string? FirstUseful(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
