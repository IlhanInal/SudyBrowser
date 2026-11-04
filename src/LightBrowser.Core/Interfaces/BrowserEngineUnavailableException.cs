namespace SudyBrowser.Core.Interfaces;

public sealed class BrowserEngineUnavailableException : Exception
{
    public BrowserEngineUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
