using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.Models;
using WindowsProgramInventory.Services;
using WindowsProgramInventory.Services.Logging;
using WindowsProgramInventory.Storage;
using WindowsProgramInventory.UI.ViewModels;

namespace WindowsProgramInventory.Startup;

/// <summary>
/// Composition root. Manual, dependency-free DI keeps the app light and easy to follow.
/// Scanners (Phase 2+) will be registered here as IProgramSourceScanner implementations.
/// </summary>
public static class Bootstrapper
{
    public static MainViewModel BuildMainViewModel()
    {
        var logger = new FileLogger();
        var settings = new JsonSettingsStore();

        var themeService = new ThemeService();
        themeService.Apply(settings.Get<string>(SettingKeys.Theme) ?? "System");

        IDialogService dialogs = new DialogService();

        IProgramRepository repository = new InMemoryProgramRepository();

        IProgramSourceScanner[] scanners =
        {
            new StartMenuScanner(logger),
            new RegistryScanner(logger),
            new AppxScanner(logger),
        };

        var scanEngine = new ScanEngine(
            scanners,
            logger,
            new IdentityResolver(),
            new ClassificationEngine(),
            new Services.Icons.IconExtractor(logger),
            new ShortcutAnalyzer(logger),
            new ExecutableAnalyzer(logger));

        var programs = new ProgramsViewModel();
        var statistics = new StatisticsViewModel();

        // SettingsViewModel forwards status messages and export requests to the main window once it exists.
        Action<string>? statusForwarder = null;
        Action<ReportExportFormat>? exportForwarder = null;
        var settingsVm = new SettingsViewModel(themeService, settings, logger, dialogs,
            message => statusForwarder?.Invoke(message),
            format => exportForwarder?.Invoke(format));

        var main = new MainViewModel(scanEngine, repository, logger, programs, statistics, settingsVm);
        statusForwarder = main.NotifyStatus;
        exportForwarder = main.ExportReport;

        return main;
    }
}