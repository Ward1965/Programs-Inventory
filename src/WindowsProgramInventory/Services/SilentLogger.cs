using System.IO;
using WindowsProgramInventory.Core.Interfaces;

namespace WindowsProgramInventory.Services;

/// <summary>No-op logger used when a service is constructed without explicit dependencies.</summary>
internal sealed class SilentLogger : ILogger
{
    public string LogDirectory => Path.GetTempPath();
    public void Write(LogLevel level, string message, Exception? exception = null) { }
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
}