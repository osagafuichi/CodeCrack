using System;

namespace CodeCrack.App.Core.Editor;

/// <summary>An open editor tab. One reused TextEditor swaps its Document per active OpenDocument.</summary>
public sealed class OpenDocument
{
    public string Path = string.Empty;
    public string Text = string.Empty;
    public bool IsDirty;
    public int CaretOffset;
    public (int Start, int Length) Selection;
    public DateTime LastWriteUtc;
    public string Name => System.IO.Path.GetFileName(Path);
}
