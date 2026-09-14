using WindowsProgramInventory.Core.Interfaces;

namespace WindowsProgramInventory.Tests;

public sealed class FakeLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public string LogDirectory => System.IO.Path.GetTempPath();

    public void Write(LogLevel level, string message, Exception? exception = null)
        => Entries.Add((level, message));

    public void Info(string message) => Write(LogLevel.Info, message);
    public void Warning(string message) => Write(LogLevel.Warning, message);
    public void Error(string message, Exception? exception = null) => Write(LogLevel.Error, message);
}

public sealed class FakeSettingsStore : ISettingsStore
{
    public Dictionary<string, string> Values { get; } = new();
    public int SaveCount { get; private set; }

    public T? Get<T>(string key)
    {
        if (!Values.TryGetValue(key, out var raw))
        {
            return default;
        }

        return System.Text.Json.JsonSerializer.Deserialize<T>(raw);
    }

    public void Set<T>(string key, T value) => Values[key] = System.Text.Json.JsonSerializer.Serialize(value);
    public bool Contains(string key) => Values.ContainsKey(key);
    public void Save() => SaveCount++;
}

public sealed class FakeThemeService : IThemeService
{
    public List<string> Applied { get; } = new();

    public IReadOnlyList<string> AvailableThemes { get; } = new[] { "System", "Light", "Dark" };
    public string CurrentTheme { get; private set; } = "System";

    public void Apply(string theme)
    {
        CurrentTheme = theme;
        Applied.Add(theme);
    }
}

public sealed class FakeDialogService : IDialogService
{
    public bool ConfirmResult { get; set; }
    public List<string> Confirmed { get; } = new();
    public List<string> Errors { get; } = new();

    public bool Confirm(string message, string title)
    {
        Confirmed.Add(title);
        return ConfirmResult;
    }

    public void ShowInfo(string message, string title) { }
    public void ShowError(string message, string title) => Errors.Add(message);
}