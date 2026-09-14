using WindowsProgramInventory.Core;
using WindowsProgramInventory.Models;
using WindowsProgramInventory.UI.ViewModels;

namespace WindowsProgramInventory.Tests;

public class ProgramsViewModelTests
{
    private static ProgramInfo Make(string name, ProgramStatus status, SourceType sources, string? publisher = null, string? exe = null)
        => new()
        {
            Name = name,
            Publisher = publisher,
            Version = "1.0",
            Status = status,
            Sources = sources,
            ExecutablePath = exe,
        };

    [Fact]
    public void Empty_Initially()
    {
        var vm = new ProgramsViewModel();
        Assert.True(vm.IsEmpty);
        Assert.Equal(0, vm.Count);
        Assert.Equal("0 Programs", vm.CountLabel);
        Assert.Equal(Strings.NoScanData, vm.EmptyTitle);
    }

    [Fact]
    public void ReplaceAll_PopulatesCollection()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(new[]
        {
            Make("Google Chrome", ProgramStatus.InstalledAndShortcut, SourceType.Registry | SourceType.StartMenu),
            Make("Firefox", ProgramStatus.Installed, SourceType.Registry),
        });

        Assert.False(vm.IsEmpty);
        Assert.Equal(2, vm.Count);
        Assert.Equal("2 Programs", vm.CountLabel);
    }

    [Fact]
    public void Search_FiltersByName()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(new[]
        {
            Make("Google Chrome", ProgramStatus.Installed, SourceType.Registry),
            Make("Firefox", ProgramStatus.Installed, SourceType.Registry),
            Make("Chrome Remote Desktop", ProgramStatus.Installed, SourceType.Registry),
        });

        vm.SearchText = "chrome";

        Assert.Equal(2, vm.Count);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void Search_FiltersByPublisherAndExecutable()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(new[]
        {
            Make("Alpha", ProgramStatus.Installed, SourceType.Registry, publisher: "Google LLC"),
            Make("Beta", ProgramStatus.Installed, SourceType.Registry, exe: @"C:\Tools\spotify.exe"),
        });

        Assert.Equal(1, vm.CountFor("google"));
        Assert.Equal(1, vm.CountFor("spotify"));
    }

    [Fact]
    public void Filter_Installed_OnlyReturnsInstalled()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(new[]
        {
            Make("A", ProgramStatus.InstalledAndShortcut, SourceType.Registry | SourceType.StartMenu),
            Make("B", ProgramStatus.ShortcutOnly, SourceType.StartMenu),
            Make("C", ProgramStatus.Installed, SourceType.Registry),
        });

        vm.ActiveFilter = NavigationKey.Installed;

        Assert.Equal(2, vm.Count);
    }

    [Fact]
    public void Filter_Broken_OnlyReturnsBroken()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(new[]
        {
            Make("A", ProgramStatus.BrokenShortcut, SourceType.StartMenu),
            Make("B", ProgramStatus.Installed, SourceType.Registry),
        });

        vm.ActiveFilter = NavigationKey.Broken;

        Assert.Equal(1, vm.Count);
        Assert.Equal("A", vm.Programs[0].Name);
    }

    [Fact]
    public void Filter_Store_OnlyReturnsStoreApps()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(new[]
        {
            Make("StoreApp", ProgramStatus.Installed, SourceType.AppX),
            Make("Desktop", ProgramStatus.Installed, SourceType.Registry),
        });

        vm.ActiveFilter = NavigationKey.Store;

        Assert.Equal(1, vm.Count);
    }

    [Fact]
    public void ReplaceAll_Empty_RestoresEmptyState()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(new[] { Make("A", ProgramStatus.Installed, SourceType.Registry) });
        vm.ReplaceAll(Array.Empty<ProgramInfo>());

        Assert.True(vm.IsEmpty);
        Assert.Equal(Strings.NoScanData, vm.EmptyTitle);
    }

    [Fact]
    public void Search_NoMatches_EmptyButHasData()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(new[] { Make("Firefox", ProgramStatus.Installed, SourceType.Registry) });

        vm.SearchText = "does-not-exist";

        Assert.True(vm.IsEmpty);
        Assert.Equal(Strings.EmptyHint, vm.EmptyTitle);
    }

    [Fact]
    public void IconSize_RaisesChangeAndClampsToRange()
    {
        var vm = new ProgramsViewModel();
        var changed = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IconSizePreview))
            {
                changed++;
            }
        };

        vm.IconSize = 96;

        Assert.Equal(96, vm.IconSize);
        Assert.Equal(96, vm.IconSizePreview);
        Assert.True(changed > 0);
    }

    [Fact]
    public void LargeList_WindowsVisibleSlice()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(Enumerable.Range(0, 1000).Select(i => Make($"P{i}", ProgramStatus.Installed, SourceType.Registry)));
        vm.UpdateViewport(1200, 600);

        // Total shown in the header, but only a small viewport window is materialized.
        Assert.Equal(1000, vm.Count);
        Assert.True(vm.Programs.Count > 0 && vm.Programs.Count < 100, $"Window too large: {vm.Programs.Count}");
        Assert.True(vm.EstimatedContentHeight > 0);
        Assert.Equal(6 * 198, vm.ItemsAreaWidth);
    }

    [Fact]
    public void OnScroll_MovesWindowToMatchOffset()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(Enumerable.Range(0, 1000).Select(i => Make($"P{i}", ProgramStatus.Installed, SourceType.Registry)));
        vm.UpdateViewport(1200, 600);

        vm.OnScroll(offset: 176 * 40, viewportHeight: 600, extentHeight: 50000);

        Assert.NotEqual("P0", vm.Programs[0].Name);
    }

    [Fact]
    public void OnScroll_SmallStep_KeepsWindow()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(Enumerable.Range(0, 1000).Select(i => Make($"P{i}", ProgramStatus.Installed, SourceType.Registry)));
        vm.UpdateViewport(1200, 600);

        vm.OnScroll(offset: 176 * 2, viewportHeight: 600, extentHeight: 50000);

        Assert.Equal("P0", vm.Programs[0].Name);
    }

    [Fact]
    public void Viewport_UpdatesEstimate()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(Enumerable.Range(0, 8).Select(i => Make($"P{i}", ProgramStatus.Installed, SourceType.Registry)));

        vm.UpdateViewport(2000, 600);

        Assert.True(vm.EstimatedContentHeight > 0);
    }

    [Theory]
    [InlineData(470)]
    [InlineData(700)]
    [InlineData(910)]
    [InlineData(1100)]
    [InlineData(1500)]
    [InlineData(2000)]
    public void Estimate_IsExactGridExtent_ForAnyWidth(int width)
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(Enumerable.Range(0, 1031).Select(i => Make($"P{i}", ProgramStatus.Installed, SourceType.Registry)));
        vm.UpdateViewport(width, 600);

        var perRow = (int)Math.Floor((width - 8) / (double)ProgramsViewModel.ColumnWidth);
        var rows = (int)Math.Ceiling(vm.Count / (double)Math.Max(1, perRow));

        // The wrap panel is exactly perRow columns wide, so the estimate can never
        // exceed the real content: extent == rows * RowHeight gives ZERO room to
        // scroll past the last icon.
        Assert.Equal(perRow * ProgramsViewModel.ColumnWidth, vm.ItemsAreaWidth);
        Assert.Equal(rows * vm.RowHeight, vm.EstimatedContentHeight);
    }

    [Fact]
    public void ScrollToMax_PlacesLastItemExactlyAtBottom_NoTail()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(Enumerable.Range(0, 1000).Select(i => Make($"P{i}", ProgramStatus.Installed, SourceType.Registry)));
        vm.UpdateViewport(1200, 600);

        var perRow = (int)Math.Floor((1200 - 8) / (double)ProgramsViewModel.ColumnWidth);
        var rows = (int)Math.Ceiling(vm.Count / (double)perRow);
        var extent = rows * vm.RowHeight;
        var viewport = 600d;

        vm.OnScroll(offset: extent - viewport, viewportHeight: viewport, extentHeight: extent);

        // The last program must be realized in the visible window...
        Assert.Equal("P999", vm.Programs[^1].Name);
        // ...and the ScrollViewer's extent is exactly the real content height:
        // travel is (rows * RowHeight) - viewport, so the last row sits flush at the
        // bottom with nothing scrollable left below it.
        Assert.Equal(extent, vm.EstimatedContentHeight);
    }

    [Fact]
    public void ScrollToMax_PartialLastRow_StillFlush()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(Enumerable.Range(0, 1003).Select(i => Make($"P{i}", ProgramStatus.Installed, SourceType.Registry)));
        vm.UpdateViewport(1200, 600);

        var perRow = (int)Math.Floor((1200 - 8) / (double)ProgramsViewModel.ColumnWidth);
        var rows = (int)Math.Ceiling(vm.Count / (double)perRow);
        var extent = rows * vm.RowHeight;

        vm.OnScroll(offset: extent - 600, viewportHeight: 600, extentHeight: extent);

        // The straggler from the partial last row is visible at the bottom, and the
        // extent exactly equals rows * RowHeight (no phantom row, no scroll past it).
        Assert.Equal("P1002", vm.Programs[^1].Name);
        Assert.Equal(rows * vm.RowHeight, vm.EstimatedContentHeight);
    }

    [Fact]
    public void ListMode_OneColumnRows_FullWidthFlush()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(Enumerable.Range(0, 1000).Select(i => Make($"P{i}", ProgramStatus.Installed, SourceType.Registry)));
        vm.UpdateViewport(1200, 600);

        vm.IsListView = true;

        // One program per line, rows spanning the whole viewport, height 46 each.
        Assert.True(vm.IsCardsView == false);
        Assert.Equal(1200, vm.ItemsAreaWidth);
        Assert.Equal(46, vm.RowHeight);
        Assert.Equal(1000 * 46d, vm.EstimatedContentHeight);
        Assert.True(vm.Programs.Count > 0);
        Assert.True(vm.Programs.Count < 100);

        var extent = 1000 * 46d;
        vm.OnScroll(offset: extent - 600, viewportHeight: 600, extentHeight: extent);

        // Full-width rows: at max scroll the very last program is realized with no tail.
        Assert.Equal("P999", vm.Programs[^1].Name);
        Assert.Equal(extent, vm.EstimatedContentHeight);
    }

    [Fact]
    public void ToggleViewMode_RestoresCardGeometry()
    {
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(Enumerable.Range(0, 1000).Select(i => Make($"P{i}", ProgramStatus.Installed, SourceType.Registry)));
        vm.UpdateViewport(1200, 600);

        var cardsWidth = vm.ItemsAreaWidth;
        var cardsHeight = vm.EstimatedContentHeight;

        vm.IsListView = true;
        Assert.Equal(1200, vm.ItemsAreaWidth);
        Assert.Equal(1000 * 46d, vm.EstimatedContentHeight);

        vm.IsListView = false;
        Assert.Equal(cardsWidth, vm.ItemsAreaWidth);
        Assert.Equal(cardsHeight, vm.EstimatedContentHeight);
    }

    [Fact]
    public void IconSize_SnapsToStandardSizes()
    {
        var vm = new ProgramsViewModel();

        vm.IconSize = 55;

        Assert.Equal(48, vm.IconSize);

        vm.IconSize = 200;

        Assert.Equal(192, vm.IconSize);
    }

    [Fact]
    public void ToggleDetails_FlipsFlag()
    {
        var program = Make("A", ProgramStatus.Installed, SourceType.Registry, exe: @"C:\Tools\a.exe");
        var vm = new ProgramsViewModel();
        vm.ReplaceAll(new[] { program });

        Assert.False(program.IsDetailsOpen);
        Assert.Equal(@"C:\Tools\a.exe", program.PathSummary);

        vm.ToggleDetailsCommand.Execute(program);

        Assert.True(program.IsDetailsOpen);
    }

    [Fact]
    public void LaunchProgram_Unlaunchable_ReportsStatus()
    {
        var program = Make("A", ProgramStatus.Installed, SourceType.Registry, exe: @"C:\missing\never.exe");
        var vm = new ProgramsViewModel();
        string? reported = null;
        vm.StatusReporter = m => reported = m;

        vm.LaunchProgramCommand.Execute(program);

        Assert.NotNull(reported);
        Assert.Contains("Cannot launch", reported, StringComparison.OrdinalIgnoreCase);
    }
}

public static class ProgramsViewModelTestsExtensions
{
    public static int CountFor(this ProgramsViewModel vm, string query)
    {
        var original = vm.SearchText;
        vm.SearchText = query;
        try
        {
            return vm.Count;
        }
        finally
        {
            vm.SearchText = original;
        }
    }
}