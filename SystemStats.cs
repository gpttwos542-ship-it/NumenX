using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NumenX;

/// <summary>Real system measurements (no fake numbers). GPU load is intentionally not measured yet.</summary>
public static class SystemStats
{
    [StructLayout(LayoutKind.Sequential)]
    struct FILETIME { public uint Low; public uint High; }

    [DllImport("kernel32.dll")]
    static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                     ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    static ulong _prevIdle, _prevTotal;

    static ulong ToU64(FILETIME f) => ((ulong)f.High << 32) | f.Low;

    public static double CpuPercent()
    {
        if (!GetSystemTimes(out var i, out var k, out var u)) return 0;
        ulong idle = ToU64(i), total = ToU64(k) + ToU64(u);
        ulong di = idle - _prevIdle, dt = total - _prevTotal;
        _prevIdle = idle; _prevTotal = total;
        return dt == 0 ? 0 : Math.Clamp(100.0 * (1.0 - (double)di / dt), 0, 100);
    }

    public static (double UsedGb, double TotalGb, uint Percent) Ram()
    {
        var m = new MEMORYSTATUSEX();
        if (!GlobalMemoryStatusEx(m)) return (0, 0, 0);
        double total = m.ullTotalPhys / 1073741824.0;
        double used = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
        return (used, total, m.dwMemoryLoad);
    }

    public static double FreeDiskGb(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            return root == null ? 0 : new DriveInfo(root).AvailableFreeSpace / 1073741824.0;
        }
        catch { return 0; }
    }

    public static double ProcessMb(Process? p)
    {
        try
        {
            if (p == null || p.HasExited) return 0;
            p.Refresh();
            return p.WorkingSet64 / 1048576.0;
        }
        catch { return 0; }
    }

    public static int ScreenWidth() => GetSystemMetrics(0);
    public static int ScreenHeight() => GetSystemMetrics(1);
}
