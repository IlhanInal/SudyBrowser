using SudyBrowser.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace SudyBrowser.Infrastructure.WebView2;

/// <summary>Creates the single shared <see cref="CoreWebView2Environment"/> and a host per tab.</summary>
public sealed class WebView2HostFactory : IWebViewHostFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<WebView2HostFactory> _logger;
    private CoreWebView2Environment? _environment;

    public WebView2HostFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<WebView2HostFactory>();
    }

    public async Task InitializeAsync()
    {
        if (_environment is not null)
        {
            return;
        }

        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--disable-features=Translate,OptimizationHints,MediaRouter --enable-features=NetworkServiceInProcess --disable-background-networking"
            };
            _environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: AppDataPaths.WebViewUserData,
                options: options);
            _logger.LogInformation("WebView2 environment created (runtime {Version})", version);
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            _logger.LogError(ex, "WebView2 Runtime not found");
            throw new BrowserEngineUnavailableException(
                "Microsoft Edge WebView2 Runtime bulunamadı. Lütfen https://go.microsoft.com/fwlink/p/?LinkId=2124703 adresinden kurun.",
                ex);
        }
        catch (Exception ex) when (ex is not BrowserEngineUnavailableException)
        {
            _logger.LogError(ex, "WebView2 environment creation failed");
            throw new BrowserEngineUnavailableException(
                $"WebView2 ortamı başlatılamadı: {ex.Message}",
                ex);
        }
    }

    public IWebViewHost Create()
    {
        var environment = _environment
            ?? throw new InvalidOperationException($"{nameof(InitializeAsync)} must complete before creating hosts.");
        return new WebView2Host(environment, _loggerFactory.CreateLogger<WebView2Host>());
    }

    public IReadOnlyList<int> GetProcessIds()
    {
        if (_environment is null)
        {
            return Array.Empty<int>();
        }

        try
        {
            return _environment.GetProcessInfos().Select(p => p.ProcessId).ToList();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }
}
