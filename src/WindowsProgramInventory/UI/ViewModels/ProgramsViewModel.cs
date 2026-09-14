using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using WindowsProgramInventory.Core;
using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.UI.ViewModels;

/// <summary>
/// Holds the collected inventory for the program grid together with search/filter/view-state.
/// Rendering is a sliding viewport: only the rows around the current scroll position are
/// materialized, so switching groups, resizing icons and scrolling large inventories stay fluid.
/// </summary>
public sealed class ProgramsViewModel : ViewModelBase
{
    // Standard Windows icon sizes (16, 20, 24, 32, 40, 48, 64, 96, 128, 192, 256).
    private static readonly int[] StandardIconSizes =
    {
        16, 20, 24, 32, 40, 48, 64, 96, 128, 192, 256,
    };

    public const int ColumnWidth = 198;       // 190px card + 8px margins
    private const int PadRows = 2;             // extra screens kept above/below the viewport
    private const double ListRowHeight = 46;   // compact single-row list cells

    // Content below the icon: icon box bottom margin (10) + name (34) + publisher (18) + status badge (30)
    // + details button (34) + card vertical padding (28) + safety slack.
    private const int CardInfoHeight = 164;

    private readonly List<ProgramInfo> _all = new();
    private List<ProgramInfo> _pool = new();
    private int _windowStart;
    private string _searchText = string.Empty;
    private NavigationKey _activeFilter = NavigationKey.Home;
    private int _iconSize = ViewMode.Medium.IconSize();
    private int _iconSizePreview = 48;
    private int _itemsPerRow = 4;
    private double _viewportHeight = 600;
    private double _viewportWidth = 1200;
    private bool _isListView;
    private double _estimatedContentHeight;
    private double _contentOffsetY;

    public ProgramsViewModel()
    {
        LaunchProgramCommand = new RelayCommand(LaunchProgram);
        ToggleDetailsCommand = new RelayCommand(p =>
        {
            if (p is ProgramInfo program)
            {
                program.IsDetailsOpen = !program.IsDetailsOpen;
            }
        });
    }

    /// <summary>Visible slice of the filtered pool (sliding viewport).</summary>
    public ObservableCollection<ProgramInfo> Programs { get; } = new();

    public int TotalDiscoverable { get; private set; }

