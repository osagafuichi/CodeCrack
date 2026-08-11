using System;
using System.Runtime.InteropServices;
using System.Threading;
using CodeCrack.App.Core.Engine;
using Xunit;

namespace CodeCrack.Tests;

public sealed class RunnerTests
{
    [Fact]
    public void Start_streams_output_and_reports_exit_code()
    {
        Assert.True(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "Runner functional test targets Windows.");

        var output = "";
        int exit = int.MinValue;
        var done = new ManualResetEventSlim(false);

        var cmd = new RunCommand("cmd", new[] { "/c", "echo hello" }, Environment.CurrentDirectory);
        var session = Runner.Start(cmd,
            onOutput: s => output += s,
            onFinish: code => { exit = code; done.Set(); });

        Assert.NotNull(session);
        Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "process did not exit in time");
        Assert.Contains("hello", output);
        Assert.Equal(0, exit);
    }
}
