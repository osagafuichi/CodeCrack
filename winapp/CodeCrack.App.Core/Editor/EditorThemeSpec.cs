namespace CodeCrack.App.Core.Editor;

/// Carries a TextMate theme id + light/dark flag + editor colors (hex strings).
public sealed record EditorThemeSpec(
    string ThemeId,
    bool IsDark,
    string Background,
    string Foreground,
    string Caret,
    string Selection);
