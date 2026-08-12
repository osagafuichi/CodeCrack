using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeCrack.App.Core.Session;

/// IKeyValueStore backed by a single JSON object on disk (e.g. %APPDATA%\CodeCrack\session.json).
public sealed class JsonKeyValueStore : IKeyValueStore
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private readonly string _path;
    private readonly JsonObject _root;

    public JsonKeyValueStore(string path)
    {
        _path = path;
        _root = Load(path);
    }

    // A corrupt or unreadable session file must never crash the app -> start empty.
    private static JsonObject Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // ignored: start with an empty store
        }
        return new JsonObject();
    }

    public string? GetString(string key) =>
        _root[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public void SetString(string key, string value) { _root[key] = value; Flush(); }

    public string[]? GetStringArray(string key) =>
        _root[key] is JsonArray a ? a.Select(n => n!.GetValue<string>()).ToArray() : null;

    public void SetStringArray(string key, string[] value)
    {
        _root[key] = new JsonArray(value.Select(s => (JsonNode)s!).ToArray());
        Flush();
    }

    public void Remove(string key) { if (_root.Remove(key)) Flush(); }

    private void Flush()
    {
        // Best-effort persistence: never crash on an unwritable/redirected %APPDATA%.
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, _root.ToJsonString(Pretty));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // ignored: session simply doesn't persist this time
        }
    }
}
