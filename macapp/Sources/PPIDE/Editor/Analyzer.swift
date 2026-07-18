import Foundation

/// Failure modes surfaced to the Issues panel instead of crashing.
enum AnalyzerError: Error {
    /// The engine directory couldn't be found on disk.
    case engineNotFound(String)
    /// The `python3` subprocess failed to launch.
    case launchFailed(String)
    /// The engine exited non-zero; carries stderr + the engine dir that ran.
    case engineFailed(code: Int32, stderr: String, engineDir: String)
    /// The engine exited 0 but its stdout didn't decode as `AnalysisResult`.
    /// Carries the engine dir that ran plus raw stdout/stderr so a version/shape
    /// skew (e.g. an old engine emitting a different JSON shape) is diagnosable.
    case decodeFailed(detail: String, engineDir: String, rawStdout: String, stderr: String)

    var message: String {
        switch self {
        case .engineNotFound(let path):
            return "CodeCrack engine not found (\(path)).\n"
                + "The bundled engine is missing; reinstall the app, or set "
                + "CODECRACK_ENGINE_DIR (or the enginePathOverride setting) to a checkout's engine/ directory."
        case .launchFailed(let detail):
            return "Failed to launch python3: \(detail)"
        case .engineFailed(let code, let stderr, let engineDir):
            let trimmed = stderr.trimmingCharacters(in: .whitespacesAndNewlines)
            return "Analysis failed (exit \(code)).\n"
                + "Engine: \(engineDir)\n"
                + (trimmed.isEmpty ? "No error output." : AnalyzerError.snippet(trimmed))
        case .decodeFailed(let detail, let engineDir, let rawStdout, let stderr):
            var parts = [
                "Couldn't read the engine's output: \(detail)",
                "Engine: \(engineDir)",
            ]
            let out = rawStdout.trimmingCharacters(in: .whitespacesAndNewlines)
            parts.append(out.isEmpty ? "Engine stdout was empty." : "stdout: \(AnalyzerError.snippet(out))")
            let err = stderr.trimmingCharacters(in: .whitespacesAndNewlines)
            if !err.isEmpty { parts.append("stderr: \(AnalyzerError.snippet(err))") }
            return parts.joined(separator: "\n")
        }
    }

    /// Trim long engine output to a readable snippet for the Issues panel.
    private static func snippet(_ text: String, limit: Int = 600) -> String {
        text.count <= limit ? text : String(text.prefix(limit)) + "… (truncated)"
    }
}

/// Runs the CodeCrack Python engine on a file and decodes its JSON findings.
///
/// Modeled on `Runner.start` (Homebrew-augmented `PATH`, main-queue callbacks), but the
/// engine speaks a single JSON document on stdout rather than a live stream — so we buffer
/// all of stdout, keep it separate from stderr, and decode once on completion.
enum Analyzer {
    /// UserDefaults key an (M3) Settings screen will write to point the app at a
    /// checkout's `engine/` directory. Read here so the override chain is wired up
    /// today even though the Settings UI lands later.
    static let enginePathOverrideKey = "enginePathOverride"

    /// A directory is a valid engine root iff it contains `codecrack/__main__.py`
    /// (the `python -m codecrack` entry point).
    private static func isEngineDir(_ dir: URL) -> Bool {
        let marker = dir.appendingPathComponent("codecrack/__main__.py")
        return FileManager.default.fileExists(atPath: marker.path)
    }

    /// Resolves the engine directory for a file, in precedence order:
    ///   (a) an explicit override — the `enginePathOverride` UserDefaults key, else the
    ///       `CODECRACK_ENGINE_DIR` environment variable — used verbatim if non-empty;
    ///   (b) the engine bundled inside the app at `Resources/engine` (the shipping path);
    ///   (c) an `engine/` dir discovered by walking UP from the file's directory (so
    ///       running from a source checkout "just works" for any file inside the repo);
    ///   (d) nil — surfaced as `engineNotFound` rather than a hardcoded fallback path.
    static func engineDir(for file: URL) -> URL? {
        if let override = overrideEngineDir(), !override.isEmpty {
            return URL(fileURLWithPath: override, isDirectory: true)
        }
        if let bundled = bundledEngineDir(), isEngineDir(bundled) {
            return bundled
        }
        if let discovered = discoverEngineDir(from: file.deletingLastPathComponent()) {
            return discovered
        }
        return nil
    }

