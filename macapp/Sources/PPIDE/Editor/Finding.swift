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

/// A pytest test the engine generated for a finding (viewed read-only in v1).
struct GeneratedTest: Codable, Identifiable {
    let finding_id: String
    let test_name: String
    let source: String        // runnable pytest source
    let expects: String       // "raises" | "assertion" | "regression"

    /// Stable identity for SwiftUI lists. `finding_id` alone can repeat if a finding
    /// yields multiple tests, so we combine it with the test name.
    var id: String { finding_id + test_name }
}

/// Counts the engine reports alongside the findings/tests.
struct Summary: Codable {
    let findings: Int
    let tests: Int
}
