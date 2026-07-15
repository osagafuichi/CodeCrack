import Foundation

/// A node in the project file tree. Leaf files have `children == nil`.
struct FileNode: Identifiable, Hashable {
    let url: URL
    let isDirectory: Bool
    var children: [FileNode]?

    var id: URL { url }
    var name: String { url.lastPathComponent }
}

enum FileTreeBuilder {
    /// Recursively build a tree rooted at `url`, skipping hidden files.
    static func build(_ url: URL) -> FileNode {
        let isDir = (try? url.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) ?? false
        guard isDir else { return FileNode(url: url, isDirectory: false, children: nil) }

        let contents = (try? FileManager.default.contentsOfDirectory(
            at: url,
            includingPropertiesForKeys: [.isDirectoryKey],
            options: [.skipsHiddenFiles]
        )) ?? []

        let kids = contents
            .sorted { a, b in
                let ad = (try? a.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) ?? false
                let bd = (try? b.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) ?? false
                if ad != bd { return ad && !bd }
                return a.lastPathComponent.localizedCaseInsensitiveCompare(b.lastPathComponent) == .orderedAscending
            }
            .map { build($0) }

        return FileNode(url: url, isDirectory: true, children: kids)
    }
}
