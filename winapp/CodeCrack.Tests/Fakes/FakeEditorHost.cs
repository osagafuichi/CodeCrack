using CodeCrack.App.Core.Editor;

namespace CodeCrack.Tests.Fakes;

public sealed class FakeEditorHost : IEditorHost
{
    public string Text { get; set; } = "";
    public int CaretOffset { get; set; }
    public (int Start, int Length) Selection { get; set; }
    public int RevealedLine { get; private set; }
    public string? LanguagePath { get; private set; }
    public EditorThemeSpec? Theme { get; private set; }
    public bool Focused { get; private set; }

    public event EventHandler? TextChanged;

    public void RevealLine(int line1Indexed) => RevealedLine = line1Indexed;
    public void SetLanguageByPath(string filePath) => LanguagePath = filePath;
    public void ApplyTheme(EditorThemeSpec theme) => Theme = theme;
    public void FocusEditor() => Focused = true;
    public void RaiseTextChanged() => TextChanged?.Invoke(this, EventArgs.Empty);
}
