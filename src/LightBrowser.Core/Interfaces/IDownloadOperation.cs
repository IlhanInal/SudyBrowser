using SudyBrowser.Core.Models;

namespace SudyBrowser.Core.Interfaces;

/// <summary>
/// Engine-agnostic adapter for a single file download operation.
/// Hides <c>CoreWebView2DownloadOperation</c> from Core and ViewModels.
/// </summary>
public interface IDownloadOperation : IDisposable
{
    Guid Id { get; }

    string Uri { get; }

    string ResultFilePath { get; }

    long BytesReceived { get; }

    long? TotalBytesToReceive { get; }

    DownloadState State { get; }

    string InterruptReasonText { get; }

    bool CanResume { get; }

    event EventHandler? ProgressChanged;

    event EventHandler? StateChanged;

    void Pause();

    void Resume();

    void Cancel();
}
