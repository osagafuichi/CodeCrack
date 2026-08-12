using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace CodeCrackApp.Editor;

/// <summary>Minimal WPF replacement for AvaloniaEdit's InstallTextMate: wires a per-line colorizer.</summary>
public sealed class TextMateInstallation
{
    private readonly TextEditor _editor;
    private readonly Registry _registry;
    private readonly RegistryOptions _options;
    private readonly TextMateColorizer _colorizer;
    private TextDocument? _document;

    internal TextMateInstallation(TextEditor editor, RegistryOptions options)
    {
        _editor = editor;
        _options = options;
        _registry = new Registry(options);
        _colorizer = new TextMateColorizer(_registry);
        _editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        _document = _editor.Document;
        if (_document is not null) _document.Changed += OnDocumentChanged;
        // A tab swap replaces TextEditor.Document; re-hook the change listener to the new
        // document so multi-line highlight invalidation keeps working after the swap.
        _editor.DocumentChanged += OnEditorDocumentChanged;
    }

    private void OnEditorDocumentChanged(object? sender, EventArgs e)
    {
        if (_document is not null) _document.Changed -= OnDocumentChanged;
        _document = _editor.Document;
        if (_document is not null) _document.Changed += OnDocumentChanged;
        _colorizer.InvalidateFrom(1);          // fresh document: drop stale token cache
        _editor.TextArea.TextView.Redraw();
    }

    public RegistryOptions Options => _options;

    public void SetGrammar(string? scopeName)
    {
        _colorizer.SetGrammar(string.IsNullOrEmpty(scopeName) ? null : _registry.LoadGrammar(scopeName));
        _editor.TextArea.TextView.Redraw();
    }

    public void SetTheme(IRawTheme rawTheme)
    {
        _registry.SetTheme(rawTheme);
        _colorizer.SetTheme(_registry.GetTheme());
        _editor.TextArea.TextView.Redraw();
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        var line = _editor.Document.GetLineByOffset(e.Offset);
        _colorizer.InvalidateFrom(line.LineNumber);
    }
}

public static class TextMateInstall
{
    /// <summary>Installs a TextMateSharp colorizer on the editor (WPF analog of InstallTextMate).</summary>
    public static TextMateInstallation InstallTextMate(this TextEditor editor, RegistryOptions options)
        => new(editor, options);
}
