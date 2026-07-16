import SwiftUI

struct ContentView: View {
    @State private var root: FileNode?
    @State private var selection: URL?
    @State private var documents: [OpenDocument] = []
    @State private var activeID: URL?
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
                .disabled(activeID == nil)
            }
        }
        .background {
            // Hidden ⌘W to close the active tab.
            Button("") {
                if let doc = activeDocument { close(doc) }
            }
            .keyboardShortcut("w", modifiers: .command)
            .opacity(0)
            .disabled(activeID == nil)
        }
        .safeAreaInset(edge: .bottom) {
            statusBar
        }
    }

    // MARK: - Sidebar

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

    // MARK: - Editor + tabs

    @ViewBuilder private var editor: some View {
        if !documents.isEmpty {
            VStack(spacing: 0) {
                TabBar(documents: documents, activeID: $activeID, onClose: close)
                Divider()
                if activeID != nil {
                    CodeEditor(text: activeText, language: activeLanguage)
                        .id(activeID)
                }
            }
            .navigationTitle(activeDocument?.name ?? "")
        } else {
            ContentUnavailableView(
                "No File Open",
                systemImage: "doc",
                description: Text("Select a file from the sidebar.")
            )
        }
    }

    private var statusBar: some View {
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

    // MARK: - Active document

    private var activeDocument: OpenDocument? {
        documents.first { $0.id == activeID }
    }

    private var activeLanguage: String? {
        guard let ext = activeID?.pathExtension, !ext.isEmpty else { return nil }
        return LanguageMap.name(forExtension: ext)
    }

    /// Two-way binding to the active document's text, marking it dirty on edit.
    private var activeText: Binding<String> {
        Binding(
            get: { activeDocument?.text ?? "" },
            set: { newValue in
                guard let idx = documents.firstIndex(where: { $0.id == activeID }) else { return }
                if documents[idx].text != newValue {
                    documents[idx].text = newValue
                    documents[idx].isDirty = true
                }
            }
        )
    }

    // MARK: - Actions

    /// Route a picked URL: a folder loads into the sidebar; a file opens as a tab and its
    /// containing folder populates the sidebar.
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
        // Already open: just focus that tab.
        if documents.contains(where: { $0.id == url }) {
            activeID = url
            status = url.path
            return
        }
        let text = (try? String(contentsOf: url, encoding: .utf8)) ?? ""
        documents.append(OpenDocument(url: url, text: text))
        activeID = url
        status = url.path
    }

    private func close(_ doc: OpenDocument) {
        guard let idx = documents.firstIndex(where: { $0.id == doc.id }) else { return }
        documents.remove(at: idx)
        if activeID == doc.id {
            activeID = documents.isEmpty ? nil : documents[min(idx, documents.count - 1)].id
        }
    }

    private func save() {
        guard let idx = documents.firstIndex(where: { $0.id == activeID }) else { return }
        let doc = documents[idx]
        do {
            try doc.text.write(to: doc.url, atomically: true, encoding: .utf8)
            documents[idx].isDirty = false
            status = "Saved \(doc.name)"
        } catch {
            status = "Save failed: \(error.localizedDescription)"
        }
    }
}
