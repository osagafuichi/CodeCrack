namespace CodeCrack.App.Core.Editor;

/// <summary>
/// Resolves the 1-indexed line a click-to-line request should reveal. Stateless by design:
/// the caller must invoke RevealLine every time (no "unchanged value" guard), so re-clicking
/// the same issue re-scrolls — matching the macOS behavior.
/// </summary>
public static class RevealTarget
{
    public static int Resolve(int lineCount, int requestedLine1)
        => EditorGeometry.ClampLine(lineCount, requestedLine1);
}
