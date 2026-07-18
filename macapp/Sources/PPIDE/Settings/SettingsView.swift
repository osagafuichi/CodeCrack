import SwiftUI

/// The Preferences window (⌘,). All controls bind directly to `@AppStorage`, so edits
/// persist to `UserDefaults` immediately and survive relaunch. Grouped into Editor, Engine,
/// and AI tabs. No logic beyond storage: the engine path and API key are read by other
/// milestones (see `SettingsKeys`).
struct SettingsView: View {
    var body: some View {
        TabView {
            EditorSettings()
                .tabItem { Label("Editor", systemImage: "textformat") }
            EngineSettings()
                .tabItem { Label("Engine", systemImage: "gearshape.2") }
            AISettings()
                .tabItem { Label("AI", systemImage: "sparkles") }
        }
        .frame(width: 480, height: 300)
    }
}

// MARK: - Editor

private struct EditorSettings: View {
    @AppStorage(SettingsKeys.fontSize) private var fontSize = SettingsDefaults.fontSize
    @AppStorage(SettingsKeys.editorTheme) private var editorTheme = SettingsDefaults.editorTheme
    @AppStorage(SettingsKeys.indentUsesSpaces) private var indentUsesSpaces = SettingsDefaults.indentUsesSpaces
    @AppStorage(SettingsKeys.indentWidth) private var indentWidth = SettingsDefaults.indentWidth

    var body: some View {
        Form {
            LabeledContent("Font size") {
                HStack {
                    Slider(value: $fontSize, in: 9...24, step: 1)
                    Text("\(Int(fontSize)) pt")
                        .font(.system(.body, design: .monospaced))
                        .frame(width: 44, alignment: .trailing)
                }
            }

            Picker("Theme", selection: $editorTheme) {
                ForEach(EditorTheme.allCases) { theme in
                    Text(theme.label).tag(theme.rawValue)
                }
            }

            Picker("Indentation", selection: $indentUsesSpaces) {
                Text("Spaces").tag(true)
                Text("Tabs").tag(false)
            }
            .pickerStyle(.segmented)

            Picker("Indent width", selection: $indentWidth) {
                ForEach([2, 4, 8], id: \.self) { w in
                    Text("\(w)").tag(w)
                }
            }
            .pickerStyle(.segmented)
        }
        .formStyle(.grouped)
    }
}

// MARK: - Engine

private struct EngineSettings: View {
    @AppStorage(SettingsKeys.enginePathOverride) private var enginePathOverride = ""

    var body: some View {
        Form {
            Section {
                TextField("Engine / Python path", text: $enginePathOverride, prompt: Text("Auto-detect"))
                    .textFieldStyle(.roundedBorder)
                Button("Choose…") {
                    if let url = FilePicker.openFileOrFolder() {
                        enginePathOverride = url.path
                    }
                }
            } header: {
                Text("Engine location")
            } footer: {
                Text("Override where CodeCrack looks for the analysis engine. Leave blank to "
                     + "auto-detect. Applies to the next analysis run.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }
}

// MARK: - AI

private struct AISettings: View {
    @AppStorage(SettingsKeys.claudeAPIKey) private var claudeAPIKey = ""

    var body: some View {
        Form {
            Section {
                SecureField("Claude API key", text: $claudeAPIKey, prompt: Text("sk-ant-…"))
                    .textFieldStyle(.roundedBorder)
            } header: {
                Text("Claude API key")
            } footer: {
                Text("Stored locally for a future AI-assisted layer. Not used to make any "
                     + "network requests yet.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }
}
