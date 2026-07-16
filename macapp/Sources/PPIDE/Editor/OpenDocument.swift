import Foundation

/// The currently open file. `isDirty` tracks unsaved edits.
struct OpenDocument: Identifiable, Equatable {
    let url: URL
    var text: String
    var isDirty: Bool = false

    var id: URL { url }
    var name: String { url.lastPathComponent }
}
