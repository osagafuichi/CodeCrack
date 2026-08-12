namespace CodeCrack.App.Core.Editor;

public interface IEditorHost
{
    string Text { get; set; }
    int CaretOffset { get; set; }
    (int Start, int Length) Selection { get; set; }
    void RevealLine(int line1Indexed);
    void SetLanguageByPath(string filePath);
    void ApplyTheme(EditorThemeSpec theme);
    void FocusEditor();
    event EventHandler? TextChanged;
}
