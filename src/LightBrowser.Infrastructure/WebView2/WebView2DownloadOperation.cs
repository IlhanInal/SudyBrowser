using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;
using Microsoft.Web.WebView2.Core;

namespace SudyBrowser.Infrastructure.WebView2;

/// <summary>
/// Wraps a <see cref="CoreWebView2DownloadOperation"/> and releases event subscriptions
/// as soon as the download reaches a terminal state or is disposed.
/// </summary>
public sealed class WebView2DownloadOperation : IDownloadOperation
{
    private readonly CoreWebView2DownloadOperation _operation;
    private bool _disposed;
    private bool _pausedByUser;

    public WebView2DownloadOperation(CoreWebView2DownloadOperation operation)
    {
        _operation = operation;
        _operation.BytesReceivedChanged += OnBytesReceivedChanged;
        _operation.StateChanged += OnStateChanged;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string Uri => _operation.Uri ?? string.Empty;

    public string ResultFilePath => _operation.ResultFilePath ?? string.Empty;

    public long BytesReceived => _operation.BytesReceived;

    public long? TotalBytesToReceive => _operation.TotalBytesToReceive is { } total ? (long)total : null;

    public DownloadState State => MapState(_operation.State, _operation.InterruptReason, _pausedByUser);

    public string InterruptReasonText => FormatInterruptReason(_operation.InterruptReason);

    public bool CanResume => _operation.CanResume;

    public event EventHandler? ProgressChanged;

    public event EventHandler? StateChanged;

    public void Pause()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_operation.State == CoreWebView2DownloadState.InProgress)
            {
                _pausedByUser = true;
                _operation.Pause();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch
        {
            // Ignore invalid state transitions from the native engine.
        }
    }

    public void Resume()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_operation.CanResume)
            {
                _pausedByUser = false;
                _operation.Resume();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch
        {
            // Ignore invalid state transitions from the native engine.
        }
    }

    public void Cancel()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _pausedByUser = false;
            _operation.Cancel();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Ignore invalid state transitions from the native engine.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _operation.BytesReceivedChanged -= OnBytesReceivedChanged;
        _operation.StateChanged -= OnStateChanged;
    }

    private void OnBytesReceivedChanged(object? sender, object e) =>
        ProgressChanged?.Invoke(this, EventArgs.Empty);

    private void OnStateChanged(object? sender, object e)
    {
        if (_operation.State == CoreWebView2DownloadState.InProgress)
        {
            _pausedByUser = false;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);

        // Detach native handlers once completed or permanently interrupted/cancelled.
        if (_operation.State == CoreWebView2DownloadState.Completed
            || (_operation.State == CoreWebView2DownloadState.Interrupted && !_operation.CanResume && !_pausedByUser))
        {
            Dispose();
        }
    }

    private static DownloadState MapState(
        CoreWebView2DownloadState state,
        CoreWebView2DownloadInterruptReason reason,
        bool pausedByUser)
    {
        return state switch
        {
            CoreWebView2DownloadState.InProgress => pausedByUser ? DownloadState.Paused : DownloadState.InProgress,
            CoreWebView2DownloadState.Completed => DownloadState.Completed,
            CoreWebView2DownloadState.Interrupted when pausedByUser && reason == CoreWebView2DownloadInterruptReason.UserPaused =>
                DownloadState.Paused,
            CoreWebView2DownloadState.Interrupted when reason == CoreWebView2DownloadInterruptReason.UserPaused =>
                DownloadState.Paused,
            CoreWebView2DownloadState.Interrupted when reason == CoreWebView2DownloadInterruptReason.UserCanceled
                || reason == CoreWebView2DownloadInterruptReason.UserShutdown =>
                DownloadState.Cancelled,
            _ => DownloadState.Interrupted,
        };
    }

    private static string FormatInterruptReason(CoreWebView2DownloadInterruptReason reason) =>
        reason switch
        {
            CoreWebView2DownloadInterruptReason.None => string.Empty,
            CoreWebView2DownloadInterruptReason.UserPaused => "Duraklatıldı",
            CoreWebView2DownloadInterruptReason.UserCanceled => "İndirme İptal Edildi",
            CoreWebView2DownloadInterruptReason.NetworkFailed
                or CoreWebView2DownloadInterruptReason.NetworkTimeout
                or CoreWebView2DownloadInterruptReason.NetworkDisconnected
                or CoreWebView2DownloadInterruptReason.NetworkServerDown
                or CoreWebView2DownloadInterruptReason.NetworkInvalidRequest => "İndirme Başarısız • Ağ Hatası",
            CoreWebView2DownloadInterruptReason.FileNoSpace => "İndirme Başarısız • Yetersiz Disk Alanı",
            CoreWebView2DownloadInterruptReason.FileAccessDenied => "İndirme Başarısız • Erişim Engellendi",
            CoreWebView2DownloadInterruptReason.FileBlockedByPolicy
                or CoreWebView2DownloadInterruptReason.FileSecurityCheckFailed
                or CoreWebView2DownloadInterruptReason.FileMalicious => "İndirme Başarısız • Güvenlik Engeli",
            CoreWebView2DownloadInterruptReason.ServerFailed
                or CoreWebView2DownloadInterruptReason.ServerBadContent
                or CoreWebView2DownloadInterruptReason.ServerUnauthorized
                or CoreWebView2DownloadInterruptReason.ServerForbidden
                or CoreWebView2DownloadInterruptReason.ServerCrossOriginRedirect => "İndirme Başarısız • Sunucu Hatası",
            _ => "İndirme Başarısız",
        };
}
