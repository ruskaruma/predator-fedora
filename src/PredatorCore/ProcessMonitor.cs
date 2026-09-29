using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PredatorCore;

public sealed class ProcessRow
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public string Detail { get; init; } = "";
    public double CpuPercent { get; init; }          // 100 = one full core, like top
    public double BarPercent { get; init; }          // share of the busiest row, for the bar
    public bool CanEnd { get; init; }                // only the user's own processes
    public string CpuText => $"{CpuPercent:0}%";
}

/// <summary>
///     Per-process CPU usage from /proc/[pid]/stat deltas (same method as top), plus the list of
///     processes holding /dev/nvidia* open, which is what keeps the dGPU from runtime-suspending.
/// </summary>
public sealed class ProcessMonitor
{
    private const double ClockTicksPerSecond = 100; // USER_HZ on Linux
    /// <summary>Ending any of these would crash or log out the desktop session, so no End button.</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.Ordinal)
    {
        "gnome-shell", "gnome-session-binary", "gnome-session-service", "gnome-session-ctl", "Xwayland", "Xorg",
        "mutter-x11-frames", "gdm-wayland-session", "gdm-x-session", "gdm-session-worker", "systemd",
        "dbus-daemon", "dbus-broker", "dbus-broker-launch", "pipewire", "pipewire-pulse", "wireplumber",
        "gsd-power", "gsd-xsettings", "at-spi-bus-launcher", "at-spi2-registryd", "xdg-desktop-portal",
        "xdg-desktop-portal-gnome", "xdg-document-portal", "xdg-permission-store", "ibus-daemon",
        "gnome-keyring-daemon", "kwin_wayland", "kwin_x11", "plasmashell", "ksmserver"
    };

    private readonly int _selfPid = Environment.ProcessId;
    private readonly Dictionary<int, long> _lastTicks = new();
    private readonly int _uid = GetUid();
    private DateTime _lastSample = DateTime.MinValue;
    private DateTime _lastGpuScan = DateTime.MinValue;
    private List<string> _gpuClients = new();

    public List<ProcessRow> TopByCpu(int count)
    {
        var now = DateTime.UtcNow;
        var seconds = _lastSample == DateTime.MinValue ? 0 : (now - _lastSample).TotalSeconds;
        _lastSample = now;

        var usage = new List<(int pid, string name, double pct)>();
        var seen = new HashSet<int>();

        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
            var stat = ReadText($"{dir}/stat");
            if (stat == null) continue;

            // comm is in parentheses and may contain spaces; fields after it are space separated
            var open = stat.IndexOf('(');
            var close = stat.LastIndexOf(')');
            if (open < 0 || close < open) continue;
            var name = stat[(open + 1)..close];
            var fields = stat[(close + 2)..].Split(' ');
            if (fields.Length < 13 || !long.TryParse(fields[11], out var utime) ||
                !long.TryParse(fields[12], out var stime)) continue;

            var ticks = utime + stime;
            seen.Add(pid);
            if (seconds > 0 && _lastTicks.TryGetValue(pid, out var previous) && ticks >= previous)
            {
                var pct = (ticks - previous) / ClockTicksPerSecond / seconds * 100;
                if (pct >= 0.5) usage.Add((pid, name, pct));
            }

            _lastTicks[pid] = ticks;
        }

        foreach (var gone in _lastTicks.Keys.Where(p => !seen.Contains(p)).ToList()) _lastTicks.Remove(gone);

        var top = usage.OrderByDescending(u => u.pct).Take(count).ToList();
        var busiest = top.Count > 0 ? top[0].pct : 1;
        return top.Select(u => new ProcessRow
        {
            Pid = u.pid,
            Name = FriendlyName(u.pid, u.name),
            Detail = $"PID {u.pid}",
            CpuPercent = u.pct,
            BarPercent = 100 * u.pct / busiest,
            CanEnd = u.pid != _selfPid && !Protected.Contains(u.name) && IsOwnedByUser(u.pid)
        }).ToList();
    }

    /// <summary>Processes with /dev/nvidia* open. Scanned at most every 20 s since it walks fd tables.</summary>
    public List<string> GpuClients()
    {
        if ((DateTime.UtcNow - _lastGpuScan).TotalSeconds < 20) return _gpuClients;
        _lastGpuScan = DateTime.UtcNow;

        var clients = new SortedSet<string>();
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
            try
            {
                foreach (var fd in Directory.EnumerateFileSystemEntries($"{dir}/fd"))
                {
                    var target = new FileInfo(fd).LinkTarget;
                    if (target != null && target.StartsWith("/dev/nvidia") && target != "/dev/nvidiactl" &&
                        target != "/dev/nvidia-modeset")
                    {
                        clients.Add(FriendlyName(pid, ReadText($"{dir}/comm") ?? pid.ToString()));
                        break;
                    }
                }
            }
            catch
            {
                // other users' fd tables aren't readable; skip them
            }
        }

        _gpuClients = clients.ToList();
        return _gpuClients;
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    /// <summary>Sends SIGTERM so the app can shut down cleanly (Chrome restores its tabs, editors save).</summary>
    public static bool TryEnd(int pid, out string error)
    {
        const int sigterm = 15;
        error = "";
        if (kill(pid, sigterm) == 0) return true;
        error = $"kill failed (errno {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()})";
        return false;
    }

    private bool IsOwnedByUser(int pid)
    {
        var status = ReadText($"/proc/{pid}/status");
        var uidLine = status?.Split('\n').FirstOrDefault(l => l.StartsWith("Uid:"));
        var parts = uidLine?.Split('\t', StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: > 1 } && int.TryParse(parts[1], out var uid) && uid == _uid;
    }

    /// <summary>Label browser/Electron helpers by their role, e.g. "chrome · renderer".</summary>
    private static string FriendlyName(int pid, string comm)
    {
        var cmdline = ReadText($"/proc/{pid}/cmdline");
        if (cmdline == null) return comm;
        var args = cmdline.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var exe = args.Length > 0 ? Path.GetFileName(args[0]) : comm;
        if (string.IsNullOrEmpty(exe)) exe = comm;
        var type = args.FirstOrDefault(a => a.StartsWith("--type="));
        return type != null ? $"{exe} · {type[7..]}" : exe;
    }

    private static int GetUid()
    {
        var status = ReadText("/proc/self/status");
        var uidLine = status?.Split('\n').FirstOrDefault(l => l.StartsWith("Uid:"));
        var parts = uidLine?.Split('\t', StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: > 1 } && int.TryParse(parts[1], out var uid) ? uid : -1;
    }

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
}
