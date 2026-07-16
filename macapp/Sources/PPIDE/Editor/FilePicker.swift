import AppKit

/// A native open panel that reliably accepts either a single file or a folder.
///
/// SwiftUI's `.fileImporter` mishandles directory selection (clicking Open on a folder
/// navigates into it instead of choosing it), so we use `NSOpenPanel` directly. AppKit
/// stays sealed in this file; `ContentView` calls this and remains pure SwiftUI.
enum FilePicker {
    /// Returns the chosen file or folder URL, or nil if the user cancelled.
    static func openFileOrFolder() -> URL? {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = false
        panel.canCreateDirectories = false
        panel.prompt = "Open"
        panel.message = "Choose a file or folder to open"
        return panel.runModal() == .OK ? panel.url : nil
    }

    /// Prompts for a destination path (for New File / Save As). Returns nil if cancelled.
    static func saveDestination(suggestedName: String) -> URL? {
        let panel = NSSavePanel()
        panel.canCreateDirectories = true
        panel.nameFieldStringValue = suggestedName
        panel.prompt = "Save"
        return panel.runModal() == .OK ? panel.url : nil
    }
}
