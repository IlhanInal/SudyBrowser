namespace SudyBrowser.Core.Models;

/// <summary>System-wide and application resource usage sample.</summary>
public sealed record SystemMetrics(
    double CpuPercent,
    double RamPercent,
    double RamUsedGb,
    double RamTotalGb,
    double AppRamMb = 0);
