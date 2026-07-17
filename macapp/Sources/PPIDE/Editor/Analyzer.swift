import Foundation

/// Failure modes surfaced to the Issues panel instead of crashing.
enum AnalyzerError: Error {
    /// The engine directory couldn't be found on disk.
    case engineNotFound(String)
    /// The `python3` subprocess failed to launch.
    case launchFailed(String)
    /// The engine exited non-zero; carries stderr for display.
    case engineFailed(code: Int32, stderr: String)
    /// The engine exited 0 but its stdout didn't decode as `AnalysisResult`.
    case decodeFailed(String)

    var message: String {
        switch self {
        case .engineNotFound(let path):
            return "CodeCrack engine not found at \(path).\n"
                + "Set CODECRACK_ENGINE_DIR to the repo's engine/ directory."
        case .launchFailed(let detail):
            return "Failed to launch python3: \(detail)"
        case .engineFailed(let code, let stderr):
            let trimmed = stderr.trimmingCharacters(in: .whitespacesAndNewlines)
            return "Analysis failed (exit \(code)).\n" + (trimmed.isEmpty ? "No error output." : trimmed)
        case .decodeFailed(let detail):
            return "Couldn't read the engine's output: \(detail)"
        }
    }
}

/// Runs the CodeCrack Python engine on a file and decodes its JSON findings.
///
/// Modeled on `Runner.start` (Homebrew-augmented `PATH`, main-queue callbacks), but the
/// engine speaks a single JSON document on stdout rather than a live stream — so we buffer
/// all of stdout, keep it separate from stderr, and decode once on completion.
enum Analyzer {
    /// Last-resort absolute fallback to the engine package's parent directory (`engine/`,
    /// which contains the importable `codecrack` package). Only used when the env var is
    /// unset and no `engine/codecrack` can be discovered by walking up from the file — see
    /// `engineDir(for:)`. Update this if the canonical checkout location changes.
    static let defaultEngineDir =
        "/Users/osagafuichi/Library/Application Support/ucode/workspaces/PP-31845c/wire-engine/engine"

    /// Resolves the engine directory for a file, in precedence order:
    ///   (a) the `CODECRACK_ENGINE_DIR` environment variable, if set;
    ///   (b) an `engine/` dir discovered by walking UP from the file's directory and
    ///       looking for `engine/codecrack/__main__.py` — so Analyze "just works" for any
    ///       file inside the repo, regardless of which workspace/checkout it lives in;
    ///   (c) the `defaultEngineDir` constant as a last resort.
    static func engineDir(for file: URL) -> URL {
        if let env = ProcessInfo.processInfo.environment["CODECRACK_ENGINE_DIR"], !env.isEmpty {
            return URL(fileURLWithPath: env, isDirectory: true)
        }
        if let discovered = discoverEngineDir(from: file.deletingLastPathComponent()) {
            return discovered
        }
        return URL(fileURLWithPath: defaultEngineDir, isDirectory: true)
    }

    /// Walks up from `start`, returning the first ancestor's `engine/` directory that
    /// contains `codecrack/__main__.py`, or nil if none is found before the filesystem root.
    private static func discoverEngineDir(from start: URL) -> URL? {
        let fm = FileManager.default
        var dir = start.standardizedFileURL
        while true {
            let engine = dir.appendingPathComponent("engine", isDirectory: true)
            let marker = engine.appendingPathComponent("codecrack/__main__.py")
            if fm.fileExists(atPath: marker.path) { return engine }
            let parent = dir.deletingLastPathComponent().standardizedFileURL
            if parent.path == dir.path { return nil }   // reached the filesystem root
            dir = parent
        }
    }

    /// Analyzes `file` and delivers the decoded result (or an error) on the main queue.
    static func analyze(_ file: URL, completion: @escaping (Result<AnalysisResult, AnalyzerError>) -> Void) {
        let engine = engineDir(for: file)

        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: engine.path, isDirectory: &isDir), isDir.boolValue else {
            DispatchQueue.main.async { completion(.failure(.engineNotFound(engine.path))) }
            return
        }

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/env")
        process.arguments = ["python3", "-m", "codecrack", "analyze", file.path, "--json"]
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
                result = .failure(.engineFailed(code: proc.terminationStatus, stderr: stderr))
            } else {
                do {
                    let decoded = try JSONDecoder().decode(AnalysisResult.self, from: outData)
                    result = .success(decoded)
                } catch {
                    let raw = String(data: outData, encoding: .utf8) ?? ""
                    let detail = raw.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                        ? error.localizedDescription
                        : "\(error.localizedDescription)"
                    result = .failure(.decodeFailed(detail))
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
