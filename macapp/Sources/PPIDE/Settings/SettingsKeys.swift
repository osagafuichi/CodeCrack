import Foundation

/// Canonical UserDefaults keys for persisted preferences, in one place so the Settings UI,
/// the editor, and other milestones agree on spelling.
///
/// COORDINATION NOTE: `enginePathOverride` and `claudeAPIKey` are cross-milestone contracts.
/// Milestone 2 (Analyzer discovery) reads `enginePathOverride`; Milestone 5 (AI layer) will
/// read `claudeAPIKey`. This milestone only creates and persists these values — do not rename.
enum SettingsKeys {
    static let fontSize = "fontSize"
    static let editorTheme = "editorTheme"
    static let indentUsesSpaces = "indentUsesSpaces"
    static let indentWidth = "indentWidth"
    static let enginePathOverride = "enginePathOverride"
    static let claudeAPIKey = "claudeAPIKey"
}

/// Default values for preferences that need a sensible non-empty fallback. `@AppStorage`
/// supplies these inline; centralizing them keeps the editor and the Settings UI in sync.
enum SettingsDefaults {
    static let fontSize: Double = 13
    static let editorTheme = EditorTheme.system.rawValue
    static let indentUsesSpaces = true
    static let indentWidth = 4
}
