import Foundation

/// One open file. `isDirty` tracks unsaved edits; `selectedRange` persists the caret /
/// selection so switching away and back to a tab restores where you were. `modificationDate`
/// is the on-disk mtime we last saw, used to detect external edits made by other programs.
struct OpenDocument: Identifiable, Equatable {
    let url: URL
    var text: String
    var isDirty: Bool = false
    /// Persisted caret/selection (character offsets), restored when the tab is reactivated.
    var selectedRange: NSRange? = nil
    /// On-disk modification date at last load/save, for external-change detection.
    var modificationDate: Date? = nil

    var id: URL { url }
    var name: String { url.lastPathComponent }
}

/// An ordered collection of open documents plus the active selection — the multi-tab
/// generalization of what used to be a single `OpenDocument?`. A value type held as
/// SwiftUI `@State`; mutating helpers keep the active-id invariant in one place.
struct OpenDocuments: Equatable {
    /// Open documents, in tab order.
    private(set) var documents: [OpenDocument] = []
    /// The id (file URL) of the active document, or nil when nothing is open.
    private(set) var activeID: URL? = nil

    var isEmpty: Bool { documents.isEmpty }

    /// The active document, or nil if none is open.
    var active: OpenDocument? {
        guard let activeID else { return nil }
        return documents.first { $0.id == activeID }
    }

    /// Index of the active document within `documents`, if any.
    private var activeIndex: Int? {
        guard let activeID else { return nil }
        return documents.firstIndex { $0.id == activeID }
    }

    /// Whether a document for `url` is already open.
    func contains(_ url: URL) -> Bool {
        documents.contains { $0.id == url }
    }

    // MARK: - Mutation

    /// Open `url` with the given text, or just activate it if already open. Returns nothing;
    /// the document becomes active either way.
    mutating func open(_ url: URL, text: String, modificationDate: Date?) {
        if contains(url) {
            activeID = url
            return
        }
        documents.append(OpenDocument(url: url, text: text, modificationDate: modificationDate))
        activeID = url
    }

    /// Make `url` the active tab (no-op if it isn't open).
    mutating func activate(_ url: URL) {
        if contains(url) { activeID = url }
    }

    /// Close the tab for `url`. The neighbor (previous tab, else next) becomes active.
    mutating func close(_ url: URL) {
        guard let idx = documents.firstIndex(where: { $0.id == url }) else { return }
        documents.remove(at: idx)
        if activeID == url {
            if documents.isEmpty {
                activeID = nil
            } else {
                let neighbor = max(0, idx - 1)
                activeID = documents[min(neighbor, documents.count - 1)].id
            }
        }
    }

    /// Apply an in-place edit to the active document, if any.
    mutating func updateActive(_ mutate: (inout OpenDocument) -> Void) {
        guard let i = activeIndex else { return }
        mutate(&documents[i])
    }

    /// Apply an in-place edit to the document for `url`, if open.
    mutating func update(_ url: URL, _ mutate: (inout OpenDocument) -> Void) {
        guard let i = documents.firstIndex(where: { $0.id == url }) else { return }
        mutate(&documents[i])
    }
}
