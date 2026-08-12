namespace CodeCrack.App.Core.Engine;

public sealed record EngineOutcome(AnalysisResult? Result, AnalyzerError? Error)
{
    public bool IsSuccess => Error is null;
}
