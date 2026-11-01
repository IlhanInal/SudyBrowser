using System.Windows.Input;
using System.Windows.Threading;
using LightBrowser.Core.Interfaces;
using LightBrowser.Core.Models;

namespace LightBrowser.App.ViewModels;

/// <summary>Drives the top-left box: clock, RAM and CPU readouts, switched with the arrow buttons.</summary>
public sealed class StatusViewModel : ObservableObject, IDisposable
{
    private enum Mode
    {
        Clock,
        Ram,
        Cpu,
    }

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly int ModeCount = Enum.GetValues<Mode>().Length;

    private readonly ISystemMetricsProvider _metrics;
    private readonly DispatcherTimer _timer;
    private Mode _mode = Mode.Clock;
    private SystemMetrics _lastMetrics = new(0, 0, 0, 0);
    private DateTime _now = DateTime.Now;

    public StatusViewModel(ISystemMetricsProvider metrics)
    {
        _metrics = metrics;
        NextCommand = new RelayCommand(_ => Cycle(1));
        PreviousCommand = new RelayCommand(_ => Cycle(-1));

        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += OnTick;
        Refresh();
        _timer.Start();
    }

    public ICommand NextCommand { get; }

    public ICommand PreviousCommand { get; }

    public string DisplayText => _mode switch
    {
        Mode.Ram => $"{_lastMetrics.AppRamMb:0} MB",
        Mode.Cpu => $"CPU %{_lastMetrics.CpuPercent:0}",
        _ => _now.ToString("HH:mm"),
    };

    public string ToolTipText =>
        $"Saat: {_now:HH:mm:ss}\n" +
        $"Uygulama RAM: {_lastMetrics.AppRamMb:0} MB\n" +
        $"Sistem RAM: {_lastMetrics.RamUsedGb:0.1} / {_lastMetrics.RamTotalGb:0.1} GB (%{_lastMetrics.RamPercent:0})\n" +
        $"CPU: %{_lastMetrics.CpuPercent:0}";

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private void Cycle(int step)
    {
        _mode = (Mode)(((int)_mode + step + ModeCount) % ModeCount);
        OnPropertyChanged(nameof(DisplayText));
    }

    private void OnTick(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        _now = DateTime.Now;
        _lastMetrics = _metrics.Read();
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(ToolTipText));
    }
}
