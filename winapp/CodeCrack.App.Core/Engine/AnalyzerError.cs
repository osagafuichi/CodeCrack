namespace CodeCrack.App.Core.Engine;

/// Failure modes surfaced to the Issues panel instead of crashing.
/// Message text mirrors macapp/Sources/PPIDE/Editor/Analyzer.swift, with the
/// engine-locator guidance adjusted to Windows paths ("engine\") and "python".
public abstract record AnalyzerError
{
    public abstract string Message { get; }

    protected static string Snippet(string text, int limit = 600) =>
        text.Length <= limit ? text : text[..limit] + "… (truncated)";
}

public sealed record EngineNotFound(string Path) : AnalyzerError
{
    public override string Message =>
        $"CodeCrack engine not found ({Path}).\n"
        + "The bundled engine is missing; reinstall the app, or set "
        + "CODECRACK_ENGINE_DIR (or the enginePathOverride setting) to a checkout's engine\\ directory.";
}

public sealed record LaunchFailed(string Detail) : AnalyzerError
{
    public override string Message => $"Failed to launch python: {Detail}";
}

public sealed record EngineFailed(int Code, string Stderr, string EngineDir) : AnalyzerError
{
    public override string Message
    {
        get
        {
            var trimmed = Stderr.Trim();
            return $"Analysis failed (exit {Code}).\n"
                + $"Engine: {EngineDir}\n"
                + (trimmed.Length == 0 ? "No error output." : Snippet(trimmed));
        }
    }
}

public sealed record DecodeFailed(string Detail, string EngineDir, string RawStdout, string Stderr) : AnalyzerError
{
    public override string Message
    {
        get
        {
            var parts = new List<string>
            {
                $"Couldn't read the engine's output: {Detail}",
                $"Engine: {EngineDir}",
            };
            var outT = RawStdout.Trim();
            parts.Add(outT.Length == 0 ? "Engine stdout was empty." : $"stdout: {Snippet(outT)}");
            var errT = Stderr.Trim();
            if (errT.Length != 0) parts.Add($"stderr: {Snippet(errT)}");
            return string.Join("\n", parts);
        }
    }
}
