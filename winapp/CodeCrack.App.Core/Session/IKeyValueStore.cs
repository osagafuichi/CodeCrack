namespace CodeCrack.App.Core.Session;

/// Minimal string/string-array persistence (the Windows analog of UserDefaults). Backed by a
/// JSON file in the app; faked in-memory for tests. Used by RecentFiles, SessionStore, WindowFrame.
public interface IKeyValueStore
{
    string? GetString(string key);
    void SetString(string key, string value);
    string[]? GetStringArray(string key);
    void SetStringArray(string key, string[] value);
    void Remove(string key);
}
