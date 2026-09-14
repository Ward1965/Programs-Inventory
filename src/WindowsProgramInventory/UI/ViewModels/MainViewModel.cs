using System.Collections.ObjectModel;
using System.Diagnostics;
using WindowsProgramInventory.Core;
using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.Models;
using WindowsProgramInventory.Services.Export;

namespace WindowsProgramInventory.UI.ViewModels;

/// <summary>
/// Root view model of the main window. Owns navigation between pages,
/// the shared search state and the scan/cancel lifecycle.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly IScanEngine _scanEngine;
    private readonly IProgramRepository _repository;
    private readonly ILogger _logger;
    private CancellationTokenSource? _cts;

    private NavItem? _selectedNavItem;
    private object? _currentViewModel;
    private string _statusMessage = Strings.StatusReady;
    private string _progressStage = string.Empty;
    private int _progressPercent;
    private bool _isScanning;
    private string _lastScanSummary = string.Empty;

    public MainViewModel(
        IScanEngine scanEngine,
        IProgramRepository repository,
        ILogger logger,
        ProgramsViewModel programs,
        StatisticsViewModel statistics,
        SettingsViewModel settings)
    {
        _scanEngine = scanEngine;
        _repository = repository;
        _logger = logger;
        Programs = programs;
        Statistics = statistics;
        Settings = settings;

        NavItems = new ObservableCollection<NavItem>
        {
            new() { Key = NavigationKey.Home, Title = Strings.NavHome, Glyph = "\uE8F1" },
            new() { Key = NavigationKey.Installed, Title = Strings.NavInstalled, Glyph = "\uE73E" },
            new() { Key = NavigationKey.Shortcuts, Title = Strings.NavShortcuts, Glyph = "\uE71B" },
            new() { Key = NavigationKey.Broken, Title = Strings.NavBroken, Glyph = "\uE7BA" },
            new() { Key = NavigationKey.Store, Title = Strings.NavStore, Glyph = "\uE71D" },
            new() { Key = NavigationKey.Statistics, Title = Strings.NavStatistics, Glyph = "\uE9D2" },
            new() { Key = NavigationKey.Settings, Title = Strings.NavSettings, Glyph = "\uE713" },
        };

        Programs.StatusReporter = NotifyStatus;

        ScanCommand = new AsyncRelayCommand(ScanAsync, _ => !IsScanning, OnScanError);
        CancelScanCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsScanning);

        SelectedNavItem = NavItems[0];
    }

    public ObservableCollection<NavItem> NavItems { get; }

    public NavItem? SelectedNavItem
    {
        get => _selectedNavItem;
        set
        {
            if (SetProperty(ref _selectedNavItem, value))
            {
                TryActivate(value);
            }
        }
    }

    public object? CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public ProgramsViewModel Programs { get; }
    public StatisticsViewModel Statistics { get; }
    public SettingsViewModel Settings { get; }

    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand CancelScanCommand { get; }
    public RelayCommand LaunchProgramCommand => Programs.LaunchProgramCommand;
    public RelayCommand ToggleDetailsCommand => Programs.ToggleDetailsCommand;

    public string SearchText
    {
        get => Programs.SearchText;
        set => Programs.SearchText = value;
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ProgressStage
    {
        get => _progressStage;
        private set => SetProperty(ref _progressStage, value);
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public string LastScanSummary
    {
        get => _lastScanSummary;
        private set => SetProperty(ref _lastScanSummary, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                ScanCommand.RaiseCanExecuteChanged();
                CancelScanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotScanning => !IsScanning;

    /// <summary>Loads cached inventory (fast startup). Refresh triggers a new scan.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        StatusMessage = Strings.LoadingCached;
        try
        {
            var cached = await _repository.LoadAllAsync(cancellationToken);
            Programs.ReplaceAll(cached);
            StatusMessage = cached.Count > 0
                ? string.Format(Strings.ProgramsFound, cached.Count)
                : Strings.StatusReady;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Loading cached inventory failed: {ex.Message}");
            StatusMessage = Strings.StatusReady;
        }
    }

    public void RescanIndividual(ProgramInfo program)
    {
        _logger.Info($"Single-program rescan requested (Phase 1 placeholder): {program.Name}");
        StatusMessage = Strings.ScanEnginePhase1;
    }

    public void NotifyStatus(string message) => StatusMessage = message;

    /// <summary>Exports the current inventory to a PDF or Markdown report via a save dialog.</summary>
    public void ExportReport(ReportExportFormat format)
    {
        try
        {
            var snapshot = _repository.LoadAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (snapshot.Count == 0)
            {
                StatusMessage = Strings.NoScanData;
                return;
            }

            var isPdf = format == ReportExportFormat.Pdf;
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = isPdf ? Strings.ExportPdf : Strings.ExportMarkdown,
                DefaultExt = isPdf ? "pdf" : "md",
                FileName = $"program-inventory-{DateTime.Now:yyyyMMdd-HHmm}",
                Filter = isPdf ? "PDF document (*.pdf)|*.pdf" : "Markdown (*.md)|*.md|All files (*.*)|*.*",
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var targetPath = dialog.FileName;
            _ = Task.Run(() =>
            {
                try
                {
                    var writtenTo = isPdf
                        ? ReportExporter.ExportPdf(snapshot, targetPath)
                        : ReportExporter.ExportMarkdown(snapshot, targetPath);

                    _logger.Info($"Report exported: {writtenTo}");
                    NotifyStatus(string.Format(Strings.ReportSaved, System.IO.Path.GetFileName(writtenTo)));
                }
                catch (Exception ex)
                {
                    _logger.Error("Report export failed", ex);
                    NotifyStatus(Strings.OperationFailed);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Report export failed", ex);
            StatusMessage = Strings.OperationFailed;
        }
    }

    private void TryActivate(NavItem? item)
    {
        if (item is null)
        {
            return;
        }

        switch (item.Key)
        {
            case NavigationKey.Statistics:
                CurrentViewModel = Statistics;
                return;
            case NavigationKey.Settings:
                CurrentViewModel = Settings;
                return;
            default:
                Programs.ActiveFilter = item.Key;
                CurrentViewModel = Programs;
                break;
        }
    }

    private async Task ScanAsync(CancellationToken token)
    {
        _cts = new CancellationTokenSource();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _cts.Token).Token;

        IsScanning = true;
        OnPropertyChanged(nameof(IsNotScanning));
        StatusMessage = Strings.Scanning;
        LastScanSummary = string.Empty;
        ProgressPercent = 0;

        var stopwatch = Stopwatch.StartNew();
        var progress = new Progress<ScanProgress>(p =>
        {
            ProgressStage = p.Stage;
            ProgressPercent = p.Percent;
        });

        try
        {
            var result = await _scanEngine.ScanAsync(progress, linked);

            await _repository.SaveAsync(result.Programs, linked);
            Programs.ReplaceAll(result.Programs);
            Statistics.UpdateFrom(result);

            stopwatch.Stop();
            LastScanSummary = string.Format(
                Strings.ScanCompletedVerbose,
                result.Programs.Count,
                result.InstalledCount,
                result.BrokenCount,
                stopwatch.Elapsed.TotalSeconds);
            StatusMessage = string.Format(Strings.ProgramsFound, result.Programs.Count);
            _logger.Info(LastScanSummary);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Strings.ScanCancelled;
            _logger.Info("Scan was cancelled by the user.");
        }
        finally
        {
            ProgressStage = string.Empty;
            IsScanning = false;
            OnPropertyChanged(nameof(IsNotScanning));
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnScanError(Exception ex)
    {
        _logger.Error("Scan failed", ex);
        StatusMessage = $"Scan failed: {ex.Message}";
    }
}