using CodeCrack.App.Core.Editor;
using Xunit;

namespace CodeCrack.Tests;

public class EditorGeometryTests
{
    [Theory]
    [InlineData(10, 5, 5)]
    [InlineData(10, -3, 0)]
    [InlineData(10, 42, 10)]   // caret may sit at TextLength (end of doc)
    public void ClampOffset_KeepsWithinZeroToLength(int len, int req, int expected)
        => Assert.Equal(expected, EditorGeometry.ClampOffset(len, req));

    [Fact]
    public void ClampSelection_TruncatesRunPastEnd()
    {
        var (start, length) = EditorGeometry.ClampSelection(textLength: 8, start: 6, length: 10);
        Assert.Equal(6, start);
        Assert.Equal(2, length);
    }

    [Fact]
    public void ClampSelection_NegativeStartCollapses()
    {
        var (start, length) = EditorGeometry.ClampSelection(textLength: 8, start: -4, length: 3);
        Assert.Equal(0, start);
        Assert.Equal(0, length); // start moved to 0, original end (=-1) < 0 → empty
    }

    [Theory]
    [InlineData(100, 0, 1)]     // AvalonEdit lines are 1-indexed
    [InlineData(100, 250, 100)]
    [InlineData(100, 37, 37)]
    public void ClampLine_KeepsWithinOneToLineCount(int lineCount, int req, int expected)
        => Assert.Equal(expected, EditorGeometry.ClampLine(lineCount, req));
}
