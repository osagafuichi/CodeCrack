using System.Diagnostics;
using CodeCrack.App.Core.Engine;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests.Integration;

// Shares a collection with PythonInvocationTests so the two don't race on
// BaseDirectory\python (that test transiently creates/deletes a fake python.exe).
[Collection("PythonEnv")]
public class EngineEndToEndTests
{
    private static string? FindRepoEngine()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            var eng = Path.Combine(dir, "engine");
            if (File.Exists(Path.Combine(eng, "codecrack", "__main__.py")))
                return eng;
            var parent = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            if (parent == dir) break;
            dir = parent;
        }
        return null;
    }

    private static bool PythonCanImportCodecrack(string engineDir, string exe, string[] leading)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = engineDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in leading) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import codecrack");
            using var p = Process.Start(psi)!;
            return p.WaitForExit(20000) && p.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public async Task Analyzes_a_real_fixture_end_to_end()
    {
        var engine = FindRepoEngine();
        if (engine is null) return; // no engine checkout — skip

        var (exe, leading) = PythonInvocation.Resolve();
        if (!PythonCanImportCodecrack(engine, exe, leading)) return; // no usable python — skip

        var fixture = Path.Combine(engine, "tests", "fixtures", "zero_division.py");
        if (!File.Exists(fixture)) return;

        IAnalyzer analyzer = new Analyzer(new FakeAppSettings { EnginePathOverride = engine });
        var outcome = await analyzer.AnalyzeAsync(fixture);

        Assert.True(outcome.IsSuccess, outcome.Error?.Message);
        Assert.NotNull(outcome.Result);
        Assert.NotEmpty(outcome.Result!.Findings);
    }
}
