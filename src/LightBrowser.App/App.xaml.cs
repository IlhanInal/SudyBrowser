using System.Windows;
using SudyBrowser.App.Services;
using SudyBrowser.App.ViewModels;
using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Policies;
using SudyBrowser.Core.Services;
using SudyBrowser.Infrastructure;
using SudyBrowser.Infrastructure.Logging;
using SudyBrowser.Infrastructure.Monitoring;
using SudyBrowser.Infrastructure.Storage;
using SudyBrowser.Infrastructure.Suggestions;
using SudyBrowser.Infrastructure.WebView2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SudyBrowser.App;

public partial class App : Application
{
    private const string ErrorCaption = "SudyBrowser";

    private ServiceProvider? _services;
    private ILogger<App>? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The window is created only after the engine is ready, so keep the app alive until then.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _services = BuildServices();
        _logger = _services.GetRequiredService<ILogger<App>>();
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            await _services.GetRequiredService<IWebViewHostFactory>().InitializeAsync();
        }
        catch (BrowserEngineUnavailableException ex)
        {
            MessageBox.Show(ex.Message, ErrorCaption, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var viewModel = _services.GetRequiredService<MainViewModel>();
        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
        viewModel.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        _services?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddProvider(new FileLoggerProvider(AppDataPaths.Logs)));
        services.AddSingleton<IWebViewHostFactory, WebView2HostFactory>();
        services.AddSingleton<IAddressParser, AddressParser>();
        services.AddSingleton<ISystemMetricsProvider, WindowsSystemMetricsProvider>();
        services.AddSingleton<IRecentSitesStore, JsonRecentSitesStore>();
        services.AddSingleton<RecentSites>();
        services.AddSingleton<IBookmarkStore, JsonBookmarkStore>();
        services.AddSingleton<BookmarkService>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IHistoryStore, SqliteHistoryStore>();
        services.AddSingleton(SearchSuggestionOptions.Google);
        services.AddSingleton<ISearchSuggestionProvider, HttpSearchSuggestionProvider>();
        services.AddSingleton<IFaviconFetcher, HttpFaviconFetcher>();
        services.AddSingleton<FaviconCacheService>();
        services.AddSingleton<SuggestionService>();
        services.AddSingleton<HistoryRecorder>();
        services.AddSingleton<OmniboxViewModel>();
        services.AddSingleton<DownloadsViewModel>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<StatusViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<TabManager>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled UI exception");
        MessageBox.Show(
            $"Beklenmeyen bir hata oluştu: {e.Exception.Message}",
            ErrorCaption,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
