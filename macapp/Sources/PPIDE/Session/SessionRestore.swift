import SwiftUI
import AppKit

/// Explicit, deterministic persistence of the last window/editing session to `UserDefaults`:
/// the open tab set, the active tab, and the window frame. Chosen over the implicit
/// `NSWindowRestoration` machinery because it is simpler and directly testable for this
/// SwiftUI/AppKit hybrid.
///
/// Restore is driven by `ContentView` (tabs) and `WindowAccessor` (frame); this type only
/// reads and writes the keys in `SettingsKeys`.
enum SessionStore {
    /// A restored session. `openURLs` may include files that have since been deleted/moved —
    /// callers must skip any that no longer exist rather than trusting the list blindly.
    struct State {
        var openURLs: [URL]
        var activeURL: URL?
    }

    // MARK: - Tab set

    /// Persist the open tab set and the active tab. An empty tab set clears the saved session
    /// so a clean-slate quit falls back to default behavior on next launch.
    static func saveTabs(openURLs: [URL], activeURL: URL?) {
        let d = UserDefaults.standard
        if openURLs.isEmpty {
            d.removeObject(forKey: SettingsKeys.sessionOpenDocumentPaths)
            d.removeObject(forKey: SettingsKeys.sessionActiveDocumentPath)
            return
        }
        d.set(openURLs.map(\.path), forKey: SettingsKeys.sessionOpenDocumentPaths)
        if let activeURL {
            d.set(activeURL.path, forKey: SettingsKeys.sessionActiveDocumentPath)
        } else {
            d.removeObject(forKey: SettingsKeys.sessionActiveDocumentPath)
        }
    }

    /// The saved tab session, or nil on first run / after a clean-slate quit.
    static func loadTabs() -> State? {
        let d = UserDefaults.standard
        guard let paths = d.stringArray(forKey: SettingsKeys.sessionOpenDocumentPaths),
              !paths.isEmpty else { return nil }
        let urls = paths.map { URL(fileURLWithPath: $0) }
        let active = d.string(forKey: SettingsKeys.sessionActiveDocumentPath)
            .map { URL(fileURLWithPath: $0) }
        return State(openURLs: urls, activeURL: active)
    }

    // MARK: - Window frame

    static func saveWindowFrame(_ frame: NSRect) {
        UserDefaults.standard.set(NSStringFromRect(frame), forKey: SettingsKeys.sessionWindowFrame)
    }

    /// The saved window frame, or nil if none / degenerate.
    static func loadWindowFrame() -> NSRect? {
        guard let s = UserDefaults.standard.string(forKey: SettingsKeys.sessionWindowFrame) else {
            return nil
        }
        let frame = NSRectFromString(s)
        return frame.width > 0 && frame.height > 0 ? frame : nil
    }
}

/// Reaches the backing `NSWindow` (SwiftUI exposes no API for this) to restore the saved frame
/// once on launch and to persist the frame on every move/resize — so the last frame is always
/// on disk by quit-time, with no reliance on a terminate hook. Kept to this one file; the rest
/// of the app stays SwiftUI.
struct WindowAccessor: NSViewRepresentable {
    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeNSView(context: Context) -> NSView {
        let view = NSView()
        DispatchQueue.main.async { context.coordinator.attach(to: view.window) }
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        DispatchQueue.main.async { context.coordinator.attach(to: nsView.window) }
    }

    final class Coordinator {
        private weak var window: NSWindow?
        private var didRestore = false

        func attach(to window: NSWindow?) {
            guard let window, self.window !== window else { return }
            self.window = window

            // Restore the saved frame once, the first time we see a window.
            if !didRestore {
                didRestore = true
                if let frame = SessionStore.loadWindowFrame() {
                    window.setFrame(frame, display: true)
                }
            }

            let center = NotificationCenter.default
            center.removeObserver(self)
            center.addObserver(self, selector: #selector(frameChanged(_:)),
                               name: NSWindow.didMoveNotification, object: window)
            center.addObserver(self, selector: #selector(frameChanged(_:)),
                               name: NSWindow.didResizeNotification, object: window)
        }

        @objc private func frameChanged(_ note: Notification) {
            guard let window = note.object as? NSWindow else { return }
            SessionStore.saveWindowFrame(window.frame)
        }

        deinit { NotificationCenter.default.removeObserver(self) }
    }
}
