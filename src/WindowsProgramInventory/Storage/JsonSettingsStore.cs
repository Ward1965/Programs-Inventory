using System.IO;
using System.Text.Json;
using WindowsProgramInventory.Core.Interfaces;

namespace WindowsProgramInventory.Storage;

/// <summary>
/// JSON-backed settings store at %LOCALAPPDATA%\WindowsProgramInventory\settings.json.
/// Values are stored as JSON strings keyed by name. Zero dependencies (System.Text.Json).
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly Dictionary<string, string> _values = new();

    public JsonSettingsStore(string? path = null)
    {
        _path = path ?? Core.AppConstants.SettingsPath;

        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (loaded is not null)
                {
                    foreach (var (key, element) in loaded)
                    {
                        // String values mirror the in-memory dictionary semantics (unquoted content);
                        // raw primitives (e.g. older files with "true" instead of "\"true\"") are kept as literals.
                        _values[key] = element.ValueKind == JsonValueKind.String
                            ? element.GetString() ?? string.Empty
                            : element.GetRawText();
                    }
                }
            }
        }
        catch
        {
            // Corrupt settings file is not fatal: start fresh.
        }
    }

    public T? Get<T>(string key)
    {
        if (!_values.TryGetValue(key, out var raw))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(raw);
        }
        catch
        {
            return default;
        }
    }

    public void Set<T>(string key, T value)
    {
        _values[key] = JsonSerializer.Serialize(value);
    }

    public bool Contains(string key) => _values.ContainsKey(key);

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Non-critical; keep running.
        }
    }
}