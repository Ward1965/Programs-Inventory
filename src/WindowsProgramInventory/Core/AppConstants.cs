using System.IO;

namespace WindowsProgramInventory.Core;

/// <summary>
/// Static application-wide constants. Kept in one place so they stay consistent.
/// </summary>
public static class AppConstants
{
    public const string AppName = "Windows Program Inventory";
    public const string AppVersion = "1.0.0";

    public static readonly string LocalAppDataRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsProgramInventory");

    public static readonly string IconCachePath = Path.Combine(LocalAppDataRoot, "IconCache");
    public static readonly string DatabasePath = Path.Combine(LocalAppDataRoot, "inventory.db");
    public static readonly string LogsPath = Path.Combine(LocalAppDataRoot, "Logs");
    public static readonly string SettingsPath = Path.Combine(LocalAppDataRoot, "settings.json");

    /// <summary>Start menu shortcuts, per-user.</summary>
    public static readonly string UserStartMenuPath =
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);

    /// <summary>Start menu shortcuts, all users.</summary>
    public static readonly string CommonStartMenuPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
}