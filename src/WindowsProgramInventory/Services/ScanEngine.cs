using System.Diagnostics;
using WindowsProgramInventory.Core;
using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Services;

/// <summary>
/// Orchestrates the whole discovery pipeline:
/// Start Menu → Registry → AppX → shortcut analysis → executable analysis → identity merge →
/// classification → icon extraction. Async, cancelable and progress-aware; every individual
/// failure is recorded while the scan continues.
///
/// Performance contract: independent sources are scanned in parallel, CPU-bound stages run on
/// bounded worker pools and progress is throttled so the UI thread is never flooded. No stage
/// ever blocks the UI thread (the pipeline is deliberately resumed off the UI SynchronizationContext).
/// </summary>
public sealed class ScanEngine : IScanEngine
{
    /// <summary>High-water mark so every icon keeps its best native resolution; cheap to display downscaled.</summary>
    public const int IconExtractionSize = 256;

    private const int AnalysisParallelism = 8;
    private const int IconParallelism = 4;

    private readonly IReadOnlyList<IProgramSourceScanner> _scanners;
    private readonly IShortcutAnalyzer _shortcutAnalyzer;
    private readonly IExecutableAnalyzer _executableAnalyzer;
    private readonly IProgramIdentityResolver _identityResolver;
    private readonly IClassificationEngine _classificationEngine;
    private readonly IIconExtractor _iconExtractor;
    private readonly ILogger _logger;

    public ScanEngine(
        IEnumerable<IProgramSourceScanner> scanners,
        ILogger? logger = null,
        IProgramIdentityResolver? identityResolver = null,
        IClassificationEngine? classificationEngine = null,
        IIconExtractor? iconExtractor = null,
        IShortcutAnalyzer? shortcutAnalyzer = null,
        IExecutableAnalyzer? executableAnalyzer = null)
    {
        _scanners = scanners.ToArray();
        _logger = logger ?? new SilentLogger();
        _identityResolver = identityResolver ?? new IdentityResolver();
        _classificationEngine = classificationEngine ?? new ClassificationEngine();
        _iconExtractor = iconExtractor ?? new Icons.IconExtractor(_logger);
        _shortcutAnalyzer = shortcutAnalyzer ?? new ShortcutAnalyzer(_logger);
        _executableAnalyzer = executableAnalyzer ?? new ExecutableAnalyzer(_logger);
    }

    public async Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var errors = 0;
        var sw = Stopwatch.StartNew();
        var progressReporter = new ThrottledProgressReporter(progress);

