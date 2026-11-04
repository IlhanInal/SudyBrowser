using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;

namespace SudyBrowser.App.ViewModels;

/// <summary>
/// Manages active and recent file downloads, the address-bar download button visibility (which stays
/// visible while any download is running and for 25 minutes after the last activity), and a single
/// 1-second timer that runs ONLY while a download is actively in progress.
/// </summary>
public sealed class DownloadsViewModel : ObservableObject, IDisposable
{
    public const int MaxRetainedItems = 25;
    public static readonly TimeSpan DefaultVisibilityHoldDuration = TimeSpan.FromMinutes(25);

    private readonly TimeSpan _visibilityHoldDuration;
    private readonly Func<DateTime> _utcNow;
    private DispatcherTimer? _progressTimer;
    private CancellationTokenSource? _hideIconCts;

    private bool _isOpen;
    private bool _isButtonVisible;
    private DateTime? _visibleUntilUtc;
    private bool _disposed;

    public DownloadsViewModel(TimeSpan? visibilityHoldDuration = null, Func<DateTime>? utcNow = null)
    {
        _visibilityHoldDuration = visibilityHoldDuration ?? DefaultVisibilityHoldDuration;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);

        ToggleOpenCommand = new RelayCommand(_ => IsOpen = !IsOpen);
        ClosePopupCommand = new RelayCommand(_ => IsOpen = false);
    }

    public ObservableCollection<DownloadItemViewModel> Items { get; } = new();

    public bool IsOpen
    {
        get => _isOpen;
        set => SetProperty(ref _isOpen, value);
    }

    /// <summary>
    /// True once a download starts; stays true while any download is active and for 25 minutes after the last download finishes.
    /// </summary>
    public bool IsButtonVisible
    {
        get => _isButtonVisible;
        private set => SetProperty(ref _isButtonVisible, value);
    }

    public ICommand ToggleOpenCommand { get; }

    public ICommand ClosePopupCommand { get; }

    /// <summary>Registers a newly started download, shows the toolbar icon, opens the popup, and starts the 1s timer if needed.</summary>
    public DownloadItemViewModel AddDownload(IDownloadOperation operation)
    {
        if (_disposed)
        {
            return new DownloadItemViewModel(operation, _ => { }, _utcNow());
        }

        var item = new DownloadItemViewModel(operation, RemoveItem, _utcNow());
        item.PropertyChanged += OnItemPropertyChanged;

        Items.Insert(0, item);
        TrimExcessItems();

        IsButtonVisible = true;
        IsOpen = true;
        UpdateTimersAndVisibilityWindow();

        return item;
    }

    public void RemoveItem(DownloadItemViewModel item)
    {
        item.PropertyChanged -= OnItemPropertyChanged;
        item.Dispose();
        Items.Remove(item);
        UpdateTimersAndVisibilityWindow();
    }

    /// <summary>Re-evaluates whether the 25-minute icon hold window has elapsed.</summary>
    public void CheckVisibilityExpiration()
    {
        if (_disposed)
        {
            return;
        }

        if (HasActiveDownload())
        {
            IsButtonVisible = true;
            return;
        }

        if (_visibleUntilUtc is { } deadline && _utcNow() >= deadline)
        {
            IsButtonVisible = false;
            IsOpen = false;
            _visibleUntilUtc = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopProgressTimer();
        CancelHideTimer();

        foreach (var item in Items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
            item.Dispose();
        }

        Items.Clear();
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadItemViewModel.State))
        {
            UpdateTimersAndVisibilityWindow();
        }
    }

    private void UpdateTimersAndVisibilityWindow()
    {
        if (_disposed)
        {
            return;
        }

        var anyInProgress = Items.Any(i => i.State == DownloadState.InProgress);
        if (anyInProgress)
        {
            EnsureProgressTimerRunning();
        }
        else
        {
            StopProgressTimer();
        }

        if (HasActiveDownload())
        {
            IsButtonVisible = true;
            CancelHideTimer();
            _visibleUntilUtc = null;
        }
        else if (IsButtonVisible)
        {
            // All downloads reached a non-active state: hold the icon visible for 25 minutes from now.
            _visibleUntilUtc = _utcNow() + _visibilityHoldDuration;
            ScheduleHideAfterHoldDuration();
        }
    }

    private bool HasActiveDownload() =>
        Items.Any(i => i.State is DownloadState.InProgress or DownloadState.Paused);

    private void EnsureProgressTimerRunning()
    {
        if (_progressTimer is not null)
        {
            return;
        }

        _progressTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnProgressTimerTick, Dispatcher.CurrentDispatcher);
        _progressTimer.Start();
    }

    private void StopProgressTimer()
    {
        if (_progressTimer is null)
        {
            return;
        }

        _progressTimer.Stop();
        _progressTimer.Tick -= OnProgressTimerTick;
        _progressTimer = null;
    }

    private void OnProgressTimerTick(object? sender, EventArgs e)
    {
        var now = _utcNow();
        var anyRunning = false;
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            if (item.State == DownloadState.InProgress)
            {
                item.Tick(now);
                anyRunning = true;
            }
        }

        if (!anyRunning)
        {
            StopProgressTimer();
        }
    }

    private void ScheduleHideAfterHoldDuration()
    {
        CancelHideTimer();
        var cts = new CancellationTokenSource();
        _hideIconCts = cts;
        _ = RunHideTimerAsync(_visibilityHoldDuration, cts.Token);
    }

    private async Task RunHideTimerAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                CheckVisibilityExpiration();
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled because a new download started or the view model was disposed.
        }
    }

    private void CancelHideTimer()
    {
        var cts = _hideIconCts;
        _hideIconCts = null;
        if (cts is not null)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private void TrimExcessItems()
    {
        while (Items.Count > MaxRetainedItems)
        {
            // Prefer evicting the oldest finished item so active downloads are never dropped.
            var candidate = Items.LastOrDefault(i => i.IsFinishedLayout) ?? Items[^1];
            candidate.PropertyChanged -= OnItemPropertyChanged;
            candidate.Dispose();
            Items.Remove(candidate);
        }
    }
}
