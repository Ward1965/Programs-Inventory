using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Tests;

public class ProgramInfoTests
{
    [Fact]
    public void NewProgram_Defaults_ToUnknown()
    {
        var p = new ProgramInfo { Name = "App" };

        Assert.Equal(ProgramStatus.Unknown, p.Status);
        Assert.Equal(SourceType.None, p.Sources);
        Assert.False(p.IsInstalled);
        Assert.False(p.IsBrokenShortcut);
        Assert.False(p.IsStoreApp);
    }

    [Fact]
    public void InstalledAndShortcut_IsInstalled_IsTrue()
    {
        var p = new ProgramInfo { Name = "App", Status = ProgramStatus.InstalledAndShortcut };
        Assert.True(p.IsInstalled);
        Assert.Equal("Installed + Shortcut", p.DisplayStatus);
    }

    [Fact]
    public void ShortcutOnly_IsInstalled_IsFalse()
    {
        var p = new ProgramInfo { Name = "App", Status = ProgramStatus.ShortcutOnly };
        Assert.False(p.IsInstalled);
    }

    [Fact]
    public void SourceFlags_OnStartMenu_FlipsFlag()
    {
        var p = new ProgramInfo { Name = "App", Sources = SourceType.StartMenu };
        Assert.True(p.HasStartMenuShortcut);
    }

    [Fact]
    public void StoreSources_IsStoreApp()
    {
        var p = new ProgramInfo { Name = "App", Sources = SourceType.AppX };
        Assert.True(p.IsStoreApp);
    }
}

public class ViewModeTests
{
    [Theory]
    [InlineData(ViewMode.Small, 32)]
    [InlineData(ViewMode.Medium, 48)]
    [InlineData(ViewMode.Large, 64)]
    [InlineData(ViewMode.ExtraLarge, 96)]
    [InlineData(ViewMode.List, 24)]
    public void IconSize_MapsCorrectly(ViewMode mode, int expected)
        => Assert.Equal(expected, mode.IconSize());
}