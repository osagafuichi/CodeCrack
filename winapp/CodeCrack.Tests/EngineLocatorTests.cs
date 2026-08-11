using CodeCrack.App.Core.Engine;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public class EngineLocatorTests : IDisposable
{
    private readonly string? _savedEnv;
    private readonly string _tmp;

    public EngineLocatorTests()
    {
        _savedEnv = Environment.GetEnvironmentVariable("CODECRACK_ENGINE_DIR");
        Environment.SetEnvironmentVariable("CODECRACK_ENGINE_DIR", null);
        _tmp = Path.Combine(Path.GetTempPath(), "cc-loc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CODECRACK_ENGINE_DIR", _savedEnv);
        try { Directory.Delete(_tmp, recursive: true); } catch { /* best effort */ }
    }

    private static string MakeEngine(string root)
    {
        var eng = Path.Combine(root, "engine");
        Directory.CreateDirectory(Path.Combine(eng, "codecrack"));
        File.WriteAllText(Path.Combine(eng, "codecrack", "__main__.py"), "");
        return eng;
    }

    [Fact]
    public void Override_wins_verbatim_even_without_marker()
    {
        var s = new FakeAppSettings { EnginePathOverride = @"C:\custom\engine" };
        Assert.Equal(@"C:\custom\engine", EngineLocator.Resolve(Path.Combine(_tmp, "a.py"), s));
    }

    [Fact]
    public void Env_used_when_no_override()
    {
        Environment.SetEnvironmentVariable("CODECRACK_ENGINE_DIR", @"D:\env\engine");
        var s = new FakeAppSettings();
        Assert.Equal(@"D:\env\engine", EngineLocator.Resolve(Path.Combine(_tmp, "a.py"), s));
    }

    [Fact]
    public void Walks_up_from_file_dir_to_find_engine()
    {
        var eng = MakeEngine(_tmp);
        var nested = Path.Combine(_tmp, "src", "pkg");
        Directory.CreateDirectory(nested);
        var file = Path.Combine(nested, "mod.py");
        File.WriteAllText(file, "");
        Assert.Equal(eng, EngineLocator.Resolve(file, new FakeAppSettings()));
    }

    [Fact]
    public void Returns_null_when_nothing_found()
    {
        var file = Path.Combine(_tmp, "lonely.py");
        File.WriteAllText(file, "");
        Assert.Null(EngineLocator.Resolve(file, new FakeAppSettings()));
    }
}
