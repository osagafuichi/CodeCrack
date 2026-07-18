import SwiftUI
import AppKit

@main
struct PPIDEApp: App {
    // Promote the process to a regular, foreground app. Without this, a SwiftPM
    // executable (no .app bundle / Info.plist) launches as a background accessory
    // — no Dock icon, window not brought to the front. The delegate flips the
    // activation policy at launch so Run (from Xcode, `swift run`, or the binary)
    // gives a normal windowed app.
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    // The recent-documents list. Owned here (App-scoped, single instance) so both the
    // Open Recent menu and ContentView share one source of truth.
    @StateObject private var recentFiles = RecentFilesStore()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(recentFiles)
                .frame(minWidth: 900, minHeight: 600)
        }
        .commands {
            OpenRecentCommands(recentFiles: recentFiles)
        }

        // Preferences window (⌘,). SwiftUI adds the standard menu item automatically.
        Settings {
            SettingsView()
        }
    }
}

/// Adds a File ▸ Open Recent submenu just after the standard New/Open items. Each entry posts
/// `.openRecentFile` (handled by `ContentView`) so the file reopens into a tab; "Clear Menu"
/// empties the list.
struct OpenRecentCommands: Commands {
    @ObservedObject var recentFiles: RecentFilesStore

    var body: some Commands {
        CommandGroup(after: .newItem) {
            Menu("Open Recent") {
                ForEach(recentFiles.urls, id: \.self) { url in
                    Button(url.lastPathComponent) {
                        NotificationCenter.default.post(name: .openRecentFile, object: url)
                    }
                }
                if !recentFiles.urls.isEmpty {
                    Divider()
                    Button("Clear Menu") { recentFiles.clear() }
                }
            }
            .disabled(recentFiles.urls.isEmpty)
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        true
    }
}
