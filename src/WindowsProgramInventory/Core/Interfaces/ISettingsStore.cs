namespace WindowsProgramInventory.Core.Interfaces;

/// <summary>
/// Simple JSON-backed settings store. No external dependencies, pure IO.
/// </summary>
public interface ISettingsStore
{
    T? Get<T>(string key);
    void Set<T>(string key, T value);
    bool Contains(string key);
    void Save();
}