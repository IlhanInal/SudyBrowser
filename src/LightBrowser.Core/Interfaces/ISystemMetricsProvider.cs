using LightBrowser.Core.Models;

namespace LightBrowser.Core.Interfaces;

public interface ISystemMetricsProvider
{
    /// <summary>Samples current usage. CPU is measured since the previous call.</summary>
    SystemMetrics Read();
}
