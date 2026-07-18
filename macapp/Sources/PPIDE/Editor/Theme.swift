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

/// User-selectable editor theme. `.system` follows the OS appearance (the original locked
/// behavior); the rest are fixed Highlightr themes the user can pick in Preferences. Each
/// case knows the Highlightr stylesheet name to load and whether it renders dark, so
/// `CodeEditor` can set the caret color and matching background.
enum EditorTheme: String, CaseIterable, Identifiable {
    case system
    case light
    case dark
    case solarizedDark
    case monokai
    case github

    var id: String { rawValue }

    /// Menu label shown in Preferences.
    var label: String {
        switch self {
        case .system: return "System (follow appearance)"
        case .light: return "Atom One Light"
        case .dark: return "Atom One Dark"
        case .solarizedDark: return "Solarized Dark"
        case .monokai: return "Monokai"
        case .github: return "GitHub"
        }
    }

    /// The Highlightr stylesheet name for this theme, given the current OS appearance
    /// (only consulted by `.system`).
    func highlightrName(systemIsDark: Bool) -> String {
        switch self {
        case .system: return systemIsDark ? "atom-one-dark" : "atom-one-light"
        case .light: return "atom-one-light"
        case .dark: return "atom-one-dark"
        case .solarizedDark: return "solarized-dark"
        case .monokai: return "monokai-sublime"
        case .github: return "github"
        }
    }

    /// Whether this theme renders on a dark background (drives caret color), given the OS
    /// appearance for `.system`.
    func isDark(systemIsDark: Bool) -> Bool {
        switch self {
        case .system: return systemIsDark
        case .light, .github: return false
        case .dark, .solarizedDark, .monokai: return true
        }
    }
}