        // Truly asynchronous start: the UI can reflect the scanning state and honor an early
        // cancel before any heavy work. ConfigureAwait(false) keeps the pipeline off the UI thread.
        try
        {
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        progressReporter.Report(Strings.DiscoveringStartMenu, 4, 0);

        // ── 1. Discovery: independent sources run in parallel ──
        var scanTasks = _scanners.Select(async scanner =>
        {
            try
            {
                return (scanner, Items: await scanner.ScanAsync(cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref errors);
                _logger.Error($"Scanner failed: {scanner.SourceName}", ex);
                return (scanner, Items: (IReadOnlyList<ProgramInfo>)Array.Empty<ProgramInfo>());
            }
        }).ToArray();

        var scanResults = await Task.WhenAll(scanTasks);
        cancellationToken.ThrowIfCancellationRequested();

        var discovered = scanResults.SelectMany(r => r.Items).ToList();
        progressReporter.Report(Strings.ScanningInstalledPrograms, 10, discovered.Count);

        // ── 2. Shortcut analysis (bounded parallel) ────────────
        progressReporter.Report(Strings.AnalyzingShortcuts, 25, discovered.Count);
        Parallel.ForEach(discovered, AnalysisOptions(cancellationToken), item =>
        {
            if (item.ShortcutPath is not null)
            {
                Safe(item, () => _shortcutAnalyzer.Analyze(item));
            }
        });

        // ── 3. Executable metadata (bounded parallel) ──────────
        cancellationToken.ThrowIfCancellationRequested();
        progressReporter.Report(Strings.ScanningInstalledPrograms, 45, discovered.Count);
        Parallel.ForEach(discovered, AnalysisOptions(cancellationToken), item =>
        {
            if (item.ExecutablePath is not null)
            {
                Safe(item, () => _executableAnalyzer.Analyze(item));
            }
        });

        // ── 4. Duplicate resolution ───────────────────────────
        cancellationToken.ThrowIfCancellationRequested();
        progressReporter.Report(Strings.ResolvingDuplicates, 62, discovered.Count);
        var merged = _identityResolver.Merge(discovered).ToList();

        // ── 5. Classification ─────────────────────────────────
        cancellationToken.ThrowIfCancellationRequested();
        progressReporter.Report(Strings.ResolvingDuplicates, 70, merged.Count);
        foreach (var program in merged)
        {
            _classificationEngine.Classify(program);
        }

        // ── 6. Icon extraction (cached; bounded to avoid I/O thrash) ──
        cancellationToken.ThrowIfCancellationRequested();
        progressReporter.Report(Strings.ExtractingIcons, 78, merged.Count);
        var completedIcons = 0;
        Parallel.ForEach(merged, IconOptions(cancellationToken), (program, _) =>
        {
            var iconTask = Safe(program, () => _iconExtractor.GetCachedIconAsync(program, IconExtractionSize, cancellationToken));
            if (iconTask is not null && iconTask.GetAwaiter().GetResult() is string file)
            {
                program.IconCachePath = file;
            }

            var current = Interlocked.Increment(ref completedIcons);
            progressReporter.Report(Strings.ExtractingIcons, 78 + 20 * current / Math.Max(1, merged.Count), merged.Count);
        });

        sw.Stop();
        progressReporter.Flush(Strings.ScanCompleted, 100, merged.Count);

        return new ScanResult
        {
            Programs = merged,
            Duration = sw.Elapsed,
            ErrorCount = errors,
        };
    }

    private static ParallelOptions AnalysisOptions(CancellationToken cancellationToken)
        => new() { MaxDegreeOfParallelism = AnalysisParallelism, CancellationToken = cancellationToken };

    private static ParallelOptions IconOptions(CancellationToken cancellationToken)
        => new() { MaxDegreeOfParallelism = IconParallelism, CancellationToken = cancellationToken };

    private void Safe(ProgramInfo program, Action action)
    {
        try
        {
            action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to process '{program.Name}': {ex.Message}");
        }
    }

    private T? Safe<T>(ProgramInfo program, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to process '{program.Name}': {ex.Message}");
            return default;
        }
    }
}

/// <summary>
/// Postpones progress callbacks: at most one per interval except the final 100% flush,
/// so thousands of parallel completion events never swamp the UI dispatcher.
/// </summary>
internal sealed class ThrottledProgressReporter
{
    private const long MinIntervalMs = 66;

    private readonly IProgress<ScanProgress>? _progress;
    private readonly object _gate = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private long _lastEmitMs;
    private int _lastPercent = -1;
    private int _lastFound = -1;

    public ThrottledProgressReporter(IProgress<ScanProgress>? progress)
    {
        _progress = progress;
    }

    public void Report(string stage, int percent, int found)
    {
        if (_progress is null)
        {
            return;
        }

        percent = Math.Clamp(percent, 0, 100);
        lock (_gate)
        {
            var now = _stopwatch.ElapsedMilliseconds;
            if (percent == _lastPercent && found == _lastFound)
            {
                return;
            }

            if (now - _lastEmitMs < MinIntervalMs && percent < 100)
            {
                return;
            }

            _lastEmitMs = now;
            _lastPercent = percent;
            _lastFound = found;
            _progress.Report(new ScanProgress { Stage = stage, Percent = percent, ProgramsFound = found });
        }
    }

    /// <summary>Unconditionally emits a final report (used for the 100% completion state).</summary>
    public void Flush(string stage, int percent, int found)
    {
        if (_progress is null)
        {
            return;
        }

        lock (_gate)
        {
            percent = Math.Clamp(percent, 0, 100);
            _lastEmitMs = _stopwatch.ElapsedMilliseconds;
            _lastPercent = percent;
            _lastFound = found;
            _progress.Report(new ScanProgress { Stage = stage, Percent = percent, ProgramsFound = found });
        }
    }
}