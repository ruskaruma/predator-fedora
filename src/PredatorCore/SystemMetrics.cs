using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace PredatorCore;

/// <summary>
///     One snapshot of the extra device metrics shown on the Monitoring page.
///     Nullable fields mean "not available on this machine / not readable".
/// </summary>
public sealed class MetricsSample
{
    public double CpuUsage;
    public double? CpuAvgGhz;
    public double? CpuMaxGhz;
    public double? CpuPackageWatts;
    public string CpuPowerPolicy = "—";

    public bool GpuPresent;
    public bool GpuAsleep;
    public double? GpuTemp;
    public double? GpuUsage;
    public double? GpuWatts;
    public double? GpuClockMhz;
    public double? GpuMemUsedMb;
    public double? GpuMemTotalMb;
    public string GpuPState = "—";

    public double RamUsedGb;
    public double RamTotalGb;
    public double SwapUsedGb;

    public double? NvmeTemp;
    public double DiskUsedGb;
    public double DiskTotalGb;

    public double? BatteryWatts;
    public double? BatteryHealth;
    public int? BatteryCycles;
    public double? BatteryHoursLeft;

    public double NetDownBps;
    public double NetUpBps;

    public TimeSpan Uptime;
    public string LoadAvg = "—";

    /// <summary>Share of the last interval the CPU package spent thermally throttled (0-100).</summary>
    public double ThrottledPercent;
    public long ThrottleEventsTotal;
}

/// <summary>
///     Cheap, stateful sampler. Rates (CPU %, package power, network) are computed
///     from the delta against the previous call, so no call ever sleeps.
///     The NVIDIA GPU is only queried when it is already awake: calling nvidia-smi
///     on a runtime-suspended Optimus GPU would power it up and cost battery.
/// </summary>
public sealed class SystemMetricsSampler
{
    private const string RaplEnergyPath = "/sys/class/powercap/intel-rapl:0/energy_uj";
    private const string RaplMaxPath = "/sys/class/powercap/intel-rapl:0/max_energy_range_uj";

    private readonly string? _batteryDir;
    private readonly string[] _cpuFreqFiles;
    private readonly string? _nvidiaPciDir;
    private readonly string? _nvmeTempPath;

    private long _lastCpuIdle, _lastCpuTotal;
    private long _lastRaplUj;
    private long _raplMaxUj;
    private long _lastRx, _lastTx;
    private long _lastThrottleMs = -1;

    // The NVIDIA driver only runtime-suspends after ~9 s without clients; querying nvidia-smi every
    // refresh kept the dGPU awake indefinitely. Query at most every 15 s and reuse the last reading.
    private static readonly TimeSpan GpuQueryInterval = TimeSpan.FromSeconds(15);
    private DateTime _lastGpuQuery = DateTime.MinValue;
    private string[]? _lastGpuFields;
    private const string ThrottleDir = "/sys/devices/system/cpu/cpu0/thermal_throttle";
    private DateTime _lastSampleAt = DateTime.MinValue;

    public SystemMetricsSampler()
    {
        _cpuFreqFiles = Directory.Exists("/sys/devices/system/cpu")
            ? Directory.GetDirectories("/sys/devices/system/cpu", "cpu*")
                .Select(d => Path.Combine(d, "cpufreq", "scaling_cur_freq"))
                .Where(File.Exists).ToArray()
            : Array.Empty<string>();

        _batteryDir = Directory.Exists("/sys/class/power_supply")
            ? Directory.GetDirectories("/sys/class/power_supply")
                .FirstOrDefault(d => ReadText(Path.Combine(d, "type")) == "Battery")
            : null;

        _nvidiaPciDir = Directory.Exists("/sys/bus/pci/devices")
            ? Directory.GetDirectories("/sys/bus/pci/devices").FirstOrDefault(d =>
                ReadText(Path.Combine(d, "vendor")) == "0x10de" &&
                (ReadText(Path.Combine(d, "class"))?.StartsWith("0x03") ?? false))
            : null;

        _nvmeTempPath = FindHwmonFile("nvme", "temp1_input");

        long.TryParse(ReadText(RaplMaxPath), out _raplMaxUj);

        ModelName = ReadText("/sys/class/dmi/id/product_name") ?? "Acer Laptop";
        CpuThreads = Environment.ProcessorCount;
    }

    public string ModelName { get; }
    public int CpuThreads { get; }
    public bool HasNvidiaGpu => _nvidiaPciDir != null;

