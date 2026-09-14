using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.Models;
using WindowsProgramInventory.Services;
using WindowsProgramInventory.Storage;
using WindowsProgramInventory.UI.ViewModels;

namespace WindowsProgramInventory.Tests;

public class MainViewModelTests
{
    private static (MainViewModel Main, FakeSettingsStore Settings, FakeLogger Logger) Create()
    {
        var settings = new FakeSettingsStore();
        var theme = new FakeThemeService();
        var logger = new FakeLogger();
        var dialogs = new FakeDialogService();
        var settingsVm = new SettingsViewModel(theme, settings, logger, dialogs);
        var programs = new ProgramsViewModel();
        var statistics = new StatisticsViewModel();
        var scanEngine = new ScanEngine(Array.Empty<IProgramSourceScanner>());
        var repository = new InMemoryProgramRepository();

        var main = new MainViewModel(scanEngine, repository, logger, programs, statistics, settingsVm);
        return (main, settings, logger);
    }

    [Fact]
    public void Constructor_SelectsHomeByDefault()
    {
        var (main, _, _) = Create();

        Assert.NotNull(main.SelectedNavItem);
        Assert.Equal(NavigationKey.Home, main.SelectedNavItem!.Key);
        Assert.Same(main.Programs, main.CurrentViewModel);
    }

    [Fact]
    public void Navigation_SwitchesPages()
    {
        var (main, _, _) = Create();

        main.SelectedNavItem = main.NavItems.Single(n => n.Key == NavigationKey.Statistics);
        Assert.Same(main.Statistics, main.CurrentViewModel);

        main.SelectedNavItem = main.NavItems.Single(n => n.Key == NavigationKey.Settings);
        Assert.Same(main.Settings, main.CurrentViewModel);
    }

    [Fact]
    public void Navigation_ProgramsFilters()
    {
        var (main, _, _) = Create();

        main.SelectedNavItem = main.NavItems.Single(n => n.Key == NavigationKey.Installed);
        Assert.Same(main.Programs, main.CurrentViewModel);
        Assert.Equal(NavigationKey.Installed, main.Programs.ActiveFilter);

        main.SelectedNavItem = main.NavItems.Single(n => n.Key == NavigationKey.Broken);
        Assert.Equal(NavigationKey.Broken, main.Programs.ActiveFilter);
    }

    [Fact]
    public void SearchText_DelegatesToPrograms()
    {
        var (main, _, _) = Create();
        main.SearchText = "chrome";
        Assert.Equal("chrome", main.Programs.SearchText);
    }

    [Fact]
    public async Task Scan_RunsAsyncAndReportsSummary()
    {
        var (main, _, logger) = Create();

        await main.ScanCommand.ExecuteAsync(null, CancellationToken.None);

        Assert.False(main.IsScanning);
        Assert.True(main.Programs.IsEmpty);
        Assert.Equal("0 Programs", main.StatusMessage);
        Assert.Contains("programs", main.LastScanSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logger.Entries, e => e.Message.Contains("programs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InitializeAsync_HandlesEmptyCache()
    {
        var (main, _, _) = Create();
        await main.InitializeAsync();

        Assert.Equal(WindowsProgramInventory.Core.Strings.StatusReady, main.StatusMessage);
        Assert.True(main.Programs.IsEmpty);
    }

    [Fact]
    public async Task CancelScan_DisablesAndCancels()
    {
        var (main, _, _) = Create();
        Assert.False(main.CancelScanCommand.CanExecute(null));

        var run = main.ScanCommand.ExecuteAsync(null, CancellationToken.None);

        Assert.True(main.IsScanning);
        Assert.True(main.CancelScanCommand.CanExecute(null));

        // Cancel the in-flight scan; it should complete quietly.
        main.CancelScanCommand.Execute(null);
        await run;

        Assert.False(main.IsScanning);
        Assert.Equal(WindowsProgramInventory.Core.Strings.ScanCancelled, main.StatusMessage);
    }
}