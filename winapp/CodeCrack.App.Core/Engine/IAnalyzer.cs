namespace CodeCrack.App.Core.Engine;

public interface IAnalyzer
{
    Task<EngineOutcome> AnalyzeAsync(string filePath, CancellationToken ct = default);
}
