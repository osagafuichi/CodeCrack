using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace CodeCrack.App.Core.Editor;

/// <summary>An open editor tab. One reused TextEditor swaps its Document per active OpenDocument.
/// Bindable: <see cref="Path"/>/<see cref="IsDirty"/> (and derived <see cref="Name"/>/<see cref="DisplayName"/>)
/// raise change notifications so the tab strip's title + dirty dot update live. Text/caret are plain state.</summary>
public sealed class OpenDocument : INotifyPropertyChanged
{
    private string _path = string.Empty;
    public string Path
    {
        get => _path;
        set { if (_path != value) { _path = value; Raise(nameof(Path)); Raise(nameof(Name)); Raise(nameof(DisplayName)); } }
    }

    public string Text { get; set; } = string.Empty;

    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        set { if (_isDirty != value) { _isDirty = value; Raise(nameof(IsDirty)); } }
    }

    private int _untitledNumber;
    /// <summary>Sequence number for an unsaved buffer (>=1), assigned when it is created.
    /// Ignored once the doc has a real <see cref="Path"/>.</summary>
    public int UntitledNumber
    {
        get => _untitledNumber;
        set { if (_untitledNumber != value) { _untitledNumber = value; Raise(nameof(DisplayName)); } }
    }

    public int CaretOffset { get; set; }
    public (int Start, int Length) Selection { get; set; }
    public DateTime LastWriteUtc { get; set; }

    /// The bare file name (empty for an unsaved buffer). Used by status strings.
    public string Name => System.IO.Path.GetFileName(Path);

    /// The tab-strip title: file name once saved, otherwise a never-blank
    /// "Untitled"/"Untitled N" placeholder so a new buffer is never a blank tab.
    public string DisplayName => DisplayNameFor(Path, UntitledNumber);

    /// <summary>Pure display-name rule: the file name when <paramref name="path"/> is set,
    /// otherwise "Untitled" (number &lt;= 1) or "Untitled {n}".</summary>
    public static string DisplayNameFor(string path, int untitledNumber)
    {
        if (!string.IsNullOrEmpty(path)) return System.IO.Path.GetFileName(path);
        return untitledNumber <= 1 ? "Untitled" : $"Untitled {untitledNumber}";
    }

    /// <summary>Smallest positive integer not already used by an existing untitled buffer,
    /// so "Untitled"/"Untitled 2"/… stay stable and don't grow without bound.</summary>
    public static int NextUntitledNumber(IEnumerable<int> usedNumbers)
    {
        var used = new HashSet<int>(usedNumbers);
        int n = 1;
        while (used.Contains(n)) n++;
        return n;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
