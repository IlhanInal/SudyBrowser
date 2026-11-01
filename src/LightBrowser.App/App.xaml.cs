using System.Windows;
using LightBrowser.App.Services;
using LightBrowser.App.ViewModels;
using LightBrowser.Core.Interfaces;
using LightBrowser.Core.Policies;
using LightBrowser.Core.Services;
using LightBrowser.Infrastructure;
using LightBrowser.Infrastructure.Logging;
using LightBrowser.Infrastructure.Monitoring;
using LightBrowser.Infrastructure.Storage;
using LightBrowser.Infrastructure.WebView2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LightBrowser.App;

public partial class App : Application
{
    private const string ErrorCaption = "LightBrowser";

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
