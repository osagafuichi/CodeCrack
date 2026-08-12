using System;
using CodeCrack.App.Core.Editor;

namespace CodeCrack.App.Core.ViewModels;

/// Owns the live editor buffer <-> active document sync. The AvalonEdit-hosting control is
/// injected as Host; headless tests inject a fake. No WPF types here.
public sealed class EditorViewModel : ObservableObject
{
    public IEditorHost? Host { get; set; }

    private OpenDocument? _document;
    public OpenDocument? Document
    {
        get => _document;
        set { if (Set(ref _document, value)) LoadIntoHost(); }
    }

    private void LoadIntoHost()
    {
        if (Host is null || _document is null) return;
        Host.Text = _document.Text;
        Host.SetLanguageByPath(_document.Path);
        Host.CaretOffset = Math.Min(_document.CaretOffset, _document.Text.Length);
    }

    /// Pull the current buffer text/caret back into the active document (called before save/analyze/run).
    public void SyncFromHost()
    {
        if (Host is null || _document is null) return;
        if (Host.Text != _document.Text) _document.IsDirty = true;
        _document.Text = Host.Text;
        _document.CaretOffset = Host.CaretOffset;
    }
}
