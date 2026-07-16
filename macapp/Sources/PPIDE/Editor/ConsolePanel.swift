import SwiftUI

/// Bottom panel showing output from running the current file, with a stdin input box.
struct ConsolePanel: View {
    let output: String
    let isRunning: Bool
    var onClear: () -> Void
    var onClose: () -> Void
    var onSubmit: (String) -> Void

    @State private var input = ""

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
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .background(Color(nsColor: .windowBackgroundColor))
            Divider()

            ScrollViewReader { proxy in
                ScrollView {
                    Text(output.isEmpty ? "No output yet." : output)
                        .font(.system(.body, design: .monospaced))
                        .foregroundStyle(output.isEmpty ? .secondary : .primary)
                        .textSelection(.enabled)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 14)
                        .padding(.vertical, 10)
                    Color.clear.frame(height: 1).id("bottom")
                }
                .onChange(of: output) { _, _ in
                    withAnimation(.linear(duration: 0.1)) { proxy.scrollTo("bottom", anchor: .bottom) }
                }
            }

            // stdin input — visible while the program is running.
            if isRunning {
                Divider()
                HStack(spacing: 8) {
                    Image(systemName: "chevron.right")
                        .font(.caption).foregroundStyle(.green)
                    TextField("Type input, press Return to send…", text: $input)
                        .textFieldStyle(.plain)
                        .font(.system(.body, design: .monospaced))
                        .onSubmit {
                            onSubmit(input)
                            input = ""
                        }
                }
                .padding(.horizontal, 14)
                .padding(.vertical, 8)
                .background(Color(nsColor: .windowBackgroundColor))
            }
        }
        .background(Color(nsColor: .textBackgroundColor))
    }
}