    public MetricsSample Sample()
    {
        var s = new MetricsSample();
        var now = DateTime.UtcNow;
        var dt = _lastSampleAt == DateTime.MinValue ? 0 : (now - _lastSampleAt).TotalSeconds;

        SampleCpu(s, dt);
        SampleGpu(s);
        SampleMemory(s);
        SampleStorage(s);
        SampleBattery(s);
        SampleNetwork(s, dt);
        SampleSystem(s);
        SampleThrottle(s, dt);

        _lastSampleAt = now;
        return s;
    }

    private void SampleCpu(MetricsSample s, double dt)
    {
        // Aggregate line: user nice system idle iowait irq softirq steal ...
        var line = File.ReadLines("/proc/stat").FirstOrDefault() ?? "";
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(p => long.TryParse(p, out var v) ? v : 0).ToArray();
        if (parts.Length >= 5)
        {
            var idle = parts[3] + parts[4];
            var total = parts.Take(8).Sum();
            if (_lastCpuTotal > 0 && total > _lastCpuTotal)
                s.CpuUsage = Math.Round(100.0 * (1 - (idle - _lastCpuIdle) / (double)(total - _lastCpuTotal)), 1);
            _lastCpuIdle = idle;
            _lastCpuTotal = total;
        }

        if (_cpuFreqFiles.Length > 0)
        {
            var khz = _cpuFreqFiles.Select(f => long.TryParse(ReadText(f), out var v) ? v : 0).Where(v => v > 0)
                .ToArray();
            if (khz.Length > 0)
            {
                s.CpuAvgGhz = khz.Average() / 1e6;
                s.CpuMaxGhz = khz.Max() / 1e6;
            }
        }

        // Package power from RAPL. Root-only by default; shown as "—" until readable.
        if (long.TryParse(ReadText(RaplEnergyPath), out var uj))
        {
            if (_lastRaplUj > 0 && dt > 0)
            {
                var delta = uj - _lastRaplUj;
                if (delta < 0 && _raplMaxUj > 0) delta += _raplMaxUj; // counter wrapped
                if (delta >= 0) s.CpuPackageWatts = delta / 1e6 / dt;
            }

            _lastRaplUj = uj;
        }

        var governor = ReadText("/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor");
        var epp = ReadText("/sys/devices/system/cpu/cpu0/cpufreq/energy_performance_preference");
        if (governor != null)
            s.CpuPowerPolicy = epp != null ? $"{governor} · {epp.Replace('_', ' ')}" : governor;
    }

    private void SampleGpu(MetricsSample s)
    {
        if (_nvidiaPciDir == null) return;
        s.GpuPresent = true;

        var runtime = ReadText(Path.Combine(_nvidiaPciDir, "power", "runtime_status"));
        if (runtime == "suspended")
        {
            s.GpuAsleep = true;
            s.GpuPState = "Sleeping";
            _lastGpuFields = null; // next wake gets a fresh reading
            return;
        }

        if (_lastGpuFields == null || DateTime.UtcNow - _lastGpuQuery >= GpuQueryInterval)
        {
            var output = RunCommand("nvidia-smi",
                "--query-gpu=temperature.gpu,utilization.gpu,power.draw,clocks.gr,memory.used,memory.total,pstate " +
                "--format=csv,noheader,nounits");
            var fields = output.Split(',').Select(x => x.Trim()).ToArray();
            _lastGpuQuery = DateTime.UtcNow;
            if (fields.Length >= 7) _lastGpuFields = fields;
        }

        var f = _lastGpuFields;
        if (f == null) return;

        s.GpuTemp = ParseDouble(f[0]);
        s.GpuUsage = ParseDouble(f[1]);
        var watts = ParseDouble(f[2]);
        s.GpuWatts = watts is > 0 and < 250 ? watts : null; // driver occasionally reports junk right after wake
        s.GpuClockMhz = ParseDouble(f[3]);
        s.GpuMemUsedMb = ParseDouble(f[4]);
        s.GpuMemTotalMb = ParseDouble(f[5]);
        s.GpuPState = f[6];
    }

    private static void SampleMemory(MetricsSample s)
    {
        long total = 0, available = 0, swapTotal = 0, swapFree = 0;
        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemTotal:")) total = ParseKb(line);
            else if (line.StartsWith("MemAvailable:")) available = ParseKb(line);
            else if (line.StartsWith("SwapTotal:")) swapTotal = ParseKb(line);
            else if (line.StartsWith("SwapFree:")) swapFree = ParseKb(line);
        }

