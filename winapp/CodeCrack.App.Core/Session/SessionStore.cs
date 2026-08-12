using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeCrack.App.Core.Session;

/// A restored session. OpenPaths are guaranteed to exist on disk (missing ones are dropped).
public sealed record SessionState(IReadOnlyList<string> OpenPaths, string? ActivePath);

/// Deterministic persistence of the open tab set + active tab. A fingerprint (paths + active,
/// not text) gates redundant re-saves; a _didRestore guard blocks saves until Restore has run,
/// so startup tab churn never clobbers the saved session.
public sealed class SessionStore
{
    private const string OpenKey = "sessionOpenDocumentPaths";
    private const string ActiveKey = "sessionActiveDocumentPath";

    private readonly IKeyValueStore _store;
    private bool _didRestore;
    private string? _lastSignature;

    public SessionStore(IKeyValueStore store) => _store = store;

    public void Save(IReadOnlyList<string> openPaths, string? activePath)
    {
        if (!_didRestore) return;                       // guard: no saving before restore
        var sig = Signature(openPaths, activePath);
        if (sig == _lastSignature) return;              // fingerprint: skip identical
        _lastSignature = sig;

        if (openPaths.Count == 0)                        // empty tab set clears the keys
        {
            _store.Remove(OpenKey);
            _store.Remove(ActiveKey);
            return;
        }
        _store.SetStringArray(OpenKey, openPaths.ToArray());
        if (activePath is null) _store.Remove(ActiveKey);
        else _store.SetString(ActiveKey, activePath);
    }

    /// Read the saved session (existing paths only), enabling future saves. Restore opens files
    /// inline in the caller WITHOUT touching RecentFiles — the recents bypass.
    public SessionState? Restore()
    {
        _didRestore = true;
        var open = _store.GetStringArray(OpenKey);
        if (open is null || open.Length == 0) return null;

        var existing = open.Where(File.Exists).ToList();
        if (existing.Count == 0) return null;

        var active = _store.GetString(ActiveKey);
        if (active is not null && !File.Exists(active)) active = null;

        _lastSignature = Signature(existing, active);    // seed: identical post-restore save is a no-op
        return new SessionState(existing, active);
    }

    // Newline + pipe separators: both are invalid in Windows paths, so the fingerprint of
    // the open set + active path is unambiguous.
    private static string Signature(IReadOnlyList<string> open, string? active) =>
        string.Join("\n", open) + "||" + (active ?? "");
}
