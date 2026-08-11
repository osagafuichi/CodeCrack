using System;
using System.Windows.Controls;
using CodeCrack.App.Core.Editor;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using TextMateSharp.Grammars;

namespace CodeCrackApp.Editor;

public partial class CodeEditorControl : UserControl, IEditorHost
{
    private readonly RegistryOptions _registryOptions;
    private readonly TextMateInstallation _textMate;
    private bool _suppressTextChanged;

    public CodeEditorControl()
    {
        InitializeComponent();
        _registryOptions = new RegistryOptions(ThemeName.DarkPlus);
        _textMate = Editor.InstallTextMate(_registryOptions);
        Editor.TextChanged += (_, _) => { if (!_suppressTextChanged) TextChanged?.Invoke(this, EventArgs.Empty); };
    }

    public event EventHandler? TextChanged;

    public string Text
    {
        get => Editor.Text;
        set
        {
            _suppressTextChanged = true;
            Editor.Text = value ?? string.Empty;
            _suppressTextChanged = false;
        }
    }

    public int CaretOffset
    {
        get => Editor.CaretOffset;
        set => Editor.CaretOffset = EditorGeometry.ClampOffset(Editor.Document.TextLength, value);
    }

    public (int Start, int Length) Selection
    {
        get => (Editor.SelectionStart, Editor.SelectionLength);
        set
        {
            var (s, l) = EditorGeometry.ClampSelection(Editor.Document.TextLength, value.Start, value.Length);
            Editor.Select(s, l);
        }
    }

    public void RevealLine(int line1Indexed)
    {
        int line = EditorGeometry.ClampLine(Editor.Document.LineCount, line1Indexed);
        DocumentLine docLine = Editor.Document.GetLineByNumber(line);
        Editor.Select(docLine.Offset, 0);
        Editor.CaretOffset = docLine.Offset;
        Editor.ScrollToLine(line);
        Editor.TextArea.Caret.BringCaretToView();
        Editor.TextArea.Focus();
    }

    public void FocusEditor() => Editor.TextArea.Focus();

    // Bodies completed in Task 2.2 (SetLanguageByPath) and Task 2.3 (ApplyTheme).
    public void SetLanguageByPath(string filePath)
        => _textMate.SetGrammar(LanguageScope.ScopeForPath(_registryOptions, filePath));

    public void ApplyTheme(EditorThemeSpec theme)
        => ThemeApplier.Apply(Editor, _textMate, _registryOptions, theme);

    /// <summary>Swaps the active tab: replaces the editor Document (fresh per-tab undo history),
    /// then restores caret/selection deferred so AvalonEdit has laid out the new document first.</summary>
    public void SwapDocument(CodeCrack.App.Core.Editor.OpenDocument doc)
    {
        _suppressTextChanged = true;
        Editor.Document = new ICSharpCode.AvalonEdit.Document.TextDocument(doc.Text);
        _suppressTextChanged = false;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var (caret, start, length) =
                CodeCrack.App.Core.Editor.TabManagement.RestoreCursor(Editor.Document.TextLength, doc);
            Editor.CaretOffset = caret;
            Editor.Select(start, length);
            Editor.TextArea.Caret.BringCaretToView();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Click-to-line handler wired from IssuesPanel/TestsPanel item selection.
    /// Always reveals (no guard on the previous line) so re-selecting the same issue re-scrolls.</summary>
    public void RevealLineRequested(int line1Indexed)
    {
        int target = CodeCrack.App.Core.Editor.RevealTarget.Resolve(Editor.Document.LineCount, line1Indexed);
        RevealLine(target);
    }
}
