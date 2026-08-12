using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeCrack.App.Core.Search;

/// One search match: a file path, a 1-based line number, and the trimmed line text.
public sealed record SearchHit(string Path, int Line, string Preview);

/// Project-wide text search and replace under a root folder. Skips hidden files and anything
/// that is not valid UTF-8 text. Search is case-INsensitive; ReplaceAll is case-SENSITIVE —
/// the asymmetry is deliberate and mirrors the mac app.
public static class ProjectSearch
{
    public const int MaxHits = 500;

    // Strict decoder: throws on invalid UTF-8 so we can skip binary files.
    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static IReadOnlyList<SearchHit> Search(string query, string root)
    {
        var hits = new List<SearchHit>();
        if (string.IsNullOrEmpty(query)) return hits;

        foreach (var path in TextFiles(root))
        {
            if (hits.Count >= MaxHits) break;
            if (!TryReadUtf8(path, out var content)) continue;

            var line = 0;
            foreach (var raw in content.Split('\n'))
            {
                line++;
                var text = raw.TrimEnd('\r');
                if (text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hits.Add(new SearchHit(path, line, text.Trim()));
                    if (hits.Count >= MaxHits) break;
                }
            }
        }
        return hits;
    }

    /// Replace every case-sensitive occurrence of query with replacement across the project.
    /// Returns the paths of files that actually changed.
    public static IReadOnlyList<string> ReplaceAll(string query, string replacement, string root)
    {
        var changed = new List<string>();
        if (string.IsNullOrEmpty(query)) return changed;

        foreach (var path in TextFiles(root))
        {
            if (!TryReadUtf8(path, out var content)) continue;
            if (!content.Contains(query, StringComparison.Ordinal)) continue;

            var updated = content.Replace(query, replacement, StringComparison.Ordinal);
            if (updated == content) continue;
            File.WriteAllText(path, updated, StrictUtf8); // UTF-8, no BOM
            changed.Add(path);
        }
        return changed;
    }

    private static IEnumerable<string> TextFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            IgnoreInaccessible = true
        };
        foreach (var path in Directory.EnumerateFiles(root, "*", opts))
        {
            if (Path.GetFileName(path).StartsWith('.')) continue; // dotfiles
            yield return path;
        }
    }

    private static bool TryReadUtf8(string path, out string content)
    {
        try { content = File.ReadAllText(path, StrictUtf8); return true; }
        catch { content = ""; return false; } // DecoderFallbackException on binary, or IO error
    }
}
