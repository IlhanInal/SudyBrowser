using System.Diagnostics;
using System.Runtime.InteropServices;
using LightBrowser.Core.Interfaces;
using LightBrowser.Core.Models;

namespace LightBrowser.Infrastructure.Monitoring;

/// <summary>Reads system CPU, total system RAM, and application-specific RAM in MB.</summary>
public sealed class WindowsSystemMetricsProvider : ISystemMetricsProvider
{
    private const double BytesPerGb = 1024d * 1024 * 1024;
    private const double BytesPerMb = 1024d * 1024;

    private readonly IWebViewHostFactory? _hostFactory;
    private ulong _lastIdle;
    private ulong _lastTotal;

    public WindowsSystemMetricsProvider(IWebViewHostFactory? hostFactory = null)
    {
        _hostFactory = hostFactory;
    }

    public SystemMetrics Read()
    {
        var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        var ramPercent = 0d;
        var usedGb = 0d;
        var totalGb = 0d;
        if (GlobalMemoryStatusEx(ref memory))
        {
            totalGb = memory.TotalPhys / BytesPerGb;
            usedGb = (memory.TotalPhys - memory.AvailPhys) / BytesPerGb;
            ramPercent = memory.MemoryLoad;
        }

        return new SystemMetrics(ReadCpuPercent(), ramPercent, usedGb, totalGb, ReadAppRamMb());
    }

    private double ReadAppRamMb()
    {
        try
        {
            long totalBytes = 0;
            using var current = Process.GetCurrentProcess();
            current.Refresh();
            totalBytes += current.WorkingSet64;

            if (_hostFactory is not null)
            {
                foreach (var pid in _hostFactory.GetProcessIds())
                {
                    try
                    {
                        using var proc = Process.GetProcessById(pid);
                        totalBytes += proc.WorkingSet64;
                    }
                    catch
                    {
                        // Child process may exit at any time
                    }
                }
            }

            return Math.Max(0, totalBytes / BytesPerMb);
        }
        catch
        {
            return 0;
        }
    }

    private double ReadCpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var idleTicks = idle.ToUInt64();
        // Kernel time already includes idle time.
        var totalTicks = kernel.ToUInt64() + user.ToUInt64();

        var idleDelta = idleTicks - _lastIdle;
        var totalDelta = totalTicks - _lastTotal;
        var isFirstSample = _lastTotal == 0;
        _lastIdle = idleTicks;
        _lastTotal = totalTicks;

        if (isFirstSample || totalDelta == 0)
        {
            return 0;
        }

        return Math.Clamp((1d - (double)idleDelta / totalDelta) * 100d, 0d, 100d);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
