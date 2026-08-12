# CodeCrack for Windows — Design Spec

> Status: approved design (2026-08-11). Scope: a full native Windows replica of the
> CodeCrack macOS app (`macapp/`, SwiftUI), driving the same Python engine (`engine/`).
> This spec is the input to the implementation plan.

## 1. Goal & principle

Deliver a native Windows IDE, `winapp/` (WPF + AvalonEdit, .NET 8, C#), that replicates
the macOS app **feature-for-feature** and drives the **same headless Python engine** via
the same JSON contract:

```
CodeCrack.exe  ──subprocess──►  python.exe -m codecrack analyze <file> --json
   (WPF/C#)                          (engine/, shared)
      ▲                                     │
      └──────── {findings, tests, summary} ─┘  (System.Text.Json)
```

The engine stays the single source of truth for analysis. The frontend is a rich shell,
exactly as on macOS. The app is **self-contained**: it bundles the engine **and** an
embedded CPython (with pytest), so it needs no system Python — matching the mac app.

### Decisions (locked)
- **UI:** WPF + AvalonEdit, `net8.0-windows`, C#, target **x64** (arm64 = one RID change).
- **Engine:** reuse the Python engine; bundle embedded Python (python-build-standalone,
  `x86_64-pc-windows-msvc-install_only`).
- **Highlighting/theming:** **TextMateSharp + AvalonEdit** (185+ languages + real
  swappable themes incl. OS light/dark follow — matches Highlightr breadth).
- **Engine scope:** **port `runner.py` now** so the execute stage (the "BUG PROVEN"
  proof loop) works on Windows day one — full parity.
- **Distribution:** self-contained folder, zipped in CI as `CodeCrack-windows.zip`
  (the analog of the mac `.app.zip`); MSIX is a later stretch, not in this spec.

## 2. The engine is NOT zero-change (corrected premise)

An adversarial audit (5 auditors + synthesis, run against the real repo) empirically
refuted the initial "zero engine changes" assumption. The **analyze + generate** path is
portable, but the **execute/sandbox stage is 100% broken on Windows**:

- `engine/codecrack/execution/runner.py:206` passes `preexec_fn` unconditionally →
  `subprocess.Popen` raises `ValueError: preexec_fn is not supported on Windows platforms`
  before pytest starts.
- `_preexec` calls `os.setsid()` (POSIX-only).
- `_kill_group` (`:223`) calls `os.killpg(os.getpgid(...), signal.SIGKILL)` — all POSIX-only.
- `_scrubbed_env` allowlist is Unix-shaped (PATH fallback `/usr/bin:/bin`; drops
  `TEMP/TMP/PATHEXT/SYSTEMDRIVE`).

Verified: on Windows the engine suite runs 13-failed/2-passed; `analyze --no-execute`
succeeds. **Fixing this is a prerequisite** (Phase 0) and also makes the shared engine
genuinely cross-platform, which CI then proves on all three OSes.

## 3. Folder structure

```
winapp/
  CodeCrack.sln
  CodeCrack.App.Core/            # net8.0 — POCO ViewModels + engine glue, NO WPF/editor types
    Engine/  Models.cs  Analyzer.cs  EngineLocator.cs  PythonInvocation.cs
             Runner.cs  RunCommand.cs  AnalyzerError.cs
    Editor/  LanguageMap.cs  OpenDocument.cs  EditorTheme.cs  IEditorHost.cs
    Session/ RecentFiles.cs  SessionStore.cs  WindowFrame.cs
    Search/  ProjectSearch.cs
    Settings/ AppSettings.cs  SettingsKeys.cs
    ViewModels/ MainViewModel.cs  EditorViewModel.cs  IssuesViewModel.cs
                TestsViewModel.cs ConsoleViewModel.cs FileTreeViewModel.cs SearchViewModel.cs
    Services/ FileIO.cs  ExternalChangeWatcher.cs  StatusBus.cs
  CodeCrackApp/                  # net8.0-windows, UseWPF — thin views only
    App.xaml(.cs)  MainWindow.xaml(.cs)
    Editor/ CodeEditorControl.xaml(.cs)   # AvalonEdit + TextMateSharp host implementing IEditorHost
    Panels/ IssuesPanel  TestsPanel  ConsolePanel
    FileTree/ FileTreeView   Search/ SearchView   Settings/ SettingsWindow
    Services/ Dpapi.cs  WindowsTheme.cs   Assets/ (icons)
  CodeCrack.Tests/              # net8.0 — xUnit
    ModelsContractTests.cs  EngineLocatorTests.cs  LanguageMapTests.cs
    RunCommandTests.cs  ProjectSearchTests.cs  SessionStoreTests.cs
    RecentFilesTests.cs  ExternalChangeTests.cs  ViewModelTests.cs
    Integration/ EngineEndToEndTests.cs
    Fakes/ FakeEditorHost.cs
  make-app.ps1                  # ← macapp/make-app.sh
  README.md
scripts/fetch-python-runtime.ps1   # ← scripts/fetch-python-runtime.sh
engine/ (Phase 0 changes)
  codecrack/execution/runner.py      # platform-branched
  codecrack/cli.py, report/render.py # encoding fix
  tests/test_determinism.py, tests/golden/analysis_result.json
.github/workflows/ci.yml            # + engine matrix win, csharp-tests, windows-build
```

### macOS → Windows file map (nothing dropped)

| macOS (`macapp/Sources/PPIDE/`) | Windows counterpart |
| --- | --- |
| `PPIDEApp.swift` (App/AppDelegate/Commands/menu) | `App.xaml(.cs)` + `MainWindow` menu + `InputBindings` |
| `ContentView.swift` (god-object shell) | `MainWindow.xaml` + decomposed `*ViewModel`s |
| `Editor/CodeEditor.swift` (NSTextView+Highlightr) | `CodeEditorControl` (AvalonEdit+TextMateSharp) |
| `Editor/LineNumberRulerView.swift` | AvalonEdit `ShowLineNumbers` (native) + issue-glyph margin |
| `Editor/Theme.swift` | `EditorTheme.cs` + TextMateSharp themes + `WindowsTheme.cs` |
| `Editor/LanguageMap.swift` | `Editor/LanguageMap.cs` |
| `Editor/TabBar.swift` + `OpenDocument.swift` | `OpenDocument.cs` + WPF `TabControl` template |
| `Editor/FilePicker.swift` | Win32 `OpenFileDialog`/folder dialog (two dialogs) |
| `Editor/SplitDividerHider.swift` | zero-width `GridSplitter` styling (no code) |
| `Editor/ConsolePanel.swift` | `Panels/ConsolePanel` + `ConsoleViewModel` |
| `Editor/IssuesPanel.swift` | `Panels/IssuesPanel` + `IssuesViewModel` |
| `Editor/Finding.swift` (Codable) | `Engine/Models.cs` (System.Text.Json) |
| `Editor/Analyzer.swift` | `Engine/{Analyzer,EngineLocator,PythonInvocation,AnalyzerError}.cs` |
| `Editor/Runner.swift` | `Engine/{Runner,RunCommand}.cs` |
| `FileTree.swift` | `FileTree/` + `FileTreeViewModel` |
| `Session/RecentFiles.swift` | `Session/RecentFiles.cs` |
| `Session/SessionRestore.swift` | `Session/SessionStore.cs` + `WindowFrame.cs` |
| `Settings/SettingsKeys.swift` + `SettingsView.swift` | `Settings/` + `SettingsWindow` + `Dpapi.cs` |
| `macapp/make-app.sh` | `winapp/make-app.ps1` |
| `scripts/fetch-python-runtime.sh` | `scripts/fetch-python-runtime.ps1` |

## 4. Data contract

`Engine/Models.cs` mirrors `Finding.swift` exactly with `System.Text.Json`:

- `AnalysisResult { findings[], tests[], summary }`
- `Finding { id, kind, target, location:int[], rationale, severity }`, `Line => location[0]||1`.
- `GeneratedTest { finding_id, test_name, source, expects, outcome?, detail, stdout, duration, reproduced }`
  — snake_case via `[JsonPropertyName]`; `NeedsInput => outcome=="skipped" || expects=="regression"`.
  **Consume `reproduced` as authoritative** — never re-derive the inverted oracle.
- `Summary { findings, tests, executed, reproduced, by_outcome{passed,failed,error,skipped} }`.
- Deserializer: `JsonUnmappedMemberHandling.Disallow` + `[JsonRequired]` on the required
  fields so a shape change fails loudly (not at runtime in front of a user).

## 5. Engine invocation (Windows specifics)

- **`EngineLocator`** precedence: `EnginePathOverride` setting → `CODECRACK_ENGINE_DIR`
  env → engine dir beside the app validated by `codecrack\__main__.py` → walk up from the
  file's dir → `EngineNotFound`. Engine cwd = `AppContext.BaseDirectory\Resources\engine`.
- **`PythonInvocation`** prefers `Path.Combine(AppContext.BaseDirectory,"python","python.exe")`;
  dev fallback `py -3`/`python` on PATH. **Never `Assembly.Location`** (returns `""` under
  single-file publish).
- **`Analyzer`** runs `Process` with `UseShellExecute=false`, `CreateNoWindow=true`,
  **separate** `RedirectStandardOutput`/`Error`, buffers, decodes once, marshals to the UI
  thread; four typed errors (`EngineNotFound/LaunchFailed/EngineFailed/DecodeFailed`) with
  message text matching the mac app (env-var guidance adjusted for Windows paths).

## 6. Architecture improvement — mandatory MVVM/Core split

The graph analysis flagged `ContentView` as a god-object (49 edges, betweenness 0.20,
bridging 7 subsystems). We do **not** port that shape. All analyze/run/state logic lives in
`CodeCrack.App.Core` POCO ViewModels behind an **`IEditorHost`** interface; the
AvalonEdit-hosting control is a thin, dumb view. This makes behavior testable headlessly
(no AvalonEdit on a UI thread) and even runnable on Linux CI for the pure-core tests.

## 7. Faithful-replica behaviors (explicit work-items)

Each is a discrete item so a file-to-file port cannot silently drop it:

- **External-file-change: 4-branch state machine** on `Window.Activated` (NOT a continuous
  `FileSystemWatcher`), storing `LastWriteTimeUtc` per doc, with a single-active-dialog
  guard: (1) skip if a prompt is up; (2) same-content-newer-mtime → silently catch up mtime
  (recognizes its own atomic save); (3) differs + dirty → "File changed on disk"
  Reload/Keep-My-Version; (4) differs + clean → silent reload + status; re-check after each.
- **Atomic UTF-8 (no BOM) save** (write-temp → `File.Replace`) that clears dirty and
  refreshes stored mtime; open reads UTF-8 with empty fallback.
- **Multi-tab model**: one reused `TextEditor`, swap `Document` per tab, save/restore
  `CaretOffset`+selection per doc (bounds-checked vs `TextLength`, deferred via Dispatcher);
  close-neighbor rule (prefer left, else right, else none); dirty-dot→hover-x header.
- **Session restore**: persist open paths + active path + window bounds; a **fingerprint**
  (paths + active, not text) gates re-saves; a `_didRestore` guard gates saving; restore
  opens files **inline without touching recents**; skip missing paths; rebuild tree from the
  active file's dir; empty tab set clears keys.
- **Window-frame persistence** on every `LocationChanged`/`SizeChanged` (reject width/height
  ≤ 0); restore once in `OnSourceInitialized`.
- **Recent files**: cap 10, dedup by `Path.GetFullPath`, newest-first, prune on load and on
  open (status "X is no longer available"); Open-Recent menu + separator + Clear Menu,
  disabled when empty; session-restore bypasses it.
- **Project search**: **case-insensitive search but case-sensitive replace-all** (preserve
  the asymmetry), 500-hit cap, skip hidden + non-UTF8 files, reload changed open buffers +
  re-run; status "Replaced in N file(s)".
- **Run command table (Windows)**: `py/pyw`→bundled `python.exe` (or `py -3`), `js/mjs/cjs`
  →node, `rb`→ruby, `sh/bash`→env-dependent (WSL/git-bash), `swift`→swift, `go`→`go run`,
  `php`→php, `pl`→perl, `java`→`JAVA_HOME`/`where javac` then javac+java, `.ps1`→powershell;
  merge stdout+err live, keep stdin open, print `$ {command}` / `[exited with code N]`,
  unknown ext → "Don't know how to run .{ext} files yet.", save before run.
- **Keyboard shortcuts** (Cmd→Ctrl): Open Ctrl+O, New Ctrl+N, Run Ctrl+R, Analyze Ctrl+B,
  Save Ctrl+S, Save As Ctrl+Shift+S, Close Tab Ctrl+W, Preferences, Find Ctrl+F
  (AvalonEdit `SearchPanel`); `CanExecute` predicates (Run/Analyze/Save need an active doc;
  Run also when running; Analyze also when analyzing or non-Python).
- **Settings**: JSON in `%APPDATA%` (or registry), same logical keys/defaults (fontSize 13,
  editorTheme system, indentUsesSpaces true, indentWidth 4, enginePathOverride "",
  claudeAPIKey ""). **Claude API key entered via `PasswordBox`, stored DPAPI-protected**
  (`ProtectedData`) — not plaintext.
- **Status bar**: bottom `TextBlock` (`TextTrimming=CharacterEllipsis`); every status string
  ported verbatim; initial "Open a file or folder to begin".
- **Click-to-line** `RevealLine(int)`: `GetLineByNumber` → `Select` → `ScrollToLine` →
  `BringCaretToView` → `Focus`; re-firable (don't guard on unchanged value).
- **File tree**: eager build skipping hidden, dirs-first case-insensitive sort, rebuild after
  New/Save-As. **Issues/Tests panels**: severity chips, reproduced-count headline, "BUG
  PROVEN" bound to `reproduced`, expandable traceback/stdout/source. **Preferences** 480×300
  tabbed (Editor/Engine/AI), control ranges (font 9–24, indent 2/4/8). **Open** needs two
  dialogs (WPF has no combined file+folder picker); opening a single file builds the tree
  from its parent.

### Non-goals (audit-confirmed restraint)
No tab drag-reorder. No dirty-tab save prompt (mac closes and discards silently). Do not add.

## 8. Engine Phase 0 changes (prerequisite)

- **`runner.py` platform branch** on `os.name=='nt'`: `preexec_fn=None` +
  `creationflags=CREATE_NEW_PROCESS_GROUP|CREATE_NO_WINDOW`; kill path via a **Win32 Job
  Object** (restores the memory cap) or `taskkill /F /T /PID`; guard `os.setsid()`; POSIX
  path unchanged.
- **`_scrubbed_env` Windows branch**: keep `PATH, SYSTEMROOT, SYSTEMDRIVE, TEMP, TMP,
  PATHEXT, NUMBER_OF_PROCESSORS, LANG, LC_ALL`; PATH fallback `%SYSTEMROOT%\System32`.
- **Tempdir cleanup**: `TemporaryDirectory(ignore_cleanup_errors=True)` + `proc.wait()`.
- **Encoding**: `sys.stdout.reconfigure(encoding='utf-8', errors='backslashreplace')` in
  `cli.py` (guarded by `hasattr`) so the em-dash in `render.py:94` can't crash under
  OEM/CJK codepages.
- Docs: replace the "engine invocation resolved / bundled Python" open-decision framing so it
  no longer implies the engine is unchanged; note the Windows sandbox branch.

## 9. Testing strategy (how we get "no failures")

- **Engine (pytest)**: add `windows-latest` (+ 3.13) to the matrix — this exercises the
  **execute stage** on Windows (only meaningful after Phase 0). `test_executor.py` already
  drives pytest-in-subprocess; rlimit-only assertions become `skipif(win32)`.
- **`test_determinism.py`**: `crack()` twice per multi-bug fixture → `render_json` byte-
  identical after masking `duration`.
- **JSON-contract golden**: `engine/tests/golden/analysis_result.json` **generated** from a
  multi-bug fixture via the CLI; a CI step regenerates + `git diff --exit-code` (fails the
  engine job on any shape change). C# `ModelsContractTests` deserializes it strictly
  (`Disallow` unknown + `[JsonRequired]`), normalizing `duration` before compare.
- **C# unit tests (xUnit)**: `EngineLocator` precedence (temp dirs), `LanguageMap`,
  `RunCommand` map (assert no `/usr/bin/env`/`bash`/`java_home`), `ProjectSearch` (incl.
  binary-skip + the search/replace asymmetry), `Session/RecentFiles` persist-reload,
  `ExternalChange` four branches (via fakes), `ViewModel` behavior via `FakeEditorHost`
  (analyze → Issues sorted by severity; select issue → caret set; run → output appended;
  error states rendered).
- **Integration**: `EngineEndToEndTests` shells to bundled Python on the real
  `engine/tests/fixtures/*.py` and asserts findings/tests decode — proves the whole
  subprocess→JSON loop on Windows. The `csharp-tests` job builds the embedded runtime first.
- **CI gates** (fail-the-build): `engine-tests` (win/linux/mac + versions), `csharp-tests`
  (windows, SDK pinned via `global.json`, `Nullable=enable`, `TreatWarningsAsErrors`,
  coverage floor; pure-core tests also on ubuntu), `windows-build` (publishes + bundles +
  zips the artifact, `if-no-files-found: error`).

## 10. Packaging & CI

- **`scripts/fetch-python-runtime.ps1`**: asset
  `cpython-3.12.13+20260623-x86_64-pc-windows-msvc-install_only.tar.gz` (versions synced with
  the `.sh`), `tar -xzf` (bsdtar ships on win10+/runner), `PythonBin=python\python.exe`, then
  `pip install "pytest>=8,<9"` guarded by a stamp file. Verify the asset URL 200s before
  pinning.
- **`winapp/make-app.ps1`**: single
  `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
  -p:IncludeNativeLibrariesForSelfExtract=true` (**no `PublishTrimmed`** — WPF forbids it) →
  copy `engine\codecrack` + `pyproject.toml` to `Resources\engine` (prune `__pycache__`) →
  fetch runtime → copy `python\` beside the exe → **guarded `signtool`** block (resolve via
  `Get-Command`/SDK path; skip gracefully when `$env:CODECRACK_WINDOWS_CERT` is unset) →
  optional launch unless `CODECRACK_SKIP_LAUNCH`.
- **CI `windows-build`** (windows-latest): checkout → `setup-dotnet` 8.0.x → run
  `make-app.ps1` with `CODECRACK_SKIP_LAUNCH=1` + guarded signing secrets →
  `Compress-Archive` → `upload-artifact CodeCrack-windows.zip` (`if-no-files-found: error`).
- **`.gitignore`** += `bin/`, `obj/`, `*.user`, `*.suo` (keep existing `build/`, `dist/`).

## 11. Build sequence

0. **Phase 0** — engine Windows portability + engine tests/CI matrix + golden + determinism.
1. **Phase 1** — solution skeleton, Core DTOs + Analyzer/locator, contract test, `csharp-tests` CI.
2. **Phase 2** — editor (AvalonEdit+TextMateSharp, themes, LanguageMap, tabs, file I/O, click-to-line).
3. **Phase 3** — analyze/Issues/Tests loop + Runner + Console + status bar.
4. **Phase 4** — shell, file-tree, search, recents, sessions, external-change, shortcuts, settings/DPAPI.
5. **Phase 5** — packaging (`fetch-python-runtime.ps1`, `make-app.ps1`) + `windows-build` CI + artifact.

Phase 0 gates everything (execution parity). Phases 1–4 each ship with their tests green;
Phase 5 produces the downloadable artifact — the parity milestone with the mac `.app.zip`.
