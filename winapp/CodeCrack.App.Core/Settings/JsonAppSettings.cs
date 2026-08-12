using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeCrack.App.Core.Settings;

/// IAppSettings persisted as JSON under %APPDATA%\CodeCrack\settings.json. Keys and defaults match
/// the mac app. claudeAPIKey holds an already-encrypted blob (DPAPI is applied by the WPF layer),
/// so Core has no Windows-only crypto dependency and still builds on Linux CI.
public sealed class JsonAppSettings : IAppSettings
{
    private sealed class Model
    {
        [JsonPropertyName("fontSize")] public int FontSize { get; set; } = 13;
        [JsonPropertyName("editorTheme")] public string EditorTheme { get; set; } = "system";
        [JsonPropertyName("indentUsesSpaces")] public bool IndentUsesSpaces { get; set; } = true;
        [JsonPropertyName("indentWidth")] public int IndentWidth { get; set; } = 4;
        [JsonPropertyName("enginePathOverride")] public string EnginePathOverride { get; set; } = "";
        [JsonPropertyName("claudeAPIKey")] public string ClaudeAPIKey { get; set; } = "";
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Model _m;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CodeCrack", "settings.json");

    public JsonAppSettings() : this(DefaultPath) { }

    public JsonAppSettings(string path)
    {
        _path = path;
        _m = Load(path);
    }

    // A corrupt or unreadable settings file must never crash the app -> fall back to defaults.
    private static Model Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Model>(File.ReadAllText(path), Options) ?? new Model();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // ignored: use defaults
        }
        return new Model();
    }

    public int FontSize { get => _m.FontSize; set => _m.FontSize = value; }
    public string EditorTheme { get => _m.EditorTheme; set => _m.EditorTheme = value; }
    public bool IndentUsesSpaces { get => _m.IndentUsesSpaces; set => _m.IndentUsesSpaces = value; }
    public int IndentWidth { get => _m.IndentWidth; set => _m.IndentWidth = value; }
    public string EnginePathOverride { get => _m.EnginePathOverride; set => _m.EnginePathOverride = value; }
    public string ClaudeApiKey { get => _m.ClaudeAPIKey; set => _m.ClaudeAPIKey = value; }

    public void Save()
    {
        // Best-effort persistence: never crash on an unwritable/redirected %APPDATA%.
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_m, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // ignored: settings simply don't persist this time
        }
    }
}
