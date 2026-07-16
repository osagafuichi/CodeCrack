import SwiftUI

/// Bottom panel showing output from running the current file.
struct ConsolePanel: View {
    let output: String
    let isRunning: Bool
    var onClear: () -> Void
    var onClose: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 8) {
                Text("Console")
                    .font(.caption).bold()
                    .foregroundStyle(.secondary)
                if isRunning { ProgressView().controlSize(.small) }
                Spacer()
                Button(action: onClear) { Image(systemName: "trash") }
                    .buttonStyle(.plain).foregroundStyle(.secondary)
                    .help("Clear")
                Button(action: onClose) { Image(systemName: "xmark") }
                    .buttonStyle(.plain).foregroundStyle(.secondary)
                    .help("Close")
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 5)
            .background(.bar)
            Divider()
            ScrollViewReader { proxy in
                ScrollView {
                    Text(output.isEmpty ? "No output yet." : output)
                        .font(.system(.caption, design: .monospaced))
                        .foregroundStyle(output.isEmpty ? .secondary : .primary)
                        .textSelection(.enabled)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(8)
                    Color.clear.frame(height: 1).id("bottom")
                }
                .onChange(of: output) { _, _ in
                    withAnimation(.linear(duration: 0.1)) { proxy.scrollTo("bottom", anchor: .bottom) }
                }
            }
        }
        .background(Color(nsColor: .textBackgroundColor))
    }
}
