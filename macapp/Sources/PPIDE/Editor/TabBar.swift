import SwiftUI

/// Horizontal strip of open-file tabs above the editor. Each tab shows the file name, a
/// dirty indicator (a dot that becomes a close "×" on hover), and highlights the active
/// document. Purely presentational — it calls back to `ContentView` to switch/close.
struct TabBar: View {
    let documents: [OpenDocument]
    let activeID: URL?
    var onSelect: (URL) -> Void
    var onClose: (URL) -> Void

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 0) {
                ForEach(documents) { doc in
                    Tab(
                        doc: doc,
                        isActive: doc.id == activeID,
                        onSelect: { onSelect(doc.id) },
                        onClose: { onClose(doc.id) }
                    )
                    Divider().frame(height: 16)
                }
            }
        }
        .frame(height: 30)
        .background(Color(nsColor: .windowBackgroundColor))
    }

    /// A single tab chip. Hovering reveals the close button in place of the dirty dot.
    private struct Tab: View {
        let doc: OpenDocument
        let isActive: Bool
        var onSelect: () -> Void
        var onClose: () -> Void

        @State private var hovering = false

        var body: some View {
            HStack(spacing: 6) {
                closeOrDirty
                Text(doc.name)
                    .font(.caption)
                    .lineLimit(1)
                    .foregroundStyle(isActive ? .primary : .secondary)
            }
            .padding(.horizontal, 10)
            .frame(height: 30)
            .background(isActive ? Color(nsColor: .selectedContentBackgroundColor).opacity(0.25) : .clear)
            .contentShape(Rectangle())
            .onTapGesture(perform: onSelect)
            .onHover { hovering = $0 }
            .help(doc.url.path)
        }

        @ViewBuilder private var closeOrDirty: some View {
            if hovering {
                Button(action: onClose) {
                    Image(systemName: "xmark")
                        .font(.system(size: 8, weight: .bold))
                        .frame(width: 14, height: 14)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .help("Close tab (⌘W)")
            } else if doc.isDirty {
                Circle()
                    .fill(Color.secondary)
                    .frame(width: 7, height: 7)
                    .frame(width: 14, height: 14)
            } else {
                // Keep width stable so the label doesn't shift on hover / dirty change.
                Color.clear.frame(width: 14, height: 14)
            }
        }
    }
}
