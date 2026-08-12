using System.Collections.Generic;
using CodeCrack.App.Core.Session;

namespace CodeCrack.Tests.Fakes;

public sealed class InMemoryKeyValueStore : IKeyValueStore
{
    private readonly Dictionary<string, object> _d = new();
    public int Writes { get; private set; }

    public string? GetString(string key) => _d.TryGetValue(key, out var v) && v is string s ? s : null;
    public void SetString(string key, string value) { _d[key] = value; Writes++; }
    public string[]? GetStringArray(string key) => _d.TryGetValue(key, out var v) && v is string[] a ? a : null;
    public void SetStringArray(string key, string[] value) { _d[key] = value; Writes++; }
    public void Remove(string key) { if (_d.Remove(key)) Writes++; }
}
