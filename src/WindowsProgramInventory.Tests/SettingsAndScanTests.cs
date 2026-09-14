using WindowsProgramInventory.Core;
using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.UI.ViewModels;
using WindowsProgramInventory.Storage;

namespace WindowsProgramInventory.Tests;

public class SettingsViewModelTests
{
    private static (SettingsViewModel Vm, FakeSettingsStore Store, FakeThemeService Theme, FakeDialogService Dialogs) Create()
    {
        var store = new FakeSettingsStore();
        var theme = new FakeThemeService();
        var dialogs = new FakeDialogService();
        var logger = new FakeLogger();
        var vm = new SettingsViewModel(theme, store, logger, dialogs);
        return (vm, store, theme, dialogs);
    }

    [Fact]
    public void DefaultTheme_ComesFromService()
    {
        var (vm, _, theme, _) = Create();
        Assert.Equal(theme.CurrentTheme, vm.SelectedTheme);
        Assert.Equal(3, vm.Themes.Count);
    }

    [Fact]
    public void ChangingTheme_AppliesAndPersists()
    {
        var (vm, store, theme, _) = Create();

        vm.SelectedTheme = "Dark";

        Assert.Equal("Dark", theme.CurrentTheme);
        Assert.Contains("Dark", theme.Applied);
        Assert.True(store.Contains(SettingKeys.Theme));
        Assert.True(store.SaveCount > 0);
    }

    [Fact]
    public void RefreshOnStartup_DefaultFalse_AndPersistedOnChange()
    {
        var (vm, store, _, _) = Create();
        Assert.False(vm.RefreshOnStartup);

        vm.RefreshOnStartup = true;

        Assert.True(store.Get<bool>(SettingKeys.RefreshOnStartup));
    }

    [Fact]
    public void ClearCommands_RequireConfirmation()
    {
        var (vm, _, _, dialogs) = Create();
        dialogs.ConfirmResult = false;

        vm.ClearAllDataCommand.Execute(null);
        vm.ClearIconCacheCommand.Execute(null);
        vm.ClearInventoryCacheCommand.Execute(null);

        Assert.Equal(3, dialogs.Confirmed.Count);
    }

    [Fact]
    public void ShowLogs_EnabledWhenFolderExists()
    {
        var (vm, _, _, _) = Create();
        Assert.True(vm.ShowLogsCommand.CanExecute(null));
    }
}

public class JsonSettingsStoreTests
{
    [Fact]
    public void RoundTrip_PersistsQuotedValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wpi-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSettingsStore(path);
            store.Set(SettingKeys.RefreshOnStartup, true);
            store.Save();

            var reloaded = new JsonSettingsStore(path);

            Assert.True(reloaded.Get<bool>(SettingKeys.RefreshOnStartup));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RawBooleanValue_IsTolerated()
    {
        // A settings file written as plain JSON primitives (no quoting) must still load.
        var path = Path.Combine(Path.GetTempPath(), $"wpi-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"RefreshOnStartup\":true}");

            var store = new JsonSettingsStore(path);

            Assert.True(store.Get<bool>(SettingKeys.RefreshOnStartup));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ScanEngineTests
{
    [Fact]
    public async Task Scan_WithNoScanners_ReportsProgressAndEmpty()
    {
        var engine = new Services.ScanEngine(Array.Empty<IProgramSourceScanner>());
        var progress = new List<Core.Interfaces.ScanProgress>();

        var result = await engine.ScanAsync(new Progress<Core.Interfaces.ScanProgress>(progress.Add), CancellationToken.None);

        Assert.Empty(result.Programs);
        Assert.Equal(0, result.ErrorCount);
        Assert.True(progress.Count > 0);
        Assert.Equal(100, progress.Last().Percent);
    }

    [Fact]
    public async Task Scan_RespectsCancellation()
    {
        var engine = new Services.ScanEngine(Array.Empty<IProgramSourceScanner>());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.ScanAsync(null, cts.Token));
    }
}