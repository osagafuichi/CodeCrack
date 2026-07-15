# CodeCrack — Core Design Brief

> Canonical, self-contained design contract for the **CodeCrack core engine**.
> Worker sessions may rely on this file (and `ARCHITECTURE.md`) for cross-file
> decisions without polling the repo. Distilled from the project design vault.

## What CodeCrack is

**Debug and test any codebase, faster.** A language-agnostic IDE that reads your
code, understands it, and **automatically generates test cases that expose runtime
and logical errors** — so developers spend less time writing tests and more time
shipping. It pairs deterministic program analysis with an AI layer that explains
*why* a test fails.

- **Form factor:** a full **standalone IDE** (Electron/Tauri), not a plugin.
- **Scope:** language-agnostic. First-class targets: **Python, JS/TS, Java.**
- **Name:** the product is **CodeCrack** (formerly PyCrack; "Smart IDE" was an early
  working title, retained only as the design-vault folder name).

## The problem & the wedge

Most bugs that reach production are **logical errors**: the code runs but does the
wrong thing on the input nobody tested. Writing thorough tests is tedious, so edge
cases get skipped. CodeCrack's wedge is **test generation that finds real bugs** —
and the techniques (parsing, flow analysis, fuzzing) generalize across languages via
a common core + per-language adapters.

## Bug taxonomy (what the engine hunts)

Language-agnostic categories; adapters map concrete errors onto them.

| Kind               | When caught       | Detection                              |
| ------------------ | ----------------- | -------------------------------------- |
| **Syntax error**   | before running    | the parser (trivial)                   |
| **Runtime error**  | during execution  | run in sandbox → it throws/hangs       |
| **Logical error**  | never (silent)    | needs an **oracle** (see below)        |

Runtime examples: null/None deref, index/key out of range, type errors, divide-by-zero,
infinite loops (Python `ZeroDivisionError`/`IndexError`; JS `TypeError`/`RangeError`;
Java `NullPointerException`/`ArrayIndexOutOfBoundsException`).

### The oracle problem (logical errors)

No automatic "this is wrong" signal exists for logical errors. Sources of an oracle:

- **Properties/invariants** — property-based testing (round-trips, "stays sorted").
- **Metamorphic relations** — known relationship between `f(x)` and `f(2x)`.
- **Assertions / type hints / docstrings** — the author's stated contract.
- **Differential testing** — compare against a reference or prior version (regression).
- **LLM judgment** — propose intended behavior from context, then verify deterministically.

A finding must report: bug class, minimal reproducing input, observed vs expected,
and a plain-language explanation.

## Test-generation strategy (core)

Strategy ladder, increasing power/cost — the core owns strategy, adapters own
language-specific value generation and emission:

1. **Heuristic / boundary** — from signature + hints: `0`, `-1`, empty, null, huge,
   unicode, NaN. Cheap, effective.
2. **Property-based** — declare invariants; generate + **shrink** counterexamples
   (Hypothesis / fast-check / jqwik).
3. **Coverage-guided fuzzing** — mutate inputs, keep those reaching new paths.
4. **Symbolic / concolic** — solve path constraints for inputs hitting a branch
   (later phase; high value on tight pure functions).
5. **LLM-assisted** — model proposes meaningful cases; **always validated deterministically.**

Pipeline: `analyze (static + flow) → pick strategy → generate inputs → run in
sandbox → did it fail? → minimize → report + explain`.

A good generated test is **reproducible, minimal, exposes a genuine bug** (not a
contract the code never promised), and comes with an explanation. Seed all randomness.

## The adapter contract (what every language adapter MUST provide)

This is the interface between the shared core and a language. Prove it end-to-end in
**Python first**, then abstract.

1. **Parse** — source → a syntax tree the core can read (tree-sitter or native), with
   line/column info for mapping findings back to the editor.
2. **Emit** — generated tests in the language's **native** test framework
   (pytest / Jest·Vitest / JUnit), idiomatic and runnable, parametrized where possible.
3. **Execute** — run those tests in a language-appropriate **sandbox** (its runtime,
   its deps) with timeouts + memory caps + no network/host-FS by default.
4. **Map results** — errors, stack traces, coverage → the core's normalized model of
   the bug taxonomy above (normalize differing coverage formats in the adapter).
5. **Context for AI** — supply language-specific prompt context for explanations.

### Invariants worker sessions must honor

- **Never run user/generated code in-process** — always through the sandbox.
- **Determinism** — generated tests must reproduce (seed RNG) in every language.
- **AI never decides** — a suggested test must actually fail; a suggested fix must
  pass; keep LLM steps out of the reproducible verification path.
- **Don't over-generalize early** — the adapter interface hardens only after the loop
  works end-to-end in one language.
- **Treat all analyzed code as hostile** — default-deny egress; isolate per-tenant.

## Target users

- **Primary — the multi-stack dev:** works across languages weekly; wants one IDE that
  debugs + auto-generates tests everywhere, same UX.
- **Secondary — the team lead:** mixed-language codebase, shaky coverage; wants
  consistent adversarial tests across services.
- **Tertiary — the learner:** doesn't know what edge cases to test; the AI explanation
  of a failing test teaches.

## Decisions

Settled:

- Product = **CodeCrack**; language-agnostic; **standalone IDE**.
- First targets: **Python → JS/TS → Java** (prove the loop in Python first).
- Version control: Jujutsu (jj), colocated with git.
- Analysis approach: deterministic static analysis + test generation; AI assists.
- Adapter architecture: shared core + per-language adapters (tree-sitter + LSP).

Open (do not assume — escalate if a task depends on these):

- **Engine implementation language:** Python (fast to build, great AST tooling) vs
  Rust (speed, native tree-sitter, embeds in a Tauri desktop shell). Drives the next one.
- **Electron vs Tauri** for the desktop shell.
- **Where the sandbox runs:** locally on the user's machine vs server-side.
- **Deterministic vs LLM-driven** balance in test generation.

See `ARCHITECTURE.md` for how these components wire together.
