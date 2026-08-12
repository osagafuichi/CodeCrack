using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace CodeCrack.App.Core.Engine;

public interface IRunSession
{
    void Send(string text);
    void Stop();
}

/// A running process you can send stdin to and stop.
public sealed class RunSession : IRunSession
{
    private readonly Process _process;
    internal RunSession(Process process) => _process = process;

    public void Send(string text)
    {
        try
        {
            _process.StandardInput.Write(text + "\n");
            _process.StandardInput.Flush();
        }
        catch { /* process already exited */ }
    }

    public void Stop()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch { /* already gone */ }
    }
}

/// Runs a file with the right interpreter/compiler and streams merged stdout+stderr.
/// Callbacks are posted to the SynchronizationContext captured at Start (the UI thread
/// in the app; null in headless tests -> invoked inline).
public static class Runner
{
    public static IRunSession? Start(RunCommand command,
                                     Action<string> onOutput,
                                     Action<int> onFinish)
    {
        var sync = SynchronizationContext.Current;
        void Post(Action a) { if (sync is null) a(); else sync.Post(_ => a(), null); }

        var psi = new ProcessStartInfo
        {
            FileName = command.Tool,
            WorkingDirectory = command.Cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in command.Args) psi.ArgumentList.Add(arg);

        var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Post(() => onOutput(e.Data + "\n")); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) Post(() => onOutput(e.Data + "\n")); };

        try { process.Start(); }
        catch (Exception ex)
        {
            Post(() => { onOutput($"Failed to launch: {ex.Message}\n"); onFinish(-1); });
            return null;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Signal completion only AFTER the process exits AND the async stdout/stderr
        // readers have fully drained. The Process.Exited event can fire before the last
        // OutputDataReceived callback, so we don't use it; the parameterless
        // WaitForExit() blocks until asynchronous output handling has completed, which
        // guarantees every onOutput is posted before onFinish (a real race that made
        // RunnerTests flaky on CI). Runs on a dedicated background thread so WaitForExit
        // never blocks the caller or a captured UI SynchronizationContext.
        var waiter = new Thread(() =>
        {
            try { process.WaitForExit(); } catch { /* process already reaped */ }
            int code;
            try { code = process.ExitCode; } catch { code = -1; }
            Post(() => onFinish(code));
        })
        { IsBackground = true, Name = "codecrack-runner-waiter" };
        waiter.Start();

        return new RunSession(process);
    }
}
