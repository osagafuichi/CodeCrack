namespace CodeCrack.App.Core.Editor;

/// <summary>Pure bounds-checking used by the AvalonEdit host and tab restore.</summary>
public static class EditorGeometry
{
    public static int ClampOffset(int textLength, int offset)
        => offset < 0 ? 0 : offset > textLength ? textLength : offset;

    /// <summary>Clamps a selection to [0, textLength]; collapses to empty if it lands past the end.</summary>
    public static (int Start, int Length) ClampSelection(int textLength, int start, int length)
    {
        int end = start + length;
        int s = ClampOffset(textLength, start);
        int e = ClampOffset(textLength, end);
        if (e < s) e = s;
        return (s, e - s);
    }

    /// <summary>Clamps a 1-indexed line number to [1, lineCount] (lineCount is at least 1).</summary>
    public static int ClampLine(int lineCount, int requestedLine1)
    {
        int max = lineCount < 1 ? 1 : lineCount;
        return requestedLine1 < 1 ? 1 : requestedLine1 > max ? max : requestedLine1;
    }
}
