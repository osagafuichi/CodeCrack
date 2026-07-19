# CodeCrack

**Debug and test your code, faster.**

CodeCrack reads your code, understands it, and **automatically generates *and runs*
test cases that expose runtime and logical errors** — then proves them by reproducing
the failure. You spend less time writing tests and more time shipping.

It pairs a deterministic analysis engine (parsing, static & flow analysis, test
generation, sandboxed execution) with a native macOS IDE. The engine is
language-agnostic by design and useful headless; the app is a rich, self-contained
shell on top of it. Your code runs in isolation, never loose on your machine.

> **v1.0 — first official release.** v1.0 supports **Python**, end to end.
> JavaScript / TypeScript and Java adapters are on the [roadmap](#roadmap) for v1.1.

## What's in v1.0

- **Analysis engine** (`engine/`) — a headless, CI-friendly Python core that analyzes a
  file, generates tests, **executes them in a sandbox**, and reports which ones
  reproduce a real failure (text or JSON). The core is stdlib-only.
- **Native macOS app** (`macapp/`) — a SwiftUI IDE: file-tree sidebar, code editor with
  syntax highlighting, an **Analyze** action, an **Issues** panel, and a **Tests** tab
  that shows *"N tests reproduce a real failure"* with per-test pass/fail badges and
  click-to-line. Plus project search, recent files, and session/window restore.
- **Self-contained distribution** — the app bundles the engine **and an embedded Python
  runtime (with pytest)**, so it runs on a stock Mac with **no system `python3`
  required**.
- **Continuous integration** — GitHub Actions builds the full `.app` bundle and uploads
  it as a downloadable artifact on every push.

## Install & run

### The macOS app (recommended)

Download the `.app` from the latest CI build artifact, **or** build it locally:

```sh
bash macapp/make-app.sh
```

This builds a release binary, bundles the engine + embedded Python runtime, wraps it
into a double-clickable `PPIDE.app`, and launches it. Then: open a folder, edit a file,
hit **Analyze**, and review results in the **Issues** and **Tests** panels.

Requirements to build: **macOS 14+** and a **Swift toolchain** (Xcode or the Swift
command-line tools). Nothing else — the runtime is bundled.

> **First launch (unsigned builds):** unless the app is built with Apple signing
> credentials, macOS Gatekeeper will block it the first time. Right-click the app →
> **Open** → **Open** to allow it. (To ship a signed/notarized build, set
> `CODECRACK_SIGN_IDENTITY`, `CODECRACK_APPLE_ID`, `CODECRACK_TEAM_ID`, and
> `CODECRACK_APP_PASSWORD` before running `make-app.sh`.)

### The engine (headless CLI)

For CI or terminal use, install the engine and run it directly. Requires **Python
3.10+**; the `[dev]` extra adds **pytest**, used to execute the generated tests.

```sh
pip install -e "./engine[dev]"

codecrack analyze path/to/file.py            # human-readable report
codecrack analyze path/to/file.py --json     # machine-readable JSON
cat file.py | codecrack analyze -            # read from stdin
```

`codecrack` and `python -m codecrack` are equivalent. Flags:

- `--json` — emit JSON instead of the text report.
- `--module NAME` — the import path the generated tests should target (default: `target`).
- `--no-execute` — analyze and generate only; skip the sandbox execute stage.

## How it works

1. You open a folder and edit code in the app (or point the CLI at a file).
2. CodeCrack invokes the engine on the file as a local process.
3. The language adapter parses the source and hands a **normalized representation** to
   the shared core.
4. The core runs static + control/data-flow analysis and feeds suspicious paths into
   automated test generation.
5. Generated tests **run in a sandbox** via the language's test runner; the ones that
   reproduce a real runtime/logical error are kept and surfaced.
6. Results appear as findings and click-to-line generated tests, each labelled with
   whether it reproduces a failure.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full design and the
language-adapter contract in [`docs/core-design-brief.md`](docs/core-design-brief.md).

## Roadmap

- **Now (v1.0):** Python engine + CLI, sandboxed test execution, self-contained native
  macOS app, CI-built `.app`.
- **Next (v1.1+):** JavaScript / TypeScript and Java language adapters; an AI layer that
  explains failures and suggests fixes (assists, never decides); stronger sandbox
  isolation; a signed/notarized release.

---

*CodeCrack was previously named PyCrack; "Smart IDE" was an early working title.*
