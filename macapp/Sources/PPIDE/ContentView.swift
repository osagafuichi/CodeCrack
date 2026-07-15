import SwiftUI
import UniformTypeIdentifiers

struct ContentView: View {
    @State private var root: FileNode?
    @State private var selection: URL?
    @State private var currentFile: URL?
    @State private var text: String = ""
    @State private var importing = false
    @State private var status = "Open a folder to begin"

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
                    importing = true
                } label: {
                    Label("Open Folder", systemImage: "folder")
                }
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
        .fileImporter(isPresented: $importing, allowedContentTypes: [.folder]) { result in
            if case .success(let url) = result {
                root = FileTreeBuilder.build(url)
                status = url.path
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
                "No Folder Open",
                systemImage: "folder",
                description: Text("Use Open Folder to start.")
            )
        }
    }

    private var language: CodeLanguage {
        guard let ext = currentFile?.pathExtension else { return .plain }
        return CodeLanguage.detect(fileExtension: ext)
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
