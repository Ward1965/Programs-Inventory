using System.Windows;
using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.Services.Logging;
using WindowsProgramInventory.Startup;
using WindowsProgramInventory.UI.ViewModels;

namespace WindowsProgramInventory;

/// <summary>
/// Application entry point. Wires the composition root, exception logging and the main window.
/// </summary>
public partial class App : Application
{
    private ILogger? _logger;
    private MainViewModel? _mainViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _logger = new FileLogger();
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _logger?.Info("Exit: ProcessExit reached");

        _mainViewModel = Bootstrapper.BuildMainViewModel();

        var window = new UI.MainWindow
        {
            DataContext = _mainViewModel,
        };
        MainWindow = window;
        window.Show();

        _ = StartupAsync();
    }

    private async Task StartupAsync()
    {
        try
        {
            _logger?.Info("Startup: beginning InitializeAsync");
            await _mainViewModel!.InitializeAsync();
            _logger?.Info($"Startup: InitializeAsync done (RefreshOnStartup={_mainViewModel.Settings.RefreshOnStartup})");

            if (_mainViewModel.Settings.RefreshOnStartup)
            {
                _logger?.Info("Startup: beginning scan");
                await _mainViewModel.ScanCommand.ExecuteAsync(null, CancellationToken.None);
                _logger?.Info("Startup: scan complete");
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("Startup failed", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _logger?.Error($"Domain unhandled exception (terminating: {e.IsTerminating})", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.Error("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info($"Exit: Application exit code={e.ApplicationExitCode} (clean shutdown)");
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        base.OnExit(e);
    }
}