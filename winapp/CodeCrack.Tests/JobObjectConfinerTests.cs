using System.Diagnostics;
using CodeCrack.App.Core.Engine;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

/// Windows-gated: the Job Object confiner is a no-op off Windows, so these silently pass
/// there. (CI runs csharp-tests on windows-latest.)
[Collection("PythonEnv")]
public class JobObjectConfinerTests
{
    [Fact]
    public void Dispose_reaps_the_confined_process()
    {
        if (!OperatingSystem.IsWindows()) return;

        // cmd launches a long ping (a child) → stays alive ~60s unless the job kills it.
        var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 61 127.0.0.1 >nul")
        { UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;

        var scope = new JobObjectProcessConfiner().Confine(p);
        Assert.False(p.HasExited);

        scope.Dispose(); // kill-on-close reaps the tree
        Assert.True(p.WaitForExit(4000), "confined process was not reaped after the job closed");
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        if (!OperatingSystem.IsWindows()) return;
        var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 61 127.0.0.1 >nul")
        { UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var scope = new JobObjectProcessConfiner().Confine(p);
        scope.Dispose();
        scope.Dispose(); // must not throw
        Assert.True(p.WaitForExit(4000));
    }

    [Fact]
    public void Memory_cap_kills_an_over_allocating_child()
    {
        if (!OperatingSystem.IsWindows()) return;
        var python = ResolvePython();
        if (python is null) return; // no python on PATH — skip

        // 256 MiB job cap; the child tries to commit ~1 GiB -> allocation fails / killed.
        var confiner = new JobObjectProcessConfiner(memoryCapBytes: 256L * 1024 * 1024, activeProcessCap: 8);
        var psi = new ProcessStartInfo(python.Value.Exe)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in python.Value.Leading) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("b = bytearray(1024*1024*1024); print(len(b))");

        using var p = Process.Start(psi)!;
        using var scope = confiner.Confine(p);
        Assert.True(p.WaitForExit(20000), "over-allocating child did not exit under the memory cap");
        Assert.NotEqual(0, p.ExitCode); // MemoryError / killed, not a clean success
    }

    [Fact]
    public async Task Confined_analyze_still_returns_findings()
    {
        if (!OperatingSystem.IsWindows()) return;
        var engine = FindRepoEngine();
        if (engine is null) return;
        var (exe, leading) = PythonInvocation.Resolve();
        if (!PythonCanImportCodecrack(engine, exe, leading)) return;
        var fixture = Path.Combine(engine, "tests", "fixtures", "zero_division.py");
        if (!File.Exists(fixture)) return;

        IAnalyzer analyzer = new Analyzer(
            new FakeAppSettings { EnginePathOverride = engine }, new JobObjectProcessConfiner());
        var outcome = await analyzer.AnalyzeAsync(fixture);

        Assert.True(outcome.IsSuccess, outcome.Error?.Message);
        Assert.NotEmpty(outcome.Result!.Findings);
    }

    // --- helpers (mirror EngineEndToEndTests) ---------------------------------------

    private static (string Exe, string[] Leading)? ResolvePython()
    {
        foreach (var (exe, leading) in new[] { ("python", System.Array.Empty<string>()), ("py", new[] { "-3" }) })
        {
            try
            {
                var psi = new ProcessStartInfo(exe)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var a in leading) psi.ArgumentList.Add(a);
                psi.ArgumentList.Add("--version");
                using var p = Process.Start(psi);
                if (p != null && p.WaitForExit(5000) && p.ExitCode == 0) return (exe, leading);
            }
            catch { /* try next */ }
        }
        return null;
    }

    private static string? FindRepoEngine()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            var eng = Path.Combine(dir, "engine");
            if (File.Exists(Path.Combine(eng, "codecrack", "__main__.py"))) return eng;
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
}
