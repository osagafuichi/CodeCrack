import Foundation
import Combine

/// Posted when the user picks an entry from the File ▸ Open Recent menu. The `object` is the
/// `URL` to open. The menu lives in the App scene (`PPIDEApp`) while the document state lives
/// in `ContentView`, so this notification bridges the two without a shared document controller.
extension Notification.Name {
    static let openRecentFile = Notification.Name("PPIDEOpenRecentFile")
}

/// The most-recently-opened files, newest first, de-duplicated and capped. Backs the
/// File ▸ Open Recent menu. Persisted to `UserDefaults` under
/// `SettingsKeys.recentDocumentPaths` as an array of file paths.
///
/// An `ObservableObject` so the menu rebuilds when the list changes. Entries that no longer
/// exist on disk are pruned when the store loads; the open path also prunes on demand.
final class RecentFilesStore: ObservableObject {
    @Published private(set) var urls: [URL] = []

    private let maxItems = 10
    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        load()
    }

    /// Record `url` as the most-recent document: move it to the front, drop duplicates, and
    /// trim to the cap. Persists immediately.
    func record(_ url: URL) {
        let standardized = url.standardizedFileURL
        var list = urls.filter { $0 != standardized }
        list.insert(standardized, at: 0)
        if list.count > maxItems { list = Array(list.prefix(maxItems)) }
        urls = list
        persist()
    }

    /// Drop `url` from the list (used when an entry turns out to be missing on disk).
    func remove(_ url: URL) {
        let standardized = url.standardizedFileURL
        let filtered = urls.filter { $0 != standardized }
        guard filtered.count != urls.count else { return }
        urls = filtered
        persist()
    }

    /// Empty the list (File ▸ Open Recent ▸ Clear Menu).
    func clear() {
        guard !urls.isEmpty else { return }
        urls = []
        persist()
    }

    /// Load from defaults, pruning any recorded file that no longer exists on disk.
    private func load() {
        let paths = defaults.stringArray(forKey: SettingsKeys.recentDocumentPaths) ?? []
        urls = paths
            .map { URL(fileURLWithPath: $0) }
            .filter { FileManager.default.fileExists(atPath: $0.path) }
    }

    private func persist() {
        defaults.set(urls.map(\.path), forKey: SettingsKeys.recentDocumentPaths)
    }
}
