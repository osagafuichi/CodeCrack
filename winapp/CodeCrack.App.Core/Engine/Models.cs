using System.Text.Json.Serialization;

namespace CodeCrack.App.Core.Engine;

/// The engine also emits an `evidence` object on each finding; it is intentionally
/// ignored (matches the macOS app). `Skip` overrides the options-level `Disallow`
/// for this type only, so extra keys on findings do not throw.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
public sealed record Finding(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string Target,
    [property: JsonRequired] int[] Location,
    [property: JsonRequired] string Rationale,
    [property: JsonRequired] string Severity)
{
    [JsonIgnore] public int Line => Location.Length > 0 ? Location[0] : 1;
}

public sealed record GeneratedTest(
    [property: JsonRequired] string FindingId,
    [property: JsonRequired] string TestName,
    [property: JsonRequired] string Source,
    [property: JsonRequired] string Expects,
    string? Outcome,
    [property: JsonRequired] string Detail,
    [property: JsonRequired] string Stdout,
    [property: JsonRequired] double Duration,
    [property: JsonRequired] bool Reproduced)
{
    [JsonIgnore] public bool NeedsInput => Outcome == "skipped" || Expects == "regression";
}

public sealed record OutcomeCounts(
    [property: JsonRequired] int Passed,
    [property: JsonRequired] int Failed,
    [property: JsonRequired] int Error,
    [property: JsonRequired] int Skipped);

public sealed record Summary(
    [property: JsonRequired] int Findings,
    [property: JsonRequired] int Tests,
    [property: JsonRequired] int Executed,
    [property: JsonRequired] int Reproduced,
    [property: JsonRequired] OutcomeCounts ByOutcome);

public sealed record AnalysisResult(
    [property: JsonRequired] List<Finding> Findings,
    [property: JsonRequired] List<GeneratedTest> Tests,
    [property: JsonRequired] Summary Summary);
