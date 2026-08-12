using System.Text.Json;
using CodeCrack.App.Core.Engine;
using Xunit;

namespace CodeCrack.Tests;

public class ModelsContractTests
{
    private static string GoldenPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "analysis_result.json");

    [Fact]
    public void Deserializes_engine_golden_strictly_and_tolerates_evidence()
    {
        var json = File.ReadAllText(GoldenPath);
        var result = JsonSerializer.Deserialize<AnalysisResult>(json, EngineJson.Options);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Findings.Count);
        Assert.Equal(3, result.Tests.Count);

        // Findings decode; the engine's extra `evidence` object is tolerated (Skip).
        var zero = result.Findings[0];
        Assert.Equal("F001", zero.Id);
        Assert.Equal("zero-division", zero.Kind);
        Assert.Equal("divide", zero.Target);
        Assert.Equal(2, zero.Line); // Location[0]

        // Every generated test proved its bug; `reproduced` is consumed, never re-derived.
        Assert.All(result.Tests, t => Assert.True(t.Reproduced));
        Assert.All(result.Tests, t => Assert.False(t.NeedsInput));

        // Summary + nested snake_case `by_outcome` decode.
        Assert.Equal(3, result.Summary.Reproduced);
        Assert.Equal(2, result.Summary.ByOutcome.Passed);
        Assert.Equal(1, result.Summary.ByOutcome.Failed);
        Assert.Equal(0, result.Summary.ByOutcome.Skipped);
    }

    [Fact]
    public void Unknown_member_on_a_test_is_rejected()
    {
        const string json = """
        {"findings":[],"tests":[{"finding_id":"F1","test_name":"t","source":"s","expects":"raises","outcome":null,"detail":"","stdout":"","duration":0.0,"reproduced":false,"bogus":1}],"summary":{"findings":0,"tests":1,"executed":0,"reproduced":0,"by_outcome":{"passed":0,"failed":0,"error":0,"skipped":0}}}
        """;
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AnalysisResult>(json, EngineJson.Options));
    }

    [Fact]
    public void Missing_required_member_is_rejected()
    {
        // Finding missing "severity".
        const string json = """
        {"findings":[{"id":"F1","kind":"k","target":"t","location":[1,1],"rationale":"r"}],"tests":[],"summary":{"findings":1,"tests":0,"executed":0,"reproduced":0,"by_outcome":{"passed":0,"failed":0,"error":0,"skipped":0}}}
        """;
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AnalysisResult>(json, EngineJson.Options));
    }
}
