using CodeCrack.App.Core.Editor;
using Xunit;

namespace CodeCrack.Tests;

public class TabManagementTests
{
    [Theory]
    [InlineData(3, 0, 0)]   // close first → left of it is none → right neighbor becomes index 0
    [InlineData(3, 1, 0)]   // close middle → prefer left → index 0
    [InlineData(3, 2, 1)]   // close last → prefer left → index 1
    public void SelectAfterClose_PrefersLeftElseRight(int count, int closing, int expected)
        => Assert.Equal(expected, TabManagement.SelectAfterClose(count, closing));

    [Fact]
    public void SelectAfterClose_LastTabLeavesNothing()
        => Assert.Null(TabManagement.SelectAfterClose(count: 1, closingIndex: 0));

    [Fact]
    public void RestoreCursor_ClampsAgainstShrunkText()
    {
        var doc = new OpenDocument
        {
            Path = @"C:\x.py", Text = "abc", CaretOffset = 99, Selection = (50, 40),
        };
        var (caret, start, length) = TabManagement.RestoreCursor(textLength: 3, doc);
        Assert.Equal(3, caret);   // clamped to end
        Assert.Equal(3, start);   // selection start clamped
        Assert.Equal(0, length);  // run past end collapses
    }

    [Fact]
    public void OpenDocument_NameIsFileName()
        => Assert.Equal("x.py", new OpenDocument { Path = @"C:\dir\x.py" }.Name);
}
