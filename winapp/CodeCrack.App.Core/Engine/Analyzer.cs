using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodeCrack.App.Core.Settings;

namespace CodeCrack.App.Core.Engine;

public sealed class Analyzer(IAppSettings settings, IProcessConfiner? confiner = null) : IAnalyzer
{
    private readonly IAppSettings _settings = settings;
    private readonly IProcessConfiner _confiner = confiner ?? NullProcessConfiner.Instance;

    public async Task<EngineOutcome> AnalyzeAsync(string filePath, CancellationToken ct = default)
    {
        var engineDir = EngineLocator.Resolve(filePath, _settings);
        if (engineDir is null)
            return new EngineOutcome(null, new EngineNotFound("no bundled, override, or checkout engine\\"));
        if (!Directory.Exists(engineDir))
            return new EngineOutcome(null, new EngineNotFound(engineDir));

        var (exe, leadingArgs) = PythonInvocation.Resolve();

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = engineDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in leadingArgs) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add("codecrack");
        psi.ArgumentList.Add("analyze");
        psi.ArgumentList.Add(filePath);
        psi.ArgumentList.Add("--json");

        using var process = new Process { StartInfo = psi };

        try
        {
            if (!process.Start())
                return new EngineOutcome(null, new LaunchFailed("process did not start"));
        }
        catch (Exception ex)
        {
            return new EngineOutcome(null, new LaunchFailed(ex.Message));
        }

        // Confine the untrusted engine child (+ the pytest it spawns) to a memory/process
        // cap; disposing at method end reaps the whole tree — including on cancel/timeout,
        // so a runaway analysis can't leave survivors. No-op off Windows.
        using var confinement = _confiner.Confine(process);

        // Buffer stdout (the JSON) and stderr separately to avoid interleave/deadlock.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
            return new EngineOutcome(null, new EngineFailed(process.ExitCode, stderr, engineDir));

        try
        {
            var result = JsonSerializer.Deserialize<AnalysisResult>(stdout, EngineJson.Options);
            return result is null
                ? new EngineOutcome(null, new DecodeFailed("engine returned null", engineDir, stdout, stderr))
                : new EngineOutcome(result, null);
        }
        catch (JsonException ex)
        {
            return new EngineOutcome(null, new DecodeFailed(ex.Message, engineDir, stdout, stderr));
        }
    }
}
