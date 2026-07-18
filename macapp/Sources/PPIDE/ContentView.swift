import SwiftUI
import AppKit

/// A pending "file changed on disk" prompt for an open document with unsaved edits.
struct ExternalChange: Identifiable {
    let url: URL
    let diskText: String
    let diskDate: Date?
    var id: URL { url }
}

struct ContentView: View {
    @EnvironmentObject private var recentFiles: RecentFilesStore
    @State private var root: FileNode?
    @State private var selection: URL?
    @State private var docs = OpenDocuments()
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

    // CodeCrack analysis
    @State private var findings: [Finding] = []
    @State private var generatedTests: [GeneratedTest] = []
    @State private var analysisSummary: Summary?
    @State private var isAnalyzing = false
    @State private var showIssues = false
    @State private var analyzeError: String?
    @State private var revealLine: Int?

    // External-change handling: a pending prompt when an open, dirty file was modified on disk.
    @State private var externalChange: ExternalChange?

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
                .disabled(docs.active == nil || isRunning)
                Button {
                    analyze()
                } label: {
                    Label("Analyze", systemImage: "ladybug")
                }
                .keyboardShortcut("b", modifiers: .command)
                .disabled(docs.active == nil || isAnalyzing || !isPython)
                Button {
                    save()
                } label: {
                    Label("Save", systemImage: "square.and.arrow.down")
                }
                .keyboardShortcut("s", modifiers: .command)
                .disabled(docs.active == nil)
            }
        }
        .background {
            // Hidden shortcuts.
            Group {
                Button("") { saveAs() }
                    .keyboardShortcut("s", modifiers: [.command, .shift])
                    .disabled(docs.active == nil)
                Button("") { if let url = docs.activeID { closeTab(url) } }
                    .keyboardShortcut("w", modifiers: .command)
                    .disabled(docs.active == nil)
            }
            .opacity(0)
        }
        .safeAreaInset(edge: .bottom) { statusBar }
        // Re-check open files whenever the app regains focus — that's when external edits
        // (from another editor, a formatter, git, etc.) have typically just happened.
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            checkExternalChanges()
        }
        // File ▸ Open Recent selections arrive here (the menu lives in the App scene).
        .onReceive(NotificationCenter.default.publisher(for: .openRecentFile)) { note in
            if let url = note.object as? URL { openRecent(url) }
        }
        .alert(
            "File changed on disk",
            isPresented: Binding(get: { externalChange != nil },
                                 set: { if !$0 { externalChange = nil } }),
            presenting: externalChange
        ) { change in
            Button("Reload", role: .destructive) { resolveExternalChange(change, reload: true) }
            Button("Keep My Version", role: .cancel) { resolveExternalChange(change, reload: false) }
        } message: { change in
            Text("“\(change.url.lastPathComponent)” was modified by another program. "
                 + "Reload it and discard your unsaved changes, or keep your version?")
        }
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
        .listStyle(.sidebar)
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
        .listStyle(.sidebar)
        .scrollContentBackground(.hidden)
    }

    // MARK: - Editor + console

    @ViewBuilder private var editor: some View {
        if let doc = docs.active {
            VStack(spacing: 0) {
                TabBar(
                    documents: docs.documents,
                    activeID: doc.id,
                    onSelect: { docs.activate($0); syncSelectionToTree() },
                    onClose: closeTab
                )
                Divider()
                CodeEditor(text: activeText, language: activeLanguage,
                           revealLine: $revealLine, selection: activeSelection)
                    .id(doc.id)
                if showIssues {
                    Divider()
                    IssuesPanel(
                        findings: findings,
                        tests: generatedTests,
                        summary: analysisSummary,
                        isAnalyzing: isAnalyzing,
                        errorMessage: analyzeError,
                        onSelect: { line in revealLine = line },
                        onSelectFinding: { findingID in
                            if let line = findings.first(where: { $0.id == findingID })?.line {
                                revealLine = line
                            }
                        },
                        onClear: { findings = []; generatedTests = []; analysisSummary = nil; analyzeError = nil },
                        onClose: { showIssues = false }
                    )
                    .frame(height: 260)
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
            .navigationTitle(doc.name)
            .navigationSubtitle(doc.isDirty ? "Edited" : "")
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
            .background(Color(nsColor: .windowBackgroundColor))
    }

    // MARK: - Active document

    private var activeLanguage: String? {
        guard let ext = docs.active?.url.pathExtension, !ext.isEmpty else { return nil }
        return LanguageMap.name(forExtension: ext)
    }

    private var activeSelection: Binding<NSRange?> {
        Binding(
            get: { docs.active?.selectedRange },
            set: { newValue in docs.updateActive { $0.selectedRange = newValue } }
        )
    }

    private var activeText: Binding<String> {
        Binding(
            get: { docs.active?.text ?? "" },
            set: { newValue in
                guard docs.active?.text != newValue else { return }
                docs.updateActive { $0.text = newValue; $0.isDirty = true }
            }
        )
    }

    // MARK: - Open file

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
        if docs.contains(url) {
            docs.activate(url)
            status = url.path
            recentFiles.record(url)
            return
        }
        let text = (try? String(contentsOf: url, encoding: .utf8)) ?? ""
        docs.open(url, text: text, modificationDate: fileModificationDate(url))
        status = url.path
        recentFiles.record(url)
    }

    /// Open a file chosen from the Open Recent menu. If it has since been deleted or moved,
    /// prune it from the list and report rather than opening an empty tab.
    private func openRecent(_ url: URL) {
        guard FileManager.default.fileExists(atPath: url.path) else {
            recentFiles.remove(url)
            status = "“\(url.lastPathComponent)” is no longer available"
            return
        }
        if root == nil {
            root = FileTreeBuilder.build(url.deletingLastPathComponent())
        }
        openFile(url)
        selection = url
    }

    /// Close a tab, warning nothing for now (dirty confirmation is future work); the
    /// neighbor tab becomes active and the sidebar selection follows it.
    private func closeTab(_ url: URL) {
        docs.close(url)
        syncSelectionToTree()
    }

    /// Keep the sidebar highlight in sync with the active tab.
    private func syncSelectionToTree() {
        if selection != docs.activeID { selection = docs.activeID }
    }

    // MARK: - Save / new

    private func save() {
        guard let doc = docs.active else { return }
        do {
            try doc.text.write(to: doc.url, atomically: true, encoding: .utf8)
            docs.updateActive {
                $0.isDirty = false
                $0.modificationDate = fileModificationDate(doc.url)
            }
            status = "Saved \(doc.name)"
        } catch {
            status = "Save failed: \(error.localizedDescription)"
        }
    }

    private func saveAs() {
        guard let doc = docs.active,
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

    private func fileModificationDate(_ url: URL) -> Date? {
        try? FileManager.default.attributesOfItem(atPath: url.path)[.modificationDate] as? Date
    }

    // MARK: - External-change detection

    /// Compare each open document's on-disk mtime against what we last saw. Clean documents
    /// are reloaded silently; a dirty document whose disk content actually differs raises a
    /// prompt (one at a time — the rest are handled once it's resolved).
    private func checkExternalChanges() {
        guard externalChange == nil else { return }   // don't stack prompts
        for doc in docs.documents {
            guard let known = doc.modificationDate,
                  let diskDate = fileModificationDate(doc.url),
                  diskDate > known else { continue }
            guard let diskText = try? String(contentsOf: doc.url, encoding: .utf8) else { continue }
            if diskText == doc.text {
                // Same content (e.g. our own atomic save touched the mtime); just catch up.
                docs.update(doc.url) { $0.modificationDate = diskDate }
                continue
            }
            if doc.isDirty {
                externalChange = ExternalChange(url: doc.url, diskText: diskText, diskDate: diskDate)
                return
            } else {
                docs.update(doc.url) {
                    $0.text = diskText
                    $0.isDirty = false
                    $0.modificationDate = diskDate
                }
                status = "Reloaded \(doc.name) — changed on disk"
            }
        }
    }

    /// Resolve a prompted external change: `reload` adopts the disk version and discards local
    /// edits; otherwise we keep the user's version but record the new mtime so it stops asking.
    private func resolveExternalChange(_ change: ExternalChange, reload: Bool) {
        docs.update(change.url) {
            if reload {
                $0.text = change.diskText
                $0.isDirty = false
            }
            $0.modificationDate = change.diskDate
        }
        status = reload ? "Reloaded \(change.url.lastPathComponent)"
                        : "Kept your version of \(change.url.lastPathComponent)"
        externalChange = nil
        // Handle any other files that also changed while this prompt was up.
        DispatchQueue.main.async { checkExternalChanges() }
    }

    // MARK: - Run

    private func run() {
        guard let doc = docs.active else { return }
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

    // MARK: - Analyze

    /// Whether the open file is Python (the engine's only supported language).
    private var isPython: Bool {
        guard let ext = docs.active?.url.pathExtension.lowercased() else { return false }
        return ext == "py" || ext == "pyw"
    }

    /// Save the current file, then run the CodeCrack engine and show its findings.
    private func analyze() {
        guard let doc = docs.active else { return }
        save()  // engine reads from disk, so persist the current text first
        showIssues = true
        analyzeError = nil
        isAnalyzing = true
        status = "Analyzing \(doc.name)…"
        Analyzer.analyze(doc.url) { result in
            isAnalyzing = false
            switch result {
            case .success(let analysis):
                findings = analysis.findings
                generatedTests = analysis.tests
                analysisSummary = analysis.summary
                analyzeError = nil
                let n = analysis.summary.findings
                status = "Analysis found \(n) issue\(n == 1 ? "" : "s") in \(doc.name)"
            case .failure(let error):
                findings = []
                generatedTests = []
                analysisSummary = nil
                analyzeError = error.message
                status = "Analysis failed"
            }
        }
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
        // Reload any open file that changed on disk.
        for url in changed where docs.contains(url) {
            if let reloaded = try? String(contentsOf: url, encoding: .utf8) {
                docs.update(url) {
                    $0.text = reloaded
                    $0.isDirty = false
                    $0.modificationDate = fileModificationDate(url)
                }
            }
        }
        status = "Replaced in \(changed.count) file\(changed.count == 1 ? "" : "s")"
        runSearch()
    }
}
