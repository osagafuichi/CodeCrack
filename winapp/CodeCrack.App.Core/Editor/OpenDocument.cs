using System;
using System.ComponentModel;

namespace CodeCrack.App.Core.Editor;

/// <summary>An open editor tab. One reused TextEditor swaps its Document per active OpenDocument.
/// Bindable: <see cref="Path"/>/<see cref="IsDirty"/> (and derived <see cref="Name"/>) raise change
/// notifications so the tab strip's title + dirty dot update live. Text/caret are plain state.</summary>
public sealed class OpenDocument : INotifyPropertyChanged
{
    private string _path = string.Empty;
    public string Path
    {
        get => _path;
        set { if (_path != value) { _path = value; Raise(nameof(Path)); Raise(nameof(Name)); } }
    }

    public string Text { get; set; } = string.Empty;

    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        set { if (_isDirty != value) { _isDirty = value; Raise(nameof(IsDirty)); } }
    }

    public int CaretOffset { get; set; }
    public (int Start, int Length) Selection { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public string Name => System.IO.Path.GetFileName(Path);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
