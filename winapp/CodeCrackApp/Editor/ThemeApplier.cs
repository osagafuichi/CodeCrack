using System;
using System.Windows.Media;
using CodeCrack.App.Core.Editor;
using ICSharpCode.AvalonEdit;
using TextMateSharp.Grammars;

namespace CodeCrackApp.Editor;

internal static class ThemeApplier
{
    public static void Apply(TextEditor editor, TextMateInstallation textMate,
        RegistryOptions options, EditorThemeSpec spec)
    {
        if (Enum.TryParse<ThemeName>(spec.TmThemeId, out var themeName))
            textMate.SetTheme(options.LoadTheme(themeName));

        editor.Background = Brush(spec.Background);
        editor.Foreground = Brush(spec.Foreground);
        editor.TextArea.Caret.CaretBrush = Brush(spec.Caret);
        editor.TextArea.SelectionBrush = Brush(spec.Selection);
        editor.LineNumbersForeground = Brush(spec.LineNumber);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
