using System.Collections.Generic;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace CodeCrackApp.Editor;

/// <summary>
/// Colorizes each visible line by tokenizing it with a TextMateSharp grammar and mapping
/// scopes → theme foreground colors. State (rule stack) is carried line-to-line so multi-line
/// constructs (block comments, strings) highlight correctly; the cache is cleared on any edit.
/// </summary>
internal sealed class TextMateColorizer : ICSharpCode.AvalonEdit.Rendering.DocumentColorizingTransformer
{
    private readonly Registry _registry;
    private IGrammar? _grammar;
    private Theme _theme;
    private readonly Dictionary<int, IStateStack?> _lineEndStates = new();
    private Dictionary<int, Brush> _brushes = new();

    public TextMateColorizer(Registry registry)
    {
        _registry = registry;
        _theme = registry.GetTheme();
        RebuildBrushes();
    }

    public void SetGrammar(IGrammar? grammar)
    {
        _grammar = grammar;
        _lineEndStates.Clear();
    }

    public void SetTheme(Theme theme)
    {
        _theme = theme;
        RebuildBrushes();
        _lineEndStates.Clear();
    }

    public void InvalidateFrom(int lineNumber)
    {
        var stale = new List<int>();
        foreach (var k in _lineEndStates.Keys)
            if (k >= lineNumber) stale.Add(k);
        foreach (var k in stale) _lineEndStates.Remove(k);
    }

    private void RebuildBrushes()
    {
        var map = new Dictionary<int, Brush>();
        foreach (string color in _theme.GetColorMap())
        {
            int id = _theme.GetColorId(color);
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                brush.Freeze();
                map[id] = brush;
            }
            catch { /* skip unparseable theme color */ }
        }
        _brushes = map;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_grammar is null) return;
        string text = CurrentContext.Document.GetText(line);
        _lineEndStates.TryGetValue(line.LineNumber - 1, out var prevState);
        var result = _grammar.TokenizeLine(text, prevState, TimeSpan.Zero);
        _lineEndStates[line.LineNumber] = result.RuleStack;

        foreach (IToken token in result.Tokens)
        {
            int startInLine = token.StartIndex;
            int endInLine = token.EndIndex > text.Length ? text.Length : token.EndIndex;
            if (endInLine <= startInLine) continue;

            List<ThemeTrieElementRule> rules = _theme.Match(token.Scopes);
            if (rules.Count == 0) continue;
            int fg = rules[0].foreground;
            if (fg <= 0 || !_brushes.TryGetValue(fg, out var brush)) continue;

            int startOffset = line.Offset + startInLine;
            int endOffset = line.Offset + endInLine;
            ChangeLinePart(startOffset, endOffset, e => e.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
