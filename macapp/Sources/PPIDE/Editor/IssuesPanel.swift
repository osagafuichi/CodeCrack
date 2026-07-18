import SwiftUI

/// Bottom panel showing the CodeCrack engine's output: detected findings and the pytest
/// tests it would generate. Structured like `ConsolePanel` (title bar + Clear/Close),
/// with a segmented control switching between the two views. Read-only in v1.
struct IssuesPanel: View {
    let findings: [Finding]
    let tests: [GeneratedTest]
    let summary: Summary?
    let isAnalyzing: Bool
    let errorMessage: String?
    var onSelect: (Int) -> Void
    /// Jump the editor to the finding a test targets (by finding id).
    var onSelectFinding: (String) -> Void
    var onClear: () -> Void
    var onClose: () -> Void

    enum Segment: String, CaseIterable, Identifiable {
        case issues = "Issues"
        case tests = "Tests"
        var id: String { rawValue }
    }

    @State private var segment: Segment = .issues

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 8) {
                Text("CodeCrack")
                    .font(.caption).bold()
                    .foregroundStyle(.secondary)
                if isAnalyzing { ProgressView().controlSize(.small) }

                Picker("", selection: $segment) {
                    ForEach(Segment.allCases) { seg in
                        Text(seg == .issues ? "Issues (\(findings.count))" : "Tests (\(tests.count))")
                            .tag(seg)
                    }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .fixedSize()
                .padding(.leading, 4)

                Spacer()
                Button(action: onClear) { Image(systemName: "trash") }
                    .buttonStyle(.plain).foregroundStyle(.secondary)
                    .help("Clear")
                Button(action: onClose) { Image(systemName: "xmark") }
                    .buttonStyle(.plain).foregroundStyle(.secondary)
                    .help("Close")
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .background(Color(nsColor: .windowBackgroundColor))
            Divider()

            content
        }
        .background(Color(nsColor: .textBackgroundColor))
    }

    @ViewBuilder private var content: some View {
        if let errorMessage {
            ScrollView {
                Text(errorMessage)
                    .font(.system(.body, design: .monospaced))
                    .foregroundStyle(.red)
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(14)
            }
        } else {
            switch segment {
            case .issues: issuesList
            case .tests: testsList
            }
        }
    }

    // MARK: - Issues

    @ViewBuilder private var issuesList: some View {
        if findings.isEmpty {
            emptyState("No issues found.")
        } else {
            List(findings) { finding in
                Button {
                    onSelect(finding.line)
                } label: {
                    HStack(alignment: .top, spacing: 10) {
                        severityChip(finding.severity)
                        Text("L\(finding.line)")
                            .font(.system(.caption, design: .monospaced))
                            .foregroundStyle(.secondary)
                            .frame(minWidth: 34, alignment: .leading)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(finding.kind)
                                .font(.caption).bold()
                                .foregroundStyle(.secondary)
                            Text(finding.rationale)
                                .font(.callout)
                                .foregroundStyle(.primary)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                        Spacer(minLength: 0)
                    }
                    .contentShape(Rectangle())
                    .padding(.vertical, 2)
                }
                .buttonStyle(.plain)
            }
            .listStyle(.inset)
            .scrollContentBackground(.hidden)
        }
    }

    private func severityChip(_ severity: String) -> some View {
        let color: Color
        switch severity.lowercased() {
        case "high": color = .red
        case "medium": color = .orange
        default: color = .gray
        }
        return Text(severity.uppercased())
            .font(.system(size: 9, weight: .bold))
            .foregroundStyle(.white)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(color, in: Capsule())
    }

    // MARK: - Tests

    @ViewBuilder private var testsList: some View {
        if tests.isEmpty {
            emptyState("No tests generated.")
        } else {
            VStack(spacing: 0) {
                testsHeadline
                Divider()
                List(tests) { test in
                    TestRow(test: test, onJump: { onSelectFinding(test.finding_id) })
                }
                .listStyle(.inset)
                .scrollContentBackground(.hidden)
            }
        }
    }

    /// The load-bearing headline: how many generated tests actually *reproduced* a real
    /// failure (the engine's `summary.reproduced`), plus how many ran.
    @ViewBuilder private var testsHeadline: some View {
        if let summary {
            let n = summary.reproduced
            HStack(spacing: 8) {
                Image(systemName: n > 0 ? "checkmark.seal.fill" : "seal")
                    .foregroundStyle(n > 0 ? .green : .secondary)
                Text("\(n) test\(n == 1 ? "" : "s") reproduce a real failure")
                    .font(.callout).bold()
                Text("· executed \(summary.executed)/\(summary.tests)")
                    .font(.caption).foregroundStyle(.secondary)
                Spacer(minLength: 0)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color(nsColor: .windowBackgroundColor))
        }
    }

    /// One test: name, verified-outcome badge, a "bug proven" / "needs input" tag, and an
    /// expandable disclosure holding the traceback (`detail`), stdout, and the test source.
    private struct TestRow: View {
        let test: GeneratedTest
        var onJump: () -> Void

        @State private var expanded = false

        var body: some View {
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 8) {
                    Button(action: onJump) {
                        Text(test.test_name)
                            .font(.system(.callout, design: .monospaced)).bold()
                            .foregroundStyle(.primary)
                    }
                    .buttonStyle(.plain)
                    .help("Jump to \(test.finding_id)")
                    outcomeBadge
                    statusTag
                    Spacer(minLength: 0)
                    if test.duration > 0 {
                        Text(String(format: "%.2fs", test.duration))
                            .font(.system(.caption2, design: .monospaced))
                            .foregroundStyle(.secondary)
                    }
                    Button {
                        withAnimation(.easeInOut(duration: 0.12)) { expanded.toggle() }
                    } label: {
                        Image(systemName: expanded ? "chevron.down" : "chevron.right")
                            .font(.caption)
                    }
                    .buttonStyle(.plain).foregroundStyle(.secondary)
                    .help(expanded ? "Hide details" : "Show details")
                }
                if expanded { details }
            }
            .padding(.vertical, 3)
        }

        @ViewBuilder private var details: some View {
            if !test.detail.isEmpty {
                labeledBlock(test.outcome == "skipped" ? "Skip reason" : "Traceback",
                             test.detail, mono: true)
            }
            if !test.stdout.isEmpty {
                labeledBlock("stdout", test.stdout, mono: true)
            }
            labeledBlock("Test source", test.source, mono: true)
        }

        private func labeledBlock(_ title: String, _ body: String, mono: Bool) -> some View {
            VStack(alignment: .leading, spacing: 3) {
                Text(title.uppercased())
                    .font(.system(size: 9, weight: .bold)).foregroundStyle(.secondary)
                Text(body)
                    .font(.system(mono ? .caption : .callout, design: mono ? .monospaced : .default))
                    .foregroundStyle(.primary)
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(8)
                    .background(Color(nsColor: .windowBackgroundColor), in: RoundedRectangle(cornerRadius: 5))
            }
        }

        /// Verified pytest outcome — passed / failed / error / skipped, or "not executed"
        /// when the engine didn't run it (outcome == nil).
        private var outcomeBadge: some View {
            let (label, color): (String, Color)
            switch test.outcome {
            case "passed": (label, color) = ("passed", .green)
            case "failed": (label, color) = ("failed", .red)
            case "error": (label, color) = ("error", .orange)
            case "skipped": (label, color) = ("skipped", .gray)
            default: (label, color) = ("not executed", .gray)
            }
            return Text(label)
                .font(.system(size: 10, weight: .bold))
                .foregroundStyle(.white)
                .padding(.horizontal, 6).padding(.vertical, 2)
                .background(color, in: Capsule())
        }

        /// The meaning of the outcome for the user. Uses the engine's `reproduced` flag
        /// directly (never re-derived): proven bug vs. needs-input vs. inconclusive.
        @ViewBuilder private var statusTag: some View {
            if test.reproduced {
                tag("BUG PROVEN", .green)
            } else if test.needsInput {
                tag("needs input", .secondary)
            }
        }

        private func tag(_ text: String, _ color: Color) -> some View {
            Text(text)
                .font(.system(size: 9, weight: .semibold))
                .foregroundStyle(color)
                .padding(.horizontal, 6).padding(.vertical, 2)
                .background(color.opacity(0.15), in: Capsule())
        }
    }

    private func emptyState(_ text: String) -> some View {
        VStack {
            Spacer()
            Text(text).foregroundStyle(.secondary)
            Spacer()
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}
