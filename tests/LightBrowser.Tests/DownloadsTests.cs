using SudyBrowser.App.ViewModels;
using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;

namespace SudyBrowser.Tests;

internal sealed class FakeDownloadOperation : IDownloadOperation
{
    public Guid Id { get; } = Guid.NewGuid();

    public string Uri { get; set; } = "https://example.com/files/downloadx.exe";

    public string ResultFilePath { get; set; } = Path.Combine(Path.GetTempPath(), "downloadx.exe");

    public long BytesReceived { get; set; }

    public long? TotalBytesToReceive { get; set; } = 530L * 1024 * 1024;

    public DownloadState State { get; set; } = DownloadState.InProgress;

    public string InterruptReasonText { get; set; } = string.Empty;

    public bool CanResume { get; set; } = true;

    public bool IsDisposed { get; private set; }

    public event EventHandler? ProgressChanged;

    public event EventHandler? StateChanged;

    public void Pause()
    {
        State = DownloadState.Paused;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        State = DownloadState.InProgress;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        State = DownloadState.Cancelled;
        CanResume = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Advance(long bytesReceived)
    {
        BytesReceived = bytesReceived;
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Complete()
    {
        if (TotalBytesToReceive is { } total)
        {
            BytesReceived = total;
        }

        State = DownloadState.Completed;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Fail(string reason, bool canResume = false)
    {
        State = DownloadState.Interrupted;
        InterruptReasonText = reason;
        CanResume = canResume;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => IsDisposed = true;
}

public class DownloadsTests
{
    [Fact]
    public void ButtonIsHiddenInitially_ShowsOnDownload_AndStaysVisibleFor25MinutesAfterCompletion()
    {
        var now = new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc);
        using var downloads = new DownloadsViewModel(TimeSpan.FromMinutes(25), () => now);

        Assert.False(downloads.IsButtonVisible);
        Assert.False(downloads.IsOpen);

        var op = new FakeDownloadOperation
        {
            BytesReceived = 402L * 1024 * 1024,
            TotalBytesToReceive = 530L * 1024 * 1024,
        };

        var item = downloads.AddDownload(op);

        Assert.True(downloads.IsButtonVisible);
        Assert.True(downloads.IsOpen);
        Assert.Equal("downloadx.exe", item.FileName);
        Assert.Equal("EXE", item.BadgeText);
        Assert.Equal(75, item.ProgressPercent);
        Assert.Equal("%75", item.PercentText);
        Assert.Equal("530 MB", item.SizeText);
        Assert.True(item.IsActiveLayout);

        // Complete the download at T = 00:01:00
        now = now.AddMinutes(1);
        op.Complete();

        Assert.True(item.IsCompleted);
        Assert.True(item.IsFinishedLayout);
        Assert.Equal("İndirme Tamamlandı", item.StatusMessage);
        Assert.True(op.IsDisposed); // Native operation is immediately released once completed!

        // At 24 minutes after completion: button must still be visible.
        now = now.AddMinutes(24);
        downloads.CheckVisibilityExpiration();
        Assert.True(downloads.IsButtonVisible);

        // At 25 minutes after completion: button hides automatically.
        now = now.AddMinutes(1);
        downloads.CheckVisibilityExpiration();
        Assert.False(downloads.IsButtonVisible);
    }

    [Fact]
    public void PauseResumeAndNetworkFailure_UpdateLayoutAndStatusText()
    {
        var now = new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc);
        using var downloads = new DownloadsViewModel(TimeSpan.FromMinutes(25), () => now);
        var op = new FakeDownloadOperation();

        var item = downloads.AddDownload(op);

        // Pause
        item.TogglePauseResumeCommand.Execute(null);
        Assert.True(item.IsPaused);
        Assert.True(item.IsActiveLayout);
        Assert.Equal("Duraklatıldı", item.SpeedText);

        // Resume
        item.TogglePauseResumeCommand.Execute(null);
        Assert.False(item.IsPaused);
        Assert.Equal(DownloadState.InProgress, item.State);

        // Fail with network error
        op.Fail("İndirme Başarısız • Ağ Hatası", canResume: true);
        Assert.True(item.IsFailed);
        Assert.True(item.IsFinishedLayout);
        Assert.True(item.CanRetry);
        Assert.Equal("İndirme Başarısız • Ağ Hatası", item.StatusMessage);
    }

    [Fact]
    public void RetainsAtMostMaxItems_EvictingOldestFinishedFirst()
    {
        using var downloads = new DownloadsViewModel();

        for (var i = 0; i < DownloadsViewModel.MaxRetainedItems + 5; i++)
        {
            var op = new FakeDownloadOperation { ResultFilePath = $@"C:\Downloads\file{i}.zip" };
            downloads.AddDownload(op);
            op.Complete();
        }

        Assert.Equal(DownloadsViewModel.MaxRetainedItems, downloads.Items.Count);
        Assert.Equal($"file{DownloadsViewModel.MaxRetainedItems + 4}.zip", downloads.Items[0].FileName);
    }
}
