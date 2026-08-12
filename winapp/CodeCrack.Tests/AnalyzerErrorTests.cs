using CodeCrack.App.Core.Engine;
using Xunit;

namespace CodeCrack.Tests;

public class AnalyzerErrorTests
{
    [Fact]
    public void EngineNotFound_mentions_path_env_and_setting()
    {
        AnalyzerError e = new EngineNotFound(@"C:\app\Resources\engine");
        Assert.Contains(@"CodeCrack engine not found (C:\app\Resources\engine).", e.Message);
        Assert.Contains("CODECRACK_ENGINE_DIR", e.Message);
        Assert.Contains("enginePathOverride", e.Message);
    }

    [Fact]
    public void LaunchFailed_prefixes_detail()
    {
        AnalyzerError e = new LaunchFailed("The system cannot find the file specified.");
        Assert.Equal("Failed to launch python: The system cannot find the file specified.", e.Message);
    }

    [Fact]
    public void EngineFailed_includes_code_dir_and_trimmed_stderr()
    {
        AnalyzerError e = new EngineFailed(2, "  boom  \n", @"C:\eng");
        Assert.Contains("Analysis failed (exit 2).", e.Message);
        Assert.Contains(@"Engine: C:\eng", e.Message);
        Assert.Contains("boom", e.Message);
        Assert.DoesNotContain("No error output.", e.Message);
    }

    [Fact]
    public void EngineFailed_empty_stderr_says_no_output()
    {
        AnalyzerError e = new EngineFailed(1, "   \n", "d");
        Assert.Contains("No error output.", e.Message);
    }

    [Fact]
    public void DecodeFailed_reports_empty_stdout_and_stderr()
    {
        AnalyzerError e = new DecodeFailed("unexpected end of data", "d", "", "trace");
        Assert.Contains("Couldn't read the engine's output: unexpected end of data", e.Message);
        Assert.Contains("Engine stdout was empty.", e.Message);
        Assert.Contains("stderr: trace", e.Message);
    }
}