        s.RamTotalGb = total / 1048576.0;
        s.RamUsedGb = (total - available) / 1048576.0;
        s.SwapUsedGb = (swapTotal - swapFree) / 1048576.0;
    }

    private void SampleStorage(MetricsSample s)
    {
        if (_nvmeTempPath != null && long.TryParse(ReadText(_nvmeTempPath), out var milli))
            s.NvmeTemp = milli / 1000.0;

        try
        {
            var root = new DriveInfo("/");
            s.DiskTotalGb = root.TotalSize / 1e9;
            s.DiskUsedGb = (root.TotalSize - root.AvailableFreeSpace) / 1e9;
        }
        catch
        {
            // ignore: statfs can fail inside sandboxes
        }
    }

    private void SampleBattery(MetricsSample s)
    {
        if (_batteryDir == null) return;

        double? Read(string name) =>
            double.TryParse(ReadText(Path.Combine(_batteryDir, name)), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var v)
                ? v
                : null;

        var status = ReadText(Path.Combine(_batteryDir, "status")) ?? "";

        // Batteries expose either energy_* (µWh) + power_now (µW) or charge_* (µAh) + current_now (µA).
        var powerNow = Read("power_now");
        var currentNow = Read("current_now");
        var voltageNow = Read("voltage_now");
        var watts = powerNow ?? (currentNow * voltageNow / 1e6);
        if (watts is > 0) s.BatteryWatts = watts / 1e6;

        var full = Read("energy_full") ?? Read("charge_full");
        var design = Read("energy_full_design") ?? Read("charge_full_design");
        var nowLevel = Read("energy_now") ?? Read("charge_now");
        var rate = powerNow ?? currentNow;

        if (full is > 0 && design is > 0) s.BatteryHealth = 100.0 * full.Value / design.Value;
        if (int.TryParse(ReadText(Path.Combine(_batteryDir, "cycle_count")), out var cycles) && cycles > 0)
            s.BatteryCycles = cycles;

        if (rate is > 0 && nowLevel.HasValue && full.HasValue)
        {
            if (status == "Discharging") s.BatteryHoursLeft = nowLevel.Value / rate.Value;
            else if (status == "Charging") s.BatteryHoursLeft = (full.Value - nowLevel.Value) / rate.Value;
        }
    }

    private void SampleNetwork(MetricsSample s, double dt)
    {
        long rx = 0, tx = 0;
        foreach (var line in File.ReadLines("/proc/net/dev").Skip(2))
        {
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var iface = line[..colon].Trim();
            if (iface == "lo" || iface.StartsWith("virbr") || iface.StartsWith("docker") || iface.StartsWith("veth"))
                continue;
            var fields = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 9) continue;
            rx += long.TryParse(fields[0], out var r) ? r : 0;
            tx += long.TryParse(fields[8], out var t) ? t : 0;
        }

        if (dt > 0 && _lastRx > 0)
        {
            s.NetDownBps = Math.Max(0, (rx - _lastRx) / dt);
            s.NetUpBps = Math.Max(0, (tx - _lastTx) / dt);
        }

        _lastRx = rx;
        _lastTx = tx;
    }

    private void SampleThrottle(MetricsSample s, double dt)
    {
        if (long.TryParse(ReadText($"{ThrottleDir}/package_throttle_count"), out var events))
            s.ThrottleEventsTotal = events;
        if (!long.TryParse(ReadText($"{ThrottleDir}/package_throttle_total_time_ms"), out var ms)) return;
        if (_lastThrottleMs >= 0 && dt > 0)
            s.ThrottledPercent = Math.Clamp((ms - _lastThrottleMs) / (dt * 1000) * 100, 0, 100);
        _lastThrottleMs = ms;
    }

    private static void SampleSystem(MetricsSample s)
    {
        var uptime = ReadText("/proc/uptime")?.Split(' ')[0];
        if (double.TryParse(uptime, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs))
            s.Uptime = TimeSpan.FromSeconds(secs);

        var load = ReadText("/proc/loadavg")?.Split(' ');
        if (load is { Length: >= 3 }) s.LoadAvg = $"{load[0]} · {load[1]} · {load[2]}";
    }

    private static string? FindHwmonFile(string hwmonName, string file)
    {
        if (!Directory.Exists("/sys/class/hwmon")) return null;
        foreach (var dir in Directory.GetDirectories("/sys/class/hwmon"))
            if (ReadText(Path.Combine(dir, "name")) == hwmonName && File.Exists(Path.Combine(dir, file)))
                return Path.Combine(dir, file);
        return null;
    }

    private static long ParseKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out var v) ? v : 0;
    }

    private static double? ParseDouble(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string? ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string RunCommand(string command, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null) return "";
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1500);
            return output;
        }
        catch
        {
            return "";
        }
    }
}

