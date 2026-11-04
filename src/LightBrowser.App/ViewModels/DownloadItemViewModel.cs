using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;

namespace SudyBrowser.App.ViewModels;

/// <summary>
/// Presentation state for a single download card in the Downloads popup.
/// - Throttles progress updates (~4 Hz) so rapid network chunks never flood WPF data bindings or allocate excess strings.
/// - Detaches and disposes its underlying <see cref="IDownloadOperation"/> as soon as the download finishes.
/// </summary>
public sealed class DownloadItemViewModel : ObservableObject, IDisposable
{
    private const long ProgressUiThrottleTicks = TimeSpan.TicksPerMillisecond * 200;

    private static readonly Brush ExeBadgeBrush = FreezeBrush("#EE4B4B");
    private static readonly Brush ArchiveBadgeBrush = FreezeBrush("#D9822B");
    private static readonly Brush MediaBadgeBrush = FreezeBrush("#7A5AF8");
    private static readonly Brush DocBadgeBrush = FreezeBrush("#2E7CF6");
    private static readonly Brush DefaultBadgeBrush = FreezeBrush("#545454");

    private readonly IDownloadOperation _operation;
    private readonly Action<DownloadItemViewModel> _removeCallback;
    private readonly DateTime _startedUtc;

    private string _fileName = string.Empty;
    private string _resultFilePath = string.Empty;
    private string _badgeText = "FILE";
    private Brush _badgeBrush = DefaultBadgeBrush;
    private DownloadState _state;
    private int _progressPercent;
    private string _percentText = "%0";
    private string _sizeText = "0 MB";
    private string _speedText = "0 Mb/s";
    private string _elapsedText = "00:00:00";
    private string _statusMessage = string.Empty;
    private bool _canResume;

    private long _lastSpeedSampleTicks;
    private long _lastSpeedSampleBytes;
    private double _smoothedBytesPerSec;
    private long _lastUiUpdateTicks;
    private bool _detached;

    public DownloadItemViewModel(
        IDownloadOperation operation,
        Action<DownloadItemViewModel> removeCallback,
        DateTime? nowUtc = null)
    {
        _operation = operation;
        _removeCallback = removeCallback;
        _startedUtc = nowUtc ?? DateTime.UtcNow;
        _lastSpeedSampleTicks = _startedUtc.Ticks;
        _lastSpeedSampleBytes = Math.Max(0, operation.BytesReceived);

        UpdateFileMetadata();
        _state = operation.State;
        _canResume = operation.CanResume;
        RefreshMetrics(force: true, _startedUtc);
        UpdateStatusMessage();

        _operation.ProgressChanged += OnOperationProgressChanged;
        _operation.StateChanged += OnOperationStateChanged;

        if (IsFinalNonResumableState())
        {
            DetachOperation();
        }

        TogglePauseResumeCommand = new RelayCommand(_ => TogglePauseResume());
        CancelOrRemoveCommand = new RelayCommand(_ => CancelOrRemove());
        OpenFolderCommand = new RelayCommand(_ => OpenFolder());
        OpenFileCommand = new RelayCommand(_ => OpenFile(), _ => State == DownloadState.Completed);
    }

    public Guid Id => _operation.Id;

    public string FileName
    {
        get => _fileName;
        private set => SetProperty(ref _fileName, value);
    }

    public string ResultFilePath
    {
        get => _resultFilePath;
        private set => SetProperty(ref _resultFilePath, value);
    }

    public string BadgeText
    {
        get => _badgeText;
        private set => SetProperty(ref _badgeText, value);
    }

    public Brush BadgeBrush
    {
        get => _badgeBrush;
        private set => SetProperty(ref _badgeBrush, value);
    }