    /// The explicit override, preferring the UserDefaults key (Settings) over the env var.
    private static func overrideEngineDir() -> String? {
        if let pref = UserDefaults.standard.string(forKey: enginePathOverrideKey),
           !pref.isEmpty {
            return pref
        }
        let env = ProcessInfo.processInfo.environment["CODECRACK_ENGINE_DIR"]
        return (env?.isEmpty == false) ? env : nil
    }

    /// The `engine/` directory shipped inside the `.app` (see `make-app.sh`), or nil when
    /// running from a `swift build` binary that has no bundled Resources.
    static func bundledEngineDir() -> URL? {
        Bundle.main.resourceURL?.appendingPathComponent("engine", isDirectory: true)
    }

    /// Resolves how to invoke Python, preferring the CPython embedded in the app bundle
    /// (`Resources/python/bin/python3`) so the app runs without a system python3 — and
    /// so the execute stage's `sys.executable -m pytest` subprocess uses an interpreter
    /// that ships pytest. Falls back to `/usr/bin/env python3` (detect-and-use system)
    /// when the bundled runtime is missing, e.g. running from a plain `swift build` binary.
    ///
    /// Returns the executable to launch plus any leading args before the engine args.
    static func pythonInvocation() -> (executable: URL, leadingArgs: [String]) {
        if let bundled = bundledPythonInterpreter() {
            return (bundled, [])
        }
        return (URL(fileURLWithPath: "/usr/bin/env"), ["python3"])
    }

    /// The embedded interpreter shipped inside the `.app`, if present and executable.
    static func bundledPythonInterpreter() -> URL? {
        guard let res = Bundle.main.resourceURL else { return nil }
        let py = res.appendingPathComponent("python/bin/python3")
        return FileManager.default.isExecutableFile(atPath: py.path) ? py : nil
    }

    /// Walks up from `start`, returning the first ancestor's `engine/` directory that
    /// contains `codecrack/__main__.py`, or nil if none is found before the filesystem root.
    private static func discoverEngineDir(from start: URL) -> URL? {
        var dir = start.standardizedFileURL
        while true {
            let engine = dir.appendingPathComponent("engine", isDirectory: true)
            if isEngineDir(engine) { return engine }
            let parent = dir.deletingLastPathComponent().standardizedFileURL
            if parent.path == dir.path { return nil }   // reached the filesystem root
            dir = parent
        }
    }

    /// Analyzes `file` and delivers the decoded result (or an error) on the main queue.
    static func analyze(_ file: URL, completion: @escaping (Result<AnalysisResult, AnalyzerError>) -> Void) {
        guard let engine = engineDir(for: file) else {
            DispatchQueue.main.async { completion(.failure(.engineNotFound("no bundled, override, or checkout engine/"))) }
            return
        }

        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: engine.path, isDirectory: &isDir), isDir.boolValue else {
            DispatchQueue.main.async { completion(.failure(.engineNotFound(engine.path))) }
            return
        }

        let (interpreter, leadingArgs) = pythonInvocation()
        let process = Process()
        process.executableURL = interpreter
        process.arguments = leadingArgs + ["-m", "codecrack", "analyze", file.path, "--json"]
        process.currentDirectoryURL = engine

        // GUI apps launch with a minimal PATH; add common tool locations (as Runner does).
        var env = ProcessInfo.processInfo.environment
        let extras = "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
        env["PATH"] = extras + ":" + (env["PATH"] ?? "")
        process.environment = env

        // Keep stdout (the JSON) strictly separate from stderr (errors/tracebacks).
        let outPipe = Pipe()
        let errPipe = Pipe()
        process.standardOutput = outPipe
        process.standardError = errPipe

        process.terminationHandler = { proc in
            let outData = outPipe.fileHandleForReading.readDataToEndOfFile()
            let errData = errPipe.fileHandleForReading.readDataToEndOfFile()
            let stderr = String(data: errData, encoding: .utf8) ?? ""

            let result: Result<AnalysisResult, AnalyzerError>
            if proc.terminationStatus != 0 {
                result = .failure(.engineFailed(
                    code: proc.terminationStatus, stderr: stderr, engineDir: engine.path))
            } else {
                do {
                    let decoded = try JSONDecoder().decode(AnalysisResult.self, from: outData)
                    result = .success(decoded)
                } catch {
                    let raw = String(data: outData, encoding: .utf8) ?? ""
                    result = .failure(.decodeFailed(
                        detail: error.localizedDescription,
                        engineDir: engine.path,
                        rawStdout: raw,
                        stderr: stderr))
                }
            }
            DispatchQueue.main.async { completion(result) }
        }

        do {
            try process.run()
        } catch {
            DispatchQueue.main.async { completion(.failure(.launchFailed(error.localizedDescription))) }
        }
    }
}
