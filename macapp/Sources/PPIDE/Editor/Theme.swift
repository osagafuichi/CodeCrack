import SwiftUI
import AppKit

/// Shared surface colors so the sidebar, tab bar, and editor read as one continuous
/// surface. Matching them makes the system `NavigationSplitView` divider — a 1px line
/// that's only visible where light meets dark — disappear against a uniform background.
///
/// The values match Highlightr's `atom-one-dark` (#282c34) / `atom-one-light` (#fafafa)
/// backgrounds used by `CodeEditor`, so there's no seam between the panes and the code.
extension Color {
    /// The editor's background, adaptive to light/dark appearance.
    static let editorSurface = Color(nsColor: NSColor(name: nil) { appearance in
        let dark = appearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
        return dark
            ? NSColor(red: 40 / 255, green: 44 / 255, blue: 52 / 255, alpha: 1)   // #282c34
            : NSColor(red: 250 / 255, green: 250 / 255, blue: 250 / 255, alpha: 1) // #fafafa
    })
}