    public DownloadState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsActiveLayout));
                OnPropertyChanged(nameof(IsFinishedLayout));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsFailed));
                OnPropertyChanged(nameof(CanRetry));
            }
        }
    }

    /// <summary>True while downloading or paused (shows the progress bar, speed pill, and pause/cancel buttons).</summary>
    public bool IsActiveLayout => _state is DownloadState.InProgress or DownloadState.Paused;

    /// <summary>True when completed, interrupted, or cancelled (shows the compact status card).</summary>
    public bool IsFinishedLayout => !IsActiveLayout;

    public bool IsPaused => _state == DownloadState.Paused;

    public bool IsCompleted => _state == DownloadState.Completed;

    public bool IsFailed => _state is DownloadState.Interrupted or DownloadState.Cancelled;

    public bool CanRetry => _state == DownloadState.Interrupted && _canResume;

    public int ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public string PercentText
    {
        get => _percentText;
        private set => SetProperty(ref _percentText, value);
    }

    public string SizeText
    {
        get => _sizeText;
        private set => SetProperty(ref _sizeText, value);
    }

    public string SpeedText
    {
        get => _speedText;
        private set => SetProperty(ref _speedText, value);
    }

    public string ElapsedText
    {
        get => _elapsedText;
        private set => SetProperty(ref _elapsedText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand TogglePauseResumeCommand { get; }

    public ICommand CancelOrRemoveCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand OpenFileCommand { get; }

    /// <summary>Called once per second by <see cref="DownloadsViewModel"/> while the download is active.</summary>
    internal void Tick(DateTime nowUtc)
    {
        if (_state == DownloadState.InProgress)
        {
            RefreshMetrics(force: true, nowUtc);
        }
    }

    public void Dispose() => DetachOperation();

    private void TogglePauseResume()
    {
        if (_state == DownloadState.InProgress)
        {
            _operation.Pause();
        }
        else if (_state == DownloadState.Paused || (_state == DownloadState.Interrupted && _operation.CanResume))
        {
            _lastSpeedSampleTicks = DateTime.UtcNow.Ticks;
            _lastSpeedSampleBytes = Math.Max(0, _operation.BytesReceived);
            _operation.Resume();
        }
    }

    private void CancelOrRemove()
    {
        if (_state is DownloadState.InProgress or DownloadState.Paused)
        {
            _operation.Cancel();
        }

        _removeCallback(this);
    }

    private void OpenFile()
    {
        if (string.IsNullOrWhiteSpace(_resultFilePath) || !File.Exists(_resultFilePath))
        {
            StatusMessage = "Dosya Bulunamadı";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _resultFilePath,
                UseShellExecute = true,
            });
        }
        catch
        {
            // User canceled SmartScreen/UAC or no file association exists.
        }
    }

    private void OpenFolder()
    {
        if (string.IsNullOrWhiteSpace(_resultFilePath))
        {
            return;
        }

        try
        {
            if (File.Exists(_resultFilePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{_resultFilePath}\"",
                    UseShellExecute = true,
                });
                return;
            }

            var directory = Path.GetDirectoryName(_resultFilePath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true,
                });
            }
        }
        catch
        {
            // Ignore shell launch errors.
        }
    }

    private void OnOperationProgressChanged(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if (now.Ticks - _lastUiUpdateTicks < ProgressUiThrottleTicks)
        {
            return;
        }

        RefreshMetrics(force: false, now);
    }

    private void OnOperationStateChanged(object? sender, EventArgs e)
    {
        UpdateFileMetadata();
        _canResume = _operation.CanResume;
        State = _operation.State;
        RefreshMetrics(force: true, DateTime.UtcNow);
        UpdateStatusMessage();

        if (IsFinalNonResumableState())
        {
            DetachOperation();
        }
    }

    private void RefreshMetrics(bool force, DateTime nowUtc)
    {
        _lastUiUpdateTicks = nowUtc.Ticks;

        var received = Math.Max(0, _operation.BytesReceived);
        var total = _operation.TotalBytesToReceive;

        if (total is > 0)
        {
            var pct = (int)Math.Clamp(received * 100L / total.Value, 0L, 100L);
            ProgressPercent = pct;
            PercentText = $"%{pct}";
            SizeText = FormatSize(total.Value);
        }
        else
        {
            ProgressPercent = _state == DownloadState.Completed ? 100 : 0;
            PercentText = _state == DownloadState.Completed ? "%100" : "%—";
            SizeText = FormatSize(received);
        }

        var elapsed = nowUtc - _startedUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        ElapsedText = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

        var dtSeconds = (nowUtc.Ticks - _lastSpeedSampleTicks) / (double)TimeSpan.TicksPerSecond;
        if (dtSeconds >= 0.25 || force)
        {
            var deltaBytes = Math.Max(0, received - _lastSpeedSampleBytes);
            if (dtSeconds > 0.05)
            {
                var instant = deltaBytes / dtSeconds;
                _smoothedBytesPerSec = _smoothedBytesPerSec <= 0
                    ? instant
                    : (_smoothedBytesPerSec * 0.6) + (instant * 0.4);
            }

            _lastSpeedSampleTicks = nowUtc.Ticks;
            _lastSpeedSampleBytes = received;
        }

        SpeedText = _state == DownloadState.Paused
            ? "Duraklatıldı"
            : FormatSpeed(_smoothedBytesPerSec);
    }

    private void UpdateStatusMessage()
    {
        StatusMessage = _state switch
        {
            DownloadState.Completed => "İndirme Tamamlandı",
            DownloadState.Paused => "Duraklatıldı",
            DownloadState.Cancelled => "İndirme İptal Edildi",
            DownloadState.Interrupted => string.IsNullOrWhiteSpace(_operation.InterruptReasonText)
                ? "İndirme Başarısız"
                : _operation.InterruptReasonText,
            _ => string.Empty,
        };
    }

    private void UpdateFileMetadata()
    {
        var path = _operation.ResultFilePath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            ResultFilePath = path;
            FileName = Path.GetFileName(path);
        }
        else if (string.IsNullOrEmpty(_fileName))
        {
            FileName = GuessFileNameFromUri(_operation.Uri);
        }

        var ext = Path.GetExtension(_fileName).TrimStart('.').ToUpperInvariant();
        if (string.IsNullOrEmpty(ext))
        {
            ext = "FILE";
        }
        else if (ext.Length > 4)
        {
            ext = ext[..4];
        }

        BadgeText = ext;
        BadgeBrush = SelectBadgeBrush(ext);
    }

    private void DetachOperation()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        _operation.ProgressChanged -= OnOperationProgressChanged;
        _operation.StateChanged -= OnOperationStateChanged;
        _operation.Dispose();
    }

    private bool IsFinalNonResumableState() =>
        _state is DownloadState.Completed or DownloadState.Cancelled
        || (_state == DownloadState.Interrupted && !_canResume);

    private static string GuessFileNameFromUri(string rawUri)
    {
        if (Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
        {
            var segment = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(segment))
            {
                return Uri.UnescapeDataString(segment);
            }
        }

        return "indirilen_dosya";
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kb = bytes / 1024.0;
        if (kb < 1024)
        {
            return $"{kb:0.#} KB";
        }

        var mb = kb / 1024.0;
        if (mb < 1024)
        {
            return mb >= 100 ? $"{mb:0} MB" : $"{mb:0.#} MB";
        }

        var gb = mb / 1024.0;
        return $"{gb:0.##} GB";
    }

    public static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return "0 Mb/s";
        }

        var mbPerSec = bytesPerSecond / (1024.0 * 1024.0);
        if (mbPerSec >= 1.0)
        {
            return mbPerSec >= 10 ? $"{mbPerSec:0} Mb/s" : $"{mbPerSec:0.#} Mb/s";
        }

        var kbPerSec = bytesPerSecond / 1024.0;
        return $"{Math.Max(1, (int)Math.Round(kbPerSec))} Kb/s";
    }

    private static Brush SelectBadgeBrush(string ext) =>
        ext switch
        {
            "EXE" or "MSI" or "BAT" or "CMD" => ExeBadgeBrush,
            "ZIP" or "RAR" or "7Z" or "TAR" or "GZ" or "ISO" => ArchiveBadgeBrush,
            "MP4" or "MKV" or "MP3" or "WAV" or "FLAC" or "PNG" or "JPG" or "JPEG" or "WEBP" => MediaBadgeBrush,
            "PDF" or "DOC" or "DOCX" or "XLS" or "XLSX" or "PPT" or "PPTX" or "TXT" => DocBadgeBrush,
            _ => DefaultBadgeBrush,
        };

    private static Brush FreezeBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
