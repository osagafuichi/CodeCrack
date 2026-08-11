using CodeCrack.App.Core.Engine;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public class AnalyzerTests
{
    [Fact]
    public async Task Returns_EngineNotFound_when_no_engine_anywhere()
    {
        var saved = Environment.GetEnvironmentVariable("CODECRACK_ENGINE_DIR");
        Environment.SetEnvironmentVariable("CODECRACK_ENGINE_DIR", null);
        var tmp = Path.Combine(Path.GetTempPath(), "cc-an-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmp);
            var file = Path.Combine(tmp, "x.py");
            File.WriteAllText(file, "print(1)");

            IAnalyzer analyzer = new Analyzer(new FakeAppSettings());
            var outcome = await analyzer.AnalyzeAsync(file);

            Assert.False(outcome.IsSuccess);
            Assert.IsType<EngineNotFound>(outcome.Error);
            Assert.Null(outcome.Result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODECRACK_ENGINE_DIR", saved);
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }
}
