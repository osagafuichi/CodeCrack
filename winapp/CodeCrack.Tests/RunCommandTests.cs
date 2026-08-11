using System.IO;
using System.Linq;
using CodeCrack.App.Core.Engine;
using Xunit;

namespace CodeCrack.Tests;

public sealed class RunCommandTests
{
    private static string P(string name) => Path.Combine(Path.GetTempPath(), name);

    [Fact]
    public void Unknown_extension_is_not_runnable()
    {
        Assert.Null(RunCommandTable.For(P("readme.md")));
        Assert.Null(RunCommandTable.For(P("data.csv")));
    }

    [Fact]
    public void Known_extensions_map_to_windows_tools()
    {
        Assert.Equal("node", RunCommandTable.For(P("a.js"))!.Tool);
        Assert.Equal("node", RunCommandTable.For(P("a.mjs"))!.Tool);
        Assert.Equal("ruby", RunCommandTable.For(P("a.rb"))!.Tool);
        Assert.Equal("go", RunCommandTable.For(P("a.go"))!.Tool);
        Assert.Equal(new[] { "run", "a.go" }, RunCommandTable.For(P("a.go"))!.Args);
        Assert.Equal("php", RunCommandTable.For(P("a.php"))!.Tool);
        Assert.Equal("perl", RunCommandTable.For(P("a.pl"))!.Tool);
        Assert.Equal("swift", RunCommandTable.For(P("a.swift"))!.Tool);
        Assert.Equal("powershell", RunCommandTable.For(P("a.ps1"))!.Tool);
    }

    [Fact]
    public void Python_uses_resolved_interpreter_not_python3_literal()
    {
        var cmd = RunCommandTable.For(P("bug.py"))!;
        Assert.Contains("bug.py", cmd.Args);
        Assert.NotEqual("python3", cmd.Tool);
        Assert.DoesNotContain("/usr/bin/env", cmd.Display);
    }

    [Fact]
    public void Java_uses_JAVA_HOME_or_path_never_posix_java_home_or_bash()
    {
        var cmd = RunCommandTable.For(P("Main.java"))!;
        Assert.Equal("cmd", cmd.Tool);
        Assert.Contains("javac", cmd.Display);
        Assert.Contains("Main", cmd.Display); // base class name
        Assert.DoesNotContain("java_home", cmd.Display);   // no /usr/libexec/java_home
        Assert.DoesNotContain("bash", cmd.Display);
        Assert.DoesNotContain("-lc", cmd.Display);
    }

    [Fact]
    public void No_command_carries_posix_leftovers()
    {
        var exts = new[] { "bug.py", "a.js", "a.mjs", "a.cjs", "a.rb",
                           "a.swift", "a.go", "a.php", "a.pl", "Main.java", "a.ps1" };
        foreach (var name in exts)
        {
            var cmd = RunCommandTable.For(P(name))!;
            Assert.DoesNotContain("/usr/bin/env", cmd.Display);
            Assert.DoesNotContain("java_home", cmd.Display);
            Assert.DoesNotContain("bash", cmd.Display);
        }
    }
}
