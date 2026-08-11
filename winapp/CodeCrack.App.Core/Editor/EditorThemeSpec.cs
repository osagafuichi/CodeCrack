namespace CodeCrack.App.Core.Editor;

/// <summary>
/// A concrete editor theme: which TextMateSharp theme to load (<see cref="TmThemeId"/> matches a
/// TextMateSharp.Grammars.ThemeName enum name) plus the chrome colors the WPF control paints
/// directly (background, caret, selection, line-number gutter) so panes read as one surface.
/// </summary>
public sealed record EditorThemeSpec(
    string Key,
    string Label,
    string TmThemeId,
    bool IsDark,
    string Background,
    string Foreground,
    string Caret,
    string Selection,
    string LineNumber);
