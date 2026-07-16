import SwiftUI

/// Horizontal strip of open-file tabs above the editor.
struct TabBar: View {
    let documents: [OpenDocument]
    @Binding var activeID: URL?
    var onClose: (OpenDocument) -> Void

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 0) {
                ForEach(documents) { doc in
                    TabItem(
                        doc: doc,
                        isActive: doc.id == activeID,
                        onSelect: { activeID = doc.id },
                        onClose: { onClose(doc) }
                    )
                    Divider().frame(height: 16)
                }
            }
        }
        .frame(height: 34)
        .background(.bar)
    }
}

private struct TabItem: View {
    let doc: OpenDocument
    let isActive: Bool
    var onSelect: () -> Void
    var onClose: () -> Void

    @State private var hovering = false

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: "doc.text")
                .font(.caption2)
                .foregroundStyle(.secondary)
            Text(doc.name)
                .font(.callout)
                .lineLimit(1)
                .foregroundStyle(isActive ? .primary : .secondary)
            // Dirty dot normally, close "x" on hover.
            Button(action: onClose) {
                Image(systemName: (doc.isDirty && !hovering) ? "circle.fill" : "xmark")
                    .font(.system(size: (doc.isDirty && !hovering) ? 7 : 9, weight: .bold))
                    .foregroundStyle(.secondary)
                    .frame(width: 16, height: 16)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .opacity((hovering || doc.isDirty) ? 1 : 0)
        }
        .padding(.horizontal, 10)
        .frame(maxHeight: .infinity)
        .background(isActive ? Color.accentColor.opacity(0.20) : Color.clear)
        .contentShape(Rectangle())
        .onTapGesture(perform: onSelect)
        .onHover { hovering = $0 }
    }
}
