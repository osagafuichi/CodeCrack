using CodeCrack.App.Core.Engine;
using Xunit;

namespace CodeCrack.Tests;

// Serialized with EngineEndToEndTests (shared collection) so the two don't race
// on BaseDirectory\python — this test transiently creates/deletes a fake python.exe.
[Collection("PythonEnv")]
public class PythonInvocationTests
{
    private static string BundledExe =>
        Path.Combine(AppContext.BaseDirectory, "python", "python.exe");

    [Fact]
    public void Falls_back_to_py_dash_3_when_no_bundled_runtime()
    {
        var dir = Path.GetDirectoryName(BundledExe)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        var (exe, args) = PythonInvocation.Resolve();

        Assert.Equal("py", exe);
        Assert.Equal(new[] { "-3" }, args);
    }

    [Fact]
    public void Prefers_bundled_python_beside_the_app()
    {
        var dir = Path.GetDirectoryName(BundledExe)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(BundledExe, ""); // presence is enough; not executed here
        try
        {
            var (exe, args) = PythonInvocation.Resolve();
            Assert.Equal(BundledExe, exe);
            Assert.Empty(args);
        }
        finally
        {
            File.Delete(BundledExe);
            Directory.Delete(dir);
        }
    }
}
