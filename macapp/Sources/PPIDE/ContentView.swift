import SwiftUI

struct ContentView: View {
    @State private var root: FileNode?
    @State private var selection: URL?
    @State private var currentFile: URL?
    @State private var text: String = ""
    @State private var status = "Open a file or folder to begin"

    var body: some View {
        NavigationSplitView {
            sidebar
                .navigationSplitViewColumnWidth(min: 200, ideal: 260)
        } detail: {
            editor
        }
        .toolbar {
            ToolbarItem(placement: .navigation) {
                Button {
                    if let url = FilePicker.openFileOrFolder() { open(url) }
                } label: {
                    Label("Open", systemImage: "folder")
                }
                .keyboardShortcut("o", modifiers: .command)
            }
            ToolbarItem {
                Button {
                    save()
                } label: {
                    Label("Save", systemImage: "square.and.arrow.down")
                }
                .keyboardShortcut("s", modifiers: .command)
                .disabled(currentFile == nil)
            }
        }
        .safeAreaInset(edge: .bottom) {
            Text(status)
                .font(.caption)
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .truncationMode(.middle)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 10)
                .padding(.vertical, 4)
                .background(.bar)
        }
    }

    @ViewBuilder private var sidebar: some View {
        if let root {
            List(selection: $selection) {
                OutlineGroup(root.children ?? [], children: \.children) { node in
                    Label(node.name, systemImage: node.isDirectory ? "folder" : "doc.text")
                        .tag(node.url)
                }
            }
            .onChange(of: selection) { _, newValue in
                if let newValue { openFile(newValue) }
            }
        } else {
            ContentUnavailableView(
                "Nothing Open",
                systemImage: "folder",
                description: Text("Open a file or folder (⌘O) to start.")
            )
        }
    }

    private var language: String? {
        guard let ext = currentFile?.pathExtension, !ext.isEmpty else { return nil }
        return LanguageMap.name(forExtension: ext)
    }

    @ViewBuilder private var editor: some View {
        if currentFile != nil {
            CodeEditor(text: $text, language: language)
                .navigationTitle(currentFile?.lastPathComponent ?? "")
        } else {
            ContentUnavailableView(
                "No File Open",
                systemImage: "doc",
                description: Text("Select a file from the sidebar.")
            )
        }
    }

    /// Route a picked URL: a folder loads into the sidebar; a file opens directly and
    /// its containing folder populates the sidebar.
    private func open(_ url: URL) {
        var isDir: ObjCBool = false
        FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir)
        if isDir.boolValue {
            root = FileTreeBuilder.build(url)
            status = url.path
        } else {
            root = FileTreeBuilder.build(url.deletingLastPathComponent())
            openFile(url)
            selection = url
        }
    }

    private func openFile(_ url: URL) {
        var isDir: ObjCBool = false
        FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir)
        guard !isDir.boolValue else { return }
        text = (try? String(contentsOf: url, encoding: .utf8)) ?? ""
        currentFile = url
        status = url.path
    }

    private func save() {
        guard let currentFile else { return }
        do {
            try text.write(to: currentFile, atomically: true, encoding: .utf8)
            status = "Saved \(currentFile.lastPathComponent)"
        } catch {
            status = "Save failed: \(error.localizedDescription)"
        }
    }
}
