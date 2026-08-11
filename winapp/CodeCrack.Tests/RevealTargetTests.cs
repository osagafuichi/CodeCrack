using CodeCrack.App.Core.Editor;
using Xunit;

namespace CodeCrack.Tests;

public class RevealTargetTests
{
    [Theory]
    [InlineData(100, 40, 40)]
    [InlineData(100, 0, 1)]
    [InlineData(100, 999, 100)]
    [InlineData(0, 5, 1)]      // empty doc → line 1
    public void Resolve_ClampsToDocument(int lineCount, int req, int expected)
        => Assert.Equal(expected, RevealTarget.Resolve(lineCount, req));

    [Fact]
    public void Resolve_IsPure_SameInputSameOutput_NoStateGuard()
    {
        // Called twice with the same value it must yield the same target both times
        // (the control must NOT early-return on an unchanged line — issue re-click re-scrolls).
        int first = RevealTarget.Resolve(100, 42);
        int second = RevealTarget.Resolve(100, 42);
        Assert.Equal(42, first);
        Assert.Equal(42, second);
    }
}
