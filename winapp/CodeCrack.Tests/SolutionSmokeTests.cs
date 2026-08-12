using Xunit;

namespace CodeCrack.Tests;

public class SolutionSmokeTests
{
    [Fact]
    public void Core_assembly_is_referenced_and_loads()
    {
        // Any public type from Core proves the ProjectReference + strict build works.
        var asm = typeof(CodeCrack.App.Core.CoreMarker).Assembly;
        Assert.Equal("CodeCrack.App.Core", asm.GetName().Name);
    }
}
