# CodeCrack — Architecture

> Canonical, self-contained architecture reference. Worker sessions may rely on this
> file (and `core-design-brief.md`) for cross-file decisions without polling the repo.
> Distilled from the project design vault; if the two disagree, this file wins for code.

CodeCrack is a native macOS IDE that reads code, understands it, and automatically
generates test cases that expose runtime and logical errors. The defining
constraint: **everything above the language-adapter line is language-agnostic** —
the engine is useful headless, and the macOS app is a rich shell on top of it.

## Layers

```
┌──────────────────────────────────────────────────────────────┐
│ FRONTEND — native macOS app (SwiftUI, macapp/)                │
│   File-tree sidebar · code editor · testing panel             │
│   NavigationSplitView shell; open folder, edit, save          │
└───────────────▲───────────────────────────────────────────────┘
                │ runs the engine as a local process
┌───────────────┴───────────────────────────────────────────────┐
│ CORE ENGINE — "CodeCrack core" (Python, LANGUAGE-AGNOSTIC)     │
│   Parse (syntax tree) → Static analysis →                      │
│   Control/data-flow analysis →                                 │
│   Automated test generation ↔ property-based testing/fuzzing   │
│   Sandboxed execution · surfaces runtime + logical errors      │
│   Usable headless via the CLI                                  │
├─────────────────────────────────────────────────────────────────┤
│ LANGUAGE ADAPTERS  (one per language)                          │
│   Python first (ast/pytest) · JS-TS and Java to follow          │
└───────────────▲────────────────────────────────────────────────┘
                │ augmented by
┌───────────────┴────────────────────────────────────────────────┐
│ AI LAYER                                                         │
│   LLM integration (Claude API) · explains failures, suggests    │
│   fixes — assists, never decides                                │
└──────────────────────────────────────────────────────────────────┘
```

## The core analysis flow (one pass, any language)

1. User opens a folder and edits code in the macOS app.
2. The app invokes the CodeCrack engine on the file/project (local process).
3. The matching **language adapter** parses source into a syntax tree and hands a
   **normalized representation** to the shared core.
4. The core runs static analysis + control/data-flow analysis.
5. Suspicious paths feed automated test generation (aided by fuzzing and, later,
   symbolic execution).
6. Candidate tests run inside a **per-language sandbox** via that language's test
   runner; failures that reveal runtime/logical errors are kept and minimized.
7. The AI layer explains failures and suggests fixes in plain language.
8. Results surface back in the app as findings + generated tests (click-to-line).

## Language-agnostic core (the pillar)

A **shared, language-neutral core** plus thin **per-language adapters**. Adding a
language means writing an adapter, not a new engine.

| Shared core (language-neutral)        | Per-language adapter                  |
| ------------------------------------- | ------------------------------------- |
| Flow analysis over normalized tree    | Concrete parser (e.g. Python `ast`)   |
| Test-generation strategies            | Native test emitter + runner          |
| Fuzzing orchestration                 | Value/type generators for that lang   |
| Bug taxonomy + reporting              | Error/stack-trace mapping             |
| AI explanations                       | Language-specific prompt context      |

See `core-design-brief.md` for the adapter **contract** every adapter must satisfy.

## Component notes

- **Engine** — implemented in Python (`engine/codecrack/`): `analysis/` (analyzer),
  `generation/` (test generator), `core/` (shared `Finding`/`GeneratedTest`
  models), `report/` (rendering), and a `cli` entry point. Headless and
  CI-friendly; the macOS app is one consumer.
- **Parsing** — Python `ast` first; broaden to tree-sitter / native parsers as more
  language adapters land.
- **Test generation** — strategy ladder: heuristic/boundary → property-based →
  coverage-guided fuzzing → symbolic/concolic → LLM-assisted (always
  deterministically validated).
- **Test runners** — pytest (Python) first; Jest/Vitest (JS/TS), JUnit (Java) as
  adapters arrive.
- **Sandboxed execution** — **never run user code in-process.** Isolation ladder:
  subprocess + rlimits → OS sandbox → containers → microVMs → WASM. Every run has
  timeouts + memory caps + no network/host-FS by default.
- **AI layer** — Claude API (latest models). AI **assists, it does not decide**:
  every suggested test/fix is executed in the sandbox and proven before it's
  trusted. Keep non-deterministic LLM steps outside the reproducible path.
- **Frontend** — native macOS app in SwiftUI (`macapp/`). As of v1.0 the engine is
  fully wired in: file-tree sidebar + editor, an **Analyze** action, an **Issues**
  panel, and a **Tests** tab that surfaces which generated tests reproduce a real
  failure (pass/fail badges, click-to-line). The app is self-contained — it bundles the
  engine and an embedded Python runtime (with pytest), so it needs no system `python3`.

## Design principles

1. **Language-agnostic core, thin language adapters.** New languages = new adapters.
2. **Engine-first.** The core is useful headless (CLI/CI); the macOS app is a rich shell.
3. **Isolate anything that runs user code** — always via the per-language sandbox.
4. **AI assists, it doesn't decide** — deterministic engine finds; LLM explains.
5. **Prove the loop in ONE language (Python) before generalizing** the adapter interface.

## Open decisions (see `core-design-brief.md` → Decisions)

- Where the sandbox runs: locally vs server-side.
- Deterministic vs LLM-driven balance in test generation.
- ~~How the macOS app invokes the engine: bundled Python vs a packaged binary.~~
  **Resolved (v1.0):** the app bundles an embedded Python runtime and runs the engine
  as a local process, so it needs no system `python3`.
