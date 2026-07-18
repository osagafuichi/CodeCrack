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

    var body: some Scene {
        WindowGroup {
            ContentView()
                .frame(minWidth: 900, minHeight: 600)
        }

        // Preferences window (⌘,). SwiftUI adds the standard menu item automatically.
        Settings {
            SettingsView()
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
