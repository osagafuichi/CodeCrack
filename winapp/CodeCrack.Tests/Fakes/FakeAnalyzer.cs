using System.Threading;
using System.Threading.Tasks;
using CodeCrack.App.Core.Engine;

namespace CodeCrack.Tests.Fakes;

public sealed class FakeAnalyzer : IAnalyzer
{
    public EngineOutcome Next { get; set; } = new(null, new EngineNotFound("none"));
    public string? LastPath { get; private set; }

    public Task<EngineOutcome> AnalyzeAsync(string filePath, CancellationToken ct = default)
    {
        LastPath = filePath;
        return Task.FromResult(Next);
    }
}
