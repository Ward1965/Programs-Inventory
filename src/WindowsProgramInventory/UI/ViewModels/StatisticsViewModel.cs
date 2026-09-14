using WindowsProgramInventory.Core;
using WindowsProgramInventory.Core.Interfaces;

namespace WindowsProgramInventory.UI.ViewModels;

/// <summary>
/// Dashboard counters shown on the Statistics page.
/// </summary>
public sealed class StatisticsViewModel : ViewModelBase
{
    public string Title { get; } = Strings.StatisticsTitle;

    public int Total { get; private set; }
    public int Installed { get; private set; }
    public int ShortcutsOnly { get; private set; }
    public int Broken { get; private set; }
    public int StoreApps { get; private set; }
    public int Publishers { get; private set; }

    public void UpdateFrom(ScanResult result)
    {
        Total = result.Programs.Count;
        Installed = result.InstalledCount;
        ShortcutsOnly = result.ShortcutOnlyCount;
        Broken = result.BrokenCount;
        StoreApps = result.StoreAppCount;
        Publishers = result.Programs
            .Select(p => p.Publisher)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Installed));
        OnPropertyChanged(nameof(ShortcutsOnly));
        OnPropertyChanged(nameof(Broken));
        OnPropertyChanged(nameof(StoreApps));
        OnPropertyChanged(nameof(Publishers));
        OnPropertyChanged(nameof(HasData));
    }

    public bool HasData => Total > 0;

    public void Reset()
    {
        Total = Installed = ShortcutsOnly = Broken = StoreApps = Publishers = 0;
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Installed));
        OnPropertyChanged(nameof(ShortcutsOnly));
        OnPropertyChanged(nameof(Broken));
        OnPropertyChanged(nameof(StoreApps));
        OnPropertyChanged(nameof(Publishers));
        OnPropertyChanged(nameof(HasData));
    }
}