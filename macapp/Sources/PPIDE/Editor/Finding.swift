import Foundation

/// Swift mirror of the JSON emitted by `python -m codecrack analyze <file> --json`.
/// The engine prints `{ "findings": [...], "tests": [...], "summary": {...} }`; these
/// `Codable` structs map that directly. The `evidence` field on each finding is
/// intentionally omitted — decoding ignores unknown keys, so we simply don't model it.
struct AnalysisResult: Codable {
    let findings: [Finding]
    let tests: [GeneratedTest]
    let summary: Summary
}

/// One risk the engine detected in the analyzed file.
struct Finding: Codable, Identifiable {
    let id: String            // e.g. "F001"
    let kind: String          // detector name, e.g. "zero-division"
    let target: String        // function the finding lives in
    let location: [Int]       // [line, col], 1-indexed line
    let rationale: String     // human-readable explanation
    let severity: String      // "low" | "medium" | "high"

    /// 1-indexed source line, used for click-to-jump. Defaults to 1 if absent.
    var line: Int { location.first ?? 1 }
}

/// A pytest test the engine generated for a finding, plus the verified outcome of actually
/// executing it (Milestone 1's execute stage). See `engine/codecrack/report/render.py` for
/// the authoritative field docs.
struct GeneratedTest: Codable, Identifiable {
    let finding_id: String
    let test_name: String
    let source: String        // runnable pytest source
    let expects: String       // "raises" | "assertion" | "regression"

    // Execution results (populated by the engine's execute stage).
    let outcome: String?      // "passed" | "failed" | "error" | "skipped", or nil if not executed
    let detail: String        // traceback / failure message / skip reason
    let stdout: String        // stdout captured while the test ran
    let duration: Double      // wall-clock seconds for the test's call phase
    /// The engine's authoritative "this test proved the bug" flag — already accounts for the
    /// inverted oracle (a passing `raises` and a failing `assertion` both mean proven). Consume
    /// this directly; never re-derive it from `outcome`/`expects`.
    let reproduced: Bool

    /// Stable identity for SwiftUI lists. `finding_id` alone can repeat if a finding
    /// yields multiple tests, so we combine it with the test name.
    var id: String { finding_id + test_name }

    /// True for tests that couldn't be run to a verdict and need human input to proceed —
    /// skip scaffolds and regression oracles the engine can't assert on its own.
    var needsInput: Bool { outcome == "skipped" || expects == "regression" }
}

/// Per-outcome counts the engine reports in `summary.by_outcome`.
struct OutcomeCounts: Codable {
    let passed: Int
    let failed: Int
    let error: Int
    let skipped: Int
}

/// Counts the engine reports alongside the findings/tests.
struct Summary: Codable {
    let findings: Int
    let tests: Int
    let executed: Int
    let reproduced: Int
    let by_outcome: OutcomeCounts
}
