using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeCrack.App.Core.ViewModels;

/// A node in the project file tree. Leaf files have an empty Children list.
public sealed record FileNode(string Path, string Name, bool IsDirectory, IReadOnlyList<FileNode> Children);

/// Eager project tree. Skips hidden entries, sorts directories first then names
/// case-insensitively — a byte-for-byte port of FileTreeBuilder.build.
public sealed class FileTreeViewModel
{
    private static readonly IReadOnlyList<FileNode> None = Array.Empty<FileNode>();

    public FileNode? Root { get; private set; }

    /// Build the tree. A directory roots there; a single file roots at its parent directory.
    public void BuildFrom(string fileOrDirPath)
    {
        var full = System.IO.Path.GetFullPath(fileOrDirPath);
        var dir = Directory.Exists(full) ? full : System.IO.Path.GetDirectoryName(full)!;
        Root = Build(dir);
    }

    /// Rebuild the current root (after New / Save-As create files on disk).
    public void Refresh()
    {
        if (Root is not null) Root = Build(Root.Path);
    }

    private static FileNode Build(string path)
    {
        if (!Directory.Exists(path))
            return new FileNode(path, System.IO.Path.GetFileName(path), false, None);

        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(path); }
        catch { entries = Array.Empty<string>(); }

        var kids = entries
            .Where(p => !IsHidden(p))
            .Select(p => new { p, dir = Directory.Exists(p), name = System.IO.Path.GetFileName(p) })
            .OrderByDescending(e => e.dir)                                   // dirs first
            .ThenBy(e => e.name, StringComparer.OrdinalIgnoreCase)           // then case-insensitive
            .Select(e => Build(e.p))
            .ToList();

        return new FileNode(path, System.IO.Path.GetFileName(path), true, kids);
    }

    private static bool IsHidden(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        if (name.StartsWith('.')) return true;                              // dotfiles
        try { return (File.GetAttributes(path) & FileAttributes.Hidden) != 0; }
        catch { return false; }
    }
}
