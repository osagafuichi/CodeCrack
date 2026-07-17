import SwiftUI

/// Bottom panel showing the CodeCrack engine's output: detected findings and the pytest
/// tests it would generate. Structured like `ConsolePanel` (title bar + Clear/Close),
/// with a segmented control switching between the two views. Read-only in v1.
struct IssuesPanel: View {
    let findings: [Finding]
    let tests: [GeneratedTest]
    let isAnalyzing: Bool
    let errorMessage: String?
    var onSelect: (Int) -> Void
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
            List(tests) { test in
                VStack(alignment: .leading, spacing: 6) {
                    HStack(spacing: 8) {
                        Text(test.test_name)
                            .font(.system(.callout, design: .monospaced)).bold()
                        expectsBadge(test.expects)
                        Spacer(minLength: 0)
                    }
                    Text(test.source)
                        .font(.system(.caption, design: .monospaced))
                        .foregroundStyle(.primary)
                        .textSelection(.enabled)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(8)
                        .background(Color(nsColor: .windowBackgroundColor), in: RoundedRectangle(cornerRadius: 5))
                }
                .padding(.vertical, 2)
            }
            .listStyle(.inset)
            .scrollContentBackground(.hidden)
        }
    }

    private func expectsBadge(_ expects: String) -> some View {
        Text(expects)
            .font(.system(size: 10, weight: .semibold))
            .foregroundStyle(.secondary)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(Color.secondary.opacity(0.15), in: Capsule())
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
