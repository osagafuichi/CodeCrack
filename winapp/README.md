# CodeCrack for Windows (`winapp/`)

Native Windows IDE (WPF + AvalonEdit, .NET 8, C#) that drives the shared Python
engine in `../engine/`. Self-contained: the shipped build bundles the engine and an
embedded CPython (with pytest), so end users need no system Python.

## Prerequisites

- Windows 10/11 x64.
- .NET 8 SDK (pinned via `global.json`). Install: `winget install Microsoft.DotNet.SDK.8`.
- PowerShell 7+ (`pwsh`) for the build scripts. `tar` (bsdtar) ships with Windows 10+.

## Build & run (development)

```powershell
dotnet build winapp\CodeCrack.sln -c Debug
dotnet run --project winapp\CodeCrackApp\CodeCrackApp.csproj
```

In dev, the engine is discovered from the source checkout and Python falls back to
`py -3` / `python` on PATH (see `PythonInvocation`).

## Tests

```powershell
# C# unit + integration tests (xUnit)
dotnet test winapp\CodeCrack.sln
# PowerShell packaging/CI tests (Pester 5)
pwsh -NoProfile -Command "Invoke-Pester -Path scripts\tests, winapp\tests -Output Detailed"
```

## Package a distributable

```powershell
# Publishes self-contained/single-file, bundles engine + embedded CPython/pytest,
# and writes dist\CodeCrack\. Set CODECRACK_SKIP_LAUNCH=1 to skip auto-launch.
$env:CODECRACK_SKIP_LAUNCH = "1"
pwsh -NoProfile -File winapp\make-app.ps1
```

Output tree:

```
dist\CodeCrack\
  CodeCrack.exe                     # self-contained single-file WPF app
  Resources\engine\codecrack\...    # bundled Python engine (+ pyproject.toml)
  python\python.exe                 # embedded CPython 3.12 with pytest
```

Run the packaged app by double-clicking `dist\CodeCrack\CodeCrack.exe`.

### Optional code signing

`make-app.ps1` signs `CodeCrack.exe` only when `CODECRACK_WINDOWS_CERT` points at a
`.pfx` (with `CODECRACK_WINDOWS_CERT_PASSWORD`); otherwise signing is skipped. CI
(`.github/workflows/ci.yml`, job `windows-build`) runs the same script headless and
uploads `CodeCrack-windows.zip`.
