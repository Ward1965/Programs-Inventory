using System.ComponentModel;

namespace WindowsProgramInventory.Models;

/// <summary>
/// Central data model representing one discovered program or shortcut.
/// All fields are nullable except Id, Name, Status and Sources – those are always present after discovery.
/// Accuracy over guessing: every property must be evidence-based, never fabricated.
/// </summary>
public sealed class ProgramInfo : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    // ── Identity ──────────────────────────────────────────────
    public required string Name { get; set; }

    /// <summary>Display name as shown in the registry / start menu / app manifest.</summary>
    public string? DisplayName { get; set; }

    public string? Publisher { get; set; }
    public string? Version { get; set; }

    // ── Executable ────────────────────────────────────────────
    public string? ExecutablePath { get; set; }
    public string? InstallLocation { get; set; }
    public string? InstallSource { get; set; }
    public string? UninstallString { get; set; }
    public string? QuietUninstallString { get; set; }

    // ── Short-cut ─────────────────────────────────────────────
    public string? ShortcutPath { get; set; }
    public string? ShortcutTarget { get; set; }
    public string? ShortcutArguments { get; set; }
    public string? ShortcutWorkingDirectory { get; set; }
    public string? ShortcutDescription { get; set; }
    public string? ShortcutIconLocation { get; set; }

    // ── Icon ──────────────────────────────────────────────────
    public string? IconPath { get; set; }

    /// <summary>Which source was used to extract the icon.</summary>
    public IconSource IconSource { get; set; } = IconSource.None;

    /// <summary>Cached extracted icon file (PNG), set after icon extraction.</summary>
    public string? IconCachePath { get; set; }

    // ── Classification ────────────────────────────────────────
    public ProgramStatus Status { get; set; } = ProgramStatus.Unknown;
    public ProgramType Type { get; set; } = ProgramType.Unknown;
    public SourceType Sources { get; set; } = SourceType.None;

    // ── File metadata (from PE / FileInfo) ────────────────────
    public long? FileSizeBytes { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? FileVersion { get; set; }
    public string? ProductVersion { get; set; }
    public string? ProductName { get; set; }
    public string? CompanyName { get; set; }
    public string? Copyright { get; set; }
    public string? OriginalFilename { get; set; }
    public string? Description { get; set; }
    public ProgramArchitecture Architecture { get; set; } = ProgramArchitecture.Unknown;

    // ── Registry metadata ─────────────────────────────────────
    public string? RegistryKey { get; set; }
    public int? EstimatedSizeKB { get; set; }
    public string? UrlInfoAbout { get; set; }
    public string? HelpLink { get; set; }
    public string? ReleaseType { get; set; }
    public bool? IsSystemComponent { get; set; }
    public bool? IsWindowsInstaller { get; set; }

    // ── AppX / MSIX ──────────────────────────────────────────
    public string? PackageName { get; set; }
    public string? PackageFamilyName { get; set; }
    public string? AppUserModelId { get; set; }

    // ── Computed flags ────────────────────────────────────────
    public bool IsInstalled => Status is ProgramStatus.Installed or ProgramStatus.InstalledAndShortcut;
    public bool HasStartMenuShortcut => (Sources & SourceType.StartMenu) != 0;
    public bool IsShortcut => (Sources & SourceType.Shortcut) != 0;
    public bool IsBrokenShortcut => Status == ProgramStatus.BrokenShortcut;
    public bool IsStoreApp => (Sources & (SourceType.AppX | SourceType.Msix)) != 0;

    public string DisplayStatus => Status switch
    {
        ProgramStatus.Installed => "Installed",
        ProgramStatus.ShortcutOnly => "Shortcut Only",
        ProgramStatus.InstalledAndShortcut => "Installed + Shortcut",
        ProgramStatus.BrokenShortcut => "Broken Shortcut",
        _ => "Unknown",
    };

    // ── Per-card UI state (not persisted) ─────────────────────
    private bool _isDetailsOpen;

    /// <summary>Whether the details block on the program card is expanded.</summary>
    public bool IsDetailsOpen
    {
        get => _isDetailsOpen;
        set
        {
            if (_isDetailsOpen != value)
            {
                _isDetailsOpen = value;
                Raise(nameof(IsDetailsOpen));
            }
        }
    }

    /// <summary>Best available path shown in the launch/details UI (evidence-based, never guessed).</summary>
    public string? PathSummary =>
        ExecutablePath ?? ShortcutPath ?? ShortcutTarget ?? InstallLocation;
}

public enum IconSource
{
    None = 0,
    Shortcut = 1,
    Registry = 2,
    Executable = 3,
    AppX = 4,
    Default = 5,
}