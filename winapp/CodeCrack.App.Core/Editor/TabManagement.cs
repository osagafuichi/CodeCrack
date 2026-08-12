namespace CodeCrack.App.Core.Editor;

/// <summary>Pure tab-lifecycle rules: which tab to select on close, and safe cursor restore.</summary>
public static class TabManagement
{
    /// <summary>
    /// Index to select after removing <paramref name="closingIndex"/> from a list of
    /// <paramref name="count"/> tabs: prefer the left neighbour, else the right, else none.
    /// Returned index is relative to the reduced (count-1) list.
    /// </summary>
    public static int? SelectAfterClose(int count, int closingIndex)
    {
        if (count <= 1) return null;
        return closingIndex > 0 ? closingIndex - 1 : 0;
    }

    /// <summary>Bounds-checks a document's stored caret + selection against the live text length.</summary>
    public static (int Caret, int Start, int Length) RestoreCursor(int textLength, OpenDocument doc)
    {
        int caret = EditorGeometry.ClampOffset(textLength, doc.CaretOffset);
        var (start, length) = EditorGeometry.ClampSelection(textLength, doc.Selection.Start, doc.Selection.Length);
        return (caret, start, length);
    }
}
