using System;
using System.Collections.Generic;
using System.IO;
using CodeCrack.App.Core.Editor;

namespace CodeCrack.App.Core.Services;

/// Filesystem probe the watcher depends on (faked in tests).
public interface IExternalFileProbe
{
    bool Exists(string path);
    DateTime LastWriteUtc(string path);
    string ReadText(string path);
}

/// Real probe over System.IO (UTF-8 read).
public sealed class DiskFileProbe : IExternalFileProbe
{
    public bool Exists(string path) => File.Exists(path);
    public DateTime LastWriteUtc(string path) => File.GetLastWriteTimeUtc(path);
    public string ReadText(string path) => File.ReadAllText(path);
}

/// A pending "file changed on disk" prompt for a dirty document whose disk content differs.
public sealed record ExternalChangePrompt(OpenDocument Doc, string DiskText, DateTime DiskWriteUtc);

/// The 4-branch external-change state machine, run on Window.Activated (not a continuous
/// FileSystemWatcher). One prompt at a time via PromptActive.
public sealed class ExternalChangeWatcher
{
    private readonly IExternalFileProbe _probe;
    private readonly StatusBus _status;

    public ExternalChangeWatcher(IExternalFileProbe probe, StatusBus status)
    {
        _probe = probe;
        _status = status;
    }

    /// True while a "File changed on disk" dialog is open — Scan is then a no-op (single-dialog guard).
    public bool PromptActive { get; set; }

    /// Reconcile each open doc against disk. Clean docs reload silently; a dirty differing doc
    /// returns a prompt (the rest are handled after it resolves). Returns null if nothing prompts.
    public ExternalChangePrompt? Scan(IReadOnlyList<OpenDocument> docs)
    {
        if (PromptActive) return null;                       // (1) don't stack prompts
        foreach (var doc in docs)
        {
            if (!_probe.Exists(doc.Path)) continue;
            var disk = _probe.LastWriteUtc(doc.Path);
            if (disk <= doc.LastWriteUtc) continue;          // (1) no newer mtime

            string diskText;
            try { diskText = _probe.ReadText(doc.Path); }
            catch { continue; }

            if (diskText == doc.Text)                        // (2) same content, newer mtime: catch up
            {
                doc.LastWriteUtc = disk;
                continue;
            }
            if (doc.IsDirty)                                 // (3) differs + dirty: prompt
                return new ExternalChangePrompt(doc, diskText, disk);

            doc.Text = diskText;                            // (4) differs + clean: silent reload
            doc.IsDirty = false;
            doc.LastWriteUtc = disk;
            _status.Set($"Reloaded {doc.Name} — changed on disk");
        }
        return null;
    }

    /// Resolve a prompted change. Reload adopts the disk version and drops edits; Keep retains the
    /// user's text but records the new mtime so it stops asking. Caller lowers PromptActive and re-Scans.
    public void Resolve(ExternalChangePrompt prompt, bool reload)
    {
        if (reload)
        {
            prompt.Doc.Text = prompt.DiskText;
            prompt.Doc.IsDirty = false;
        }
        prompt.Doc.LastWriteUtc = prompt.DiskWriteUtc;
        _status.Set(reload ? $"Reloaded {prompt.Doc.Name}"
                           : $"Kept your version of {prompt.Doc.Name}");
    }
}
