namespace WindowsProgramInventory.Core.Interfaces;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>
/// Minimal diagnostic logger writing to %LOCALAPPDATA%\WindowsProgramInventory\Logs.
/// Never shown to regular users; exposed under Settings → Diagnostics.
/// </summary>
public interface ILogger
{
    void Write(LogLevel level, string message, Exception? exception = null);
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);

    /// <summary>Path of the current log file, for Diagnostics.</summary>
    string LogDirectory { get; }
}