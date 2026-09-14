using System.Collections.ObjectModel;
using System.IO;
using WindowsProgramInventory.Core;
using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.UI.ViewModels;

/// <summary>
/// Settings page: theme, startup behavior, report export, cache management and diagnostics.
/// Every destructive action asks for confirmation first and never touches data outside the app folder.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IThemeService _themeService;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogger _logger;
    private readonly IDialogService _dialogService;
    private readonly Action<string>? _notifyStatus;
    private readonly Action<ReportExportFormat>? _exportReport;
    private string _selectedTheme;
    private bool _refreshOnStartup;

    public SettingsViewModel(
        IThemeService themeService,
        ISettingsStore settingsStore,
        ILogger logger,
        IDialogService dialogService,
        Action<string>? notifyStatus = null,
        Action<ReportExportFormat>? exportReport = null)
    {
        _themeService = themeService;
        _settingsStore = settingsStore;
        _logger = logger;
        _dialogService = dialogService;
        _notifyStatus = notifyStatus;
        _exportReport = exportReport;

        Themes = new ObservableCollection<string>(themeService.AvailableThemes);
        _selectedTheme = settingsStore.Get<string>(SettingKeys.Theme) ?? themeService.CurrentTheme;
        if (!Themes.Contains(_selectedTheme))
        {
            _selectedTheme = themeService.CurrentTheme;
        }

        _refreshOnStartup = settingsStore.Get<bool>(SettingKeys.RefreshOnStartup);
        LogsPath = logger.LogDirectory;

        ShowLogsCommand = new RelayCommand(o => ShowLogs(), o => Directory.Exists(LogsPath));
        ExportPdfCommand = new RelayCommand(o => _exportReport?.Invoke(ReportExportFormat.Pdf));
        ExportMarkdownCommand = new RelayCommand(o => _exportReport?.Invoke(ReportExportFormat.Markdown));
        ClearIconCacheCommand = new RelayCommand(o => ClearIconCache());
        ClearInventoryCacheCommand = new RelayCommand(o => ClearInventoryCache());
        ClearAllDataCommand = new RelayCommand(o => ClearAllData());
    }

    public string Title { get; } = Strings.SettingsTitle;

    public ObservableCollection<string> Themes { get; }

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                _themeService.Apply(value);
                _settingsStore.Set(SettingKeys.Theme, value);
                _settingsStore.Save();
            }
        }
    }

    public bool RefreshOnStartup
    {
        get => _refreshOnStartup;
        set
        {
            if (SetProperty(ref _refreshOnStartup, value))
            {
                _settingsStore.Set(SettingKeys.RefreshOnStartup, value);
                _settingsStore.Save();
            }
        }
    }

    public string LogsPath { get; }

    public RelayCommand ShowLogsCommand { get; }
    public RelayCommand ExportPdfCommand { get; }
    public RelayCommand ExportMarkdownCommand { get; }
    public RelayCommand ClearIconCacheCommand { get; }
    public RelayCommand ClearInventoryCacheCommand { get; }
    public RelayCommand ClearAllDataCommand { get; }

    private void ShowLogs()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = LogsPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to open logs folder", ex);
        }
    }

    private void ClearIconCache()
    {
        if (Confirm(Strings.ConfirmClearIconCache, Strings.ClearIconCache))
        {
            DeleteDirectory(AppConstants.IconCachePath);
        }
    }

    private void ClearInventoryCache()
    {
        if (Confirm(Strings.ConfirmClearInventoryCache, Strings.ClearInventoryCache))
        {
            DeleteFile(AppConstants.DatabasePath);
        }
    }

    private void ClearAllData()
    {
        if (Confirm(Strings.ConfirmClearAllData, Strings.ClearAllData))
        {
            DeleteDirectory(AppConstants.LocalAppDataRoot);
        }
    }

    private bool Confirm(string message, string title) => _dialogService.Confirm(message, title);

    private void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                _notifyStatus?.Invoke(Strings.CacheCleared);
            }

            _logger.Info($"Deleted directory: {path}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to delete directory: {path}", ex);
            _dialogService.ShowError(Strings.OperationFailed, Strings.SettingsTitle);
        }
    }

    private void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            _notifyStatus?.Invoke(Strings.CacheCleared);
            _logger.Info($"Deleted file: {path}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to delete file: {path}", ex);
            _dialogService.ShowError(Strings.OperationFailed, Strings.SettingsTitle);
        }
    }
}

/// <summary>
/// Settings keys. Values live in the JSON settings store.
/// </summary>
public static class SettingKeys
{
    public const string Theme = "Theme";
    public const string RefreshOnStartup = "RefreshOnStartup";
}