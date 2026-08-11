using CodeCrack.App.Core.Engine;
using Xunit;

namespace CodeCrack.Tests;

public class ModelsComputedTests
{
    [Fact]
    public void Finding_Line_is_first_location_or_1()
    {
        Assert.Equal(7, new Finding("F1", "k", "t", new[] { 7, 3 }, "r", "high").Line);
        Assert.Equal(1, new Finding("F1", "k", "t", System.Array.Empty<int>(), "r", "high").Line);
    }

    [Fact]
    public void GeneratedTest_NeedsInput_on_skipped_or_regression()
    {
        var skipped = new GeneratedTest("F1", "t", "s", "raises", "skipped", "", "", 0.0, false);
        var regression = new GeneratedTest("F1", "t", "s", "regression", null, "", "", 0.0, false);
        var proven = new GeneratedTest("F1", "t", "s", "raises", "passed", "", "", 0.01, true);

        Assert.True(skipped.NeedsInput);
        Assert.True(regression.NeedsInput);
        Assert.False(proven.NeedsInput);
    }

    [Fact]
    public void EngineJson_Options_are_snake_case_and_strict()
    {
        Assert.Same(System.Text.Json.JsonNamingPolicy.SnakeCaseLower, EngineJson.Options.PropertyNamingPolicy);
        Assert.Equal(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
            EngineJson.Options.UnmappedMemberHandling);
    }
}
