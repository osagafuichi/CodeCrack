using System.Threading;
using System.Threading.Tasks;
using CodeCrack.App.Core.Engine;

namespace CodeCrack.Tests.Fakes;

public sealed class FakeAnalyzer : IAnalyzer
{
    public EngineOutcome Next { get; set; } = new(null, new EngineNotFound("none"));
    public string? LastPath { get; private set; }

    /// When set, AnalyzeAsync returns this (initially incomplete) task instead of a
    /// completed one, so a test can observe the synchronous pre-await state — e.g. the
    /// transient "Analyzing…" status — before releasing the result.
    public TaskCompletionSource<EngineOutcome>? Gate { get; set; }

    public Task<EngineOutcome> AnalyzeAsync(string filePath, CancellationToken ct = default)
    {
        LastPath = filePath;
        // Honor cancellation while gated so timeout/cancel paths are testable; an
        // ungated call resolves immediately with Next.
        return Gate is null ? Task.FromResult(Next) : Gate.Task.WaitAsync(ct);
    }
}
