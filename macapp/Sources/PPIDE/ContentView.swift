import SwiftUI

struct ContentView: View {
    @State private var root: FileNode?
    @State private var selection: URL?
    @State private var documents: [OpenDocument] = []
    @State private var activeID: URL?
    @State private var status = "Open a file or folder to begin"

    // Project search
    @State private var searchQuery = ""
    @State private var replaceText = ""
    @State private var searchResults: [SearchHit] = []
    @State private var searching = false

    // Run console
    @State private var consoleOutput = ""
    @State private var showConsole = false
    @State private var isRunning = false
    @State private var session: RunSession?

    var body: some View {
        NavigationSplitView {
            sidebar
                .navigationSplitViewColumnWidth(min: 220, ideal: 280)
        } detail: {
            editor
                .background(SplitDividerHider())
        }
        .toolbar {
            ToolbarItemGroup(placement: .navigation) {
                Button {
                    if let url = FilePicker.openFileOrFolder() { open(url) }
                } label: {
                    Label("Open", systemImage: "folder")
                }
                .keyboardShortcut("o", modifiers: .command)
                Button {
                    newFile()
                } label: {
                    Label("New File", systemImage: "doc.badge.plus")
                }
                .keyboardShortcut("n", modifiers: .command)
            }
            ToolbarItemGroup {
                Button {
                    run()
                } label: {
                    Label("Run", systemImage: "play.fill")
                }
                .keyboardShortcut("r", modifiers: .command)
                .disabled(activeID == nil || isRunning)
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
            // Hidden shortcuts: ⌘W close tab, ⇧⌘S save as.
            Group {
                Button("") { if let doc = activeDocument { close(doc) } }
                    .keyboardShortcut("w", modifiers: .command)
                    .disabled(activeID == nil)
                Button("") { saveAs() }
                    .keyboardShortcut("s", modifiers: [.command, .shift])
                    .disabled(activeID == nil)
            }
            .opacity(0)
        }
        .safeAreaInset(edge: .bottom) { statusBar }
    }

    // MARK: - Sidebar

    @ViewBuilder private var sidebar: some View {
        if root == nil {
            ContentUnavailableView(
                "Nothing Open",
                systemImage: "folder",
                description: Text("Open a file or folder (⌘O) to start.")
            )
        } else {
            VStack(spacing: 0) {
                searchField
                Divider()
                if searchQuery.isEmpty {
                    fileTree
                } else {
                    searchResultsList
                }
            }
            .background(Color.editorSurface)
        }
    }

    private var searchField: some View {
        VStack(spacing: 6) {
            HStack(spacing: 6) {
                Image(systemName: "magnifyingglass")
                    .font(.caption).foregroundStyle(.secondary)
                TextField("Search project", text: $searchQuery)
                    .textFieldStyle(.plain)
                    .onSubmit(runSearch)
                if !searchQuery.isEmpty {
                    Button {
                        searchQuery = ""; searchResults = []
                    } label: {
                        Image(systemName: "xmark.circle.fill")
                    }
                    .buttonStyle(.plain).foregroundStyle(.secondary)
                }
            }
            if !searchQuery.isEmpty {
                HStack(spacing: 6) {
                    Image(systemName: "arrow.2.squarepath")
                        .font(.caption).foregroundStyle(.secondary)
                    TextField("Replace", text: $replaceText)
                        .textFieldStyle(.plain)
                    Button("All") { replaceAll() }
                        .controlSize(.small)
                }
            }
        }
        .padding(8)
    }

    private var fileTree: some View {
        List(selection: $selection) {
            OutlineGroup(root?.children ?? [], children: \.children) { node in
                Label(node.name, systemImage: node.isDirectory ? "folder" : "doc.text")
                    .tag(node.url)
            }
        }
        .scrollContentBackground(.hidden)
        .onChange(of: selection) { _, newValue in
            if let newValue { openFile(newValue) }
        }
    }

    private var searchResultsList: some View {
        List {
            if searching {
                HStack(spacing: 8) {
                    ProgressView().controlSize(.small)
                    Text("Searching…").foregroundStyle(.secondary)
                }
            } else if searchResults.isEmpty {
                Text("No results").foregroundStyle(.secondary)
            } else {
                Section("\(searchResults.count) result\(searchResults.count == 1 ? "" : "s")") {
                    ForEach(searchResults) { hit in
                        Button {
                            openFile(hit.url); selection = hit.url
                        } label: {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("\(hit.url.lastPathComponent):\(hit.line)")
                                    .font(.caption).foregroundStyle(.secondary)
                                Text(hit.preview)
                                    .font(.callout).lineLimit(1)
                            }
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
        }
        .scrollContentBackground(.hidden)
    }

    // MARK: - Editor + tabs + console

    @ViewBuilder private var editor: some View {
        if !documents.isEmpty {
            VStack(spacing: 0) {
                TabBar(documents: documents, activeID: $activeID, onClose: close)
                Divider()
                if activeID != nil {
                    CodeEditor(text: activeText, language: activeLanguage)
                        .id(activeID)
                }
                if showConsole {
                    Divider()
                    ConsolePanel(
                        output: consoleOutput,
                        isRunning: isRunning,
                        onClear: { consoleOutput = "" },
                        onClose: { showConsole = false; session?.stop() },
                        onSubmit: sendInput
                    )
                    .frame(height: 200)
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

    // MARK: - Open / tabs

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

    // MARK: - Save / new

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

    private func saveAs() {
        guard let doc = activeDocument,
              let url = FilePicker.saveDestination(suggestedName: doc.name) else { return }
        do {
            try doc.text.write(to: url, atomically: true, encoding: .utf8)
            refreshTree()
            openFile(url)
            selection = url
            status = "Saved \(url.lastPathComponent)"
        } catch {
            status = "Save failed: \(error.localizedDescription)"
        }
    }

    private func newFile() {
        guard let url = FilePicker.saveDestination(suggestedName: "Untitled.txt") else { return }
        do {
            try "".write(to: url, atomically: true, encoding: .utf8)
            refreshTree()
            openFile(url)
            selection = url
        } catch {
            status = "Couldn't create file: \(error.localizedDescription)"
        }
    }

    private func refreshTree() {
        if let root { self.root = FileTreeBuilder.build(root.url) }
    }

    // MARK: - Run

    private func run() {
        guard let doc = activeDocument else { return }
        save()
        showConsole = true
        guard let command = Runner.command(for: doc.url) else {
            consoleOutput = "Don't know how to run .\(doc.url.pathExtension) files yet.\n"
            status = "No run configuration for .\(doc.url.pathExtension)"
            return
        }
        session?.stop()
        consoleOutput = "$ \(command.display)\n\n"
        isRunning = true
        session = Runner.start(command,
                               onOutput: { consoleOutput += $0 },
                               onFinish: { code in
                                   consoleOutput += "\n[exited with code \(code)]\n"
                                   isRunning = false
                                   session = nil
                               })
    }

    /// Send a line to the running program's stdin, echoing it in the console.
    private func sendInput(_ text: String) {
        session?.send(text)
        consoleOutput += text + "\n"
    }

    // MARK: - Search / replace

    private func runSearch() {
        guard let root, !searchQuery.isEmpty else { searchResults = []; return }
        let query = searchQuery
        let rootURL = root.url
        searching = true
        DispatchQueue.global(qos: .userInitiated).async {
            let hits = ProjectSearch.search(query, in: rootURL)
            DispatchQueue.main.async {
                searchResults = hits
                searching = false
            }
        }
    }

    private func replaceAll() {
        guard let root, !searchQuery.isEmpty else { return }
        let changed = ProjectSearch.replaceAll(searchQuery, with: replaceText, in: root.url)
        // Reload any open tabs whose files changed on disk.
        for url in changed {
            if let idx = documents.firstIndex(where: { $0.id == url }),
               let reloaded = try? String(contentsOf: url, encoding: .utf8) {
                documents[idx].text = reloaded
                documents[idx].isDirty = false
            }
        }
        status = "Replaced in \(changed.count) file\(changed.count == 1 ? "" : "s")"
        runSearch()
    }
}
