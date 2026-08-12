# CodeCrack for Windows

**Debug and test your Python code, faster.** CodeCrack reads a file, finds suspected
bugs, **automatically generates tests, runs them in a sandbox, and proves the bug** by
reproducing the failure — all in a native Windows IDE.

It's **self-contained**: the download bundles the analysis engine *and* an embedded
Python runtime (with pytest), so it runs on a stock Windows PC with **no .NET and no
Python installed**.

> **Early build.** This is a functional, CI-tested preview, not a hardened 1.0. It is
> **unsigned** (so Windows SmartScreen will warn on first launch — see below) and runs
> analyzed code with **time-limited isolation only**. Analyze code you trust; don't
> point it at files from untrusted sources yet. See [Known limitations](#known-limitations).

---

## Download & run

1. **Download** `CodeCrack-windows.zip` from the
   [**Releases**](https://github.com/osagafuichi/CodeCrack/releases) page.
   *(No release yet? Grab it from the latest green
   [Actions run](https://github.com/osagafuichi/CodeCrack/actions) → the `CodeCrack-windows`
   artifact — you need to be signed into GitHub.)*
2. **Unzip** it anywhere (e.g. `C:\Users\<you>\CodeCrack`). Keep the folder together —
   `CodeCrack.exe`, `python\`, and `Resources\engine\` must stay side by side.
3. **Run** `CodeCrack.exe`.

### First launch: SmartScreen
Because the app isn't code-signed yet, Windows shows **"Windows protected your PC"**.
Click **More info → Run anyway**. (This is expected for unsigned apps; a signed build is
on the roadmap.) The first launch is also a little slower while Windows Defender scans
the ~170 MB executable — subsequent launches are fast.

### Requirements
- Windows 10 or 11, 64-bit (x64).
- Nothing else — the .NET runtime and Python are bundled.

---

## How to use

1. **Open a Python file** — `Ctrl+O` (or use the file tree on the left).
2. **Analyze** — press **`Ctrl+B`** (or **Run ▸ Analyze**). CodeCrack finds suspected
   bugs, generates tests, runs them, and shows results.
3. **Read the results** in the bottom panels:
   - **Issues** — each suspected bug; click to jump to the line.
   - **Tests** — the generated tests, with a **"BUG PROVEN"** badge where a test
     reproduced a real failure, and *"N tests reproduce a real failure"* in the header.
4. **Run the file** — `Ctrl+R` streams its output to the **Console** tab.

Try it on the bundled samples if you have the source checkout — e.g.
`engine\tests\fixtures\zero_division.py` gives a guaranteed proven bug.

### Keyboard shortcuts
| Action | Key |
| --- | --- |
| Open file | `Ctrl+O` |
| New file | `Ctrl+N` |
| Save / Save As | `Ctrl+S` / `Ctrl+Shift+S` |
| Close tab | `Ctrl+W` |
| **Analyze** | `Ctrl+B` |
| Run file | `Ctrl+R` |
| Preferences | `Ctrl+,` |

---

## What's in the download

```
CodeCrack.exe                     self-contained Windows app (WPF, .NET 8)
python\python.exe                 embedded CPython 3.12 with pytest
Resources\engine\codecrack\...    the analysis engine (+ pyproject.toml)
```

Settings and your last session are stored under `%APPDATA%\CodeCrack\`.

---

## Troubleshooting

- **"Analysis failed"** — make sure you're running the **packaged** `CodeCrack.exe` (not
  a `dotnet run` dev build). The packaged app carries the engine + Python beside it and
  works on any saved `.py` file. Also confirm the file is **saved to disk** (Analyze reads
  from disk and is only enabled for `.py`).
- **SmartScreen won't let it run** — click **More info → Run anyway** (unsigned build).
- **First launch is slow** — Defender is scanning the bundled runtime once; it's quick
  afterward.
- **Nothing happens after Analyze** — click the **Issues**/**Tests** tab in the bottom
  panel (it doesn't auto-focus yet — see below).

## Known limitations

This is an early build; these are the rough edges to expect:
- **Unsigned** → SmartScreen warning on first run.
- **Sandbox is time-limited only on Windows** — analyzed code runs with a wall-clock
  timeout but no memory/network/filesystem cap. **Only analyze code you trust.**
- Some UI polish is still pending: **`Ctrl+F`** just focuses the editor (no find bar yet),
  there's **no project-search or theme-picker UI**, the indentation preference isn't
  applied to the editor, results panels don't auto-focus after Analyze/Run, and a new
  (untitled) tab shows a blank title.

## Uninstall

Delete the unzipped folder and, if you want to remove your settings, the
`%APPDATA%\CodeCrack\` directory. Nothing is written to the registry or Program Files.

---

## Build from source (developers)

Prerequisites: Windows 10/11 x64, **.NET 8 SDK** (`winget install Microsoft.DotNet.SDK.8`),
and PowerShell 7+ (`pwsh`). `tar` ships with Windows 10+.

```powershell
# Dev build + run (uses your source checkout's engine; falls back to py -3 on PATH)
dotnet build winapp\CodeCrack.sln -c Debug
dotnet run --project winapp\CodeCrackApp\CodeCrackApp.csproj

# Tests (deterministic unit + integration gate)
dotnet test winapp\CodeCrack.sln
pwsh -NoProfile -Command "Invoke-Pester -Path scripts\tests, winapp\tests -Output Detailed"

# UI smoke: drives the PACKAGED exe (launch -> open file -> Analyze -> assert BUG PROVEN)
# via FlaUI. Needs a built dist\CodeCrack\ (make-app.ps1 first). Not part of the sln gate.
dotnet test winapp\CodeCrack.UiTests\CodeCrack.UiTests.csproj

# Package the self-contained distributable -> dist\CodeCrack\
$env:CODECRACK_SKIP_LAUNCH = "1"
pwsh -NoProfile -File winapp\make-app.ps1
```

`make-app.ps1` publishes the WPF app self-contained/single-file, bundles the engine and
an embedded CPython (with pytest via `scripts\fetch-python-runtime.ps1`), and writes
`dist\CodeCrack\`. It signs `CodeCrack.exe` only when `CODECRACK_WINDOWS_CERT` points at a
`.pfx` (with `CODECRACK_WINDOWS_CERT_PASSWORD`); otherwise signing is skipped. CI
(`.github/workflows/ci.yml`, job `windows-build`) runs the same script headless and, once
its tests are green, uploads `CodeCrack-windows.zip`; a parallel `ui-smoke` job runs the
FlaUI test above against the freshly built bundle.

### Clean-room verification (pre-release)

The self-contained claim ("no .NET / no Python needed") should be verified on a pristine
machine before a public release — CI runners are pre-provisioned, so they don't prove it.
`winapp\clean-machine\` has a **Windows Sandbox** config (`CodeCrack.wsb`) and
`run-clean-smoke.ps1` that, inside a fresh no-Python/no-.NET box, run the bundled engine
to prove a bug reproduces and confirm the GUI launches. Windows Sandbox needs Windows
Pro/Enterprise/Education (not Home); see the comments in `CodeCrack.wsb`.