/// <summary>Pre-formatted, bindable view of a <see cref="MetricsSample" />.</summary>
public sealed class MonitoringViewModel : INotifyPropertyChanged
{
    /// <summary>Highest RPM measured on the PHN16-71 fans at 100% duty.</summary>
    public const double FanMaxRpm = 7400;

    private string _cpuClock = "—", _cpuClockMax = "—", _cpuPower = "—", _cpuPolicy = "—";
    private string _gpuState = "—", _gpuClock = "—", _gpuPower = "—", _gpuVram = "—";
    private double _gpuVramPercent;
    private string _ramText = "—", _swapText = "—";
    private double _ramPercent;
    private string _diskText = "—", _nvmeTemp = "—";
    private double _diskPercent;
    private string _batteryWatts = "—", _batteryHealth = "—", _batteryCycles = "—", _batteryTimeLeft = "—";
    private string _netDown = "—", _netUp = "—";
    private string _uptime = "—", _loadAvg = "—";
    private double _cpuFanPercent, _gpuFanPercent;
    private bool _isThrottling;
    private string _throttleText = "", _throttleSummary = "—", _gpuClients = "—";

    public string ModelName { get; init; } = "";
    public string CpuThreadsText { get; init; } = "";

    public string CpuClock { get => _cpuClock; set => Set(ref _cpuClock, value); }
    public string CpuClockMax { get => _cpuClockMax; set => Set(ref _cpuClockMax, value); }
    public string CpuPower { get => _cpuPower; set => Set(ref _cpuPower, value); }
    public string CpuPolicy { get => _cpuPolicy; set => Set(ref _cpuPolicy, value); }
    public string GpuState { get => _gpuState; set => Set(ref _gpuState, value); }
    public string GpuClock { get => _gpuClock; set => Set(ref _gpuClock, value); }
    public string GpuPower { get => _gpuPower; set => Set(ref _gpuPower, value); }
    public string GpuVram { get => _gpuVram; set => Set(ref _gpuVram, value); }
    public double GpuVramPercent { get => _gpuVramPercent; set => Set(ref _gpuVramPercent, value); }
    public string RamText { get => _ramText; set => Set(ref _ramText, value); }
    public double RamPercent { get => _ramPercent; set => Set(ref _ramPercent, value); }
    public string SwapText { get => _swapText; set => Set(ref _swapText, value); }
    public string DiskText { get => _diskText; set => Set(ref _diskText, value); }
    public double DiskPercent { get => _diskPercent; set => Set(ref _diskPercent, value); }
    public string NvmeTemp { get => _nvmeTemp; set => Set(ref _nvmeTemp, value); }
    public string BatteryWatts { get => _batteryWatts; set => Set(ref _batteryWatts, value); }
    public string BatteryHealth { get => _batteryHealth; set => Set(ref _batteryHealth, value); }
    public string BatteryCycles { get => _batteryCycles; set => Set(ref _batteryCycles, value); }
    public string BatteryTimeLeft { get => _batteryTimeLeft; set => Set(ref _batteryTimeLeft, value); }
    public string NetDown { get => _netDown; set => Set(ref _netDown, value); }
    public string NetUp { get => _netUp; set => Set(ref _netUp, value); }
    public string Uptime { get => _uptime; set => Set(ref _uptime, value); }
    public string LoadAvg { get => _loadAvg; set => Set(ref _loadAvg, value); }
    public double CpuFanPercent { get => _cpuFanPercent; set => Set(ref _cpuFanPercent, value); }
    public double GpuFanPercent { get => _gpuFanPercent; set => Set(ref _gpuFanPercent, value); }
    public bool IsThrottling { get => _isThrottling; set => Set(ref _isThrottling, value); }
    public string ThrottleText { get => _throttleText; set => Set(ref _throttleText, value); }
    public string ThrottleSummary { get => _throttleSummary; set => Set(ref _throttleSummary, value); }
    public string GpuClients { get => _gpuClients; set => Set(ref _gpuClients, value); }
    private string _gpuTempText = "—", _gpuLoadText = "—";
    public string GpuTempText { get => _gpuTempText; set => Set(ref _gpuTempText, value); }
    public string GpuLoadText { get => _gpuLoadText; set => Set(ref _gpuLoadText, value); }
    private string _gpuSleepSummary = "";
    public string GpuSleepSummary { get => _gpuSleepSummary; set => Set(ref _gpuSleepSummary, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(MetricsSample s, int cpuFanRpm, int gpuFanRpm)
    {
        CpuClock = s.CpuAvgGhz is { } avg ? $"{avg:0.00} GHz" : "—";
        CpuClockMax = s.CpuMaxGhz is { } max ? $"{max:0.00} GHz" : "—";
        CpuPower = s.CpuPackageWatts is { } cw ? $"{cw:0.0} W" : "—";
        CpuPolicy = s.CpuPowerPolicy;

        if (!s.GpuPresent) GpuState = "Not detected";
        else if (s.GpuAsleep) GpuState = "Sleeping (saving power)";
        else GpuState = $"Active · {s.GpuPState}";
        // A runtime-suspended dGPU is powered off: its clock, load and power really are zero, so show
        // that instead of "—" (and still never wake it to find out).
        GpuTempText = s.GpuAsleep ? "SLEEP" : s.GpuTemp is { } gt ? $"{gt:0}°" : "—";
        GpuLoadText = s.GpuAsleep ? "0%" : s.GpuUsage is { } gu ? $"{gu:0}%" : "—";
        GpuClock = s.GpuAsleep ? "0 MHz · asleep" : s.GpuClockMhz is { } gc ? $"{gc:0} MHz" : "—";
        GpuPower = s.GpuAsleep ? "0 W · asleep" : s.GpuWatts is { } gw ? $"{gw:0.0} W" : "—";
        if (s.GpuMemUsedMb is { } used && s.GpuMemTotalMb is > 0)
        {
            GpuVram = $"{used / 1024:0.0} / {s.GpuMemTotalMb.Value / 1024:0} GB";
            GpuVramPercent = 100 * used / s.GpuMemTotalMb.Value;
        }
        else
        {
            GpuVram = s.GpuAsleep ? "0 GB · powered off" : "—";
            GpuVramPercent = 0;
        }

        RamText = $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0.0} GB";
        RamPercent = s.RamTotalGb > 0 ? 100 * s.RamUsedGb / s.RamTotalGb : 0;
        SwapText = $"{s.SwapUsedGb:0.0} GB";

        DiskText = s.DiskTotalGb > 0 ? $"{s.DiskUsedGb:0} / {s.DiskTotalGb:0} GB" : "—";
        DiskPercent = s.DiskTotalGb > 0 ? 100 * s.DiskUsedGb / s.DiskTotalGb : 0;
        NvmeTemp = s.NvmeTemp is { } nt ? $"{nt:0}°C" : "—";

        BatteryWatts = s.BatteryWatts is { } bw ? $"{bw:0.0} W" : "—";
        BatteryHealth = s.BatteryHealth is { } bh ? $"{bh:0}%" : "—";
        BatteryCycles = s.BatteryCycles?.ToString() ?? "—";
        BatteryTimeLeft = s.BatteryHoursLeft is { } h and > 0
            ? $"{(int)h}h {(int)((h - (int)h) * 60):00}m"
            : "—";

        NetDown = FormatRate(s.NetDownBps);
        NetUp = FormatRate(s.NetUpBps);

        Uptime = s.Uptime.TotalDays >= 1
            ? $"{(int)s.Uptime.TotalDays}d {s.Uptime.Hours}h {s.Uptime.Minutes}m"
            : $"{s.Uptime.Hours}h {s.Uptime.Minutes}m";
        LoadAvg = s.LoadAvg;

        IsThrottling = s.ThrottledPercent >= 1;
        ThrottleText = $"THERMAL THROTTLING · {s.ThrottledPercent:0}% OF THE TIME";
        ThrottleSummary = s.ThrottleEventsTotal > 0 ? $"{s.ThrottleEventsTotal:N0} throttle events since boot" : "No throttling since boot";

        CpuFanPercent = Math.Min(100, 100 * cpuFanRpm / FanMaxRpm);
        GpuFanPercent = Math.Min(100, 100 * gpuFanRpm / FanMaxRpm);
    }

    private static string FormatRate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1e6 => $"{bytesPerSecond / 1e6:0.0} MB/s",
        >= 1e3 => $"{bytesPerSecond / 1e3:0} KB/s",
        _ => $"{bytesPerSecond:0} B/s"
    };

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
