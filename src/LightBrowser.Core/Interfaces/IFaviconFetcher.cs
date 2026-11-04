namespace SudyBrowser.Core.Interfaces;

/// <summary>
/// Downloads raw favicon bytes for a single candidate URL.
/// Implementations must validate scheme, redirect count, content type, and payload size,
/// and return null on any network or validation failure (throwing only on caller cancellation).
/// </summary>
public interface IFaviconFetcher
{
    Task<byte[]?> FetchBytesAsync(string faviconUrl, CancellationToken cancellationToken);
}
