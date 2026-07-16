import Foundation

/// A running process you can send stdin to and stop.
final class RunSession {
    fileprivate let process: Process
    fileprivate let stdin: FileHandle

    fileprivate init(process: Process, stdin: FileHandle) {
        self.process = process
        self.stdin = stdin
    }

    /// Send a line of input (a newline is appended) to the process's stdin.
    func send(_ text: String) {
        guard let data = (text + "\n").data(using: .utf8) else { return }
        try? stdin.write(contentsOf: data)
    }

    func stop() {
        if process.isRunning { process.terminate() }
    }
}

/// Runs the current file with the right interpreter/compiler and streams its output.
/// Foundation only — no AppKit. UI callbacks are delivered on the main queue.
enum Runner {
    struct Command {
        let tool: String
        let args: [String]
        let cwd: URL
        var display: String { "\(tool) \(args.joined(separator: " "))" }
    }

    /// Returns how to run `url`, or nil if the file type isn't runnable.
    static func command(for url: URL) -> Command? {
        let dir = url.deletingLastPathComponent()
        let name = url.lastPathComponent
        let base = url.deletingPathExtension().lastPathComponent
        switch url.pathExtension.lowercased() {
        case "py", "pyw":        return Command(tool: "python3", args: [name], cwd: dir)
        case "js", "mjs", "cjs": return Command(tool: "node", args: [name], cwd: dir)
        case "rb":               return Command(tool: "ruby", args: [name], cwd: dir)
        case "sh", "bash":       return Command(tool: "bash", args: [name], cwd: dir)
        case "swift":            return Command(tool: "swift", args: [name], cwd: dir)
        case "go":               return Command(tool: "go", args: ["run", name], cwd: dir)
        case "php":              return Command(tool: "php", args: [name], cwd: dir)
        case "pl":               return Command(tool: "perl", args: [name], cwd: dir)
        // Locate the JDK via java_home, then compile and run the matching class.
        case "java":
            return Command(tool: "bash", args: ["-lc",
                "JH=$(/usr/libexec/java_home 2>&1) && \"$JH/bin/javac\" \(shq(name)) && \"$JH/bin/java\" \(shq(base))"],
                cwd: dir)
        default:                 return nil
        }
    }

    /// Starts `command`, streaming combined stdout/stderr via `onOutput`, then `onFinish`.
    /// Returns a session for sending input, or nil if the process failed to launch.
    static func start(_ command: Command,
                      onOutput: @escaping (String) -> Void,
                      onFinish: @escaping (Int32) -> Void) -> RunSession? {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/env")
        process.arguments = [command.tool] + command.args
        process.currentDirectoryURL = command.cwd

        // GUI apps launch with a minimal PATH; add common tool locations.
        var env = ProcessInfo.processInfo.environment
        let extras = "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
        env["PATH"] = extras + ":" + (env["PATH"] ?? "")
        process.environment = env

        let outPipe = Pipe()
        let inPipe = Pipe()
        process.standardOutput = outPipe
        process.standardError = outPipe
        process.standardInput = inPipe

        outPipe.fileHandleForReading.readabilityHandler = { handle in
            let data = handle.availableData
            guard !data.isEmpty, let text = String(data: data, encoding: .utf8) else { return }
            DispatchQueue.main.async { onOutput(text) }
        }

        process.terminationHandler = { proc in
            outPipe.fileHandleForReading.readabilityHandler = nil
            let rest = outPipe.fileHandleForReading.readDataToEndOfFile()
            DispatchQueue.main.async {
                if !rest.isEmpty, let text = String(data: rest, encoding: .utf8) { onOutput(text) }
                onFinish(proc.terminationStatus)
            }
        }

        do {
            try process.run()
        } catch {
            DispatchQueue.main.async {
                onOutput("Failed to launch: \(error.localizedDescription)\n")
                onFinish(-1)
            }
            return nil
        }
        return RunSession(process: process, stdin: inPipe.fileHandleForWriting)
    }

    /// Single-quote a string for safe use in a bash `-lc` command.
    private static func shq(_ s: String) -> String {
        "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }
}
