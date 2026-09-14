namespace WindowsProgramInventory.Core;

/// <summary>
/// Centralized UI strings. Localization / RTL support will be layered on top of this
/// without touching XAML: replace this static source with a resource-backed provider later.
/// </summary>
public static class Strings
{
    // Window / brand
    public const string AppTitle = "Windows Program Inventory";
    public const string Subtitle = "Windows App Inventory Explorer";

    // Sidebar navigation
    public const string NavHome = "All Programs";
    public const string NavInstalled = "Installed";
    public const string NavShortcuts = "Shortcuts";
    public const string NavBroken = "Broken";
    public const string NavStore = "Store Apps";
    public const string NavStatistics = "Statistics";
    public const string NavSettings = "Settings";

    // Header
    public const string SearchPlaceholder = "Search programs...";
    public const string ProgramsFound = "{0} Programs";

    // Empty / first-run state
    public const string NoScanData = "No scan data found.";
    public const string EmptyHint = "Run a scan to discover the programs on this device.";
    public const string ScanNow = "Scan Now";
    public const string Scan = "Scan";
    public const string Refresh = "Refresh";
    public const string CancelScan = "Cancel Scan";

    // Scan progress
    public const string Scanning = "Scanning…";
    public const string DiscoveringStartMenu = "Discovering Start Menu…";
    public const string ScanningInstalledPrograms = "Scanning installed programs…";
    public const string ScanningStoreApps = "Scanning Store Apps…";
    public const string AnalyzingShortcuts = "Analyzing shortcuts…";
    public const string ExtractingIcons = "Extracting icons…";
    public const string ResolvingDuplicates = "Resolving duplicates…";
    public const string ScanCompleted = "Scan completed";
    public const string ScanTimeSeconds = "Scan time: {0:F1} seconds";
    public const string ScanCancelled = "Scan cancelled";
    public const string ScanCompletedVerbose = "{0} programs discovered · {1} installed · {2} broken · {3:F1}s";
    public const string LoadingCached = "Loading cached inventory…";
    public const string ScanEnginePhase1 = "Scanner engine starts in Phase 2.";

    // Settings
    public const string SettingsTitle = "Settings";
    public const string SettingsAppearance = "Appearance";
    public const string SettingsStartup = "Startup";
    public const string SettingsTheme = "Theme";
    public const string SettingsThemeLight = "Light";
    public const string SettingsThemeDark = "Dark";
    public const string SettingsThemeSystem = "System";
    public const string SettingsIconSize = "Icon Size";
    public const string ViewCards = "Cards";
    public const string ViewList = "List";
    public const string SettingsSmall = "Small";
    public const string SettingsMedium = "Medium";
    public const string SettingsLarge = "Large";
    public const string SettingsExtraLarge = "Extra Large";
    public const string SettingsRefreshOnStartup = "Refresh inventory on startup";
    public const string SettingsCache = "Cache";
    public const string SettingsReport = "Report";
    public const string ClearIconCache = "Clear Icon Cache";
    public const string ClearInventoryCache = "Clear Inventory Cache";
    public const string ClearAllData = "Clear All Data";
    public const string ExportPdf = "Export as PDF";
    public const string ExportMarkdown = "Export as Markdown";
    public const string ReportSaved = "Report saved: {0}";
    public const string Diagnostics = "Diagnostics";
    public const string PlaceholderComingSoon = "This will become available in a later phase.";
    public const string ShowLogs = "Show Logs";

    // Statistics
    public const string StatisticsTitle = "Statistics";
    public const string StatTotal = "Total Programs";
    public const string StatInstalled = "Installed";
    public const string StatShortcutsOnly = "Shortcut Only";
    public const string StatBroken = "Broken Shortcuts";
    public const string StatStoreApps = "Store Apps";
    public const string StatPublishers = "Publishers";

    // Misc
    public const string StatusReady = "Ready";
    public const string StatusIdle = "Idle";
    public const string Unknown = "Unknown";
    public const string NotAvailable = "N/A";

    // Exit / dialogue
    public const string ShowDetails = "Details";
    public const string HideDetails = "Hide";
    public const string RunProgram = "Run program";
    public const string Launched = "Launched {0}";
    public const string CannotLaunch = "Cannot launch {0}";

    // Cache / dialogs
    public const string ConfirmClearIconCache = "Delete all extracted icons from the local cache?";
    public const string ConfirmClearInventoryCache = "Delete the cached inventory database?";
    public const string ConfirmClearAllData = "Delete ALL application data (inventory, icons, settings, logs)? This cannot be undone.";
    public const string CacheCleared = "Cache cleared";
    public const string OperationFailed = "Operation failed";
}