using System.Globalization;
using System.IO;
using System.Text;
using WindowsProgramInventory.Core;
using WindowsProgramInventory.Core.Interfaces;

namespace WindowsProgramInventory.Services.Logging;

/// <summary>
/// Plain-text file logger. One file per day inside %LOCALAPPDATA%\WindowsProgramInventory\Logs.
/// Format: [LEVEL][time] message | exception.
/// </summary>
public sealed class FileLogger : ILogger
{
    private readonly object _gate = new();
    private readonly string _dailyFile;

    public FileLogger(string? logDirectory = null)
    {
        LogDirectory = logDirectory ?? AppConstants.LogsPath;

        try
        {
            Directory.CreateDirectory(LogDirectory);
        }
        catch
        {
            LogDirectory = Path.GetTempPath();
        }

        _dailyFile = Path.Combine(LogDirectory, $"wpi-{DateTime.Now:yyyyMMdd}.log");
    }

    public string LogDirectory { get; }

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(level.ToString().ToUpperInvariant().PadRight(7)).Append("] ");
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        sb.Append("  ").Append(message);
        if (exception is not null)
        {
            sb.AppendLine().Append("    ").Append(exception);
        }

        try
        {
            lock (_gate)
            {
                File.AppendAllText(_dailyFile, sb.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    public void Info(string message) => Write(LogLevel.Info, message);
    public void Warning(string message) => Write(LogLevel.Warning, message);
    public void Error(string message, Exception? exception = null) => Write(LogLevel.Error, message, exception);
}