    /// <summary>Reports status messages (scan/launch results) to the main window.</summary>
    public Action<string>? StatusReporter { get; set; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public NavigationKey ActiveFilter
    {
        get => _activeFilter;
        set
        {
            if (SetProperty(ref _activeFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>Smallest supported tile size.</summary>
    public int IconSizeMin => 16;

    /// <summary>Largest supported tile size (icons keep their best native resolution up to this).</summary>
    public int IconSizeMax => 256;

    /// <summary>Known/snap points for the icon size slider (standard Windows sizes).</summary>
    public IReadOnlyList<int> IconSizeSteps => StandardIconSizes;

    public int IconSize
    {
        get => _iconSize;
        set
        {
            var snapped = SnapToStandardSize(value);
            if (SetProperty(ref _iconSize, snapped))
            {
                IconSizePreview = snapped;
                OnPropertyChanged(nameof(CardHeight));
                OnPropertyChanged(nameof(RowHeight));
                UpdateEstimate();
                RebuildWindow(_windowStart);
            }
        }
    }

    /// <summary>
    /// Compact single-row mode (one program per line, full width) vs the card grid.
    /// </summary>
    public bool IsListView
    {
        get => _isListView;
        set
        {
            if (SetProperty(ref _isListView, value))
            {
                _itemsPerRow = value
                    ? 1
                    : Math.Max(1, (int)Math.Floor((_viewportWidth - 8) / (double)ColumnWidth));
                OnPropertyChanged(nameof(IsCardsView));
                OnPropertyChanged(nameof(ItemWidth));
                OnPropertyChanged(nameof(RowHeight));
                OnPropertyChanged(nameof(ItemsAreaWidth));
                UpdateEstimate();
                RebuildWindow(_windowStart);
            }
        }
    }

    public bool IsCardsView => !IsListView;

    /// <summary>Instant preview size; the grid will keep actually-extracted icons to this floor.</summary>
    public int IconSizePreview
    {
        get => _iconSizePreview;
        set => SetProperty(ref _iconSizePreview, value);
    }

    /// <summary>
    /// Uniform card height that truly fits the whole card content (icon + name + publisher + badge +
    /// details button), so nothing is ever clipped and the list visually ends at the last card.
    /// </summary>
    public int CardHeight => Math.Max(IconSize, 32) + CardInfoHeight;

    public bool IsEmpty => Programs.Count == 0;

    /// <summary>Total matches in the current search + filter (not just the rendered viewport).</summary>
    public int Count => _pool.Count;

    public string CountLabel => string.Format(Core.Strings.ProgramsFound, Count);

    public string EmptyTitle => _all.Count == 0 ? Strings.NoScanData : Strings.EmptyHint;

    /// <summary>Exact wrap width for the realized grid (matches row math, no phantom rows/tail).</summary>
    public double ItemsAreaWidth => IsListView ? _viewportWidth : _itemsPerRow * (double)ColumnWidth;

    /// <summary>Single wrap-cell width: 198px tiles in card mode, the full width in list mode.</summary>
    public double ItemWidth => IsListView ? ItemsAreaWidth : ColumnWidth;

    /// <summary>Estimated full-content height (provides the scroll extent for the whole pool).</summary>
    public double EstimatedContentHeight
    {
        get => _estimatedContentHeight;
        private set => SetProperty(ref _estimatedContentHeight, value);
    }

    /// <summary>Offsets the realized viewport so it matches the current scroll position.</summary>
    public double ContentOffsetY
    {
        get => _contentOffsetY;
        private set => SetProperty(ref _contentOffsetY, value);
    }

    public RelayCommand LaunchProgramCommand { get; }
    public RelayCommand ToggleDetailsCommand { get; }

    /// <summary>Exact row pitch used by the wrap panel (one row cell, gap included).</summary>
    public double RowHeight => IsListView ? ListRowHeight : CardHeight + 8;

    public void SetViewMode(ViewMode mode)
    {
        IconSize = mode.IconSize();
        ViewMode = mode;
    }

    public ViewMode ViewMode { get; private set; } = ViewMode.Medium;

    public void ReplaceAll(IEnumerable<ProgramInfo> programs)
    {
        _all.Clear();
        _all.AddRange(programs);
        TotalDiscoverable = _all.Count;
        ApplyFilter();
        OnPropertyChanged(nameof(EmptyTitle));
    }

    public void SetEmpty()
    {
        _all.Clear();
        TotalDiscoverable = 0;
        ApplyFilter();
    }

    /// <summary>Updates layout inputs (viewport size) and re-anchors the window.</summary>
    public void UpdateViewport(double width, double height)
    {
        var changed = false;
        if (width > 0)
        {
            _viewportWidth = width;
            if (IsListView)
            {
                if (_itemsPerRow != 1)
                {
                    _itemsPerRow = 1;
                    changed = true;
                }
            }
            else
            {
                var columns = Math.Max(1, (int)Math.Floor((width - 8) / (double)ColumnWidth));
                if (columns != _itemsPerRow)
                {
                    _itemsPerRow = columns;
                    changed = true;
                }
            }
        }

        if (height > 0)
        {
            _viewportHeight = height;
        }

        if (changed)
        {
            OnPropertyChanged(nameof(ItemsAreaWidth));
            OnPropertyChanged(nameof(ItemWidth));
            UpdateEstimate();
            RebuildWindow(_windowStart);
        }
    }

    /// <summary>Called on scroll: slides the viewport only when the user leaves the realized area.</summary>
    public void OnScroll(double offset, double viewportHeight, double extentHeight)
    {
        if (viewportHeight > 0)
        {
            _viewportHeight = viewportHeight;
        }

        var rowHeight = RowHeight;
        if (rowHeight <= 0 || _itemsPerRow <= 0)
        {
            return;
        }

        var maxRows = Math.Max(1, (int)Math.Ceiling(_pool.Count / (double)_itemsPerRow));
        var topRow = Math.Max(0, Math.Min(maxRows - 1, (int)Math.Floor(offset / rowHeight)));
        var topIndex = topRow * _itemsPerRow;

        if (Math.Abs(topIndex - _windowStart) >= 2 * PadRows * _itemsPerRow)
        {
            var start = Math.Max(0, topIndex - PadRows * _itemsPerRow);
            RebuildWindow(start);
        }
    }

    private void ApplyFilter()
    {
        var query = _searchText?.Trim() ?? string.Empty;
        _pool = _all.Where(p => MatchesQuery(p, query) && MatchesFilter(p, _activeFilter)).ToList();
        _windowStart = 0;
        UpdateEstimate();
        RebuildWindow(0);
        OnCollectionChanged();
        OnPropertyChanged(nameof(EmptyTitle));
    }

    private void RebuildWindow(int start)
    {
        _windowStart = Math.Max(0, Math.Min(Math.Max(0, _pool.Count - 1), start));

        var rowHeight = RowHeight;
        var rows = Math.Max(2, (int)Math.Ceiling(_viewportHeight / rowHeight) + 2 * PadRows);
        var count = Math.Min(_pool.Count - _windowStart, _itemsPerRow * rows);

        Programs.CollectionChanged -= OnProgramsCollectionChanged;
        Programs.Clear();
        for (var i = 0; i < count; i++)
        {
            Programs.Add(_pool[_windowStart + i]);
        }

        Programs.CollectionChanged += OnProgramsCollectionChanged;

        ContentOffsetY = -(_windowStart / (double)_itemsPerRow) * rowHeight;
        OnCollectionChanged();
    }

    private void UpdateEstimate()
    {
        var rows = (int)Math.Ceiling(_pool.Count / (double)Math.Max(1, _itemsPerRow));
        EstimatedContentHeight = Math.Max(0, rows * RowHeight);
    }

    private static int SnapToStandardSize(int value)
    {
        var best = value;
        var bestDistance = int.MaxValue;
        foreach (var size in StandardIconSizes)
        {
            var distance = Math.Abs(size - value);
            if (distance < bestDistance || (distance == bestDistance && size > best))
            {
                bestDistance = distance;
                best = size;
            }
        }

        return best;
    }

    private static bool MatchesQuery(ProgramInfo p, string query)
    {
        if (query.Length == 0)
        {
            return true;
        }

        return Contains(p.Name, query)
            || Contains(p.DisplayName, query)
            || Contains(p.Publisher, query)
            || Contains(p.Version, query)
            || Contains(p.ExecutablePath, query)
            || Contains(p.InstallLocation, query)
            || Contains(p.ShortcutPath, query)
            || Contains(p.ShortcutTarget, query)
            || Contains(p.PackageName, query)
            || Contains(p.PackageFamilyName, query);

        static bool Contains(string? value, string q)
            => value is not null && value.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesFilter(ProgramInfo p, NavigationKey filter) => filter switch
    {
        NavigationKey.Installed => p.IsInstalled,
        NavigationKey.Shortcuts => p.IsShortcut,
        NavigationKey.Broken => p.IsBrokenShortcut,
        NavigationKey.Store => p.IsStoreApp,
        _ => true,
    };

    private void OnProgramsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnCollectionChanged();

    private void OnCollectionChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(CountLabel));
    }

    private void LaunchProgram(object? parameter)
    {
        if (parameter is not ProgramInfo program)
        {
            return;
        }

        try
        {
            var startInfo = BuildStartInfo(program);
            if (startInfo is null)
            {
                StatusReporter?.Invoke(string.Format(Strings.CannotLaunch, program.Name));
                return;
            }

            Process.Start(startInfo);
            StatusReporter?.Invoke(string.Format(Strings.Launched, program.Name));
        }
        catch (Exception)
        {
            StatusReporter?.Invoke(string.Format(Strings.CannotLaunch, program.Name));
        }
    }

    private static ProcessStartInfo? BuildStartInfo(ProgramInfo program)
    {
        // Prefer the shortcut itself so arguments / working directory survive.
        if (program.ShortcutPath is { Length: > 0 } shortcut && IsShellLaunchable(shortcut))
        {
            return new ProcessStartInfo(shortcut) { UseShellExecute = true };
        }

        // Store (MSIX) apps launch through the Apps Folder with their AUMID.
        if (program.IsStoreApp && program.AppUserModelId is { Length: > 0 } appUserModelId)
        {
            return new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{appUserModelId}") { UseShellExecute = true };
        }

        if (program.ExecutablePath is { Length: > 0 } executable && File.Exists(executable))
        {
            return new ProcessStartInfo(executable) { UseShellExecute = true };
        }

        if (program.ShortcutTarget is { Length: > 0 } target)
        {
            return new ProcessStartInfo(target) { UseShellExecute = true };
        }

        if (program.InstallLocation is { Length: > 0 } directory && Directory.Exists(directory))
        {
            return new ProcessStartInfo(directory) { UseShellExecute = true };
        }

        return null;
    }

    private static bool IsShellLaunchable(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".lnk" or ".exe" or ".url" or ".bat" or ".cmd";
    }
}