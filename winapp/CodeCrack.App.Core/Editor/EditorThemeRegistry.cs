using System.Collections.Generic;
using System.Linq;

namespace CodeCrack.App.Core.Editor;

/// <summary>The six selectable themes (ports Theme.swift). 'system' resolves to atom-one at runtime.</summary>
public static class EditorThemeRegistry
{
    private static readonly EditorThemeSpec AtomOneLight = new(
        "atom-one-light", "Atom One Light", "LightPlus", false,
        "#FAFAFA", "#383A42", "#526FFF", "#E5E5E6", "#9D9D9F");

    private static readonly EditorThemeSpec AtomOneDark = new(
        "atom-one-dark", "Atom One Dark", "DarkPlus", true,
        "#282C34", "#ABB2BF", "#528BFF", "#3E4451", "#4B5263");

    private static readonly EditorThemeSpec SolarizedDark = new(
        "solarized-dark", "Solarized Dark", "SolarizedDark", true,
        "#002B36", "#839496", "#839496", "#073642", "#586E75");

    private static readonly EditorThemeSpec Monokai = new(
        "monokai", "Monokai", "Monokai", true,
        "#272822", "#F8F8F2", "#F8F8F0", "#49483E", "#90908A");

    private static readonly EditorThemeSpec GitHub = new(
        "github", "GitHub", "Light", false,
        "#FFFFFF", "#24292E", "#044289", "#C8E1FF", "#BABBBD");

    private static readonly EditorThemeSpec System = new(
        "system", "System (follow appearance)", "DarkPlus", true,
        AtomOneDark.Background, AtomOneDark.Foreground, AtomOneDark.Caret, AtomOneDark.Selection, AtomOneDark.LineNumber);

    public static IReadOnlyList<EditorThemeSpec> All { get; } = new[]
    {
        System, AtomOneLight, AtomOneDark, SolarizedDark, Monokai, GitHub,
    };

    private static readonly IReadOnlyDictionary<string, EditorThemeSpec> Concrete =
        new[] { AtomOneLight, AtomOneDark, SolarizedDark, Monokai, GitHub }
            .ToDictionary(t => t.Key);

    /// <summary>Resolves a menu key to a concrete spec. 'system' (and any unknown key) follows the OS.</summary>
    public static EditorThemeSpec Resolve(string key, bool systemIsDark)
    {
        if (Concrete.TryGetValue(key, out var spec)) return spec;
        return systemIsDark ? AtomOneDark : AtomOneLight;
    }
}
