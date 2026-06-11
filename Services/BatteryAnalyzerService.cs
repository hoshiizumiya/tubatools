using System.Diagnostics;
using System.Management;
using System.Xml;
using System.Xml.Linq;

namespace TubaWinUi3.Services;

public sealed record BatteryTrendPoint(DateTime Timestamp, int ChargePercent, int CapacityMwh, bool OnAcPower, string State);

public sealed record BatteryWeeklyEntry(DateTime StartDate, DateTime EndDate, double FullChargeMwh, double DesignMwh, int CycleCount, TimeSpan ActiveDcTime, double ActiveDcEnergyMwh);

public sealed record BatteryRealtimeStatus(int DischargeRateMw, int ChargeRateMw, int RemainingCapacityMwh, int VoltageMv, bool Charging, bool Discharging, bool PowerOnline);

public sealed record ProcessPowerEntry(string ProcessName, double CpuPercent, long MemoryBytes, int Pid);

public static class BatteryAnalyzerService
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/battery/2012";
    private static Dictionary<int, (TimeSpan CpuTime, DateTime SampleTime)> _prevCpu = [];
    private static DateTime _prevSampleTime = DateTime.MinValue;

    public static async Task<List<BatteryTrendPoint>> GetTrendAsync(int days)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"tuba-bat-trend-{Guid.NewGuid():N}.xml");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = $"/batteryreport /output \"{xmlPath}\" /xml /duration {days}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return [];
            await process.WaitForExitAsync();
            if (!File.Exists(xmlPath)) return [];

            var xml = await File.ReadAllTextAsync(xmlPath);
            return ParseTrendFromXml(xml);
        }
        catch { return []; }
        finally
        {
            try { if (File.Exists(xmlPath)) File.Delete(xmlPath); } catch { }
        }
    }

    public static async Task<List<BatteryWeeklyEntry>> GetWeeklyHistoryAsync()
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"tuba-bat-weekly-{Guid.NewGuid():N}.xml");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = $"/batteryreport /output \"{xmlPath}\" /xml",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return [];
            await process.WaitForExitAsync();
            if (!File.Exists(xmlPath)) return [];

            var xml = await File.ReadAllTextAsync(xmlPath);
            return ParseWeeklyFromXml(xml);
        }
        catch { return []; }
        finally
        {
            try { if (File.Exists(xmlPath)) File.Delete(xmlPath); } catch { }
        }
    }

    public static async Task<BatteryRealtimeStatus?> GetRealtimeStatusAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT DischargeRate, ChargeRate, RemainingCapacity, Voltage, Charging, Discharging, PowerOnline FROM BatteryStatus");
                foreach (ManagementObject obj in searcher.Get())
                {
                    return new BatteryRealtimeStatus(
                        Convert.ToInt32(obj["DischargeRate"]),
                        Convert.ToInt32(obj["ChargeRate"]),
                        Convert.ToInt32(obj["RemainingCapacity"]),
                        Convert.ToInt32(obj["Voltage"]),
                        Convert.ToBoolean(obj["Charging"]),
                        Convert.ToBoolean(obj["Discharging"]),
                        Convert.ToBoolean(obj["PowerOnline"])
                    );
                }
            }
            catch { }
            return null;
        });
    }

    public static async Task<List<ProcessPowerEntry>> GetTopProcessesAsync(int topN = 15)
    {
        return await Task.Run(() =>
        {
            var now = DateTime.UtcNow;
            var current = new Dictionary<int, (TimeSpan CpuTime, DateTime SampleTime)>();
            var results = new List<ProcessPowerEntry>();

            try
            {
                var procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        var cpuTime = p.TotalProcessorTime;
                        current[p.Id] = (cpuTime, now);
                    }
                    catch { }
                }

                if (_prevSampleTime != DateTime.MinValue && _prevCpu.Count > 0)
                {
                    var elapsed = (now - _prevSampleTime).TotalSeconds;
                    if (elapsed > 0.1)
                    {
                        var cpuCount = Environment.ProcessorCount;
                        foreach (var (pid, (curTime, _)) in current)
                        {
                            if (!_prevCpu.TryGetValue(pid, out var prev)) continue;
                            var diff = (curTime - prev.CpuTime).TotalSeconds;
                            var pct = Math.Min(100.0 * diff / elapsed / cpuCount, 100.0);
                            if (pct < 0.01) continue;

                            try
                            {
                                var proc = Process.GetProcessById(pid);
                                var name = proc.ProcessName;
                                var mem = proc.WorkingSet64;
                                results.Add(new ProcessPowerEntry(name, pct, mem, pid));
                            }
                            catch { }
                        }
                    }
                }

                _prevCpu = current;
                _prevSampleTime = now;
            }
            catch { }

            return results.OrderByDescending(p => p.CpuPercent).Take(topN).ToList();
        });
    }

    public static async Task<string> ExportHtmlReportAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "tuba-battery-report-export.html");
        var psi = new ProcessStartInfo
        {
            FileName = "powercfg",
            Arguments = $"/batteryreport /output \"{path}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process is not null)
            await process.WaitForExitAsync();
        return File.Exists(path) ? path : "";
    }

    private static List<BatteryTrendPoint> ParseTrendFromXml(string xml)
    {
        var points = new List<BatteryTrendPoint>();
        try
        {
            var doc = XDocument.Parse(xml);
            foreach (var entry in doc.Descendants(Ns + "UsageEntry"))
            {
                var localTs = (string?)entry.Attribute("LocalTimestamp");
                if (string.IsNullOrEmpty(localTs)) continue;
                if (!DateTime.TryParse(localTs, out var ts)) continue;

                var capacity = (int?)entry.Attribute("ChargeCapacity") ?? 0;
                var fullCharge = (int?)entry.Attribute("FullChargeCapacity") ?? 0;
                var ac = (string?)entry.Attribute("Ac") == "1";
                var state = (string?)entry.Attribute("EntryType") ?? "";

                var pct = fullCharge > 0 ? (int)Math.Round(capacity * 100.0 / fullCharge) : 0;
                points.Add(new BatteryTrendPoint(ts, pct, capacity, ac, state));
            }
        }
        catch { }
        return points;
    }

    private static List<BatteryWeeklyEntry> ParseWeeklyFromXml(string xml)
    {
        var entries = new List<BatteryWeeklyEntry>();
        try
        {
            var doc = XDocument.Parse(xml);
            foreach (var entry in doc.Descendants(Ns + "HistoryEntry"))
            {
                var startStr = (string?)entry.Attribute("StartDate");
                var endStr = (string?)entry.Attribute("EndDate");
                if (string.IsNullOrEmpty(startStr) || string.IsNullOrEmpty(endStr)) continue;
                if (!DateTime.TryParse(startStr, out var start) || !DateTime.TryParse(endStr, out var end)) continue;

                var fullCharge = (double?)entry.Attribute("FullChargeCapacity") ?? 0;
                var design = (double?)entry.Attribute("DesignCapacity") ?? 0;
                var cycle = (int?)entry.Attribute("CycleCount") ?? 0;
                var activeDcStr = (string?)entry.Attribute("ActiveDcTime") ?? "";
                var activeDcTime = ParseDuration(activeDcStr);
                var energy = (double?)entry.Attribute("ActiveDcEnergy") ?? 0;

                entries.Add(new BatteryWeeklyEntry(start, end, fullCharge, design, cycle, activeDcTime, energy));
            }
        }
        catch { }
        return entries;
    }

    private static TimeSpan ParseDuration(string iso)
    {
        try
        {
            if (string.IsNullOrEmpty(iso) || iso == "PT0S") return TimeSpan.Zero;
            return XmlConvert.ToTimeSpan(iso);
        }
        catch { return TimeSpan.Zero; }
    }

    public static bool IsAdmin()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
