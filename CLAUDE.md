# CodeCrack

A language-agnostic IDE that reads your code, understands it, and automatically
generates test cases that expose runtime and logical errors. Shared, language-agnostic
core + per-language adapters (first targets: Python, JS/TS, Java). See `README.md`.

## ⚠️ AUTOMATED EDITING & CONTEXT-LIMITING DIRECTIVES
When working on the CodeCrack project, you must operate as an automated agent that directly executes workspace modifications using available Revcode MCP tools while preserving token limits.

1. **Direct File Mutation Over Chat Output**
   * Do not print full code blocks or file diffs in the chat box if you can write them directly.
   * Use the `Write()` or file-modification tools immediately to apply code changes directly to the target files (e.g., `codecrack/core/models.py`).
   * Keep chat logs to a bare minimum: state the tool you are calling, execute it, and report success/failure in 1-2 lines.

2. **Strict Folder & Contract Isolation**
   * Do not read or search files outside the immediate module folder you are assigned to.
   * For cross-file architectural decisions, rely *only* on `docs/ARCHITECTURE.md` and `docs/core-design-brief.md`. Never poll the entire repository tree.

3. **Incremental Execution & Single-Tool Passes**
   * Execute file modifications incrementally. Do not chain massive, speculative tool calls that might fail and bloat the token history with error traces.
   * Write the core logic, verify it compiles/parses if a terminal tool is available, and stop.

4. **Self-Termination Protocol**
   * Once you have successfully written and saved the required changes to your designated file, output exactly: `[TASK_COMPLETE: IDLE]`.
   * Do not ask follow-up questions or prompt for more work in that worker session.

---

> [!note] Current project status — referenced paths
> The directives above are the standing operating contract for worker sessions.
> - `docs/ARCHITECTURE.md` and `docs/core-design-brief.md` — **✅ created** (distilled from the design vault, self-contained). Directive #2's strict-isolation mode is operational.
> - `codecrack/` module (e.g. `codecrack/core/models.py`) — **not scaffolded yet;** the path in directive #1 is illustrative until the package exists.
> Remove this note once the `codecrack/` package is in place.
