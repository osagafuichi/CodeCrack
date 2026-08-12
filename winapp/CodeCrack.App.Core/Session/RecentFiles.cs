using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeCrack.App.Core.Session;

/// Most-recently-opened files, newest first, de-duplicated by full path and capped.
/// Entries missing on disk are pruned at load; the Open-Recent path prunes on demand.
public sealed class RecentFiles
{
    public const int Cap = 10;
    private const string Key = "recentDocumentPaths";

    private readonly IKeyValueStore _store;
    private List<string> _paths;

    public RecentFiles(IKeyValueStore store)
    {
        _store = store;
        _paths = (store.GetStringArray(Key) ?? Array.Empty<string>())
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Cap)
            .ToList();
    }

    public IReadOnlyList<string> Paths => _paths;
    public bool IsEmpty => _paths.Count == 0;

    public void Record(string path)
    {
        var full = Path.GetFullPath(path);
        _paths.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        _paths.Insert(0, full);
        if (_paths.Count > Cap) _paths = _paths.Take(Cap).ToList();
        Persist();
    }

    public void Remove(string path)
    {
        var full = Path.GetFullPath(path);
        if (_paths.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase)) > 0)
            Persist();
    }

    public void Clear()
    {
        if (_paths.Count == 0) return;
        _paths.Clear();
        Persist();
    }

    private void Persist() => _store.SetStringArray(Key, _paths.ToArray());
}
