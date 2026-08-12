# CodeCrack for Windows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `winapp/` — a native Windows IDE (WPF + AvalonEdit, .NET 8) that replicates the CodeCrack macOS app feature-for-feature and drives the same Python engine, and make that engine run its sandboxed execute stage on Windows.

**Architecture:** A shared, headless Python engine (unchanged public surface) invoked as a subprocess emitting JSON. A strict MVVM/Core split: all logic lives in `CodeCrack.App.Core` (POCO, no UI types, testable headlessly and on Linux) behind an `IEditorHost` interface; `CodeCrackApp` is a thin WPF view layer hosting AvalonEdit + TextMateSharp. Self-contained distribution bundles the engine + an embedded CPython (with pytest).

**Tech Stack:** .NET 8 / C# / WPF; AvalonEdit + TextMateSharp; System.Text.Json; xUnit; Python 3.10–3.13 engine (stdlib + pytest); python-build-standalone; PowerShell packaging; GitHub Actions.

**Design spec:** `docs/superpowers/specs/2026-08-11-windows-port-design.md`

## Global Constraints

- .NET 8 SDK pinned via `global.json`; `net8.0-windows` (WPF app) / `net8.0` (Core + Tests).
- `Nullable=enable`; `TreatWarningsAsErrors=true` (`Directory.Build.props`).
- `System.Text.Json` only (no Newtonsoft); DTOs are records.
- `CodeCrack.App.Core` has NO WPF/AvalonEdit/TextMateSharp references (must build & test on Linux CI too).
- Engine POSIX code path stays byte-for-byte unchanged; Windows behavior added only behind `if os.name == "nt"` / `IS_WINDOWS`.
- Repo root `C:\Users\samet\CodeCrack`; branch `windows-port`; commit after every green step.
- Engine invoked as `<python> -m codecrack analyze <file> --json`, cwd = engine dir. Never re-derive `GeneratedTest.Reproduced` — consume the engine's flag.
- Python interpreter resolved via `AppContext.BaseDirectory` (NEVER `Assembly.Location`). Windows PBS layout is `python\python.exe` (top-level) + `Lib\site-packages`.
- Target `win-x64`. WPF forbids `PublishTrimmed`. Self-contained single-file publish.
- **Phase 0 gates all app phases** (execution parity). Phases 1–4 each land with their tests green; Phase 5 produces the downloadable artifact.

## Phase 0: Engine Windows portability

**Prerequisite for the whole phase (run once).** All execute-stage tests require pytest to be importable by a *scrubbed* subprocess. On a global `--user` install the scrubbed env can't find it, so work inside a venv (this also mirrors CI's `pip install -e "./engine[dev]"`):

```powershell
cd C:\Users\samet\CodeCrack
git checkout windows-port
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -e ".\engine[dev]"
```

> **Ordering note:** Tasks are listed in *execution* order (dependencies first). The numbers are the spec's coverage IDs (§8/§9), so they are intentionally **not** in ascending numeric order. Execute top-to-bottom: 0.2 → 0.3 → 0.4 → 0.5 → 0.6 → 0.1 → 0.7 → 0.8 → 0.9 → 0.10. Commit after every green step; the branch is `windows-port`.

---

### Task 0.2: `runner.py` — platform-branch the `Popen` launch
**Files:** Modify `engine/codecrack/execution/runner.py`. Test `engine/tests/test_runner_platform.py` (create).
**Interfaces:** Consumes: `execute_tests(list[GeneratedTest], module_source: str, *, module: str, config: SandboxConfig|None)`, `GeneratedTest(finding_id, test_name, source, expects)`. Produces: module-level `IS_WINDOWS: bool`; `execute_tests` runs on Windows (was `ValueError`).

- [ ] **Step 1: Write the failing test** — create `engine/tests/test_runner_platform.py`:
```python
"""Platform-portability of the sandbox executor (POSIX + Windows)."""

from __future__ import annotations

import sys
import time

import pytest

from codecrack.core.models import GeneratedTest
from codecrack.execution import SandboxConfig, execute_tests


def _mk(name: str, body: str, expects: str = "assertion") -> GeneratedTest:
    src = f"def {name}():\n" + "\n".join(f"    {line}" for line in body.splitlines()) + "\n"
    return GeneratedTest(finding_id="F001", test_name=name, source=src, expects=expects)


def test_execute_starts_subprocess_on_this_platform():
    passing = _mk("test_ok", "assert 1 + 1 == 2", expects="assertion")
    failing = _mk("test_bad", "assert 1 + 1 == 3", expects="assertion")
    execute_tests([passing, failing], module_source="", module="target")
    assert passing.outcome == "passed"
    assert failing.outcome == "failed"
```

- [ ] **Step 2: Run it, expect FAIL** — `pytest engine/tests/test_runner_platform.py::test_execute_starts_subprocess_on_this_platform -v`. Expect `ValueError: preexec_fn is not supported on Windows platforms` raised from `subprocess.Popen`.

- [ ] **Step 3: Implement** — in `engine/codecrack/execution/runner.py`, add the constant right after the `import tempfile` / `from dataclasses import dataclass` block (before `try: import resource`):
```python
IS_WINDOWS = os.name == "nt"
```
Then replace the launch block inside `execute_tests` (currently the `timed_out = False` ... `_attach_results(...)` block) with:
```python
        timed_out = False
        popen_kwargs = dict(
            cwd=scratch,
            env=env,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
        )
        if IS_WINDOWS:
            # Windows has no POSIX process groups / preexec_fn. A new process
            # group + no console window lets us kill the whole tree on timeout.
            popen_kwargs["creationflags"] = (
                subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_NO_WINDOW
            )
        else:
            popen_kwargs["preexec_fn"] = _preexec(config)
        proc = subprocess.Popen(cmd, **popen_kwargs)
        try:
            captured, _ = proc.communicate(timeout=config.wall_timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
            _kill_group(proc)
            captured, _ = proc.communicate()

        _attach_results(tests, by_nodeid, results_path, timed_out, config, captured)
```
(The `CREATE_*` flags are referenced only inside `if IS_WINDOWS:`, so POSIX never evaluates them; the POSIX branch keeps `preexec_fn=_preexec(config)` byte-for-byte.)

- [ ] **Step 4: Run tests, expect PASS** — `pytest engine/tests/test_runner_platform.py::test_execute_starts_subprocess_on_this_platform -v`

- [ ] **Step 5: Commit**
```
git add engine/codecrack/execution/runner.py engine/tests/test_runner_platform.py
git commit -m "fix(engine): branch sandbox Popen for Windows (creationflags, no preexec_fn)"
```

---

### Task 0.3: `runner.py` — Windows `_kill_group` + POSIX-guard `os.setsid`
**Files:** Modify `engine/codecrack/execution/runner.py`. Test `engine/tests/test_runner_platform.py` (append).
**Interfaces:** Consumes: `IS_WINDOWS` (Task 0.2), `_kill_group(proc)`, `SandboxConfig(wall_timeout=...)`. Produces: timeout kill that works on Windows (`taskkill /F /T`) instead of raising `AttributeError` on `os.getpgid`.

- [ ] **Step 1: Write the failing test** — append to `engine/tests/test_runner_platform.py`:
```python
def test_infinite_loop_killed_within_timeout():
    module = "def loop():\n    while True:\n        pass\n"
    t = _mk("test_loop", "from target import loop\nloop()", expects="raises")
    start = time.monotonic()
    execute_tests(
        [t], module_source=module, module="target", config=SandboxConfig(wall_timeout=3)
    )
    elapsed = time.monotonic() - start
    assert elapsed < 30, f"executor hung for {elapsed:.1f}s"
    assert t.outcome == "error"
    assert "timed out" in t.detail
```

- [ ] **Step 2: Run it, expect FAIL** — `pytest engine/tests/test_runner_platform.py::test_infinite_loop_killed_within_timeout -v`. Expect `AttributeError: module 'os' has no attribute 'getpgid'` (Windows has no `os.getpgid`/`os.killpg`), propagated out of `_kill_group`.

- [ ] **Step 3: Implement** — in `runner.py`, replace `_kill_group` entirely with:
```python
def _kill_group(proc: subprocess.Popen) -> None:
    """Kill the child's whole process tree so infinite loops can't linger."""
    if IS_WINDOWS:
        # No os.killpg on Windows; taskkill /T terminates the whole tree by PID.
        try:
            subprocess.run(
                ["taskkill", "/F", "/T", "/PID", str(proc.pid)],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                check=False,
            )
        except OSError:
            try:
                proc.kill()
            except OSError:
                pass
        return
    try:
        os.killpg(os.getpgid(proc.pid), signal.SIGKILL)
    except (ProcessLookupError, PermissionError):
        try:
            proc.kill()
        except ProcessLookupError:
            pass
```
Then guard `os.setsid` inside `_preexec.apply` — replace the two lines:
```python
        # New session/process group so a timeout can kill the whole tree.
        os.setsid()
```
with:
```python
        # New session/process group so a timeout can kill the whole tree.
        if hasattr(os, "setsid"):
            os.setsid()
```

- [ ] **Step 4: Run tests, expect PASS** — `pytest engine/tests/test_runner_platform.py::test_infinite_loop_killed_within_timeout -v`

- [ ] **Step 5: Commit**
```
git add engine/codecrack/execution/runner.py engine/tests/test_runner_platform.py
git commit -m "fix(engine): kill sandbox tree via taskkill on Windows; guard os.setsid"
```

---

### Task 0.4: `runner.py` — Windows branch for `_scrubbed_env`
**Files:** Modify `engine/codecrack/execution/runner.py`. Test `engine/tests/test_runner_platform.py` (append).
**Interfaces:** Consumes: `IS_WINDOWS` (Task 0.2), private `_scrubbed_env(results_path: str) -> dict[str, str]`. Produces: Windows env allowlist keeping `PATH, SYSTEMROOT, SYSTEMDRIVE, TEMP, TMP, PATHEXT, NUMBER_OF_PROCESSORS, LANG, LC_ALL` with `PATH` fallback `%SYSTEMROOT%\System32`.

- [ ] **Step 1: Write the failing test** — append to `engine/tests/test_runner_platform.py`:
```python
from codecrack.execution.runner import IS_WINDOWS, _scrubbed_env  # noqa: E402


def test_scrubbed_env_always_sets_determinism_and_results(tmp_path):
    env = _scrubbed_env(str(tmp_path / "r.json"))
    assert env["PYTHONHASHSEED"] == "0"
    assert env["PYTHONDONTWRITEBYTECODE"] == "1"
    assert env["CODECRACK_RESULTS"].endswith("r.json")
    assert env.get("PATH")


@pytest.mark.skipif(not IS_WINDOWS, reason="Windows-only env allowlist")
def test_scrubbed_env_windows_allowlist(monkeypatch, tmp_path):
    monkeypatch.setenv("PATHEXT", ".COM;.EXE;.BAT")
    monkeypatch.setenv("SYSTEMDRIVE", "C:")
    monkeypatch.setenv("TEMP", str(tmp_path))
    monkeypatch.setenv("NUMBER_OF_PROCESSORS", "8")
    env = _scrubbed_env(str(tmp_path / "r.json"))
    assert env["PATHEXT"] == ".COM;.EXE;.BAT"
    assert env["SYSTEMDRIVE"] == "C:"
    assert env["TEMP"] == str(tmp_path)
    assert env["NUMBER_OF_PROCESSORS"] == "8"
```

- [ ] **Step 2: Run it, expect FAIL** — `pytest engine/tests/test_runner_platform.py::test_scrubbed_env_windows_allowlist -v`. Expect `KeyError: 'PATHEXT'` (the current Unix-only allowlist drops `PATHEXT/SYSTEMDRIVE/TEMP/NUMBER_OF_PROCESSORS`).

- [ ] **Step 3: Implement** — in `runner.py`, replace `_scrubbed_env` entirely with:
```python
def _scrubbed_env(results_path: str) -> dict[str, str]:
    """A minimal environment: enough to import pytest/python, nothing else.

    We deliberately drop the caller's environment (no secrets, no network
    config) but preserve what a stdlib interpreter + pytest need to start. The
    allowlist is platform-shaped: POSIX and Windows require different keys.
    """
    src = os.environ
    env: dict[str, str] = {}
    if IS_WINDOWS:
        for key in (
            "PATH",
            "SYSTEMROOT",
            "SYSTEMDRIVE",
            "TEMP",
            "TMP",
            "PATHEXT",
            "NUMBER_OF_PROCESSORS",
            "LANG",
            "LC_ALL",
        ):
            if key in src:
                env[key] = src[key]
        env.setdefault(
            "PATH", os.path.join(src.get("SYSTEMROOT", r"C:\Windows"), "System32")
        )
    else:
        for key in ("PATH", "HOME", "TMPDIR", "LANG", "LC_ALL", "SYSTEMROOT"):
            if key in src:
                env[key] = src[key]
        env.setdefault("PATH", "/usr/bin:/bin")
    env["PYTHONHASHSEED"] = "0"  # determinism
    env["PYTHONDONTWRITEBYTECODE"] = "1"
    env["CODECRACK_RESULTS"] = results_path
    return env
```
(The `else` branch is the original POSIX allowlist unchanged.)

- [ ] **Step 4: Run tests, expect PASS** — `pytest engine/tests/test_runner_platform.py -k scrubbed_env -v`

- [ ] **Step 5: Commit**
```
git add engine/codecrack/execution/runner.py engine/tests/test_runner_platform.py
git commit -m "fix(engine): Windows env allowlist for the sandbox subprocess"
```

---

### Task 0.5: `runner.py` — tempdir cleanup resilience + `proc.wait()`
**Files:** Modify `engine/codecrack/execution/runner.py`. Test `engine/tests/test_runner_platform.py` (append).
**Interfaces:** Consumes: `execute_tests(...)`. Produces: no leaked `codecrack_exec_*` scratch dirs; child handle reaped before cleanup (Windows file-lock safe). `ignore_cleanup_errors` requires Python ≥ 3.10 (repo `requires-python = ">=3.10"`).

- [ ] **Step 1: Write the failing test** — append to `engine/tests/test_runner_platform.py`:
```python
import glob  # noqa: E402
import os  # noqa: E402
import tempfile  # noqa: E402


def test_no_scratch_dirs_leak_after_run():
    t = _mk("test_ok", "assert True", expects="assertion")
    execute_tests([t], module_source="", module="target")
    leftovers = glob.glob(os.path.join(tempfile.gettempdir(), "codecrack_exec_*"))
    assert leftovers == [], f"leaked scratch dirs: {leftovers}"
```

- [ ] **Step 2: Run it, expect FAIL** — `pytest engine/tests/test_runner_platform.py::test_no_scratch_dirs_leak_after_run -v`. On Windows the still-open child handle makes `TemporaryDirectory.__exit__` raise `PermissionError` (or leave the dir), so the run errors / leaves a `codecrack_exec_*` directory.

- [ ] **Step 3: Implement** — in `runner.py`: (a) change the tempdir line:
```python
    with tempfile.TemporaryDirectory(
        prefix="codecrack_exec_", ignore_cleanup_errors=True
    ) as scratch:
```
(b) reap the child before the `with` block exits — replace the `try/except` around `proc.communicate` (from Task 0.2) with the same block plus a `finally`:
```python
        try:
            captured, _ = proc.communicate(timeout=config.wall_timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
            _kill_group(proc)
            captured, _ = proc.communicate()
        finally:
            proc.wait()
```

- [ ] **Step 4: Run tests, expect PASS** — `pytest engine/tests/test_runner_platform.py -v`

- [ ] **Step 5: Commit**
```
git add engine/codecrack/execution/runner.py engine/tests/test_runner_platform.py
git commit -m "fix(engine): reap child + tolerate scratch-dir cleanup errors on Windows"
```

---

### Task 0.6: `cli.py` — UTF-8 stdout reconfigure (guarded)
**Files:** Modify `engine/codecrack/cli.py`. Test `engine/tests/test_cli_encoding.py` (create).
**Interfaces:** Consumes: `main(argv)`. Produces: `_configure_stdout()` that reconfigures stdout to `utf-8`/`backslashreplace` when supported, so the em-dash in `render.py` (`— BUG REPRODUCED`) can't crash under OEM/CJK consoles.

- [ ] **Step 1: Write the failing test** — create `engine/tests/test_cli_encoding.py`:
```python
"""cli._configure_stdout must switch stdout to UTF-8 when possible, else no-op."""

from __future__ import annotations

import sys

from codecrack import cli


class _FakeStdout:
    def __init__(self) -> None:
        self.kwargs: dict | None = None

    def reconfigure(self, **kwargs) -> None:
        self.kwargs = kwargs


def test_configure_stdout_reconfigures_to_utf8(monkeypatch):
    fake = _FakeStdout()
    monkeypatch.setattr(sys, "stdout", fake)
    cli._configure_stdout()
    assert fake.kwargs == {"encoding": "utf-8", "errors": "backslashreplace"}


def test_configure_stdout_is_noop_without_reconfigure(monkeypatch):
    class _Bare:
        pass

    monkeypatch.setattr(sys, "stdout", _Bare())
    cli._configure_stdout()  # must not raise
```

- [ ] **Step 2: Run it, expect FAIL** — `pytest engine/tests/test_cli_encoding.py -v`. Expect `AttributeError: module 'codecrack.cli' has no attribute '_configure_stdout'`.

- [ ] **Step 3: Implement** — in `engine/codecrack/cli.py`, add after the imports:
```python
def _configure_stdout() -> None:
    """Force UTF-8 output so non-ASCII report glyphs survive OEM/CJK consoles."""
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="backslashreplace")
```
and make it the first statement in `main`:
```python
def main(argv: list[str] | None = None) -> int:
    _configure_stdout()
    parser = argparse.ArgumentParser(
```

- [ ] **Step 4: Run tests, expect PASS** — `pytest engine/tests/test_cli_encoding.py -v`

- [ ] **Step 5: Commit**
```
git add engine/codecrack/cli.py engine/tests/test_cli_encoding.py
git commit -m "fix(engine): reconfigure CLI stdout to UTF-8 (guarded) for Windows consoles"
```

---

### Task 0.1: Golden JSON contract fixture + regeneration test
**Files:** Create `engine/tests/fixtures/multi_bug.py`, `engine/tests/golden/normalize.py`, `engine/tests/golden/analysis_result.json`, `engine/tests/test_golden.py`. Modify `engine/pyproject.toml`.
**Interfaces:** Consumes: CLI `python -m codecrack analyze <file> --json` (cwd = `engine/`), `render_json` shape `{findings, tests, summary}`. Produces: committed golden `engine/tests/golden/analysis_result.json` and `normalize.py` (zeros the volatile `duration` field) reused by the CI drift gate (Task 0.9).

> Runs after 0.2–0.6: it exercises the full execute stage on Windows. `detail` uses **relative** test paths (`test_002_F003.py:12`), so `duration` is the only volatile field. Pin pytest to a single major so the traceback text stays byte-stable across the CI matrix.

- [ ] **Step 1: Write the failing test** — create `engine/tests/test_golden.py`:
```python
"""The committed JSON golden must match a fresh CLI regeneration (duration masked)."""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ENGINE = Path(__file__).resolve().parents[1]  # engine/
FIXTURE = ENGINE / "tests" / "fixtures" / "multi_bug.py"
GOLDEN = ENGINE / "tests" / "golden" / "analysis_result.json"


def _mask(payload: dict) -> dict:
    payload = json.loads(json.dumps(payload))
    for t in payload["tests"]:
        t["duration"] = 0.0
    return payload


def test_golden_matches_regenerated_output():
    proc = subprocess.run(
        [sys.executable, "-m", "codecrack", "analyze", str(FIXTURE), "--json"],
        cwd=str(ENGINE),
        capture_output=True,
        text=True,
    )
    assert proc.returncode == 0, proc.stderr
    fresh = json.loads(proc.stdout)
    golden = json.loads(GOLDEN.read_text(encoding="utf-8"))
    assert _mask(fresh) == _mask(golden)
    # The three seeded bugs must be proven.
    assert fresh["summary"]["reproduced"] == 3
```

- [ ] **Step 2: Run it, expect FAIL** — `pytest engine/tests/test_golden.py -v`. Expect `FileNotFoundError` for `engine/tests/golden/analysis_result.json` (golden + fixture do not exist yet).

- [ ] **Step 3: Implement** — create the fixture `engine/tests/fixtures/multi_bug.py`:
```python
def divide(a, b):
    return a / b


def first(xs):
    return xs[0]


def accumulate(item, acc=[]):
    acc += [item]
    return acc
```
create the normalizer `engine/tests/golden/normalize.py`:
```python
"""Zero volatile fields (wall-clock duration) so the golden JSON is byte-stable.

Used both to produce the committed golden and by the CI drift gate, so the two
sides normalize identically.
"""

from __future__ import annotations

import json
import sys


def normalize(text: str) -> str:
    data = json.loads(text)
    for test in data.get("tests", []):
        test["duration"] = 0.0
    return json.dumps(data, indent=2)


if __name__ == "__main__":
    sys.stdout.write(normalize(sys.stdin.read()))
```
pin pytest in `engine/pyproject.toml` — change the `dev` extra:
```toml
[project.optional-dependencies]
dev = ["pytest>=8,<9"]
```
reinstall so the pin takes effect, then generate the golden (from the repo root, venv active):
```powershell
pip install -e ".\engine[dev]"
New-Item -ItemType Directory -Force engine\tests\golden | Out-Null
python -m codecrack analyze engine\tests\fixtures\multi_bug.py --json `
  | python engine\tests\golden\normalize.py `
  | Out-File -Encoding utf8 engine\tests\golden\analysis_result.json
```
The committed file (source of truth: the command above) has `summary` = `{"findings":3,"tests":3,"executed":3,"reproduced":3,"by_outcome":{"passed":2,"failed":1,"error":0,"skipped":0}}` and every test's `"duration": 0.0`. Sanity-check it before committing:
```powershell
python -c "import json;d=json.load(open('engine/tests/golden/analysis_result.json'));print(d['summary'])"
```

- [ ] **Step 4: Run tests, expect PASS** — `pytest engine/tests/test_golden.py -v`

- [ ] **Step 5: Commit**
```
git add engine/tests/fixtures/multi_bug.py engine/tests/golden/normalize.py engine/tests/golden/analysis_result.json engine/tests/test_golden.py engine/pyproject.toml
git commit -m "test(engine): add multi_bug golden JSON contract + regeneration test"
```

---

### Task 0.7: Determinism test
**Files:** Create `engine/tests/test_determinism.py`.
**Interfaces:** Consumes: `crack(source, *, module, execute) -> Result(findings, tests)`, `render_json(findings, tests) -> str`, the `multi_bug.py` fixture (Task 0.1). Produces: proof that two `crack()` runs render byte-identical JSON after masking `duration`.

- [ ] **Step 1: Write the failing test** — create `engine/tests/test_determinism.py`:
```python
"""crack() + render_json must be reproducible run-to-run (duration masked)."""

from __future__ import annotations

import json
from pathlib import Path

from codecrack.pipeline import crack
from codecrack.report import render_json

FIXTURE = Path(__file__).parent / "fixtures" / "multi_bug.py"


def _render(source: str) -> dict:
    result = crack(source, module="target", execute=True)
    payload = json.loads(render_json(result.findings, result.tests))
    for test in payload["tests"]:
        test["duration"] = 0.0
    return payload


def test_render_json_is_stable_across_runs():
    source = FIXTURE.read_text(encoding="utf-8")
    first = _render(source)
    second = _render(source)
    assert first == second
    assert first["summary"]["reproduced"] == 3
```

- [ ] **Step 2: Run it, expect FAIL** — `pytest engine/tests/test_determinism.py -v`. Before Task 0.1 the fixture is absent → `FileNotFoundError`. (If run after 0.1 it already passes; if so, add it as a regression guard and skip to Step 5.)

- [ ] **Step 3: Implement** — no engine code change; the determinism is provided by `PYTHONHASHSEED=0` in `_scrubbed_env` plus the `duration` mask. Ensure the fixture from Task 0.1 exists.

- [ ] **Step 4: Run tests, expect PASS** — `pytest engine/tests/test_determinism.py -v`

- [ ] **Step 5: Commit**
```
git add engine/tests/test_determinism.py
git commit -m "test(engine): assert crack()+render_json determinism (duration masked)"
```

---

### Task 0.8: `test_executor.py` — mark rlimit-only assertion `skipif(win32)`
**Files:** Modify `engine/tests/test_executor.py`.
**Interfaces:** Consumes: `execute_tests(...)`, `SandboxConfig(memory_bytes=..., wall_timeout=...)`. Produces: a POSIX-only memory-cap regression test skipped on Windows (no `RLIMIT_AS`; Windows relies on wall-timeout + `taskkill`).

- [ ] **Step 1: Write the failing test** — append to `engine/tests/test_executor.py`:
```python
import sys

import pytest


@pytest.mark.skipif(
    sys.platform == "win32",
    reason="RLIMIT_AS memory cap is POSIX-only; Windows uses wall-timeout + taskkill",
)
def test_memory_cap_enforced_by_rlimit():
    # 4 GiB allocation under a 256 MiB address-space cap must be stopped.
    t = _test(
        "test_hog",
        "x = bytearray(4 * 1024 * 1024 * 1024)\nassert x",
        expects="assertion",
    )
    execute_tests(
        [t],
        module_source="",
        module="target",
        config=SandboxConfig(memory_bytes=256 * 1024 * 1024, wall_timeout=15),
    )
    assert t.outcome in ("failed", "error")
    assert t.duration >= 0.0
```

- [ ] **Step 2: Run it, expect FAIL** — `pytest engine/tests/test_executor.py::test_memory_cap_enforced_by_rlimit -v`. On Windows expect **SKIPPED** (`1 skipped`) — the desired steady state (no `RLIMIT_AS` on Windows). On a POSIX box it should PASS (the cap triggers `MemoryError`).

- [ ] **Step 3: Implement** — none beyond the marker in Step 1; this task *is* the `skipif` annotation. The whole file must still import cleanly (`import sys` / `import pytest` at the top with the other imports).

- [ ] **Step 4: Run tests, expect PASS/SKIP** — `pytest engine/tests/test_executor.py -v` (expect `test_memory_cap_enforced_by_rlimit` skipped on Windows, all others passing).

- [ ] **Step 5: Commit**
```
git add engine/tests/test_executor.py
git commit -m "test(engine): guard RLIMIT_AS memory-cap assertion with skipif(win32)"
```

---

### Task 0.9: CI — Windows + Python 3.13 engine matrix + golden-drift gate
**Files:** Modify `.github/workflows/ci.yml`.
**Interfaces:** Consumes: `python -m codecrack analyze ... --json`, `engine/tests/golden/normalize.py` (Task 0.1). Produces: `engine-tests` job running on `{ubuntu, windows, macos} × {3.10, 3.12, 3.13}` plus a `git diff --exit-code` golden-drift step. Leave the existing `macos-build` job untouched.

- [ ] **Step 1: Write the failing check** — validate the intended matrix + drift step locally by dry-running the drift command from the repo root (venv active):
```powershell
python -m codecrack analyze engine/tests/fixtures/multi_bug.py --json `
  | python engine/tests/golden/normalize.py `
  | Out-File -Encoding utf8 engine/tests/golden/analysis_result.json
git diff --exit-code -- engine/tests/golden/analysis_result.json
```

- [ ] **Step 2: Run it, expect FAIL** — before wiring CI, run `git grep -n "windows-latest" .github/workflows/ci.yml`. Expect **no match** (the engine matrix is ubuntu-only, python 3.10/3.12) — the gap this task closes.

- [ ] **Step 3: Implement** — replace the entire `engine-tests:` job in `.github/workflows/ci.yml` with:
```yaml
  engine-tests:
    name: Engine (pytest)
    runs-on: ${{ matrix.os }}
    strategy:
      fail-fast: false
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]
        python-version: ["3.10", "3.12", "3.13"]
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-python@v5
        with:
          python-version: ${{ matrix.python-version }}
      - name: Install engine (with dev deps)
        run: python -m pip install --upgrade pip && pip install -e "./engine[dev]"
      - name: Run test suite
        run: pytest engine/tests -q
      - name: Golden JSON drift check
        shell: bash
        run: |
          python -m codecrack analyze engine/tests/fixtures/multi_bug.py --json \
            | python engine/tests/golden/normalize.py > engine/tests/golden/analysis_result.json
          git diff --exit-code -- engine/tests/golden/analysis_result.json
```
(`shell: bash` is used so the pipe is byte-exact on every OS — bash ships on the Windows runner. The `macos-build` job below is unchanged.)

- [ ] **Step 4: Verify** — `python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml')); print('yaml ok')"` (or paste into the GitHub Actions editor). Push the branch and confirm all 9 `engine-tests` matrix cells are green, including `windows-latest`.

- [ ] **Step 5: Commit**
```
git add .github/workflows/ci.yml
git commit -m "ci: add windows + python 3.13 to engine matrix and a golden-drift gate"
```

---

### Task 0.10: Docs — retract "zero engine changes"; note the Windows sandbox branch
**Files:** Modify `docs/ARCHITECTURE.md`, `docs/core-design-brief.md`.
**Interfaces:** Consumes: nothing. Produces: documentation that the subprocess sandbox rung is platform-branched (POSIX vs Windows), so the engine is **not** zero-change for Windows.

- [ ] **Step 1: Write the failing check** — assert the retraction is absent today:
```powershell
git grep -n "platform-branched" docs/ARCHITECTURE.md docs/core-design-brief.md
```

- [ ] **Step 2: Run it, expect FAIL** — the `git grep` above prints **nothing** (exit code 1): neither doc mentions the Windows sandbox branch yet.

- [ ] **Step 3: Implement** — in `docs/ARCHITECTURE.md`, replace the `Sandboxed execution` bullet:
```markdown
- **Sandboxed execution** — **never run user code in-process.** Isolation ladder:
  subprocess + rlimits → OS sandbox → containers → microVMs → WASM. Every run has
  timeouts + memory caps + no network/host-FS by default.
```
with:
```markdown
- **Sandboxed execution** — **never run user code in-process.** Isolation ladder:
  subprocess + rlimits → OS sandbox → containers → microVMs → WASM. Every run has
  timeouts + memory caps + no network/host-FS by default. The subprocess rung is
  **platform-branched** (`os.name == "nt"`): POSIX uses `preexec_fn` (`os.setsid` +
  `RLIMIT_CPU`/`RLIMIT_AS`) and `os.killpg`; Windows uses
  `CREATE_NEW_PROCESS_GROUP | CREATE_NO_WINDOW` + `taskkill /F /T` and enforces the
  wall-clock timeout only (no `RLIMIT_AS` memory cap). The engine is therefore **not
  zero-change** on Windows — see the Phase 0 port in the Windows design spec.
```
In `docs/core-design-brief.md`, find the bullet `**Never run user/generated code in-process** — always through the sandbox.` and append a sibling line directly under it:
```markdown
- The subprocess sandbox is **platform-branched** (POSIX vs Windows); the Windows
  port (design spec Phase 0) makes the execute stage cross-platform, so the shared
  engine is **not zero-change** — a corrected premise from the original brief.
```

- [ ] **Step 4: Verify** — `git grep -n "platform-branched" docs/ARCHITECTURE.md docs/core-design-brief.md` now prints two matches; `git grep -n "not zero-change\|not.*zero-change" docs/` shows the retraction. Manually read both edited paragraphs for flow.

- [ ] **Step 5: Commit**
```
git add docs/ARCHITECTURE.md docs/core-design-brief.md
git commit -m "docs: retract zero-engine-changes; document Windows sandbox branch"
```

---

**Phase 0 exit check (all green before Phase 1):**
```
pytest engine/tests -q
```
Expect all tests passing on Windows, with `test_memory_cap_enforced_by_rlimit` reported skipped. Key files touched: `engine/codecrack/execution/runner.py`, `engine/codecrack/cli.py`, `engine/pyproject.toml`, `engine/tests/{test_runner_platform,test_cli_encoding,test_golden,test_determinism,test_executor}.py`, `engine/tests/fixtures/multi_bug.py`, `engine/tests/golden/{normalize.py,analysis_result.json}`, `.github/workflows/ci.yml`, `docs/ARCHITECTURE.md`, `docs/core-design-brief.md`.

---

## Phase 1: Solution skeleton + testable core + contract

> Dependency note: the tiny contract types (`EngineOutcome`, `IAppSettings`, `IFileIO`, `IEditorHost`, `EditorThemeSpec`, `StatusBus`) are created in Task 1.5 **before** `EngineLocator`/`Analyzer` because those consume `IAppSettings` and `EngineOutcome`. All names match the canonical contract verbatim. Assumes Phase 0 has already initialized the `windows-port` git branch and the `engine/` Windows fixes; Phase 1 touches only `winapp/`, `CodeCrack.Tests/`, and `.github/workflows/ci.yml`.

---

### Task 1.1: Solution skeleton + project scaffolding
**Files:**
- Create `winapp/CodeCrack.sln`
- Create `winapp/global.json`
- Create `winapp/Directory.Build.props`
- Create `winapp/CodeCrack.App.Core/CodeCrack.App.Core.csproj`
- Create `winapp/CodeCrackApp/CodeCrackApp.csproj`, `winapp/CodeCrackApp/App.xaml`, `winapp/CodeCrackApp/App.xaml.cs`, `winapp/CodeCrackApp/MainWindow.xaml`, `winapp/CodeCrackApp/MainWindow.xaml.cs`
- Create `winapp/CodeCrack.Tests/CodeCrack.Tests.csproj`, `winapp/CodeCrack.Tests/SolutionSmokeTests.cs`
- Modify `.gitignore` (repo root)

**Interfaces:** Consumes: nothing. Produces: three build targets (`CodeCrack.App.Core` net8.0, `CodeCrackApp` net8.0-windows/UseWPF, `CodeCrack.Tests` net8.0/xUnit), the pinned SDK (`global.json`), and the global compiler policy (`Nullable=enable`, `TreatWarningsAsErrors=true`, `ImplicitUsings=enable`) every later task relies on.

- [ ] **Step 1: Write the failing test** — create `winapp/CodeCrack.Tests/SolutionSmokeTests.cs`. This proves the Core project is referenced and the toolchain compiles under the strict props.
```csharp
using Xunit;

namespace CodeCrack.Tests;

public class SolutionSmokeTests
{
    [Fact]
    public void Core_assembly_is_referenced_and_loads()
    {
        // Any public type from Core proves the ProjectReference + strict build works.
        var asm = typeof(CodeCrack.App.Core.CoreMarker).Assembly;
        Assert.Equal("CodeCrack.App.Core", asm.GetName().Name);
    }
}
```
  Also create the marker type `winapp/CodeCrack.App.Core/CoreMarker.cs`:
```csharp
namespace CodeCrack.App.Core;

/// <summary>Empty anchor type so tests can assert the Core assembly resolves.</summary>
public static class CoreMarker;
```

- [ ] **Step 2: Run it, expect FAIL** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj
```
  Expected failure: build error `CS0234`/`MSB1009` — the projects/solution do not exist yet, so restore/build fails before any test runs.

- [ ] **Step 3: Implement** — create the files.

  `winapp/global.json`:
```json
{
  "sdk": {
    "version": "8.0.400",
    "rollForward": "latestFeature"
  }
}
```

  `winapp/Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <LangVersion>12</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

  `winapp/CodeCrack.App.Core/CodeCrack.App.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
```

  `winapp/CodeCrackApp/CodeCrackApp.csproj` (no `RuntimeIdentifier` here — `-r win-x64` is passed at publish in Phase 5; the WPF SDK auto-globs `App.xaml` as `ApplicationDefinition` and `MainWindow.xaml` as `Page`):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CodeCrack.App.Core\CodeCrack.App.Core.csproj" />
  </ItemGroup>
</Project>
```

  `winapp/CodeCrackApp/App.xaml`:
```xml
<Application x:Class="CodeCrackApp.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources />
</Application>
```

  `winapp/CodeCrackApp/App.xaml.cs`:
```csharp
using System.Windows;

namespace CodeCrackApp;

public partial class App : Application
{
}
```

  `winapp/CodeCrackApp/MainWindow.xaml`:
```xml
<Window x:Class="CodeCrackApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="CodeCrack" Height="600" Width="900">
    <Grid>
        <TextBlock Text="Open a file or folder to begin"
                   HorizontalAlignment="Center" VerticalAlignment="Center" FontSize="14" />
    </Grid>
</Window>
```

  `winapp/CodeCrackApp/MainWindow.xaml.cs`:
```csharp
using System.Windows;

namespace CodeCrackApp;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}
```

  `winapp/CodeCrack.Tests/CodeCrack.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="coverlet.collector" Version="6.0.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CodeCrack.App.Core\CodeCrack.App.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

  Append to repo-root `.gitignore` (keep existing `build/`, `dist/`):
```
bin/
obj/
*.user
*.suo
```

  Build the solution and add the projects (run from `winapp/`):
```
dotnet new sln -n CodeCrack
dotnet sln add CodeCrack.App.Core/CodeCrack.App.Core.csproj CodeCrackApp/CodeCrackApp.csproj CodeCrack.Tests/CodeCrack.Tests.csproj
```

- [ ] **Step 4: Run tests, expect PASS** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj
```
  Expect `Passed! - Failed: 0, Passed: 1`. **Manual verification** (Windows only, WPF shell has no headless test): `dotnet run --project CodeCrackApp/CodeCrackApp.csproj` opens a 900×600 window reading "Open a file or folder to begin". Close it.

- [ ] **Step 5: Commit**
```
git add winapp/ .gitignore
git commit -m "chore(winapp): scaffold solution, three projects, global.json + strict Directory.Build.props"
```

---

### Task 1.2: Engine DTOs + `EngineJson.Options`
**Files:**
- Create `winapp/CodeCrack.App.Core/Engine/Models.cs`
- Create `winapp/CodeCrack.App.Core/Engine/EngineJson.cs`
- Test `winapp/CodeCrack.Tests/ModelsComputedTests.cs`

**Interfaces:** Consumes: nothing. Produces: `CodeCrack.App.Core.Engine.{Finding, GeneratedTest, OutcomeCounts, Summary, AnalysisResult}` records with computed `Finding.Line`, `GeneratedTest.NeedsInput`; and `EngineJson.Options` (`JsonSerializerOptions`, snake_case + `Disallow` unmapped). `Finding` tolerates the engine's extra `evidence` object (macOS parity); the other DTOs fail loudly on shape drift.

- [ ] **Step 1: Write the failing test** — `winapp/CodeCrack.Tests/ModelsComputedTests.cs`:
```csharp
using CodeCrack.App.Core.Engine;
using Xunit;

namespace CodeCrack.Tests;

public class ModelsComputedTests
{
    [Fact]
    public void Finding_Line_is_first_location_or_1()
    {
        Assert.Equal(7, new Finding("F1", "k", "t", new[] { 7, 3 }, "r", "high").Line);
        Assert.Equal(1, new Finding("F1", "k", "t", System.Array.Empty<int>(), "r", "high").Line);
    }

    [Fact]
    public void GeneratedTest_NeedsInput_on_skipped_or_regression()
    {
        var skipped = new GeneratedTest("F1", "t", "s", "raises", "skipped", "", "", 0.0, false);
        var regression = new GeneratedTest("F1", "t", "s", "regression", null, "", "", 0.0, false);
        var proven = new GeneratedTest("F1", "t", "s", "raises", "passed", "", "", 0.01, true);

        Assert.True(skipped.NeedsInput);
        Assert.True(regression.NeedsInput);
        Assert.False(proven.NeedsInput);
    }

    [Fact]
    public void EngineJson_Options_are_snake_case_and_strict()
    {
        Assert.Same(System.Text.Json.JsonNamingPolicy.SnakeCaseLower, EngineJson.Options.PropertyNamingPolicy);
        Assert.Equal(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
            EngineJson.Options.UnmappedMemberHandling);
    }
}
```

- [ ] **Step 2: Run it, expect FAIL** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj --filter FullyQualifiedName~ModelsComputedTests
```
  Expected failure: `CS0246: The type or namespace name 'Finding' could not be found` (Models/EngineJson not created).

- [ ] **Step 3: Implement** — `winapp/CodeCrack.App.Core/Engine/Models.cs`:
```csharp
using System.Text.Json.Serialization;

namespace CodeCrack.App.Core.Engine;

/// The engine also emits an `evidence` object on each finding; it is intentionally
/// ignored (matches the macOS app). `Skip` overrides the options-level `Disallow`
/// for this type only, so extra keys on findings do not throw.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
public sealed record Finding(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string Target,
    [property: JsonRequired] int[] Location,
    [property: JsonRequired] string Rationale,
    [property: JsonRequired] string Severity)
{
    [JsonIgnore] public int Line => Location.Length > 0 ? Location[0] : 1;
}

public sealed record GeneratedTest(
    [property: JsonRequired] string FindingId,
    [property: JsonRequired] string TestName,
    [property: JsonRequired] string Source,
    [property: JsonRequired] string Expects,
    string? Outcome,
    [property: JsonRequired] string Detail,
    [property: JsonRequired] string Stdout,
    [property: JsonRequired] double Duration,
    [property: JsonRequired] bool Reproduced)
{
    [JsonIgnore] public bool NeedsInput => Outcome == "skipped" || Expects == "regression";
}

public sealed record OutcomeCounts(
    [property: JsonRequired] int Passed,
    [property: JsonRequired] int Failed,
    [property: JsonRequired] int Error,
    [property: JsonRequired] int Skipped);

public sealed record Summary(
    [property: JsonRequired] int Findings,
    [property: JsonRequired] int Tests,
    [property: JsonRequired] int Executed,
    [property: JsonRequired] int Reproduced,
    [property: JsonRequired] OutcomeCounts ByOutcome);

public sealed record AnalysisResult(
    [property: JsonRequired] List<Finding> Findings,
    [property: JsonRequired] List<GeneratedTest> Tests,
    [property: JsonRequired] Summary Summary);
```
  `winapp/CodeCrack.App.Core/Engine/EngineJson.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeCrack.App.Core.Engine;

/// Central, shared serializer options for the engine JSON contract.
/// snake_case names (finding_id, test_name, by_outcome) map to PascalCase members;
/// unmapped members are rejected (a shape change fails loudly) except where a type
/// opts out via [JsonUnmappedMemberHandling(Skip)].
public static class EngineJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };
}
```

- [ ] **Step 4: Run tests, expect PASS** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj --filter FullyQualifiedName~ModelsComputedTests
```
  Expect `Failed: 0, Passed: 3`.

- [ ] **Step 5: Commit**
```
git add winapp/CodeCrack.App.Core/Engine/Models.cs winapp/CodeCrack.App.Core/Engine/EngineJson.cs winapp/CodeCrack.Tests/ModelsComputedTests.cs
git commit -m "feat(core): engine DTOs + central snake_case/strict JSON options"
```

---

### Task 1.3: Strict contract test against a golden JSON fixture
**Files:**
- Modify `winapp/CodeCrack.Tests/CodeCrack.Tests.csproj` — link the engine-generated golden into test output (single source of truth)
- Test `winapp/CodeCrack.Tests/ModelsContractTests.cs`

**Interfaces:** Consumes: `AnalysisResult`, `EngineJson.Options` (Task 1.2). Produces: proof the DTOs deserialize a real multi-bug engine payload strictly (unknown members on tests rejected, missing required members rejected, `evidence` on findings tolerated, `reproduced` consumed authoritatively).

> **Single source of truth (audit blocker #4):** Do NOT hand-maintain a second golden. The contract fixture MUST be the byte-identical engine-generated `engine/tests/golden/analysis_result.json` from Task 0.1, linked into the test output via csproj so this consumer test decodes the *current* engine shape — a stale hand-authored copy would give a false green when the engine's JSON shape changes (Task 0.9's drift gate only catches the engine side). Add to `CodeCrack.Tests.csproj`:
> ```xml
> <ItemGroup>
>   <Content Include="..\..\engine\tests\golden\analysis_result.json" Link="Fixtures\analysis_result.json" CopyToOutputDirectory="PreserveNewest" />
> </ItemGroup>
> ```
> The JSON shown below is the *expected shape* (illustrative). Keep the structural assertions; if a literal (e.g. a `detail`/`source` string, or the `F001`/`F002` id order) differs from the generated file, align the expectation to the generated golden — never edit the golden to match the test. Task 0.1's `multi_bug.py` is authored so `divide` (zero-division) sorts to `F001` and `first` (index-error) to `F002`, matching the asserts below.

- [ ] **Step 1: Write the failing test** — the fixture is the linked engine golden (see the note above), not a hand-authored file. Its expected shape (multi-bug, snake_case, `evidence` present, one non-executed regression test with `outcome: null`) is:
```json
{
  "findings": [
    {
      "id": "F001",
      "kind": "zero-division",
      "target": "divide",
      "location": [3, 12],
      "rationale": "Division by a parameter that can be zero.",
      "severity": "high",
      "evidence": { "call": "a / b", "param": "b" }
    },
    {
      "id": "F002",
      "kind": "index-error",
      "target": "first",
      "location": [7, 4],
      "rationale": "Indexing [0] on a possibly-empty list.",
      "severity": "medium",
      "evidence": {}
    }
  ],
  "tests": [
    {
      "finding_id": "F001",
      "test_name": "test_divide_raises_zero_division",
      "source": "def test_divide_raises_zero_division():\n    import pytest\n    ...",
      "expects": "raises",
      "outcome": "passed",
      "detail": "",
      "stdout": "",
      "duration": 0.0123,
      "reproduced": true
    },
    {
      "finding_id": "F002",
      "test_name": "test_first_regression",
      "source": "def test_first_regression():\n    ...",
      "expects": "regression",
      "outcome": null,
      "detail": "needs input",
      "stdout": "",
      "duration": 0.0,
      "reproduced": false
    }
  ],
  "summary": {
    "findings": 2,
    "tests": 2,
    "executed": 1,
    "reproduced": 1,
    "by_outcome": { "passed": 1, "failed": 0, "error": 0, "skipped": 0 }
  }
}
```
  Then `winapp/CodeCrack.Tests/ModelsContractTests.cs`:
```csharp
using System.Text.Json;
using CodeCrack.App.Core.Engine;
using Xunit;

namespace CodeCrack.Tests;

public class ModelsContractTests
{
    private static string GoldenPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "analysis_result.json");

    [Fact]
    public void Deserializes_golden_strictly_and_tolerates_evidence()
    {
        var json = File.ReadAllText(GoldenPath);
        var result = JsonSerializer.Deserialize<AnalysisResult>(json, EngineJson.Options);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Findings.Count);
        Assert.Equal(2, result.Tests.Count);

        var f1 = result.Findings[0];
        Assert.Equal("F001", f1.Id);
        Assert.Equal("zero-division", f1.Kind);
        Assert.Equal(3, f1.Line); // Location[0]

        var t1 = result.Tests[0];
        Assert.True(t1.Reproduced);  // authoritative flag, never re-derived
        Assert.False(t1.NeedsInput);

        var t2 = result.Tests[1];
        Assert.Null(t2.Outcome);
        Assert.True(t2.NeedsInput);  // expects == "regression"

        Assert.Equal(1, result.Summary.Reproduced);
        Assert.Equal(1, result.Summary.ByOutcome.Passed);
        Assert.Equal(0, result.Summary.ByOutcome.Skipped);
    }

    [Fact]
    public void Unknown_member_on_a_test_is_rejected()
    {
        const string json = """
        {"findings":[],"tests":[{"finding_id":"F1","test_name":"t","source":"s","expects":"raises","outcome":null,"detail":"","stdout":"","duration":0.0,"reproduced":false,"bogus":1}],"summary":{"findings":0,"tests":1,"executed":0,"reproduced":0,"by_outcome":{"passed":0,"failed":0,"error":0,"skipped":0}}}
        """;
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AnalysisResult>(json, EngineJson.Options));
    }

    [Fact]
    public void Missing_required_member_is_rejected()
    {
        // Finding missing "severity"
        const string json = """
        {"findings":[{"id":"F1","kind":"k","target":"t","location":[1,1],"rationale":"r"}],"tests":[],"summary":{"findings":1,"tests":0,"executed":0,"reproduced":0,"by_outcome":{"passed":0,"failed":0,"error":0,"skipped":0}}}
        """;
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AnalysisResult>(json, EngineJson.Options));
    }
}
```

- [ ] **Step 2: Run it, expect FAIL** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj --filter FullyQualifiedName~ModelsContractTests
```
  Expected failure: `FileNotFoundException` for `Fixtures/analysis_result.json` if the fixture is not yet copied, or an assertion failure — run after saving the fixture so the strictness asserts are what pass.

- [ ] **Step 3: Implement** — no production code changes; the DTOs from Task 1.2 already satisfy this. (If the first run failed on `FileNotFoundException`, confirm the csproj `<Content Link>` above resolves — Task 0.1's `engine/tests/golden/analysis_result.json` must exist and copy to the test output's `Fixtures/`.)

- [ ] **Step 4: Run tests, expect PASS** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj --filter FullyQualifiedName~ModelsContractTests
```
  Expect `Failed: 0, Passed: 3`.

- [ ] **Step 5: Commit**
```
git add winapp/CodeCrack.Tests/CodeCrack.Tests.csproj winapp/CodeCrack.Tests/ModelsContractTests.cs
git commit -m "test(core): strict contract test against the engine-generated golden (single source of truth)"
```

---

### Task 1.4: `AnalyzerError` records + macOS-matching message text
**Files:**
- Create `winapp/CodeCrack.App.Core/Engine/AnalyzerError.cs`
- Test `winapp/CodeCrack.Tests/AnalyzerErrorTests.cs`

**Interfaces:** Consumes: nothing. Produces: `abstract record AnalyzerError { string Message }` + `EngineNotFound(string Path)`, `LaunchFailed(string Detail)`, `EngineFailed(int Code, string Stderr, string EngineDir)`, `DecodeFailed(string Detail, string EngineDir, string RawStdout, string Stderr)` — consumed by `EngineOutcome` (1.5) and `Analyzer` (1.8). Message text mirrors `macapp/Sources/PPIDE/Editor/Analyzer.swift`, with Windows path/`python` wording.

- [ ] **Step 1: Write the failing test** — `winapp/CodeCrack.Tests/AnalyzerErrorTests.cs`:
```csharp
using CodeCrack.App.Core.Engine;
using Xunit;

namespace CodeCrack.Tests;

public class AnalyzerErrorTests
{
    [Fact]
    public void EngineNotFound_mentions_path_env_and_setting()
    {
        AnalyzerError e = new EngineNotFound(@"C:\app\Resources\engine");
        Assert.Contains(@"CodeCrack engine not found (C:\app\Resources\engine).", e.Message);
        Assert.Contains("CODECRACK_ENGINE_DIR", e.Message);
        Assert.Contains("enginePathOverride", e.Message);
    }

    [Fact]
    public void LaunchFailed_prefixes_detail()
    {
        AnalyzerError e = new LaunchFailed("The system cannot find the file specified.");
        Assert.Equal("Failed to launch python: The system cannot find the file specified.", e.Message);
    }

    [Fact]
    public void EngineFailed_includes_code_dir_and_trimmed_stderr()
    {
        AnalyzerError e = new EngineFailed(2, "  boom  \n", @"C:\eng");
        Assert.Contains("Analysis failed (exit 2).", e.Message);
        Assert.Contains(@"Engine: C:\eng", e.Message);
        Assert.Contains("boom", e.Message);
        Assert.DoesNotContain("No error output.", e.Message);
    }

    [Fact]
    public void EngineFailed_empty_stderr_says_no_output()
    {
        AnalyzerError e = new EngineFailed(1, "   \n", "d");
        Assert.Contains("No error output.", e.Message);
    }

    [Fact]
    public void DecodeFailed_reports_empty_stdout_and_stderr()
    {
        AnalyzerError e = new DecodeFailed("unexpected end of data", "d", "", "trace");
        Assert.Contains("Couldn't read the engine's output: unexpected end of data", e.Message);
        Assert.Contains("Engine stdout was empty.", e.Message);
        Assert.Contains("stderr: trace", e.Message);
    }
}
```

- [ ] **Step 2: Run it, expect FAIL** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj --filter FullyQualifiedName~AnalyzerErrorTests
```
  Expected failure: `CS0246: 'EngineNotFound' could not be found`.

- [ ] **Step 3: Implement** — `winapp/CodeCrack.App.Core/Engine/AnalyzerError.cs`:
```csharp
namespace CodeCrack.App.Core.Engine;

/// Failure modes surfaced to the Issues panel instead of crashing.
/// Message text mirrors macapp/Sources/PPIDE/Editor/Analyzer.swift, with the
/// engine-locator guidance adjusted to Windows paths ("engine\") and "python".
public abstract record AnalyzerError
{
    public abstract string Message { get; }

    protected static string Snippet(string text, int limit = 600) =>
        text.Length <= limit ? text : text[..limit] + "\u2026 (truncated)";
}

public sealed record EngineNotFound(string Path) : AnalyzerError
{
    public override string Message =>
        $"CodeCrack engine not found ({Path}).\n"
        + "The bundled engine is missing; reinstall the app, or set "
        + "CODECRACK_ENGINE_DIR (or the enginePathOverride setting) to a checkout's engine\\ directory.";
}

public sealed record LaunchFailed(string Detail) : AnalyzerError
{
    public override string Message => $"Failed to launch python: {Detail}";
}

public sealed record EngineFailed(int Code, string Stderr, string EngineDir) : AnalyzerError
{
    public override string Message
    {
        get
        {
            var trimmed = Stderr.Trim();
            return $"Analysis failed (exit {Code}).\n"
                + $"Engine: {EngineDir}\n"
                + (trimmed.Length == 0 ? "No error output." : Snippet(trimmed));
        }
    }
}

public sealed record DecodeFailed(string Detail, string EngineDir, string RawStdout, string Stderr) : AnalyzerError
{
    public override string Message
    {
        get
        {
            var parts = new List<string>
            {
                $"Couldn't read the engine's output: {Detail}",
                $"Engine: {EngineDir}",
            };
            var outT = RawStdout.Trim();
            parts.Add(outT.Length == 0 ? "Engine stdout was empty." : $"stdout: {Snippet(outT)}");
            var errT = Stderr.Trim();
            if (errT.Length != 0) parts.Add($"stderr: {Snippet(errT)}");
            return string.Join("\n", parts);
        }
    }
}
```

- [ ] **Step 4: Run tests, expect PASS** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj --filter FullyQualifiedName~AnalyzerErrorTests
```
  Expect `Failed: 0, Passed: 5`.

- [ ] **Step 5: Commit**
```
git add winapp/CodeCrack.App.Core/Engine/AnalyzerError.cs winapp/CodeCrack.Tests/AnalyzerErrorTests.cs
git commit -m "feat(core): typed AnalyzerError records with macOS-matching messages"
```

---

### Task 1.5: Contract types — `EngineOutcome`, `IAppSettings`, `IFileIO`, `IEditorHost`, `EditorThemeSpec`, `StatusBus`
**Files:**
- Create `winapp/CodeCrack.App.Core/Engine/EngineOutcome.cs`
- Create `winapp/CodeCrack.App.Core/Settings/IAppSettings.cs`
- Create `winapp/CodeCrack.App.Core/Services/IFileIO.cs`
- Create `winapp/CodeCrack.App.Core/Editor/EditorThemeSpec.cs`
- Create `winapp/CodeCrack.App.Core/Editor/IEditorHost.cs`
- Create `winapp/CodeCrack.App.Core/Services/StatusBus.cs`
- Create `winapp/CodeCrack.Tests/Fakes/FakeAppSettings.cs`
- Create `winapp/CodeCrack.Tests/Fakes/FakeEditorHost.cs`
- Test `winapp/CodeCrack.Tests/ContractsTests.cs`

**Interfaces:** Consumes: `AnalysisResult` (1.2), `AnalyzerError` (1.4). Produces: `EngineOutcome(AnalysisResult?, AnalyzerError?){ IsSuccess }`; interfaces `IAppSettings`, `IFileIO`, `IEditorHost`; record `EditorThemeSpec`; class `StatusBus`; and test doubles `FakeAppSettings`, `FakeEditorHost` reused by Tasks 1.6/1.8 (and Phases 3–4).

- [ ] **Step 1: Write the failing test** — the fakes first.

  `winapp/CodeCrack.Tests/Fakes/FakeAppSettings.cs`:
```csharp
using CodeCrack.App.Core.Settings;

namespace CodeCrack.Tests.Fakes;

public sealed class FakeAppSettings : IAppSettings
{
    public string EnginePathOverride { get; set; } = "";
    public int FontSize { get; set; } = 13;
    public string EditorTheme { get; set; } = "system";
    public bool IndentUsesSpaces { get; set; } = true;
    public int IndentWidth { get; set; } = 4;
    public string ClaudeApiKey { get; set; } = "";
    public int SaveCount { get; private set; }
    public void Save() => SaveCount++;
}
```
  `winapp/CodeCrack.Tests/Fakes/FakeEditorHost.cs`:
```csharp
using CodeCrack.App.Core.Editor;

namespace CodeCrack.Tests.Fakes;

public sealed class FakeEditorHost : IEditorHost
{
    public string Text { get; set; } = "";
    public int CaretOffset { get; set; }
    public (int Start, int Length) Selection { get; set; }
    public int RevealedLine { get; private set; }
    public string? LanguagePath { get; private set; }
    public EditorThemeSpec? Theme { get; private set; }
    public bool Focused { get; private set; }

    public event EventHandler? TextChanged;

    public void RevealLine(int line1Indexed) => RevealedLine = line1Indexed;
    public void SetLanguageByPath(string filePath) => LanguagePath = filePath;
    public void ApplyTheme(EditorThemeSpec theme) => Theme = theme;
    public void FocusEditor() => Focused = true;
    public void RaiseTextChanged() => TextChanged?.Invoke(this, EventArgs.Empty);
}
```
  `winapp/CodeCrack.Tests/ContractsTests.cs`:
```csharp
using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.Engine;
using CodeCrack.App.Core.Services;
using CodeCrack.App.Core.Settings;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public class ContractsTests
{
    [Fact]
    public void StatusBus_starts_with_initial_message_and_raises_changed()
    {
        var bus = new StatusBus();
        Assert.Equal("Open a file or folder to begin", bus.Text);

        var fired = 0;
        bus.Changed += (_, _) => fired++;
        bus.Set("Replaced in 3 file(s)");

        Assert.Equal("Replaced in 3 file(s)", bus.Text);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void EngineOutcome_IsSuccess_tracks_error_presence()
    {
        var summary = new Summary(0, 0, 0, 0, new OutcomeCounts(0, 0, 0, 0));
        var ok = new EngineOutcome(new AnalysisResult(new(), new(), summary), null);
        Assert.True(ok.IsSuccess);

        var bad = new EngineOutcome(null, new EngineNotFound("x"));
        Assert.False(bad.IsSuccess);
    }

    [Fact]
    public void FakeEditorHost_satisfies_IEditorHost_shape()
    {
        IEditorHost host = new FakeEditorHost();
        host.Text = "print(1)";
        host.CaretOffset = 3;
        host.Selection = (1, 2);
        host.RevealLine(10);
        host.SetLanguageByPath(@"C:\a\b.py");
        host.ApplyTheme(new EditorThemeSpec("dark_plus", true, "#1e1e1e", "#d4d4d4", "#ffffff", "#264f78"));
        host.FocusEditor();

        Assert.Equal("print(1)", host.Text);
        Assert.Equal((1, 2), host.Selection);
    }

    [Fact]
    public void FakeAppSettings_satisfies_IAppSettings_defaults()
    {
        IAppSettings s = new FakeAppSettings();
        Assert.Equal(13, s.FontSize);
        Assert.Equal("system", s.EditorTheme);
        Assert.True(s.IndentUsesSpaces);
        Assert.Equal(4, s.IndentWidth);
        Assert.Equal("", s.EnginePathOverride);
        s.Save();
    }
}
```

- [ ] **Step 2: Run it, expect FAIL** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj --filter FullyQualifiedName~ContractsTests
```
  Expected failure: `CS0246` for `IAppSettings`, `IEditorHost`, `StatusBus`, `EngineOutcome`, `EditorThemeSpec`.

- [ ] **Step 3: Implement** — create the six production files.

  `winapp/CodeCrack.App.Core/Engine/EngineOutcome.cs`:
```csharp
namespace CodeCrack.App.Core.Engine;

public sealed record EngineOutcome(AnalysisResult? Result, AnalyzerError? Error)
{
    public bool IsSuccess => Error is null;
}
```
  `winapp/CodeCrack.App.Core/Settings/IAppSettings.cs`:
```csharp
namespace CodeCrack.App.Core.Settings;

public interface IAppSettings
{
    string EnginePathOverride { get; set; }
    int FontSize { get; set; }
    string EditorTheme { get; set; }
    bool IndentUsesSpaces { get; set; }
    int IndentWidth { get; set; }
    string ClaudeApiKey { get; set; }
    void Save();
}
```
  `winapp/CodeCrack.App.Core/Services/IFileIO.cs`:
```csharp
namespace CodeCrack.App.Core.Services;

public interface IFileIO
{
    string Read(string path);
    DateTime AtomicWrite(string path, string text); // UTF-8 no BOM; returns new LastWriteUtc.
}
```
  `winapp/CodeCrack.App.Core/Editor/EditorThemeSpec.cs`:
```csharp
namespace CodeCrack.App.Core.Editor;

/// Carries a TextMate theme id + light/dark flag + editor colors (hex strings).
public sealed record EditorThemeSpec(
    string ThemeId,
    bool IsDark,
    string Background,
    string Foreground,
    string Caret,
    string Selection);
```
  `winapp/CodeCrack.App.Core/Editor/IEditorHost.cs`:
```csharp
namespace CodeCrack.App.Core.Editor;

public interface IEditorHost
{
    string Text { get; set; }
    int CaretOffset { get; set; }
    (int Start, int Length) Selection { get; set; }
    void RevealLine(int line1Indexed);
    void SetLanguageByPath(string filePath);
    void ApplyTheme(EditorThemeSpec theme);
    void FocusEditor();
    event EventHandler? TextChanged;
}
```
  `winapp/CodeCrack.App.Core/Services/StatusBus.cs`:
```csharp
namespace CodeCrack.App.Core.Services;

public sealed class StatusBus
{
    public string Text { get; private set; } = "Open a file or folder to begin";

    public event EventHandler? Changed;

    public void Set(string message)
    {
        Text = message;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
```

- [ ] **Step 4: Run tests, expect PASS** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj --filter FullyQualifiedName~ContractsTests
```
  Expect `Failed: 0, Passed: 4`.

- [ ] **Step 5: Commit**
```
git add winapp/CodeCrack.App.Core/Engine/EngineOutcome.cs winapp/CodeCrack.App.Core/Settings/IAppSettings.cs winapp/CodeCrack.App.Core/Services/IFileIO.cs winapp/CodeCrack.App.Core/Editor/EditorThemeSpec.cs winapp/CodeCrack.App.Core/Editor/IEditorHost.cs winapp/CodeCrack.App.Core/Services/StatusBus.cs winapp/CodeCrack.Tests/Fakes/ winapp/CodeCrack.Tests/ContractsTests.cs
git commit -m "feat(core): contract types (EngineOutcome, IAppSettings, IFileIO, IEditorHost, EditorThemeSpec, StatusBus) + test fakes"
```

---

### Task 1.6: `EngineLocator` + precedence tests
**Files:**
- Create `winapp/CodeCrack.App.Core/Engine/EngineLocator.cs`
- Test `winapp/CodeCrack.Tests/EngineLocatorTests.cs`

**Interfaces:** Consumes: `IAppSettings` (1.5), `FakeAppSettings` (1.5). Produces: `static class EngineLocator { static string? Resolve(string filePath, IAppSettings settings) }` — consumed by `Analyzer` (1.8). Precedence: `EnginePathOverride` (verbatim) → `CODECRACK_ENGINE_DIR` env (verbatim) → `AppContext.BaseDirectory\Resources\engine` (validated) → walk up from the file's dir for an `engine` dir containing `codecrack\__main__.py` → `null`.

- [ ] **Step 1: Write the failing test** — `winapp/CodeCrack.Tests/EngineLocatorTests.cs`:
```csharp
using CodeCrack.App.Core.Engine;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public class EngineLocatorTests : IDisposable
{
    private readonly string? _savedEnv;
    private readonly string _tmp;

    public EngineLocatorTests()
    {
        _savedEnv = Environment.GetEnvironmentVariable("CODECRACK_ENGINE_DIR");
        Environment.SetEnvironmentVariable("CODECRACK_ENGINE_DIR", null);
        _tmp = Path.Combine(Path.GetTempPath(), "cc-loc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CODECRACK_ENGINE_DIR", _savedEnv);
        try { Directory.Delete(_tmp, recursive: true); } catch { /* best effort */ }
    }

    private static string MakeEngine(string root)
    {
        var eng = Path.Combine(root, "engine");
        Directory.CreateDirectory(Path.Combine(eng, "codecrack"));
        File.WriteAllText(Path.Combine(eng, "codecrack", "__main__.py"), "");
        return eng;
    }

    [Fact]
    public void Override_wins_verbatim_even_without_marker()
    {
        var s = new FakeAppSettings { EnginePathOverride = @"C:\custom\engine" };
        Assert.Equal(@"C:\custom\engine", EngineLocator.Resolve(Path.Combine(_tmp, "a.py"), s));
    }

    [Fact]
    public void Env_used_when_no_override()
    {
        Environment.SetEnvironmentVariable("CODECRACK_ENGINE_DIR", @"D:\env\engine");
        var s = new FakeAppSettings();
        Assert.Equal(@"D:\env\engine", EngineLocator.Resolve(Path.Combine(_tmp, "a.py"), s));
    }

    [Fact]
    public void Walks_up_from_file_dir_to_find_engine()
    {
        var eng = MakeEngine(_tmp);
        var nested = Path.Combine(_tmp, "src", "pkg");
        Directory.CreateDirectory(nested);
        var file = Path.Combine(nested, "mod.py");
        File.WriteAllText(file, "");
        Assert.Equal(eng, EngineLocator.Resolve(file, new FakeAppSettings()));
    }

    [Fact]
    public void Returns_null_when_nothing_found()
    {
        var file = Path.Combine(_tmp, "lonely.py");
        File.WriteAllText(file, "");
        Assert.Null(EngineLocator.Resolve(file, new FakeAppSettings()));
    }
}
```

- [ ] **Step 2: Run it, expect FAIL** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj --filter FullyQualifiedName~EngineLocatorTests
```
  Expected failure: `CS0246: 'EngineLocator' could not be found`.

- [ ] **Step 3: Implement** — `winapp/CodeCrack.App.Core/Engine/EngineLocator.cs`:
```csharp
using CodeCrack.App.Core.Settings;

namespace CodeCrack.App.Core.Engine;

public static class EngineLocator
{
    private static bool IsEngineDir(string dir) =>
        File.Exists(Path.Combine(dir, "codecrack", "__main__.py"));

    public static string? Resolve(string filePath, IAppSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.EnginePathOverride))
            return settings.EnginePathOverride;

        var env = Environment.GetEnvironmentVariable("CODECRACK_ENGINE_DIR");
        if (!string.IsNullOrEmpty(env))
            return env;

        var bundled = Path.Combine(AppContext.BaseDirectory, "Resources", "engine");
        if (IsEngineDir(bundled))
            return bundled;

        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "engine");
            if (IsEngineDir(candidate))
                return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break; // reached the filesystem root
            dir = parent;
        }
        return null;
    }
}
```

- [ ] **Step 4: Run tests, expect PASS** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj --filter FullyQualifiedName~EngineLocatorTests
```
  Expect `Failed: 0, Passed: 4`.

- [ ] **Step 5: Commit**
```
git add winapp/CodeCrack.App.Core/Engine/EngineLocator.cs winapp/CodeCrack.Tests/EngineLocatorTests.cs
git commit -m "feat(core): EngineLocator with override/env/bundled/walk-up precedence"
```

---

### Task 1.7: `PythonInvocation` + tests
**Files:**
- Create `winapp/CodeCrack.App.Core/Engine/PythonInvocation.cs`
- Test `winapp/CodeCrack.Tests/PythonInvocationTests.cs`

**Interfaces:** Consumes: nothing. Produces: `static class PythonInvocation { static (string Exe, string[] LeadingArgs) Resolve() }` — consumed by `Analyzer` (1.8). Prefers `AppContext.BaseDirectory\python\python.exe` → `("...python.exe", [])`; dev fallback `("py", ["-3"])`. Uses `AppContext.BaseDirectory` (NEVER `Assembly.Location`).

- [ ] **Step 1: Write the failing test** — `winapp/CodeCrack.Tests/PythonInvocationTests.cs` (both tests live in one class so xUnit runs them serially; each establishes its own precondition on `BaseDirectory\python`):
```csharp
using CodeCrack.App.Core.Engine;
using Xunit;

namespace CodeCrack.Tests;

public class PythonInvocationTests
{
    private static string BundledExe =>
        Path.Combine(AppContext.BaseDirectory, "python", "python.exe");

    [Fact]
    public void Falls_back_to_py_dash_3_when_no_bundled_runtime()
    {
        var dir = Path.GetDirectoryName(BundledExe)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        var (exe, args) = PythonInvocation.Resolve();

        Assert.Equal("py", exe);
        Assert.Equal(new[] { "-3" }, args);
    }

    [Fact]
    public void Prefers_bundled_python_beside_the_app()
    {
        var dir = Path.GetDirectoryName(BundledExe)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(BundledExe, ""); // presence is enough; not executed here
        try
        {
            var (exe, args) = PythonInvocation.Resolve();
            Assert.Equal(BundledExe, exe);
            Assert.Empty(args);
        }
        finally
        {
            File.Delete(BundledExe);
            Directory.Delete(dir);
        }
    }
}
```

- [ ] **Step 2: Run it, expect FAIL** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj --filter FullyQualifiedName~PythonInvocationTests
```
  Expected failure: `CS0246: 'PythonInvocation' could not be found`.

- [ ] **Step 3: Implement** — `winapp/CodeCrack.App.Core/Engine/PythonInvocation.cs`:
```csharp
namespace CodeCrack.App.Core.Engine;

public static class PythonInvocation
{
    public static (string Exe, string[] LeadingArgs) Resolve()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "python", "python.exe");
        if (File.Exists(bundled))
            return (bundled, Array.Empty<string>());
        return ("py", new[] { "-3" });
    }
}
```

- [ ] **Step 4: Run tests, expect PASS** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj --filter FullyQualifiedName~PythonInvocationTests
```
  Expect `Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**
```
git add winapp/CodeCrack.App.Core/Engine/PythonInvocation.cs winapp/CodeCrack.Tests/PythonInvocationTests.cs
git commit -m "feat(core): PythonInvocation (bundled python.exe via BaseDirectory, py -3 fallback)"
```

---

### Task 1.8: `IAnalyzer` / `Analyzer` + end-to-end integration test
**Files:**
- Create `winapp/CodeCrack.App.Core/Engine/IAnalyzer.cs`
- Create `winapp/CodeCrack.App.Core/Engine/Analyzer.cs`
- Test `winapp/CodeCrack.Tests/AnalyzerTests.cs`
- Test `winapp/CodeCrack.Tests/Integration/EngineEndToEndTests.cs`

**Interfaces:** Consumes: `EngineLocator.Resolve` (1.6), `PythonInvocation.Resolve` (1.7), `EngineJson.Options`+`AnalysisResult` (1.2), `AnalyzerError` (1.4), `EngineOutcome`+`IAppSettings` (1.5), `FakeAppSettings` (1.5). Produces: `interface IAnalyzer { Task<EngineOutcome> AnalyzeAsync(string filePath, CancellationToken ct = default) }` and `sealed class Analyzer(IAppSettings settings) : IAnalyzer` — consumed by `MainViewModel` (Phase 3). Engine invoked as `<python> [-3] -m codecrack analyze <file> --json`, cwd = engine dir, separate stdout/stderr buffers, decode once.

- [ ] **Step 1: Write the failing test** — `winapp/CodeCrack.Tests/AnalyzerTests.cs` (deterministic error-path unit test, no Python needed):
```csharp
using CodeCrack.App.Core.Engine;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public class AnalyzerTests
{
    [Fact]
    public async Task Returns_EngineNotFound_when_no_engine_anywhere()
    {
        var saved = Environment.GetEnvironmentVariable("CODECRACK_ENGINE_DIR");
        Environment.SetEnvironmentVariable("CODECRACK_ENGINE_DIR", null);
        var tmp = Path.Combine(Path.GetTempPath(), "cc-an-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmp);
            var file = Path.Combine(tmp, "x.py");
            File.WriteAllText(file, "print(1)");

            IAnalyzer analyzer = new Analyzer(new FakeAppSettings());
            var outcome = await analyzer.AnalyzeAsync(file);

            Assert.False(outcome.IsSuccess);
            Assert.IsType<EngineNotFound>(outcome.Error);
            Assert.Null(outcome.Result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODECRACK_ENGINE_DIR", saved);
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }
}
```
  `winapp/CodeCrack.Tests/Integration/EngineEndToEndTests.cs` (shells to real Python on a real fixture; self-skips when no engine/usable Python is present):
```csharp
using System.Diagnostics;
using CodeCrack.App.Core.Engine;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests.Integration;

public class EngineEndToEndTests
{
    private static string? FindRepoEngine()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            var eng = Path.Combine(dir, "engine");
            if (File.Exists(Path.Combine(eng, "codecrack", "__main__.py")))
                return eng;
            var parent = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            if (parent == dir) break;
            dir = parent;
        }
        return null;
    }

    private static bool PythonCanImportCodecrack(string engineDir, string exe, string[] leading)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = engineDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in leading) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import codecrack");
            using var p = Process.Start(psi)!;
            return p.WaitForExit(20000) && p.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public async Task Analyzes_a_real_fixture_end_to_end()
    {
        var engine = FindRepoEngine();
        if (engine is null) return; // no engine checkout — skip

        var (exe, leading) = PythonInvocation.Resolve();
        if (!PythonCanImportCodecrack(engine, exe, leading)) return; // no usable python — skip

        var fixture = Path.Combine(engine, "tests", "fixtures", "zero_division.py");
        if (!File.Exists(fixture)) return;

        IAnalyzer analyzer = new Analyzer(new FakeAppSettings { EnginePathOverride = engine });
        var outcome = await analyzer.AnalyzeAsync(fixture);

        Assert.True(outcome.IsSuccess, outcome.Error?.Message);
        Assert.NotNull(outcome.Result);
        Assert.NotEmpty(outcome.Result!.Findings);
    }
}
```

- [ ] **Step 2: Run it, expect FAIL** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~AnalyzerTests|FullyQualifiedName~EngineEndToEndTests"
```
  Expected failure: `CS0246: 'Analyzer'/'IAnalyzer' could not be found`.

- [ ] **Step 3: Implement** — `winapp/CodeCrack.App.Core/Engine/IAnalyzer.cs`:
```csharp
namespace CodeCrack.App.Core.Engine;

public interface IAnalyzer
{
    Task<EngineOutcome> AnalyzeAsync(string filePath, CancellationToken ct = default);
}
```
  `winapp/CodeCrack.App.Core/Engine/Analyzer.cs`:
```csharp
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodeCrack.App.Core.Settings;

namespace CodeCrack.App.Core.Engine;

public sealed class Analyzer(IAppSettings settings) : IAnalyzer
{
    private readonly IAppSettings _settings = settings;

    public async Task<EngineOutcome> AnalyzeAsync(string filePath, CancellationToken ct = default)
    {
        var engineDir = EngineLocator.Resolve(filePath, _settings);
        if (engineDir is null)
            return new EngineOutcome(null, new EngineNotFound("no bundled, override, or checkout engine\\"));
        if (!Directory.Exists(engineDir))
            return new EngineOutcome(null, new EngineNotFound(engineDir));

        var (exe, leadingArgs) = PythonInvocation.Resolve();

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = engineDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in leadingArgs) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add("codecrack");
        psi.ArgumentList.Add("analyze");
        psi.ArgumentList.Add(filePath);
        psi.ArgumentList.Add("--json");

        using var process = new Process { StartInfo = psi };

        try
        {
            if (!process.Start())
                return new EngineOutcome(null, new LaunchFailed("process did not start"));
        }
        catch (Exception ex)
        {
            return new EngineOutcome(null, new LaunchFailed(ex.Message));
        }

        // Buffer stdout (the JSON) and stderr separately to avoid interleave/deadlock.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
            return new EngineOutcome(null, new EngineFailed(process.ExitCode, stderr, engineDir));

        try
        {
            var result = JsonSerializer.Deserialize<AnalysisResult>(stdout, EngineJson.Options);
            return result is null
                ? new EngineOutcome(null, new DecodeFailed("engine returned null", engineDir, stdout, stderr))
                : new EngineOutcome(result, null);
        }
        catch (JsonException ex)
        {
            return new EngineOutcome(null, new DecodeFailed(ex.Message, engineDir, stdout, stderr));
        }
    }
}
```

- [ ] **Step 4: Run tests, expect PASS** — from `winapp/`:
```
dotnet test CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~AnalyzerTests|FullyQualifiedName~EngineEndToEndTests"
```
  Expect `Failed: 0, Passed: 2` (the integration test executes fully when a Python that can `import codecrack` is present after Phase 0; otherwise it self-skips and still counts as passed).

- [ ] **Step 5: Commit**
```
git add winapp/CodeCrack.App.Core/Engine/IAnalyzer.cs winapp/CodeCrack.App.Core/Engine/Analyzer.cs winapp/CodeCrack.Tests/AnalyzerTests.cs winapp/CodeCrack.Tests/Integration/EngineEndToEndTests.cs
git commit -m "feat(core): Analyzer subprocess->JSON loop + error-path unit test + engine E2E integration test"
```

---

### Task 1.9: CI `csharp-tests` job (windows + ubuntu)
**Files:**
- Modify `.github/workflows/ci.yml` (add the `csharp-tests` job created here to the workflow authored in Phase 0)

**Interfaces:** Consumes: the pinned SDK (`winapp/global.json`), the strict `Directory.Build.props`, and `CodeCrack.Tests` (Tasks 1.1–1.8). Produces: a required CI gate that restores/builds/tests `CodeCrack.Tests` (and, transitively, `CodeCrack.App.Core`) with coverage on `windows-latest` and, to prove the pure-core layer is Linux-clean, `ubuntu-latest`. The WPF app is NOT built here (Phase 5 `windows-build` does that).

- [ ] **Step 1: Write the failing test** — assert the job does not yet exist (grep is the check; from repo root):
```
grep -n "csharp-tests:" .github/workflows/ci.yml
```

- [ ] **Step 2: Run it, expect FAIL** — the grep prints nothing and exits non-zero:
```
grep -n "csharp-tests:" .github/workflows/ci.yml ; echo "exit=$?"
```
  Expected: `exit=1` (job absent).

- [ ] **Step 3: Implement** — add this job under the top-level `jobs:` key in `.github/workflows/ci.yml` (leave the Phase 0 `engine-tests` job intact):
```yaml
  csharp-tests:
    name: csharp-tests (${{ matrix.os }})
    strategy:
      fail-fast: false
      matrix:
        os: [windows-latest, ubuntu-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET (pinned by winapp/global.json)
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - name: Restore
        run: dotnet restore winapp/CodeCrack.Tests/CodeCrack.Tests.csproj
      - name: Build Core + Tests
        run: dotnet build winapp/CodeCrack.Tests/CodeCrack.Tests.csproj -c Release --no-restore
      - name: Test with coverage
        run: >
          dotnet test winapp/CodeCrack.Tests/CodeCrack.Tests.csproj
          -c Release --no-build
          --collect:"XPlat Code Coverage"
          --logger "trx;LogFileName=csharp-results.trx"
      - name: Upload coverage
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: csharp-coverage-${{ matrix.os }}
          path: '**/TestResults/**/coverage.cobertura.xml'
          if-no-files-found: warn
```
  Building the **Tests** project (net8.0) pulls in only `CodeCrack.App.Core` (net8.0) — no `net8.0-windows`/WPF — so the job builds and runs on `ubuntu-latest` as well as `windows-latest`.

- [ ] **Step 4: Run tests, expect PASS** — verify the job is present and the YAML parses (from repo root):
```
grep -n "csharp-tests:" .github/workflows/ci.yml
python -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml')); print('yaml-ok')"
```
  Expect the `csharp-tests:` line to print and `yaml-ok`. **CI verification:** push the branch; the `csharp-tests (windows-latest)` and `csharp-tests (ubuntu-latest)` checks both go green.

- [ ] **Step 5: Commit**
```
git add .github/workflows/ci.yml
git commit -m "ci: add csharp-tests job (dotnet test + coverage on windows-latest and ubuntu-latest)"
```

---

## Phase 2: Editor (AvalonEdit + TextMateSharp)

> **Package pins (added in Task 2.1, used by all view tasks):** `AvalonEdit` `6.3.1.120` (WPF `ICSharpCode.AvalonEdit`), `TextMateSharp` `1.0.66`, `TextMateSharp.Grammars` `1.0.66`. WPF has no maintained `AvalonEdit.TextMate` NuGet package, so Task 2.1 vendors a minimal `InstallTextMate` binding over `TextMateSharp` core. `RegistryOptions`, `ThemeName`, `IRegistryOptions`, `IGrammar`, `Registry`, `Theme` all come from those two TextMateSharp packages. **CodeCrack.App.Core never references any of these** — it stays WPF/TextMate-free and Linux-CI-buildable; all TextMate/AvalonEdit types live only in `CodeCrackApp` (`net8.0-windows`).

---

### Task 2.1: `CodeEditorControl` hosting AvalonEdit, implementing `IEditorHost` + vendored `InstallTextMate`

**Files:**
- Create: `winapp/CodeCrackApp/CodeCrackApp.csproj` package refs (modify existing Phase-1 csproj)
- Create: `winapp/CodeCrack.App.Core/Editor/EditorGeometry.cs`
- Create: `winapp/CodeCrackApp/Editor/TextMateInstallation.cs`
- Create: `winapp/CodeCrackApp/Editor/TextMateColorizer.cs`
- Create: `winapp/CodeCrackApp/Editor/CodeEditorControl.xaml`
- Create: `winapp/CodeCrackApp/Editor/CodeEditorControl.xaml.cs`
- Test: `winapp/CodeCrack.Tests/EditorGeometryTests.cs`

**Interfaces:**
- Consumes: `IEditorHost` (`string Text{get;set;}`, `int CaretOffset{get;set;}`, `(int Start,int Length) Selection{get;set;}`, `void RevealLine(int)`, `void SetLanguageByPath(string)`, `void ApplyTheme(EditorThemeSpec)`, `void FocusEditor()`, `event EventHandler? TextChanged`), `EditorThemeSpec` (from Task 2.3 — this task references it in the signature only; the field-level use is stubbed until 2.3, so **do Task 2.3 before compiling `ApplyTheme`'s body** — see note in Step 3).
- Produces: `EditorGeometry.ClampOffset(int,int)`, `EditorGeometry.ClampSelection(int,int,int)`, `EditorGeometry.ClampLine(int,int)` (Core, pure); `CodeEditorControl : UserControl, IEditorHost`; `TextEditor editor.InstallTextMate(RegistryOptions)` → `TextMateInstallation`.

- [ ] **Step 1: Write the failing test** — pure clamp helpers used by the view (offset/selection bounds-checking, line clamp). Create `winapp/CodeCrack.Tests/EditorGeometryTests.cs`:
```csharp
using CodeCrack.App.Core.Editor;
using Xunit;

namespace CodeCrack.Tests;

public class EditorGeometryTests
{
    [Theory]
    [InlineData(10, 5, 5)]
    [InlineData(10, -3, 0)]
    [InlineData(10, 42, 10)]   // caret may sit at TextLength (end of doc)
    public void ClampOffset_KeepsWithinZeroToLength(int len, int req, int expected)
        => Assert.Equal(expected, EditorGeometry.ClampOffset(len, req));

    [Fact]
    public void ClampSelection_TruncatesRunPastEnd()
    {
        var (start, length) = EditorGeometry.ClampSelection(textLength: 8, start: 6, length: 10);
        Assert.Equal(6, start);
        Assert.Equal(2, length);
    }

    [Fact]
    public void ClampSelection_NegativeStartCollapses()
    {
        var (start, length) = EditorGeometry.ClampSelection(textLength: 8, start: -4, length: 3);
        Assert.Equal(0, start);
        Assert.Equal(0, length); // start moved to 0, original end (=-1) < 0 → empty
    }

    [Theory]
    [InlineData(100, 0, 1)]     // AvalonEdit lines are 1-indexed
    [InlineData(100, 250, 100)]
    [InlineData(100, 37, 37)]
    public void ClampLine_KeepsWithinOneToLineCount(int lineCount, int req, int expected)
        => Assert.Equal(expected, EditorGeometry.ClampLine(lineCount, req));
}
```

- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp/CodeCrack.Tests --filter FullyQualifiedName~EditorGeometryTests`. Expected: build error `CS0103: The name 'EditorGeometry' does not exist` / `type or namespace 'EditorGeometry' could not be found`.

- [ ] **Step 3: Implement** — first the Core helper (`winapp/CodeCrack.App.Core/Editor/EditorGeometry.cs`):
```csharp
namespace CodeCrack.App.Core.Editor;

/// <summary>Pure bounds-checking used by the AvalonEdit host and tab restore.</summary>
public static class EditorGeometry
{
    public static int ClampOffset(int textLength, int offset)
        => offset < 0 ? 0 : offset > textLength ? textLength : offset;

    /// <summary>Clamps a selection to [0, textLength]; collapses to empty if it lands past the end.</summary>
    public static (int Start, int Length) ClampSelection(int textLength, int start, int length)
    {
        int end = start + length;
        int s = ClampOffset(textLength, start);
        int e = ClampOffset(textLength, end);
        if (e < s) e = s;
        return (s, e - s);
    }

    /// <summary>Clamps a 1-indexed line number to [1, lineCount] (lineCount is at least 1).</summary>
    public static int ClampLine(int lineCount, int requestedLine1)
    {
        int max = lineCount < 1 ? 1 : lineCount;
        return requestedLine1 < 1 ? 1 : requestedLine1 > max ? max : requestedLine1;
    }
}
```
Then add package references to `winapp/CodeCrackApp/CodeCrackApp.csproj` (inside the existing `<Project>`, add one `<ItemGroup>`):
```xml
<ItemGroup>
  <PackageReference Include="AvalonEdit" Version="6.3.1.120" />
  <PackageReference Include="TextMateSharp" Version="1.0.66" />
  <PackageReference Include="TextMateSharp.Grammars" Version="1.0.66" />
</ItemGroup>
```
Vendored per-line colorizer `winapp/CodeCrackApp/Editor/TextMateColorizer.cs`:
```csharp
using System.Collections.Generic;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace CodeCrackApp.Editor;

/// <summary>
/// Colorizes each visible line by tokenizing it with a TextMateSharp grammar and mapping
/// scopes → theme foreground colors. State (rule stack) is carried line-to-line so multi-line
/// constructs (block comments, strings) highlight correctly; the cache is cleared on any edit.
/// </summary>
internal sealed class TextMateColorizer : ICSharpCode.AvalonEdit.Rendering.DocumentColorizingTransformer
{
    private readonly Registry _registry;
    private IGrammar? _grammar;
    private Theme _theme;
    private readonly Dictionary<int, IStateStack?> _lineEndStates = new();
    private Dictionary<int, Brush> _brushes = new();

    public TextMateColorizer(Registry registry)
    {
        _registry = registry;
        _theme = registry.GetTheme();
        RebuildBrushes();
    }

    public void SetGrammar(IGrammar? grammar)
    {
        _grammar = grammar;
        _lineEndStates.Clear();
    }

    public void SetTheme(Theme theme)
    {
        _theme = theme;
        RebuildBrushes();
        _lineEndStates.Clear();
    }

    public void InvalidateFrom(int lineNumber)
    {
        var stale = new List<int>();
        foreach (var k in _lineEndStates.Keys)
            if (k >= lineNumber) stale.Add(k);
        foreach (var k in stale) _lineEndStates.Remove(k);
    }

    private void RebuildBrushes()
    {
        var map = new Dictionary<int, Brush>();
        foreach (string color in _theme.GetColorMap())
        {
            int id = _theme.GetColorId(color);
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                brush.Freeze();
                map[id] = brush;
            }
            catch { /* skip unparseable theme color */ }
        }
        _brushes = map;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_grammar is null) return;
        string text = CurrentContext.Document.GetText(line);
        _lineEndStates.TryGetValue(line.LineNumber - 1, out var prevState);
        var result = _grammar.TokenizeLine(text, prevState, null);
        _lineEndStates[line.LineNumber] = result.RuleStack;

        foreach (IToken token in result.Tokens)
        {
            int startInLine = token.StartIndex;
            int endInLine = token.EndIndex > text.Length ? text.Length : token.EndIndex;
            if (endInLine <= startInLine) continue;

            List<ThemeTrieElementRule> rules = _theme.Match(token.Scopes);
            if (rules.Count == 0) continue;
            int fg = rules[0].foreground;
            if (fg <= 0 || !_brushes.TryGetValue(fg, out var brush)) continue;

            int startOffset = line.Offset + startInLine;
            int endOffset = line.Offset + endInLine;
            ChangeLinePart(startOffset, endOffset, e => e.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
```
Vendored installation `winapp/CodeCrackApp/Editor/TextMateInstallation.cs`:
```csharp
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace CodeCrackApp.Editor;

/// <summary>Minimal WPF replacement for AvaloniaEdit's InstallTextMate: wires a per-line colorizer.</summary>
public sealed class TextMateInstallation
{
    private readonly TextEditor _editor;
    private readonly Registry _registry;
    private readonly RegistryOptions _options;
    private readonly TextMateColorizer _colorizer;

    internal TextMateInstallation(TextEditor editor, RegistryOptions options)
    {
        _editor = editor;
        _options = options;
        _registry = new Registry(options);
        _colorizer = new TextMateColorizer(_registry);
        _editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        _editor.Document.Changed += OnDocumentChanged;
    }

    public RegistryOptions Options => _options;

    public void SetGrammar(string? scopeName)
    {
        _colorizer.SetGrammar(string.IsNullOrEmpty(scopeName) ? null : _registry.LoadGrammar(scopeName));
        _editor.TextArea.TextView.Redraw();
    }

    public void SetTheme(IRawTheme rawTheme)
    {
        _registry.SetTheme(rawTheme);
        _colorizer.SetTheme(_registry.GetTheme());
        _editor.TextArea.TextView.Redraw();
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        var line = _editor.Document.GetLineByOffset(e.Offset);
        _colorizer.InvalidateFrom(line.LineNumber);
    }
}

public static class TextMateInstall
{
    /// <summary>Installs a TextMateSharp colorizer on the editor (WPF analog of InstallTextMate).</summary>
    public static TextMateInstallation InstallTextMate(this TextEditor editor, RegistryOptions options)
        => new(editor, options);
}
```
XAML `winapp/CodeCrackApp/Editor/CodeEditorControl.xaml`:
```xml
<UserControl x:Class="CodeCrackApp.Editor.CodeEditorControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:avalon="http://icsharpcode.net/sharpdevelop/avalonedit">
    <avalon:TextEditor x:Name="Editor"
                       ShowLineNumbers="True"
                       FontFamily="Consolas"
                       FontSize="13"
                       WordWrap="False"
                       HorizontalScrollBarVisibility="Auto"
                       VerticalScrollBarVisibility="Auto" />
</UserControl>
```
Code-behind `winapp/CodeCrackApp/Editor/CodeEditorControl.xaml.cs` (the `ApplyTheme`, `SetLanguageByPath` bodies land fully in Tasks 2.2/2.3 — here they compile against the signatures; `ApplyThemeSpec`/`ScopeForPath` referenced below are added in 2.2/2.3):
```csharp
using System;
using System.Windows.Controls;
using CodeCrack.App.Core.Editor;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using TextMateSharp.Grammars;

namespace CodeCrackApp.Editor;

public partial class CodeEditorControl : UserControl, IEditorHost
{
    private readonly RegistryOptions _registryOptions;
    private readonly TextMateInstallation _textMate;
    private bool _suppressTextChanged;

    public CodeEditorControl()
    {
        InitializeComponent();
        _registryOptions = new RegistryOptions(ThemeName.DarkPlus);
        _textMate = Editor.InstallTextMate(_registryOptions);
        Editor.TextChanged += (_, _) => { if (!_suppressTextChanged) TextChanged?.Invoke(this, EventArgs.Empty); };
    }

    public event EventHandler? TextChanged;

    public string Text
    {
        get => Editor.Text;
        set
        {
            _suppressTextChanged = true;
            Editor.Text = value ?? string.Empty;
            _suppressTextChanged = false;
        }
    }

    public int CaretOffset
    {
        get => Editor.CaretOffset;
        set => Editor.CaretOffset = EditorGeometry.ClampOffset(Editor.Document.TextLength, value);
    }

    public (int Start, int Length) Selection
    {
        get => (Editor.SelectionStart, Editor.SelectionLength);
        set
        {
            var (s, l) = EditorGeometry.ClampSelection(Editor.Document.TextLength, value.Start, value.Length);
            Editor.Select(s, l);
        }
    }

    public void RevealLine(int line1Indexed)
    {
        int line = EditorGeometry.ClampLine(Editor.Document.LineCount, line1Indexed);
        DocumentLine docLine = Editor.Document.GetLineByNumber(line);
        Editor.Select(docLine.Offset, 0);
        Editor.CaretOffset = docLine.Offset;
        Editor.ScrollToLine(line);
        Editor.TextArea.Caret.BringCaretToView();
        Editor.TextArea.Focus();
    }

    public void FocusEditor() => Editor.TextArea.Focus();

    // Bodies completed in Task 2.2 (SetLanguageByPath) and Task 2.3 (ApplyTheme).
    public void SetLanguageByPath(string filePath)
        => _textMate.SetGrammar(LanguageScope.ScopeForPath(_registryOptions, filePath));

    public void ApplyTheme(EditorThemeSpec theme)
        => ThemeApplier.Apply(Editor, _textMate, _registryOptions, theme);
}
```

- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp/CodeCrack.Tests --filter FullyQualifiedName~EditorGeometryTests`. The Core helper compiles/passes. (The `CodeCrackApp` view project won't fully build until 2.2/2.3 supply `LanguageScope.ScopeForPath` and `ThemeApplier.Apply`; that is expected — the *test project* under test here references only Core.)

- [ ] **Step 5: Commit** —
```
git add winapp/CodeCrack.App.Core/Editor/EditorGeometry.cs winapp/CodeCrackApp/ winapp/CodeCrack.Tests/EditorGeometryTests.cs
git commit -m "feat(editor): CodeEditorControl host + EditorGeometry clamps + vendored TextMate binding"
```

---

### Task 2.2: `LanguageMap.ScopeForExtension` (Core) + `LanguageScope` wiring for `SetLanguageByPath`

**Files:**
- Create: `winapp/CodeCrack.App.Core/Editor/LanguageMap.cs`
- Create: `winapp/CodeCrackApp/Editor/LanguageScope.cs`
- Test: `winapp/CodeCrack.Tests/LanguageMapTests.cs`

**Interfaces:**
- Consumes: `RegistryOptions.GetLanguageByExtension(string)`, `RegistryOptions.GetScopeByLanguageId(string)` (TextMateSharp.Grammars).
- Produces: `LanguageMap.ScopeForExtension(string? ext)` → `string?` (a TextMate scope name via a fixed table, no dot); `LanguageScope.ScopeForPath(RegistryOptions, string filePath)` → `string?`.

- [ ] **Step 1: Write the failing test** — `winapp/CodeCrack.Tests/LanguageMapTests.cs`:
```csharp
using CodeCrack.App.Core.Editor;
using Xunit;

namespace CodeCrack.Tests;

public class LanguageMapTests
{
    [Theory]
    [InlineData("py", "source.python")]
    [InlineData("pyw", "source.python")]
    [InlineData("js", "source.js")]
    [InlineData("mjs", "source.js")]
    [InlineData("ts", "source.ts")]
    [InlineData("cs", "source.cs")]
    [InlineData("java", "source.java")]
    [InlineData("go", "source.go")]
    [InlineData("rs", "source.rust")]
    [InlineData("rb", "source.ruby")]
    [InlineData("json", "source.json")]
    [InlineData("md", "text.html.markdown")]
    [InlineData("yaml", "source.yaml")]
    [InlineData("sh", "source.shell")]
    public void ScopeForExtension_MapsKnown(string ext, string expected)
        => Assert.Equal(expected, LanguageMap.ScopeForExtension(ext));

    [Theory]
    [InlineData("PY", "source.python")]   // case-insensitive
    [InlineData(".py", "source.python")]  // leading dot tolerated
    public void ScopeForExtension_Normalizes(string ext, string expected)
        => Assert.Equal(expected, LanguageMap.ScopeForExtension(ext));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("bin")]
    [InlineData("xyz")]
    public void ScopeForExtension_UnknownReturnsNull(string? ext)
        => Assert.Null(LanguageMap.ScopeForExtension(ext));
}
```

- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp/CodeCrack.Tests --filter FullyQualifiedName~LanguageMapTests`. Expected: `CS0103: 'LanguageMap' does not exist`.

- [ ] **Step 3: Implement** — `winapp/CodeCrack.App.Core/Editor/LanguageMap.cs` (a deterministic, TextMate-free table so it's unit-testable on Linux CI; ports `LanguageMap.swift` to VS Code TextMate scope names):
```csharp
using System.Collections.Generic;

namespace CodeCrack.App.Core.Editor;

/// <summary>Maps a bare file extension (no dot) to a TextMate scope name, or null if unknown.</summary>
public static class LanguageMap
{
    private static readonly IReadOnlyDictionary<string, string> Scopes = new Dictionary<string, string>
    {
        ["swift"] = "source.swift",
        ["py"] = "source.python", ["pyw"] = "source.python", ["pyi"] = "source.python",
        ["js"] = "source.js", ["jsx"] = "source.js.jsx", ["mjs"] = "source.js", ["cjs"] = "source.js",
        ["ts"] = "source.ts", ["tsx"] = "source.tsx",
        ["java"] = "source.java",
        ["kt"] = "source.kotlin", ["kts"] = "source.kotlin",
        ["c"] = "source.c", ["h"] = "source.c",
        ["cpp"] = "source.cpp", ["cc"] = "source.cpp", ["cxx"] = "source.cpp",
        ["hpp"] = "source.cpp", ["hh"] = "source.cpp",
        ["cs"] = "source.cs",
        ["go"] = "source.go",
        ["rs"] = "source.rust",
        ["rb"] = "source.ruby",
        ["php"] = "source.php",
        ["json"] = "source.json", ["jsonc"] = "source.json.comments",
        ["md"] = "text.html.markdown", ["markdown"] = "text.html.markdown",
        ["html"] = "text.html.basic", ["htm"] = "text.html.basic", ["xhtml"] = "text.html.basic",
        ["xml"] = "text.xml", ["plist"] = "text.xml", ["svg"] = "text.xml",
        ["css"] = "source.css", ["scss"] = "source.css.scss", ["less"] = "source.css.less",
        ["sh"] = "source.shell", ["bash"] = "source.shell", ["zsh"] = "source.shell",
        ["yml"] = "source.yaml", ["yaml"] = "source.yaml",
        ["toml"] = "source.toml", ["ini"] = "source.ini", ["cfg"] = "source.ini", ["conf"] = "source.ini",
        ["sql"] = "source.sql",
        ["dart"] = "source.dart",
        ["scala"] = "source.scala", ["sc"] = "source.scala",
        ["lua"] = "source.lua",
        ["r"] = "source.r",
        ["pl"] = "source.perl", ["pm"] = "source.perl",
        ["groovy"] = "source.groovy", ["gradle"] = "source.groovy",
        ["dockerfile"] = "source.dockerfile",
        ["makefile"] = "source.makefile", ["mk"] = "source.makefile",
        ["diff"] = "source.diff", ["patch"] = "source.diff",
        ["ps1"] = "source.powershell",
    };

    public static string? ScopeForExtension(string? ext)
    {
        if (string.IsNullOrEmpty(ext)) return null;
        string key = ext.TrimStart('.').ToLowerInvariant();
        return Scopes.TryGetValue(key, out string? scope) ? scope : null;
    }
}
```
`winapp/CodeCrackApp/Editor/LanguageScope.cs` (resolves via our table first, then falls back to TextMateSharp's own extension registry so all 185+ bundled grammars still work):
```csharp
using System.IO;
using CodeCrack.App.Core.Editor;
using TextMateSharp.Grammars;

namespace CodeCrackApp.Editor;

internal static class LanguageScope
{
    public static string? ScopeForPath(RegistryOptions options, string filePath)
    {
        string ext = Path.GetExtension(filePath); // includes leading '.'
        string? scope = LanguageMap.ScopeForExtension(ext);
        if (scope is not null) return scope;

        Language? lang = options.GetLanguageByExtension(ext);
        return lang is null ? null : options.GetScopeByLanguageId(lang.Id);
    }
}
```

- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp/CodeCrack.Tests --filter FullyQualifiedName~LanguageMapTests`.

- [ ] **Step 5: Commit** —
```
git add winapp/CodeCrack.App.Core/Editor/LanguageMap.cs winapp/CodeCrackApp/Editor/LanguageScope.cs winapp/CodeCrack.Tests/LanguageMapTests.cs
git commit -m "feat(editor): LanguageMap scope table + SetLanguageByPath wiring"
```

---

### Task 2.3: `EditorThemeSpec` + `EditorThemeRegistry` (Core) + `ThemeApplier`/`WindowsTheme` (view)

**Files:**
- Create: `winapp/CodeCrack.App.Core/Editor/EditorThemeSpec.cs`
- Create: `winapp/CodeCrack.App.Core/Editor/EditorThemeRegistry.cs`
- Create: `winapp/CodeCrackApp/Editor/ThemeApplier.cs`
- Create: `winapp/CodeCrackApp/Services/WindowsTheme.cs`
- Test: `winapp/CodeCrack.Tests/EditorThemeRegistryTests.cs`

**Interfaces:**
- Consumes: `RegistryOptions.LoadTheme(ThemeName)` (returns `IRawTheme`), `TextMateInstallation.SetTheme(IRawTheme)` (Task 2.1), `TextEditor` chrome props.
- Produces: `EditorThemeSpec(string Key,string Label,string TmThemeId,bool IsDark,string Background,string Foreground,string Caret,string Selection,string LineNumber)`; `EditorThemeRegistry.All`, `EditorThemeRegistry.Resolve(string key, bool systemIsDark)` → `EditorThemeSpec` (never `system`); `WindowsTheme.AppsUseLightTheme` + `WindowsTheme.SystemThemeChanged`.

- [ ] **Step 1: Write the failing test** — `winapp/CodeCrack.Tests/EditorThemeRegistryTests.cs`:
```csharp
using System.Linq;
using CodeCrack.App.Core.Editor;
using Xunit;

namespace CodeCrack.Tests;

public class EditorThemeRegistryTests
{
    [Fact]
    public void All_ContainsTheSixMenuThemes()
    {
        var keys = EditorThemeRegistry.All.Select(t => t.Key).ToArray();
        Assert.Equal(
            new[] { "system", "atom-one-light", "atom-one-dark", "solarized-dark", "monokai", "github" },
            keys);
    }

    [Fact]
    public void Resolve_SystemFollowsOsAppearance()
    {
        Assert.Equal("atom-one-dark", EditorThemeRegistry.Resolve("system", systemIsDark: true).Key);
        Assert.Equal("atom-one-light", EditorThemeRegistry.Resolve("system", systemIsDark: false).Key);
    }

    [Fact]
    public void Resolve_NeverReturnsSystem()
        => Assert.All(EditorThemeRegistry.All,
            t => Assert.NotEqual("system", EditorThemeRegistry.Resolve(t.Key, true).Key));

    [Theory]
    [InlineData("atom-one-dark", true, "SolarizedDark_or_DarkPlus")]
    [InlineData("monokai", true, "Monokai")]
    [InlineData("solarized-dark", true, "SolarizedDark")]
    [InlineData("github", false, "Light")]
    public void Resolve_CarriesDarknessAndTmId(string key, bool isDark, string _)
    {
        var spec = EditorThemeRegistry.Resolve(key, systemIsDark: true);
        Assert.Equal(isDark, spec.IsDark);
        Assert.False(string.IsNullOrWhiteSpace(spec.TmThemeId));
        Assert.StartsWith("#", spec.Background);
        Assert.StartsWith("#", spec.Caret);
    }

    [Fact]
    public void Resolve_UnknownKeyFallsBackToSystem()
        => Assert.Equal("atom-one-light", EditorThemeRegistry.Resolve("bogus", systemIsDark: false).Key);
}
```

- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp/CodeCrack.Tests --filter FullyQualifiedName~EditorThemeRegistryTests`. Expected: `CS0103: 'EditorThemeRegistry' does not exist`.

- [ ] **Step 3: Implement** — `winapp/CodeCrack.App.Core/Editor/EditorThemeSpec.cs`:
```csharp
namespace CodeCrack.App.Core.Editor;

/// <summary>
/// A concrete editor theme: which TextMateSharp theme to load (<see cref="TmThemeId"/> matches a
/// TextMateSharp.Grammars.ThemeName enum name) plus the chrome colors the WPF control paints
/// directly (background, caret, selection, line-number gutter) so panes read as one surface.
/// </summary>
public sealed record EditorThemeSpec(
    string Key,
    string Label,
    string TmThemeId,
    bool IsDark,
    string Background,
    string Foreground,
    string Caret,
    string Selection,
    string LineNumber);
```
`winapp/CodeCrack.App.Core/Editor/EditorThemeRegistry.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;

namespace CodeCrack.App.Core.Editor;

/// <summary>The six selectable themes (ports Theme.swift). 'system' resolves to atom-one at runtime.</summary>
public static class EditorThemeRegistry
{
    private static readonly EditorThemeSpec AtomOneLight = new(
        "atom-one-light", "Atom One Light", "LightPlus", false,
        "#FAFAFA", "#383A42", "#526FFF", "#E5E5E6", "#9D9D9F");

    private static readonly EditorThemeSpec AtomOneDark = new(
        "atom-one-dark", "Atom One Dark", "DarkPlus", true,
        "#282C34", "#ABB2BF", "#528BFF", "#3E4451", "#4B5263");

    private static readonly EditorThemeSpec SolarizedDark = new(
        "solarized-dark", "Solarized Dark", "SolarizedDark", true,
        "#002B36", "#839496", "#839496", "#073642", "#586E75");

    private static readonly EditorThemeSpec Monokai = new(
        "monokai", "Monokai", "Monokai", true,
        "#272822", "#F8F8F2", "#F8F8F0", "#49483E", "#90908A");

    private static readonly EditorThemeSpec GitHub = new(
        "github", "GitHub", "Light", false,
        "#FFFFFF", "#24292E", "#044289", "#C8E1FF", "#BABBBD");

    private static readonly EditorThemeSpec System = new(
        "system", "System (follow appearance)", "DarkPlus", true,
        AtomOneDark.Background, AtomOneDark.Foreground, AtomOneDark.Caret, AtomOneDark.Selection, AtomOneDark.LineNumber);

    public static IReadOnlyList<EditorThemeSpec> All { get; } = new[]
    {
        System, AtomOneLight, AtomOneDark, SolarizedDark, Monokai, GitHub,
    };

    private static readonly IReadOnlyDictionary<string, EditorThemeSpec> Concrete =
        new[] { AtomOneLight, AtomOneDark, SolarizedDark, Monokai, GitHub }
            .ToDictionary(t => t.Key);

    /// <summary>Resolves a menu key to a concrete spec. 'system' (and any unknown key) follows the OS.</summary>
    public static EditorThemeSpec Resolve(string key, bool systemIsDark)
    {
        if (Concrete.TryGetValue(key, out var spec)) return spec;
        return systemIsDark ? AtomOneDark : AtomOneLight;
    }
}
```
`winapp/CodeCrackApp/Services/WindowsTheme.cs` (reads `AppsUseLightTheme`; raises an event on `WM_SETTINGCHANGE` "ImmersiveColorSet"):
```csharp
using System;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace CodeCrackApp.Services;

/// <summary>Windows light/dark follow. Read <see cref="AppsUseLightTheme"/>; subscribe to changes.</summary>
public static class WindowsTheme
{
    private const int WM_SETTINGCHANGE = 0x001A;

    public static event EventHandler? SystemThemeChanged;

    public static bool AppsUseLightTheme
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("AppsUseLightTheme");
            return value is int i ? i != 0 : true; // default light
        }
    }

    /// <summary>Call once from a Window (e.g. OnSourceInitialized) to relay WM_SETTINGCHANGE.</summary>
    public static void Attach(Window window)
    {
        var helper = new WindowInteropHelper(window);
        var source = HwndSource.FromHwnd(helper.Handle);
        source?.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SETTINGCHANGE && lParam != IntPtr.Zero)
        {
            string? area = System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam);
            if (area == "ImmersiveColorSet")
                SystemThemeChanged?.Invoke(null, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }
}
```
`winapp/CodeCrackApp/Editor/ThemeApplier.cs` (parses hex → WPF brushes; loads the TextMateSharp theme by enum name; paints editor chrome):
```csharp
using System;
using System.Windows.Media;
using CodeCrack.App.Core.Editor;
using ICSharpCode.AvalonEdit;
using TextMateSharp.Grammars;

namespace CodeCrackApp.Editor;

internal static class ThemeApplier
{
    public static void Apply(TextEditor editor, TextMateInstallation textMate,
        RegistryOptions options, EditorThemeSpec spec)
    {
        if (Enum.TryParse<ThemeName>(spec.TmThemeId, out var themeName))
            textMate.SetTheme(options.LoadTheme(themeName));

        editor.Background = Brush(spec.Background);
        editor.Foreground = Brush(spec.Foreground);
        editor.TextArea.Caret.CaretBrush = Brush(spec.Caret);
        editor.TextArea.SelectionBrush = Brush(spec.Selection);
        editor.LineNumbersForeground = Brush(spec.LineNumber);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
```

- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp/CodeCrack.Tests --filter FullyQualifiedName~EditorThemeRegistryTests`.
  **Manual verify (Windows, after Phase 3/4 wires a window):** open a `.py` file, switch each of the 6 themes in Preferences → text recolors, editor background/gutter/caret/selection update; set OS to Dark while `system` is selected → editor flips to Atom One Dark within a second.

- [ ] **Step 5: Commit** —
```
git add winapp/CodeCrack.App.Core/Editor/EditorThemeSpec.cs winapp/CodeCrack.App.Core/Editor/EditorThemeRegistry.cs winapp/CodeCrackApp/Editor/ThemeApplier.cs winapp/CodeCrackApp/Services/WindowsTheme.cs winapp/CodeCrack.Tests/EditorThemeRegistryTests.cs
git commit -m "feat(editor): 6-theme registry + ApplyTheme + Windows light/dark follow"
```

---

### Task 2.4: `OpenDocument` + multi-tab close-neighbor rule + bounds-checked caret/selection restore

**Files:**
- Create: `winapp/CodeCrack.App.Core/Editor/OpenDocument.cs`
- Create: `winapp/CodeCrack.App.Core/Editor/TabManagement.cs`
- Test: `winapp/CodeCrack.Tests/TabManagementTests.cs`

**Interfaces:**
- Consumes: `EditorGeometry.ClampSelection`/`ClampOffset` (Task 2.1).
- Produces: `OpenDocument` (fields per contract); `TabManagement.SelectAfterClose(int count, int closingIndex)` → `int?` (post-removal selected index; `null` = no tabs left); `TabManagement.RestoreCursor(int textLength, OpenDocument doc)` → `(int Caret, int Start, int Length)`.

- [ ] **Step 1: Write the failing test** — `winapp/CodeCrack.Tests/TabManagementTests.cs`:
```csharp
using CodeCrack.App.Core.Editor;
using Xunit;

namespace CodeCrack.Tests;

public class TabManagementTests
{
    [Theory]
    [InlineData(3, 0, 0)]   // close first → left of it is none → right neighbor becomes index 0
    [InlineData(3, 1, 0)]   // close middle → prefer left → index 0
    [InlineData(3, 2, 1)]   // close last → prefer left → index 1
    public void SelectAfterClose_PrefersLeftElseRight(int count, int closing, int expected)
        => Assert.Equal(expected, TabManagement.SelectAfterClose(count, closing));

    [Fact]
    public void SelectAfterClose_LastTabLeavesNothing()
        => Assert.Null(TabManagement.SelectAfterClose(count: 1, closingIndex: 0));

    [Fact]
    public void RestoreCursor_ClampsAgainstShrunkText()
    {
        var doc = new OpenDocument
        {
            Path = @"C:\x.py", Text = "abc", CaretOffset = 99, Selection = (50, 40),
        };
        var (caret, start, length) = TabManagement.RestoreCursor(textLength: 3, doc);
        Assert.Equal(3, caret);   // clamped to end
        Assert.Equal(3, start);   // selection start clamped
        Assert.Equal(0, length);  // run past end collapses
    }

    [Fact]
    public void OpenDocument_NameIsFileName()
        => Assert.Equal("x.py", new OpenDocument { Path = @"C:\dir\x.py" }.Name);
}
```

- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp/CodeCrack.Tests --filter FullyQualifiedName~TabManagementTests`. Expected: `CS0246: 'OpenDocument' / 'TabManagement' could not be found`.

- [ ] **Step 3: Implement** — `winapp/CodeCrack.App.Core/Editor/OpenDocument.cs` (fields exactly per contract):
```csharp
using System;

namespace CodeCrack.App.Core.Editor;

/// <summary>An open editor tab. One reused TextEditor swaps its Document per active OpenDocument.</summary>
public sealed class OpenDocument
{
    public string Path = string.Empty;
    public string Text = string.Empty;
    public bool IsDirty;
    public int CaretOffset;
    public (int Start, int Length) Selection;
    public DateTime LastWriteUtc;
    public string Name => System.IO.Path.GetFileName(Path);
}
```
`winapp/CodeCrack.App.Core/Editor/TabManagement.cs`:
```csharp
namespace CodeCrack.App.Core.Editor;

/// <summary>Pure tab-lifecycle rules: which tab to select on close, and safe cursor restore.</summary>
public static class TabManagement
{
    /// <summary>
    /// Index to select after removing <paramref name="closingIndex"/> from a list of
    /// <paramref name="count"/> tabs: prefer the left neighbour, else the right, else none.
    /// Returned index is relative to the reduced (count-1) list.
    /// </summary>
    public static int? SelectAfterClose(int count, int closingIndex)
    {
        if (count <= 1) return null;
        return closingIndex > 0 ? closingIndex - 1 : 0;
    }

    /// <summary>Bounds-checks a document's stored caret + selection against the live text length.</summary>
    public static (int Caret, int Start, int Length) RestoreCursor(int textLength, OpenDocument doc)
    {
        int caret = EditorGeometry.ClampOffset(textLength, doc.CaretOffset);
        var (start, length) = EditorGeometry.ClampSelection(textLength, doc.Selection.Start, doc.Selection.Length);
        return (caret, start, length);
    }
}
```
**View wiring (manual-verify only — no unit test):** in `CodeEditorControl`, add a method to swap the active document. Append to `winapp/CodeCrackApp/Editor/CodeEditorControl.xaml.cs`:
```csharp
    /// <summary>Swaps the active tab: replaces the editor Document, then restores caret/selection
    /// deferred so AvalonEdit has laid out the new document before we scroll into view.</summary>
    public void SwapDocument(CodeCrack.App.Core.Editor.OpenDocument doc)
    {
        _suppressTextChanged = true;
        Editor.Document = new ICSharpCode.AvalonEdit.Document.TextDocument(doc.Text);
        _suppressTextChanged = false;
        Editor.Document.Changed += (_, e) =>
        {
            var line = Editor.Document.GetLineByOffset(e.Offset);
            // colorizer invalidation handled by TextMateInstallation's own Document.Changed hook
        };
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var (caret, start, length) =
                CodeCrack.App.Core.Editor.TabManagement.RestoreCursor(Editor.Document.TextLength, doc);
            Editor.CaretOffset = caret;
            Editor.Select(start, length);
            Editor.TextArea.Caret.BringCaretToView();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
```
**Header template (manual-verify):** the dirty-dot→hover-x tab header is a WPF `DataTemplate` bound to `OpenDocument.IsDirty`/`Name`; it is authored in Phase 4's `MainWindow` `TabControl`. Note it here so the port isn't dropped:
```xml
<!-- Reference snippet for Phase 4 TabControl.ItemTemplate:
<DataTemplate>
  <StackPanel Orientation="Horizontal">
    <TextBlock Text="{Binding Name}"/>
    <TextBlock Text="●" Visibility="{Binding IsDirty, Converter={StaticResource BoolToVis}}"/>
    <Button Content="×" Style="{StaticResource TabCloseButton}"/>  <!-- shown on hover -->
  </StackPanel>
</DataTemplate>
-->
```

- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp/CodeCrack.Tests --filter FullyQualifiedName~TabManagementTests`.

- [ ] **Step 5: Commit** —
```
git add winapp/CodeCrack.App.Core/Editor/OpenDocument.cs winapp/CodeCrack.App.Core/Editor/TabManagement.cs winapp/CodeCrackApp/Editor/CodeEditorControl.xaml.cs winapp/CodeCrack.Tests/TabManagementTests.cs
git commit -m "feat(editor): OpenDocument + close-neighbor rule + bounds-checked cursor restore"
```

---

### Task 2.5: `FileIO` atomic UTF-8-no-BOM write + read

**Files:**
- Create: `winapp/CodeCrack.App.Core/Services/IFileIO.cs`
- Create: `winapp/CodeCrack.App.Core/Services/FileIO.cs`
- Test: `winapp/CodeCrack.Tests/FileIOTests.cs`

**Interfaces:**
- Produces: `IFileIO { string Read(string path); DateTime AtomicWrite(string path, string text); }`; `FileIO : IFileIO`.

- [ ] **Step 1: Write the failing test** — `winapp/CodeCrack.Tests/FileIOTests.cs`:
```csharp
using System;
using System.IO;
using System.Text;
using CodeCrack.App.Core.Services;
using Xunit;

namespace CodeCrack.Tests;

public class FileIOTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccio_" + Guid.NewGuid().ToString("N"));
    public FileIOTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void AtomicWrite_WritesUtf8WithoutBom()
    {
        var io = new FileIO();
        string path = Path.Combine(_dir, "a.py");
        io.AtomicWrite(path, "print('héllo')\n");

        byte[] bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("print('héllo')\n", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void AtomicWrite_ReturnsFreshLastWriteUtc()
    {
        var io = new FileIO();
        string path = Path.Combine(_dir, "b.txt");
        DateTime returned = io.AtomicWrite(path, "x");
        Assert.Equal(File.GetLastWriteTimeUtc(path), returned);
        Assert.Equal(DateTimeKind.Utc, returned.Kind);
    }

    [Fact]
    public void AtomicWrite_OverwritesExistingAtomically()
    {
        var io = new FileIO();
        string path = Path.Combine(_dir, "c.txt");
        io.AtomicWrite(path, "old contents");
        io.AtomicWrite(path, "new");
        Assert.Equal("new", io.Read(path));
        // no leftover temp files
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Read_MissingFileReturnsEmpty()
        => Assert.Equal(string.Empty, new FileIO().Read(Path.Combine(_dir, "nope.txt")));
}
```

- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp/CodeCrack.Tests --filter FullyQualifiedName~FileIOTests`. Expected: `CS0246: 'FileIO' could not be found`.

- [ ] **Step 3: Implement** — `winapp/CodeCrack.App.Core/Services/IFileIO.cs`:
```csharp
using System;

namespace CodeCrack.App.Core.Services;

/// <summary>File read/write for editor buffers. Writes are atomic UTF-8 (no BOM).</summary>
public interface IFileIO
{
    /// <summary>Reads UTF-8 text; returns empty string if the file is missing or unreadable.</summary>
    string Read(string path);

    /// <summary>Atomically writes UTF-8 (no BOM) via temp+replace; returns the new LastWriteTimeUtc.</summary>
    DateTime AtomicWrite(string path, string text);
}
```
`winapp/CodeCrack.App.Core/Services/FileIO.cs`:
```csharp
using System;
using System.IO;
using System.Text;

namespace CodeCrack.App.Core.Services;

public sealed class FileIO : IFileIO
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path, Utf8NoBom) : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    public DateTime AtomicWrite(string path, string text)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(dir);
        string temp = Path.Combine(dir, "." + Guid.NewGuid().ToString("N") + ".tmp");

        File.WriteAllText(temp, text, Utf8NoBom);
        try
        {
            if (File.Exists(path))
                File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temp, path);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
        return File.GetLastWriteTimeUtc(path);
    }
}
```

- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp/CodeCrack.Tests --filter FullyQualifiedName~FileIOTests`.

- [ ] **Step 5: Commit** —
```
git add winapp/CodeCrack.App.Core/Services/IFileIO.cs winapp/CodeCrack.App.Core/Services/FileIO.cs winapp/CodeCrack.Tests/FileIOTests.cs
git commit -m "feat(io): atomic UTF-8-no-BOM FileIO with empty-fallback read"
```

---

### Task 2.6: `RevealLine` click-to-line wiring (re-firable)

**Files:**
- Create: `winapp/CodeCrack.App.Core/Editor/RevealTarget.cs`
- Modify: `winapp/CodeCrackApp/Editor/CodeEditorControl.xaml.cs` (already has `RevealLine` from 2.1 — add a `RevealLineRequested` re-firable entry point)
- Test: `winapp/CodeCrack.Tests/RevealTargetTests.cs`

**Interfaces:**
- Consumes: `EditorGeometry.ClampLine` (Task 2.1); `IEditorHost.RevealLine(int)` (Task 2.1).
- Produces: `RevealTarget.Resolve(int lineCount, int requestedLine1)` → `int` (clamped, always fires even when unchanged); confirms `RevealLine` is not guarded on the previous value.

- [ ] **Step 1: Write the failing test** — the *re-firable* contract lives in a pure resolver (the mac behavior: selecting the same issue twice must still scroll). `winapp/CodeCrack.Tests/RevealTargetTests.cs`:
```csharp
using CodeCrack.App.Core.Editor;
using Xunit;

namespace CodeCrack.Tests;

public class RevealTargetTests
{
    [Theory]
    [InlineData(100, 40, 40)]
    [InlineData(100, 0, 1)]
    [InlineData(100, 999, 100)]
    [InlineData(0, 5, 1)]      // empty doc → line 1
    public void Resolve_ClampsToDocument(int lineCount, int req, int expected)
        => Assert.Equal(expected, RevealTarget.Resolve(lineCount, req));

    [Fact]
    public void Resolve_IsPure_SameInputSameOutput_NoStateGuard()
    {
        // Called twice with the same value it must yield the same target both times
        // (the control must NOT early-return on an unchanged line — issue re-click re-scrolls).
        int first = RevealTarget.Resolve(100, 42);
        int second = RevealTarget.Resolve(100, 42);
        Assert.Equal(42, first);
        Assert.Equal(42, second);
    }
}
```

- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp/CodeCrack.Tests --filter FullyQualifiedName~RevealTargetTests`. Expected: `CS0103: 'RevealTarget' does not exist`.

- [ ] **Step 3: Implement** — `winapp/CodeCrack.App.Core/Editor/RevealTarget.cs`:
```csharp
namespace CodeCrack.App.Core.Editor;

/// <summary>
/// Resolves the 1-indexed line a click-to-line request should reveal. Stateless by design:
/// the caller must invoke RevealLine every time (no "unchanged value" guard), so re-clicking
/// the same issue re-scrolls — matching the macOS behavior.
/// </summary>
public static class RevealTarget
{
    public static int Resolve(int lineCount, int requestedLine1)
        => EditorGeometry.ClampLine(lineCount, requestedLine1);
}
```
Add a re-firable entry point to `winapp/CodeCrackApp/Editor/CodeEditorControl.xaml.cs` (append inside the class). It calls the existing `RevealLine` unconditionally — no field caching the last line:
```csharp
    /// <summary>Click-to-line handler wired from IssuesPanel/TestsPanel item selection.
    /// Always reveals (no guard on the previous line) so re-selecting the same issue re-scrolls.</summary>
    public void RevealLineRequested(int line1Indexed)
    {
        int target = CodeCrack.App.Core.Editor.RevealTarget.Resolve(Editor.Document.LineCount, line1Indexed);
        RevealLine(target);
    }
```

- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp/CodeCrack.Tests --filter FullyQualifiedName~RevealTargetTests`.
  **Manual verify (Windows, after Phase 3 wires Issues panel):** click an issue at line 120 → editor scrolls, selects, and focuses that line. Click a *different* issue then click the same line-120 issue again → it re-scrolls both times (does not no-op on the repeat).

- [ ] **Step 5: Commit** —
```
git add winapp/CodeCrack.App.Core/Editor/RevealTarget.cs winapp/CodeCrackApp/Editor/CodeEditorControl.xaml.cs winapp/CodeCrack.Tests/RevealTargetTests.cs
git commit -m "feat(editor): re-firable click-to-line RevealLine wiring"
```

---

**Phase 2 exit check (run before Phase 3):**
```
dotnet build winapp/CodeCrack.sln -c Release
dotnet test winapp/CodeCrack.Tests
```
Expected: solution builds with `TreatWarningsAsErrors=true`; all Core tests (`EditorGeometry`, `LanguageMap`, `EditorThemeRegistry`, `TabManagement`, `FileIO`, `RevealTarget`) green. Core stays free of AvalonEdit/TextMateSharp (verify: `dotnet build winapp/CodeCrack.App.Core -c Release` succeeds on Linux CI). AvalonEdit-hosting view members (`CodeEditorControl`, `TextMateColorizer`, `ThemeApplier`, `WindowsTheme`, tab swap, click-to-line) covered by the per-task manual-verify steps, which Phase 3/4 exercise once a window hosts the control.

Sources: [WPF AvalonEdit (icsharpcode)](https://github.com/icsharpcode/AvalonEdit), [AvaloniaEdit.TextMate colorizer reference](https://github.com/AvaloniaUI/AvaloniaEdit/blob/master/src/AvaloniaEdit.TextMate/TextMateColoringTransformer.cs), [TextMateSharp (danipen)](https://github.com/danipen/TextMateSharp), [AvalonEdit NuGet](https://www.nuget.org/packages/avalonedit).

---

## Phase 3: Analyze / Issues / Tests / Run loop

> **Context for the executing engineer.** Repo root is `C:\Users\samet\CodeCrack`. All C# lives under `winapp\`: `CodeCrack.App.Core\` (net8.0, no WPF), `CodeCrackApp\` (net8.0-windows, WPF views), `CodeCrack.Tests\` (net8.0, xUnit). The solution is `winapp\CodeCrack.sln`. Everything below is on branch `windows-port`. Run all commands from the repo root unless stated. Types named in the CANONICAL INTERFACE CONTRACT (e.g. `Finding`, `GeneratedTest`, `Summary`, `AnalysisResult`, `IAnalyzer`, `EngineOutcome`, `AnalyzerError`/`EngineNotFound`, `StatusBus`, `IFileIO`, `OpenDocument`, `IEditorHost`, `EditorThemeSpec`, `PythonInvocation`, `RunCommand`) were created in Phases 1–2 and already build. This phase only *consumes* them and adds the ViewModels, the run table, the Runner, and the WPF panels.

---

### Task 3.1: MainViewModel + EditorViewModel + Issues/Tests VMs — analyze wiring, status strings, CanExecute

**Files:**
- Create `winapp\CodeCrack.App.Core\ViewModels\ObservableObject.cs`
- Create `winapp\CodeCrack.App.Core\ViewModels\EditorViewModel.cs`
- Create `winapp\CodeCrack.App.Core\ViewModels\IssuesViewModel.cs`
- Create `winapp\CodeCrack.App.Core\ViewModels\TestsViewModel.cs`
- Create `winapp\CodeCrack.App.Core\ViewModels\MainViewModel.cs`
- Create (test fakes) `winapp\CodeCrack.Tests\Fakes\FakeEditorHost.cs`, `winapp\CodeCrack.Tests\Fakes\FakeAnalyzer.cs`, `winapp\CodeCrack.Tests\Fakes\FakeFileIO.cs`
- Test `winapp\CodeCrack.Tests\ViewModelTests.cs`

**Interfaces:**
Consumes: `IAnalyzer.AnalyzeAsync(string, CancellationToken) : Task<EngineOutcome>`; `EngineOutcome(AnalysisResult? Result, AnalyzerError? Error){ bool IsSuccess }`; `AnalysisResult(List<Finding>, List<GeneratedTest>, Summary)`; `Summary.Findings/.Tests/.Executed/.Reproduced`; `Finding.Line/.Severity/.Id`; `StatusBus.Set(string)/.Text`; `IFileIO.AtomicWrite(string,string) : DateTime`; `IEditorHost` (Text/CaretOffset/RevealLine/SetLanguageByPath); `OpenDocument{ Path; Text; IsDirty; CaretOffset; LastWriteUtc; Name }`; `IAppSettings`.
Produces: `ObservableObject`; `EditorViewModel`; `IssuesViewModel{ ObservableCollection<Finding> Findings; SetFindings(); static SeverityRank(); event Action<int> LineActivated; Activate(Finding); string? ErrorMessage }`; `TestsViewModel{ ObservableCollection<GeneratedTest> Tests; SetResult(); Summary?; int ReproducedCount; string Headline; event Action<string> FindingActivated; JumpToFinding() }`; `MainViewModel{ IEditorHost? EditorHost; ObservableCollection<OpenDocument> Documents; OpenDocument? Active; EditorViewModel Editor; IssuesViewModel Issues; TestsViewModel Tests; bool CanSave/CanRun/CanAnalyze; bool IsAnalyzing; bool ShowIssues; string Save(); Task AnalyzeAsync(); void RevealLine(int) }`; test fakes `FakeEditorHost`, `FakeAnalyzer`, `FakeFileIO`.

- [ ] **Step 1: Write the failing test** — create `winapp\CodeCrack.Tests\Fakes\FakeEditorHost.cs`, `FakeAnalyzer.cs`, `FakeFileIO.cs`, then `ViewModelTests.cs`.

`winapp\CodeCrack.Tests\Fakes\FakeEditorHost.cs`:
```csharp
using System;
using CodeCrack.App.Core.Editor;

namespace CodeCrack.Tests.Fakes;

public sealed class FakeEditorHost : IEditorHost
{
    public string Text { get; set; } = "";
    public int CaretOffset { get; set; }
    public (int Start, int Length) Selection { get; set; }

    public int RevealedLine { get; private set; } = -1;
    public string? LanguagePath { get; private set; }
    public EditorThemeSpec? AppliedTheme { get; private set; }
    public int FocusCount { get; private set; }

    public void RevealLine(int line1Indexed) => RevealedLine = line1Indexed;
    public void SetLanguageByPath(string filePath) => LanguagePath = filePath;
    public void ApplyTheme(EditorThemeSpec theme) => AppliedTheme = theme;
    public void FocusEditor() => FocusCount++;

    public event EventHandler? TextChanged;
    public void RaiseTextChanged() => TextChanged?.Invoke(this, EventArgs.Empty);
}
```

`winapp\CodeCrack.Tests\Fakes\FakeAnalyzer.cs`:
```csharp
using System.Threading;
using System.Threading.Tasks;
using CodeCrack.App.Core.Engine;

namespace CodeCrack.Tests.Fakes;

public sealed class FakeAnalyzer : IAnalyzer
{
    public EngineOutcome Next { get; set; } = new(null, new EngineNotFound("none"));
    public string? LastPath { get; private set; }

    public Task<EngineOutcome> AnalyzeAsync(string filePath, CancellationToken ct = default)
    {
        LastPath = filePath;
        return Task.FromResult(Next);
    }
}
```

`winapp\CodeCrack.Tests\Fakes\FakeFileIO.cs`:
```csharp
using System;
using System.Collections.Generic;
using CodeCrack.App.Core.Services;

namespace CodeCrack.Tests.Fakes;

public sealed class FakeFileIO : IFileIO
{
    public Dictionary<string, string> Files { get; } = new();
    public DateTime NextWriteUtc { get; set; } = new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc);

    public string Read(string path) => Files.TryGetValue(path, out var t) ? t : "";
    public DateTime AtomicWrite(string path, string text) { Files[path] = text; return NextWriteUtc; }
}
```

`winapp\CodeCrack.Tests\ViewModelTests.cs`:
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeCrack.App.Core.Engine;
using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.Services;
using CodeCrack.App.Core.ViewModels;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public sealed class ViewModelTests
{
    private static (MainViewModel vm, FakeAnalyzer analyzer, StatusBus status, FakeFileIO io, FakeEditorHost host)
        Build()
    {
        var analyzer = new FakeAnalyzer();
        var status = new StatusBus();
        var io = new FakeFileIO();
        var vm = new MainViewModel(analyzer, new StubSettings(), status, io);
        var host = new FakeEditorHost();
        vm.EditorHost = host;
        return (vm, analyzer, status, io, host);
    }

    private static OpenDocument Doc(string path, string text = "print(1)\n") =>
        new() { Path = path, Text = text, IsDirty = true };

    private static Finding F(string id, int line, string sev) =>
        new(id, "kind", "target", new[] { line }, "why", sev);

    private static Summary Sum(int findings) => new(findings, findings, findings, 0,
        new OutcomeCounts(0, 0, 0, 0));

    [Fact]
    public async Task Analyze_success_sets_status_with_pluralized_count()
    {
        var (vm, analyzer, status, _, _) = Build();
        vm.Active = Doc(@"C:\proj\bug.py");
        analyzer.Next = new EngineOutcome(
            new AnalysisResult(new List<Finding> { F("f1", 3, "high") },
                               new List<GeneratedTest>(), Sum(1)), null);

        await vm.AnalyzeAsync();

        Assert.True(vm.ShowIssues);
        Assert.False(vm.IsAnalyzing);
        Assert.Single(vm.Issues.Findings);
        Assert.Equal("Analysis found 1 issue in bug.py", status.Text);
    }

    [Fact]
    public async Task Analyze_zero_findings_uses_plural_s()
    {
        var (vm, analyzer, status, _, _) = Build();
        vm.Active = Doc(@"C:\proj\bug.py");
        analyzer.Next = new EngineOutcome(
            new AnalysisResult(new List<Finding>(), new List<GeneratedTest>(), Sum(0)), null);

        await vm.AnalyzeAsync();

        Assert.Equal("Analysis found 0 issues in bug.py", status.Text);
    }

    [Fact]
    public async Task Analyze_error_renders_message_and_failed_status()
    {
        var (vm, analyzer, status, _, _) = Build();
        vm.Active = Doc(@"C:\proj\bug.py");
        analyzer.Next = new EngineOutcome(null, new EngineNotFound(@"C:\proj\engine"));

        await vm.AnalyzeAsync();

        Assert.Equal("Analysis failed", status.Text);
        Assert.NotNull(vm.Issues.ErrorMessage);
        Assert.Empty(vm.Issues.Findings);
    }

    [Fact]
    public void CanExecute_predicates_track_active_and_language()
    {
        var (vm, _, _, _, _) = Build();
        Assert.False(vm.CanSave);
        Assert.False(vm.CanRun);
        Assert.False(vm.CanAnalyze);

        vm.Active = Doc(@"C:\proj\notes.txt");
        Assert.True(vm.CanSave);
        Assert.True(vm.CanRun);
        Assert.False(vm.CanAnalyze); // non-Python

        vm.Active = Doc(@"C:\proj\bug.py");
        Assert.True(vm.CanAnalyze);
    }

    [Fact]
    public void Save_writes_via_io_and_status()
    {
        var (vm, _, status, io, host) = Build();
        host.Text = "edited body\n";
        vm.Active = Doc(@"C:\proj\bug.py", "old\n");

        var msg = vm.Save();

        Assert.Equal("Saved bug.py", msg);
        Assert.Equal("Saved bug.py", status.Text);
        Assert.Equal("edited body\n", io.Files[@"C:\proj\bug.py"]);
        Assert.False(vm.Active!.IsDirty);
    }

    private sealed class StubSettings : IAppSettings
    {
        public string EnginePathOverride { get; set; } = "";
        public int FontSize { get; set; } = 13;
        public string EditorTheme { get; set; } = "system";
        public bool IndentUsesSpaces { get; set; } = true;
        public int IndentWidth { get; set; } = 4;
        public string ClaudeApiKey { get; set; } = "";
        public void Save() { }
    }
}
```

- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp\CodeCrack.Tests\CodeCrack.Tests.csproj --filter "FullyQualifiedName~ViewModelTests"`. Expect compile errors: `The type or namespace name 'MainViewModel' / 'ObservableObject' / 'EditorViewModel' / 'IssuesViewModel' / 'TestsViewModel' could not be found`.

- [ ] **Step 3: Implement** — create the five ViewModel files.

`winapp\CodeCrack.App.Core\ViewModels\ObservableObject.cs`:
```csharp
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CodeCrack.App.Core.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

`winapp\CodeCrack.App.Core\ViewModels\EditorViewModel.cs`:
```csharp
using System;
using CodeCrack.App.Core.Editor;

namespace CodeCrack.App.Core.ViewModels;

/// Owns the live editor buffer <-> active document sync. The AvalonEdit-hosting control is
/// injected as Host; headless tests inject a fake. No WPF types here.
public sealed class EditorViewModel : ObservableObject
{
    public IEditorHost? Host { get; set; }

    private OpenDocument? _document;
    public OpenDocument? Document
    {
        get => _document;
        set { if (Set(ref _document, value)) LoadIntoHost(); }
    }

    private void LoadIntoHost()
    {
        if (Host is null || _document is null) return;
        Host.Text = _document.Text;
        Host.SetLanguageByPath(_document.Path);
        Host.CaretOffset = Math.Min(_document.CaretOffset, _document.Text.Length);
    }

    /// Pull the current buffer text/caret back into the active document (called before save/analyze/run).
    public void SyncFromHost()
    {
        if (Host is null || _document is null) return;
        _document.Text = Host.Text;
        _document.CaretOffset = Host.CaretOffset;
    }
}
```

`winapp\CodeCrack.App.Core\ViewModels\IssuesViewModel.cs`:
```csharp
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using CodeCrack.App.Core.Engine;

namespace CodeCrack.App.Core.ViewModels;

public sealed class IssuesViewModel : ObservableObject
{
    public ObservableCollection<Finding> Findings { get; } = new();

    private string? _errorMessage;
    public string? ErrorMessage { get => _errorMessage; set => Set(ref _errorMessage, value); }

    /// Raised when a finding row is activated; MainViewModel maps it to editor RevealLine.
    public event Action<int>? LineActivated;

    /// high before medium before anything else (low/unknown). LINQ OrderBy is stable,
    /// so equal-severity findings keep engine order.
    public static int SeverityRank(string severity) => severity.ToLowerInvariant() switch
    {
        "high" => 0,
        "medium" => 1,
        _ => 2,
    };

    public void SetFindings(IEnumerable<Finding> findings)
    {
        Findings.Clear();
        foreach (var f in findings.OrderBy(f => SeverityRank(f.Severity)))
            Findings.Add(f);
    }

    public void Activate(Finding finding) => LineActivated?.Invoke(finding.Line);
}
```

`winapp\CodeCrack.App.Core\ViewModels\TestsViewModel.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CodeCrack.App.Core.Engine;

namespace CodeCrack.App.Core.ViewModels;

public sealed class TestsViewModel : ObservableObject
{
    public ObservableCollection<GeneratedTest> Tests { get; } = new();

    private Summary? _summary;
    public Summary? Summary
    {
        get => _summary;
        private set
        {
            if (Set(ref _summary, value))
            {
                Raise(nameof(ReproducedCount));
                Raise(nameof(Headline));
                Raise(nameof(HasSummary));
            }
        }
    }

    public bool HasSummary => _summary is not null;
    public int ReproducedCount => _summary?.Reproduced ?? 0;

    /// Load-bearing headline: how many generated tests reproduced a real failure
    /// (engine's summary.reproduced — never re-derived).
    public string Headline
    {
        get
        {
            if (_summary is null) return "";
            int n = _summary.Reproduced;
            return $"{n} test{(n == 1 ? "" : "s")} reproduce a real failure";
        }
    }

    public event Action<string>? FindingActivated;

    public void SetResult(IReadOnlyList<GeneratedTest> tests, Summary? summary)
    {
        Tests.Clear();
        foreach (var t in tests) Tests.Add(t);
        Summary = summary;
    }

    public void JumpToFinding(GeneratedTest test) => FindingActivated?.Invoke(test.FindingId);
}
```

`winapp\CodeCrack.App.Core\ViewModels\MainViewModel.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeCrack.App.Core.Engine;
using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.Services;

namespace CodeCrack.App.Core.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IAnalyzer _analyzer;
    private readonly IAppSettings _settings;
    private readonly StatusBus _status;
    private readonly IFileIO _io;

    public MainViewModel(IAnalyzer analyzer, IAppSettings settings, StatusBus status, IFileIO io)
    {
        _analyzer = analyzer;
        _settings = settings;
        _status = status;
        _io = io;
        Issues.LineActivated += RevealLine;
        Tests.FindingActivated += RevealFinding;
    }

    public EditorViewModel Editor { get; } = new();
    public IssuesViewModel Issues { get; } = new();
    public TestsViewModel Tests { get; } = new();

    /// The AvalonEdit-hosting control (set by the view). Forwarded to the EditorViewModel.
    public IEditorHost? EditorHost
    {
        get => Editor.Host;
        set => Editor.Host = value;
    }

    public ObservableCollection<OpenDocument> Documents { get; } = new();

    private OpenDocument? _active;
    public OpenDocument? Active
    {
        get => _active;
        set
        {
            if (!Set(ref _active, value)) return;
            Editor.Document = value;
            Raise(nameof(CanSave));
            Raise(nameof(CanRun));
            Raise(nameof(CanAnalyze));
        }
    }

    private bool _isAnalyzing;
    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set { if (Set(ref _isAnalyzing, value)) Raise(nameof(CanAnalyze)); }
    }

    public bool ShowIssues { get; private set; }

    public bool CanSave => Active is not null;
    public bool CanRun => Active is not null;
    public bool CanAnalyze => Active is not null && !IsAnalyzing && IsPython;

    private bool IsPython
    {
        get
        {
            if (Active is null) return false;
            var ext = Path.GetExtension(Active.Path).TrimStart('.').ToLowerInvariant();
            return ext == "py" || ext == "pyw";
        }
    }

    public void RevealLine(int line1Indexed) => EditorHost?.RevealLine(line1Indexed);

    private void RevealFinding(string findingId)
    {
        var f = Issues.Findings.FirstOrDefault(x => x.Id == findingId);
        if (f is not null) RevealLine(f.Line);
    }

    /// Atomic UTF-8 (no BOM) save via IFileIO; clears dirty and refreshes stored mtime.
    public string Save()
    {
        if (Active is null) return "";
        Editor.SyncFromHost();
        Active.LastWriteUtc = _io.AtomicWrite(Active.Path, Active.Text);
        Active.IsDirty = false;
        var msg = $"Saved {Active.Name}";
        _status.Set(msg);
        return msg;
    }

    /// Save the current file, run the engine, and surface findings/tests.
    public async Task AnalyzeAsync(CancellationToken ct = default)
    {
        if (Active is null) return;
        Save(); // engine reads from disk
        var name = Active.Name;
        ShowIssues = true;
        Issues.ErrorMessage = null;
        IsAnalyzing = true;
        _status.Set($"Analyzing {name}\u2026");

        var outcome = await _analyzer.AnalyzeAsync(Active.Path, ct).ConfigureAwait(true);

        IsAnalyzing = false;
        if (outcome.IsSuccess && outcome.Result is not null)
        {
            Issues.SetFindings(outcome.Result.Findings);
            Tests.SetResult(outcome.Result.Tests, outcome.Result.Summary);
            Issues.ErrorMessage = null;
            int n = outcome.Result.Summary.Findings;
            _status.Set($"Analysis found {n} issue{(n == 1 ? "" : "s")} in {name}");
        }
        else
        {
            Issues.SetFindings(Array.Empty<Finding>());
            Tests.SetResult(Array.Empty<GeneratedTest>(), null);
            Issues.ErrorMessage = outcome.Error?.Message;
            _status.Set("Analysis failed");
        }
    }
}
```

- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp\CodeCrack.Tests\CodeCrack.Tests.csproj --filter "FullyQualifiedName~ViewModelTests"`.

- [ ] **Step 5: Commit** — `git add winapp/CodeCrack.App.Core/ViewModels/ winapp/CodeCrack.Tests/Fakes/ winapp/CodeCrack.Tests/ViewModelTests.cs` then `git commit -m "feat(core): MainViewModel analyze wiring, EditorViewModel, Issues/Tests VMs"`.

---

### Task 3.2: IssuesViewModel severity sort + click-to-line + IssuesPanel view

**Files:**
- Modify `winapp\CodeCrack.Tests\ViewModelTests.cs` (add issues facts)
- Create `winapp\CodeCrackApp\Panels\IssuesPanel.xaml`
- Create `winapp\CodeCrackApp\Panels\IssuesPanel.xaml.cs`

**Interfaces:**
Consumes: `IssuesViewModel.SetFindings()/.SeverityRank()/.Findings/.Activate()/.LineActivated`; `MainViewModel.Issues/.EditorHost/.RevealLine`; `Finding.Severity/.Line/.Kind/.Rationale`; `FakeEditorHost.RevealedLine`.
Produces: `CodeCrackApp.Panels.IssuesPanel` (WPF UserControl bound to `IssuesViewModel`).

- [ ] **Step 1: Write the failing test** — append to `ViewModelTests.cs`:
```csharp
    [Fact]
    public void Issues_are_sorted_high_medium_low_stable_within_severity()
    {
        var vm = new IssuesViewModel();
        vm.SetFindings(new[]
        {
            new Finding("a", "k", "t", new[] { 5 }, "r", "low"),
            new Finding("b", "k", "t", new[] { 6 }, "r", "high"),
            new Finding("c", "k", "t", new[] { 7 }, "r", "medium"),
            new Finding("d", "k", "t", new[] { 8 }, "r", "high"),
        });

        Assert.Equal(new[] { "b", "d", "c", "a" }, vm.Findings.Select(f => f.Id).ToArray());
    }

    [Fact]
    public void Activating_a_finding_reveals_its_line_through_the_editor_host()
    {
        var (vm, _, _, _, host) = Build();
        var finding = new Finding("f9", "k", "t", new[] { 42 }, "r", "high");
        vm.Issues.SetFindings(new[] { finding });

        vm.Issues.Activate(vm.Issues.Findings[0]);

        Assert.Equal(42, host.RevealedLine);
    }
```
Add `using System.Linq;` and `using CodeCrack.App.Core.ViewModels;` at the top of the test file if not already present.

- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp\CodeCrack.Tests\CodeCrack.Tests.csproj --filter "FullyQualifiedName~ViewModelTests"`. Expect the two new facts to fail only if wiring is wrong; if `SetFindings`/`Activate`/`LineActivated` from Task 3.1 are correct they may already pass. If `Activating_a_finding...` fails, it exposes a missing `Issues.LineActivated += RevealLine` subscription — fix in `MainViewModel` constructor (already present in 3.1). Expected initial failure is a compile error only if you skipped the `using System.Linq;` import.

- [ ] **Step 3: Implement** — the VM logic already exists from Task 3.1; this step adds the WPF view.

`winapp\CodeCrackApp\Panels\IssuesPanel.xaml`:
```xml
<UserControl x:Class="CodeCrackApp.Panels.IssuesPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:CodeCrack.App.Core.ViewModels;assembly=CodeCrack.App.Core"
             d:DataContext="{d:DesignInstance vm:IssuesViewModel}"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006">
    <DockPanel>
        <TextBlock DockPanel.Dock="Top" Margin="12,6" FontWeight="Bold"
                   Text="{Binding Findings.Count, StringFormat='Issues ({0})'}" />
        <TextBlock DockPanel.Dock="Top" Margin="12,6" Foreground="Red"
                   TextWrapping="Wrap" FontFamily="Consolas"
                   Text="{Binding ErrorMessage}"
                   Visibility="{Binding ErrorMessage, Converter={StaticResource NullToCollapsed}}" />
        <ListBox ItemsSource="{Binding Findings}" BorderThickness="0"
                 HorizontalContentAlignment="Stretch"
                 MouseDoubleClick="OnRowActivated" KeyDown="OnRowKeyDown">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <StackPanel Orientation="Horizontal" Margin="0,2">
                        <Border CornerRadius="7" Padding="6,1" VerticalAlignment="Center"
                                Background="{Binding Severity, Converter={StaticResource SeverityToBrush}}">
                            <TextBlock Text="{Binding Severity}" Foreground="White"
                                       FontSize="9" FontWeight="Bold" />
                        </Border>
                        <TextBlock Text="{Binding Line, StringFormat='L{0}'}" Margin="10,0"
                                   FontFamily="Consolas" Foreground="Gray" MinWidth="34" />
                        <StackPanel>
                            <TextBlock Text="{Binding Kind}" FontWeight="Bold" Foreground="Gray" FontSize="11" />
                            <TextBlock Text="{Binding Rationale}" TextWrapping="Wrap" />
                        </StackPanel>
                    </StackPanel>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
    </DockPanel>
</UserControl>
```

`winapp\CodeCrackApp\Panels\IssuesPanel.xaml.cs`:
```csharp
using System.Windows.Controls;
using System.Windows.Input;
using CodeCrack.App.Core.Engine;
using CodeCrack.App.Core.ViewModels;

namespace CodeCrackApp.Panels;

public partial class IssuesPanel : UserControl
{
    public IssuesPanel() => InitializeComponent();

    private void OnRowActivated(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is IssuesViewModel vm && ((ListBox)sender).SelectedItem is Finding f)
            vm.Activate(f);
    }

    private void OnRowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is IssuesViewModel vm &&
            ((ListBox)sender).SelectedItem is Finding f)
            vm.Activate(f);
    }
}
```

> The `SeverityToBrush` and `NullToCollapsed` value converters are declared once in `App.xaml` resources (Phase 4 owns `App.xaml`); for this phase add a placeholder `<local:SeverityToBrushConverter/>` resource in `IssuesPanel.xaml`'s `<UserControl.Resources>` if `App.xaml` does not yet exist, so the control loads standalone. The panel carries no logic — all behavior lives in `IssuesViewModel`, which the tests above cover.

- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp\CodeCrack.Tests\CodeCrack.Tests.csproj --filter "FullyQualifiedName~ViewModelTests"`. Then manual view check: `dotnet build winapp\CodeCrackApp\CodeCrackApp.csproj -c Debug` must succeed (view compiles); confirm no `TreatWarningsAsErrors` breakage.

- [ ] **Step 5: Commit** — `git add winapp/CodeCrack.Tests/ViewModelTests.cs winapp/CodeCrackApp/Panels/IssuesPanel.xaml winapp/CodeCrackApp/Panels/IssuesPanel.xaml.cs` then `git commit -m "feat(app): IssuesPanel + severity-sorted click-to-line issues VM tests"`.

---

### Task 3.3: TestsViewModel headline/BUG-PROVEN + jump-to-finding + TestsPanel view

**Files:**
- Modify `winapp\CodeCrack.Tests\ViewModelTests.cs` (add tests facts)
- Create `winapp\CodeCrackApp\Panels\TestsPanel.xaml`
- Create `winapp\CodeCrackApp\Panels\TestsPanel.xaml.cs`

**Interfaces:**
Consumes: `TestsViewModel.SetResult()/.Headline/.ReproducedCount/.Tests/.JumpToFinding()/.FindingActivated`; `GeneratedTest.Reproduced/.NeedsInput/.Outcome/.TestName/.FindingId/.Detail/.Stdout/.Source/.Duration`; `Summary.Reproduced/.Executed/.Tests`; `MainViewModel.Tests/.Issues`; `FakeEditorHost.RevealedLine`.
Produces: `CodeCrackApp.Panels.TestsPanel` (WPF UserControl bound to `TestsViewModel`).

- [ ] **Step 1: Write the failing test** — append to `ViewModelTests.cs`:
```csharp
    private static GeneratedTest T(string findingId, string name, bool reproduced,
        string? outcome, string expects) =>
        new(findingId, name, "def test(): assert False", expects, outcome,
            "traceback", "captured stdout", 0.12, reproduced);

    [Fact]
    public void Headline_and_reproduced_count_come_from_summary()
    {
        var vm = new TestsViewModel();
        vm.SetResult(
            new[] { T("f1", "test_a", true, "failed", "regression") },
            new Summary(2, 3, 3, 1, new OutcomeCounts(0, 1, 0, 0)));

        Assert.Equal(1, vm.ReproducedCount);
        Assert.Equal("1 test reproduces a real failure", vm.Headline);
    }

    [Fact]
    public void Headline_pluralizes_when_multiple_reproduced()
    {
        var vm = new TestsViewModel();
        vm.SetResult(System.Array.Empty<GeneratedTest>(),
            new Summary(0, 5, 5, 2, new OutcomeCounts(0, 2, 0, 0)));
        Assert.Equal("2 tests reproduce a real failure", vm.Headline);
    }

    [Fact]
    public void BugProven_binds_to_reproduced_and_NeedsInput_to_outcome_or_expects()
    {
        var proven = T("f1", "t1", reproduced: true, outcome: "failed", expects: "regression");
        var skipped = T("f2", "t2", reproduced: false, outcome: "skipped", expects: "raises");
        var normal = T("f3", "t3", reproduced: false, outcome: "passed", expects: "raises");

        Assert.True(proven.Reproduced);
        Assert.True(skipped.NeedsInput);   // outcome == "skipped"
        Assert.False(normal.NeedsInput);
    }

    [Fact]
    public void Jumping_from_a_test_reveals_the_matching_findings_line()
    {
        var (vm, _, _, _, host) = Build();
        vm.Issues.SetFindings(new[] { new Finding("f7", "k", "t", new[] { 99 }, "r", "high") });
        vm.Tests.SetResult(new[] { T("f7", "test_x", false, "passed", "raises") }, null);

        vm.Tests.JumpToFinding(vm.Tests.Tests[0]);

        Assert.Equal(99, host.RevealedLine);
    }
```

> Note: `Headline` in Task 3.1 produces `"1 test reproduce a real failure"` (matching the mac string which does not conjugate the verb). The assertion above expects `"1 test reproduces a real failure"`. Reconcile by making the mac-faithful choice explicit: **the mac app renders `"N test(s) reproduce a real failure"` verbatim** — so change the assertion to `"1 test reproduce a real failure"` to match the ported string, OR keep the conjugated form and update `TestsViewModel.Headline`. Pick the mac-verbatim form: edit the assertion to `Assert.Equal("1 test reproduce a real failure", vm.Headline);` and the plural fact stays `"2 tests reproduce a real failure"`.

- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp\CodeCrack.Tests\CodeCrack.Tests.csproj --filter "FullyQualifiedName~ViewModelTests"`. With the mac-verbatim assertion, `Headline_and_reproduced_count...` passes against Task 3.1 code; `Jumping_from_a_test...` fails if `Tests.FindingActivated += RevealFinding` wiring is missing — it is present from Task 3.1, so the facts pass once compiled. The genuine failing state here is the missing `TestsPanel` view type referenced in Step 3.

- [ ] **Step 3: Implement** — add the view (VM logic already exists).

`winapp\CodeCrackApp\Panels\TestsPanel.xaml`:
```xml
<UserControl x:Class="CodeCrackApp.Panels.TestsPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:CodeCrack.App.Core.ViewModels;assembly=CodeCrack.App.Core"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d"
             d:DataContext="{d:DesignInstance vm:TestsViewModel}">
    <DockPanel>
        <TextBlock DockPanel.Dock="Top" Margin="14,8" FontWeight="Bold"
                   Text="{Binding Headline}"
                   Visibility="{Binding HasSummary, Converter={StaticResource BoolToVisible}}" />
        <ListBox ItemsSource="{Binding Tests}" BorderThickness="0"
                 HorizontalContentAlignment="Stretch"
                 MouseDoubleClick="OnJump" KeyDown="OnJumpKey">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <Expander Header="{Binding TestName}">
                        <Expander.HeaderTemplate>
                            <DataTemplate>
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Text="{Binding}" FontFamily="Consolas" FontWeight="Bold" />
                                    <Border Margin="8,0" CornerRadius="7" Padding="6,1" Background="#2E7D32"
                                            Visibility="{Binding DataContext.Reproduced,
                                                RelativeSource={RelativeSource AncestorType=ContentPresenter},
                                                Converter={StaticResource BoolToVisible}}">
                                        <TextBlock Text="BUG PROVEN" Foreground="White" FontSize="9" FontWeight="Bold" />
                                    </Border>
                                    <Border Margin="4,0" CornerRadius="7" Padding="6,1" Background="#616161"
                                            Visibility="{Binding DataContext.NeedsInput,
                                                RelativeSource={RelativeSource AncestorType=ContentPresenter},
                                                Converter={StaticResource BoolToVisible}}">
                                        <TextBlock Text="needs input" Foreground="White" FontSize="9" />
                                    </Border>
                                </StackPanel>
                            </DataTemplate>
                        </Expander.HeaderTemplate>
                        <StackPanel Margin="20,4">
                            <TextBlock Text="TRACEBACK" FontSize="9" FontWeight="Bold" Foreground="Gray" />
                            <TextBlock Text="{Binding Detail}" FontFamily="Consolas" TextWrapping="Wrap" />
                            <TextBlock Text="STDOUT" FontSize="9" FontWeight="Bold" Foreground="Gray" Margin="0,6,0,0" />
                            <TextBlock Text="{Binding Stdout}" FontFamily="Consolas" TextWrapping="Wrap" />
                            <TextBlock Text="TEST SOURCE" FontSize="9" FontWeight="Bold" Foreground="Gray" Margin="0,6,0,0" />
                            <TextBlock Text="{Binding Source}" FontFamily="Consolas" TextWrapping="Wrap" />
                        </StackPanel>
                    </Expander>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
    </DockPanel>
</UserControl>
```

`winapp\CodeCrackApp\Panels\TestsPanel.xaml.cs`:
```csharp
using System.Windows.Controls;
using System.Windows.Input;
using CodeCrack.App.Core.Engine;
using CodeCrack.App.Core.ViewModels;

namespace CodeCrackApp.Panels;

public partial class TestsPanel : UserControl
{
    public TestsPanel() => InitializeComponent();

    private void OnJump(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is TestsViewModel vm && ((ListBox)sender).SelectedItem is GeneratedTest t)
            vm.JumpToFinding(t);
    }

    private void OnJumpKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is TestsViewModel vm &&
            ((ListBox)sender).SelectedItem is GeneratedTest t)
            vm.JumpToFinding(t);
    }
}
```

> `BoolToVisible` converter lives in `App.xaml` resources (Phase 4); add a local resource declaration inside `TestsPanel.xaml` if `App.xaml` is not present yet so the control compiles standalone. All behavior (headline text, reproduced count, BUG-PROVEN/needs-input flags, jump-to-finding) is covered by the `TestsViewModel` tests above.

- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp\CodeCrack.Tests\CodeCrack.Tests.csproj --filter "FullyQualifiedName~ViewModelTests"`, then `dotnet build winapp\CodeCrackApp\CodeCrackApp.csproj -c Debug`.

- [ ] **Step 5: Commit** — `git add winapp/CodeCrack.Tests/ViewModelTests.cs winapp/CodeCrackApp/Panels/TestsPanel.xaml winapp/CodeCrackApp/Panels/TestsPanel.xaml.cs` then `git commit -m "feat(app): TestsPanel + BUG-PROVEN/reproduced/jump tests VM tests"`.

---

### Task 3.4: RunCommandTable.For (Windows table) + RunCommand record + tests

**Files:**
- Create `winapp\CodeCrack.App.Core\Engine\RunCommand.cs`
- Test `winapp\CodeCrack.Tests\RunCommandTests.cs`

**Interfaces:**
Consumes: `PythonInvocation.Resolve() : (string Exe, string[] LeadingArgs)`.
Produces: `RunCommand(string Tool, string[] Args, string Cwd){ string Display }`; `RunCommandTable.For(string filePath) : RunCommand?`.

- [ ] **Step 1: Write the failing test** — `winapp\CodeCrack.Tests\RunCommandTests.cs`:
```csharp
using System.IO;
using System.Linq;
using CodeCrack.App.Core.Engine;
using Xunit;

namespace CodeCrack.Tests;

public sealed class RunCommandTests
{
    private static string P(string name) => Path.Combine(Path.GetTempPath(), name);

    [Fact]
    public void Unknown_extension_is_not_runnable()
    {
        Assert.Null(RunCommandTable.For(P("readme.md")));
        Assert.Null(RunCommandTable.For(P("data.csv")));
    }

    [Fact]
    public void Known_extensions_map_to_windows_tools()
    {
        Assert.Equal("node", RunCommandTable.For(P("a.js"))!.Tool);
        Assert.Equal("node", RunCommandTable.For(P("a.mjs"))!.Tool);
        Assert.Equal("ruby", RunCommandTable.For(P("a.rb"))!.Tool);
        Assert.Equal("go", RunCommandTable.For(P("a.go"))!.Tool);
        Assert.Equal(new[] { "run", "a.go" }, RunCommandTable.For(P("a.go"))!.Args);
        Assert.Equal("php", RunCommandTable.For(P("a.php"))!.Tool);
        Assert.Equal("perl", RunCommandTable.For(P("a.pl"))!.Tool);
        Assert.Equal("swift", RunCommandTable.For(P("a.swift"))!.Tool);
        Assert.Equal("powershell", RunCommandTable.For(P("a.ps1"))!.Tool);
    }

    [Fact]
    public void Python_uses_resolved_interpreter_not_python3_literal()
    {
        var cmd = RunCommandTable.For(P("bug.py"))!;
        Assert.Contains("bug.py", cmd.Args);
        Assert.NotEqual("python3", cmd.Tool);
        Assert.DoesNotContain("/usr/bin/env", cmd.Display);
    }

    [Fact]
    public void Java_uses_JAVA_HOME_or_path_never_posix_java_home_or_bash()
    {
        var cmd = RunCommandTable.For(P("Main.java"))!;
        Assert.Equal("cmd", cmd.Tool);
        Assert.Contains("javac", cmd.Display);
        Assert.Contains("Main", cmd.Display); // base class name
        Assert.DoesNotContain("java_home", cmd.Display);   // no /usr/libexec/java_home
        Assert.DoesNotContain("bash", cmd.Display);
        Assert.DoesNotContain("-lc", cmd.Display);
    }

    [Fact]
    public void No_command_carries_posix_leftovers()
    {
        var exts = new[] { "bug.py", "a.js", "a.mjs", "a.cjs", "a.rb",
                           "a.swift", "a.go", "a.php", "a.pl", "Main.java", "a.ps1" };
        foreach (var name in exts)
        {
            var cmd = RunCommandTable.For(P(name))!;
            Assert.DoesNotContain("/usr/bin/env", cmd.Display);
            Assert.DoesNotContain("java_home", cmd.Display);
            Assert.DoesNotContain("bash", cmd.Display);
        }
    }
}
```

- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp\CodeCrack.Tests\CodeCrack.Tests.csproj --filter "FullyQualifiedName~RunCommandTests"`. Expect `The type or namespace name 'RunCommandTable' could not be found` (and `RunCommand` if Phase 1 did not create it).

- [ ] **Step 3: Implement** — `winapp\CodeCrack.App.Core\Engine\RunCommand.cs`:
```csharp
using System;
using System.IO;

namespace CodeCrack.App.Core.Engine;

public sealed record RunCommand(string Tool, string[] Args, string Cwd)
{
    public string Display => $"{Tool} {string.Join(' ', Args)}";
}

/// Windows run table (mirrors the mac Runner.command table, POSIX-isms removed).
/// null => the file type isn't runnable.
public static class RunCommandTable
{
    public static RunCommand? For(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        var dir = Path.GetDirectoryName(full) ?? ".";
        var name = Path.GetFileName(full);
        var baseName = Path.GetFileNameWithoutExtension(full);
        var ext = Path.GetExtension(full).TrimStart('.').ToLowerInvariant();

        switch (ext)
        {
            case "py":
            case "pyw":
            {
                var (exe, leading) = PythonInvocation.Resolve();
                var args = new string[leading.Length + 1];
                Array.Copy(leading, args, leading.Length);
                args[leading.Length] = name;
                return new RunCommand(exe, args, dir);
            }
            case "js":
            case "mjs":
            case "cjs": return new RunCommand("node", new[] { name }, dir);
            case "rb":  return new RunCommand("ruby", new[] { name }, dir);
            case "sh":
            case "bash": return new RunCommand("bash", new[] { name }, dir); // git-bash / WSL on PATH
            case "swift": return new RunCommand("swift", new[] { name }, dir);
            case "go":   return new RunCommand("go", new[] { "run", name }, dir);
            case "php":  return new RunCommand("php", new[] { name }, dir);
            case "pl":   return new RunCommand("perl", new[] { name }, dir);
            case "ps1":  return new RunCommand("powershell",
                             new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", name }, dir);
            case "java":
                // Prefer JAVA_HOME's toolchain, else javac/java from PATH. No POSIX java_home, no bash.
                return new RunCommand("cmd", new[]
                {
                    "/c",
                    $"if defined JAVA_HOME (\"%JAVA_HOME%\\bin\\javac\" \"{name}\" && " +
                    $"\"%JAVA_HOME%\\bin\\java\" \"{baseName}\") else " +
                    $"(javac \"{name}\" && java \"{baseName}\")"
                }, dir);
            default: return null;
        }
    }
}
```

> If Phase 1 already created a `RunCommand` record in `RunCommand.cs`, delete the duplicate `record` declaration above and keep only `RunCommandTable`; the record body is identical to the contract. The `.sh`/`.bash` entry intentionally uses `bash` (git-bash/WSL) and is excluded from the POSIX-leftover assertion, which targets the compiled-language and interpreter rows where the mac table's `/usr/bin/env`, `bash -lc`, and `java_home` must not survive the port.

- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp\CodeCrack.Tests\CodeCrack.Tests.csproj --filter "FullyQualifiedName~RunCommandTests"`.

- [ ] **Step 5: Commit** — `git add winapp/CodeCrack.App.Core/Engine/RunCommand.cs winapp/CodeCrack.Tests/RunCommandTests.cs` then `git commit -m "feat(core): Windows RunCommandTable with POSIX-leftover guard tests"`.

---

### Task 3.5: Runner (Process streaming) + ConsoleViewModel + MainViewModel.Run + ConsolePanel

**Files:**
- Create `winapp\CodeCrack.App.Core\Engine\Runner.cs`
- Create `winapp\CodeCrack.App.Core\ViewModels\ConsoleViewModel.cs`
- Modify `winapp\CodeCrack.App.Core\ViewModels\MainViewModel.cs` (add run/console members)
- Create `winapp\CodeCrackApp\Panels\ConsolePanel.xaml` + `.xaml.cs`
- Test `winapp\CodeCrack.Tests\RunnerTests.cs`; modify `winapp\CodeCrack.Tests\ViewModelTests.cs`

**Interfaces:**
Consumes: `RunCommand.Tool/.Args/.Cwd/.Display`; `RunCommandTable.For()`; `StatusBus.Set()`.
Produces: `IRunSession{ Send(string); Stop() }`; `RunSession : IRunSession`; `Runner.Start(RunCommand, Action<string>, Action<int>) : IRunSession?`; `ConsoleViewModel{ string Output; string Display; bool IsRunning; bool CanSubmitInput; Append(); Clear() }`; `MainViewModel{ ConsoleViewModel Console; bool ShowConsole; Func<RunCommand,Action<string>,Action<int>,IRunSession?> RunnerFactory; string Run(); void SendInput(string) }`.

- [ ] **Step 1: Write the failing test** — `winapp\CodeCrack.Tests\RunnerTests.cs` (real functional test, Windows-only) plus VM facts.

`winapp\CodeCrack.Tests\RunnerTests.cs`:
```csharp
using System;
using System.Runtime.InteropServices;
using System.Threading;
using CodeCrack.App.Core.Engine;
using Xunit;

namespace CodeCrack.Tests;

public sealed class RunnerTests
{
    [Fact]
    public void Start_streams_output_and_reports_exit_code()
    {
        Assert.True(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "Runner functional test targets Windows.");

        var output = "";
        int exit = int.MinValue;
        var done = new ManualResetEventSlim(false);

        var cmd = new RunCommand("cmd", new[] { "/c", "echo hello" }, Environment.CurrentDirectory);
        var session = Runner.Start(cmd,
            onOutput: s => output += s,
            onFinish: code => { exit = code; done.Set(); });

        Assert.NotNull(session);
        Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "process did not exit in time");
        Assert.Contains("hello", output);
        Assert.Equal(0, exit);
    }
}
```

Append to `winapp\CodeCrack.Tests\ViewModelTests.cs`:
```csharp
    [Fact]
    public void Run_unknown_extension_prints_help_and_sets_status()
    {
        var (vm, _, status, _, _) = Build();
        vm.Active = Doc(@"C:\proj\notes.md", "hi\n");

        var result = vm.Run();

        Assert.Equal("No run configuration for .md", status.Text);
        Assert.Equal("No run configuration for .md", result);
        Assert.Contains("Don't know how to run .md files yet.", vm.Console.Display);
        Assert.True(vm.ShowConsole);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public void Run_runnable_file_echoes_command_and_streams_via_factory()
    {
        var (vm, _, _, _, _) = Build();
        vm.Active = Doc(@"C:\proj\bug.py", "print(1)\n");

        Action<string>? captured = null;
        Action<int>? finish = null;
        vm.RunnerFactory = (cmd, onOut, onFin) =>
        {
            captured = onOut; finish = onFin;
            return new StubSession();
        };

        vm.Run();
        Assert.StartsWith("$ ", vm.Console.Display);
        Assert.True(vm.IsRunning);

        captured!("line one\n");
        finish!(0);

        Assert.Contains("line one", vm.Console.Display);
        Assert.Contains("[exited with code 0]", vm.Console.Display);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public void Console_display_falls_back_when_empty()
    {
        var c = new ConsoleViewModel();
        Assert.Equal("No output yet.", c.Display);
        Assert.False(c.CanSubmitInput);
        c.IsRunning = true;
        Assert.True(c.CanSubmitInput);
        c.Append("x");
        Assert.Equal("x", c.Display);
    }

    private sealed class StubSession : IRunSession
    {
        public void Send(string text) { }
        public void Stop() { }
    }
```
Add `using CodeCrack.App.Core.Engine;` and `using System;` to the test file if not already present.

- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp\CodeCrack.Tests\CodeCrack.Tests.csproj --filter "FullyQualifiedName~RunnerTests|FullyQualifiedName~ViewModelTests"`. Expect `The type or namespace name 'Runner'/'IRunSession'/'ConsoleViewModel' could not be found` and `MainViewModel does not contain a definition for 'Run'/'Console'/'RunnerFactory'/'ShowConsole'`.

- [ ] **Step 3: Implement** — Runner, ConsoleViewModel, then extend MainViewModel.

`winapp\CodeCrack.App.Core\Engine\Runner.cs`:
```csharp
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace CodeCrack.App.Core.Engine;

public interface IRunSession
{
    void Send(string text);
    void Stop();
}

/// A running process you can send stdin to and stop.
public sealed class RunSession : IRunSession
{
    private readonly Process _process;
    internal RunSession(Process process) => _process = process;

    public void Send(string text)
    {
        try
        {
            _process.StandardInput.Write(text + "\n");
            _process.StandardInput.Flush();
        }
        catch { /* process already exited */ }
    }

    public void Stop()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch { /* already gone */ }
    }
}

/// Runs a file with the right interpreter/compiler and streams merged stdout+stderr.
/// Callbacks are posted to the SynchronizationContext captured at Start (the UI thread
/// in the app; null in headless tests -> invoked inline).
public static class Runner
{
    public static IRunSession? Start(RunCommand command,
                                     Action<string> onOutput,
                                     Action<int> onFinish)
    {
        var sync = SynchronizationContext.Current;
        void Post(Action a) { if (sync is null) a(); else sync.Post(_ => a(), null); }

        var psi = new ProcessStartInfo
        {
            FileName = command.Tool,
            WorkingDirectory = command.Cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in command.Args) psi.ArgumentList.Add(arg);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Post(() => onOutput(e.Data + "\n")); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) Post(() => onOutput(e.Data + "\n")); };
        process.Exited += (_, _) => Post(() => onFinish(process.ExitCode));

        try { process.Start(); }
        catch (Exception ex)
        {
            Post(() => { onOutput($"Failed to launch: {ex.Message}\n"); onFinish(-1); });
            return null;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new RunSession(process);
    }
}
```

`winapp\CodeCrack.App.Core\ViewModels\ConsoleViewModel.cs`:
```csharp
namespace CodeCrack.App.Core.ViewModels;

public sealed class ConsoleViewModel : ObservableObject
{
    private string _output = "";
    public string Output
    {
        get => _output;
        private set { if (Set(ref _output, value)) Raise(nameof(Display)); }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set { if (Set(ref _isRunning, value)) Raise(nameof(CanSubmitInput)); }
    }

    /// "No output yet." placeholder while empty; raw output otherwise.
    public string Display => _output.Length == 0 ? "No output yet." : _output;

    /// stdin box is visible/usable only while a program is running.
    public bool CanSubmitInput => _isRunning;

    public void Append(string text) => Output = _output + text;
    public void Clear() => Output = "";
}
```

Now extend `MainViewModel`. Add these members inside the `MainViewModel` class (e.g. after `AnalyzeAsync`), and the `using` for `System.Threading` is already present:

```csharp
    // ---- Run / console (Task 3.5) ----

    public ConsoleViewModel Console { get; } = new();
    public bool ShowConsole { get; private set; }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set { if (Set(ref _isRunning, value)) Raise(nameof(CanRun)); }
    }

    private IRunSession? _session;

    /// Injection seam for tests; defaults to the real process Runner.
    public Func<RunCommand, Action<string>, Action<int>, IRunSession?> RunnerFactory { get; set; }
        = Runner.Start;

    public string Run()
    {
        if (Active is null) return "";
        Save(); // run reads from disk
        ShowConsole = true;

        var cmd = RunCommandTable.For(Active.Path);
        if (cmd is null)
        {
            var ext = Path.GetExtension(Active.Path).TrimStart('.');
            Console.Clear();
            Console.Append($"Don't know how to run .{ext} files yet.\n");
            var noRun = $"No run configuration for .{ext}";
            _status.Set(noRun);
            return noRun;
        }

        _session?.Stop();
        Console.Clear();
        Console.Append($"$ {cmd.Display}\n\n");
        IsRunning = true;
        Console.IsRunning = true;

        _session = RunnerFactory(cmd,
            text => Console.Append(text),
            code =>
            {
                Console.Append($"\n[exited with code {code}]\n");
                IsRunning = false;
                Console.IsRunning = false;
                _session = null;
            });

        return cmd.Display;
    }

    public void SendInput(string text)
    {
        _session?.Send(text);
        Console.Append(text + "\n");
    }
```

Update the existing `CanRun` in `MainViewModel` (from Task 3.1) so Run is disabled while running:
```csharp
    public bool CanRun => Active is not null && !IsRunning;
```

`winapp\CodeCrackApp\Panels\ConsolePanel.xaml`:
```xml
<UserControl x:Class="CodeCrackApp.Panels.ConsolePanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:CodeCrack.App.Core.ViewModels;assembly=CodeCrack.App.Core"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d"
             d:DataContext="{d:DesignInstance vm:ConsoleViewModel}">
    <DockPanel>
        <TextBlock DockPanel.Dock="Top" Margin="12,6" FontWeight="Bold" Foreground="Gray" Text="Console" />
        <Grid DockPanel.Dock="Bottom"
              Visibility="{Binding CanSubmitInput, Converter={StaticResource BoolToVisible}}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Text="&gt;" Foreground="Green" Margin="14,8" />
            <TextBox x:Name="Input" Grid.Column="1" Margin="4,6,14,6" FontFamily="Consolas"
                     KeyDown="OnInputKeyDown" />
        </Grid>
        <ScrollViewer x:Name="Scroller" VerticalScrollBarVisibility="Auto">
            <TextBlock x:Name="OutputText" Text="{Binding Display}" FontFamily="Consolas"
                       TextWrapping="Wrap" Margin="14,10" />
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

`winapp\CodeCrackApp\Panels\ConsolePanel.xaml.cs`:
```csharp
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using CodeCrack.App.Core.ViewModels;

namespace CodeCrackApp.Panels;

public partial class ConsolePanel : UserControl
{
    public ConsolePanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ConsoleViewModel vm)
                vm.PropertyChanged += OnVmChanged;
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConsoleViewModel.Display))
            Scroller.ScrollToBottom(); // auto-scroll
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        // The owning MainViewModel handles SendInput; the view forwards the text via its parent.
        if (Tag is MainViewModel main)
        {
            main.SendInput(Input.Text);
            Input.Clear();
        }
    }
}
```

> The console panel's `Tag` is bound to the `MainViewModel` by `MainWindow` (Phase 4). Auto-scroll, the "No output yet." placeholder, running-only stdin visibility, and the `$ cmd` / `[exited with code N]` framing are all covered by the `ConsoleViewModel` + `MainViewModel.Run` tests above and the `Runner` functional test.

- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp\CodeCrack.Tests\CodeCrack.Tests.csproj --filter "FullyQualifiedName~RunnerTests|FullyQualifiedName~ViewModelTests"`, then `dotnet build winapp\CodeCrackApp\CodeCrackApp.csproj -c Debug`.

- [ ] **Step 5: Commit** — `git add winapp/CodeCrack.App.Core/Engine/Runner.cs winapp/CodeCrack.App.Core/ViewModels/ConsoleViewModel.cs winapp/CodeCrack.App.Core/ViewModels/MainViewModel.cs winapp/CodeCrackApp/Panels/ConsolePanel.xaml winapp/CodeCrackApp/Panels/ConsolePanel.xaml.cs winapp/CodeCrack.Tests/RunnerTests.cs winapp/CodeCrack.Tests/ViewModelTests.cs` then `git commit -m "feat(core): process Runner + ConsoleViewModel + run loop wiring"`.

---

### Task 3.6: Status-strings audit (verbatim mac parity)

**Files:**
- Test `winapp\CodeCrack.Tests\StatusStringsTests.cs`
- Modify `winapp\CodeCrack.App.Core\ViewModels\MainViewModel.cs` only if an assertion fails.

**Interfaces:**
Consumes: `MainViewModel.Save()/.Run()/.AnalyzeAsync()`; `StatusBus.Text`; `ConsoleViewModel.Display`.
Produces: nothing new — this task locks the ported status strings so a later refactor can't drift them.

- [ ] **Step 1: Write the failing test** — `winapp\CodeCrack.Tests\StatusStringsTests.cs`:
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeCrack.App.Core.Engine;
using CodeCrack.App.Core.Services;
using CodeCrack.App.Core.ViewModels;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public sealed class StatusStringsTests
{
    private sealed class StubSettings : IAppSettings
    {
        public string EnginePathOverride { get; set; } = "";
        public int FontSize { get; set; } = 13;
        public string EditorTheme { get; set; } = "system";
        public bool IndentUsesSpaces { get; set; } = true;
        public int IndentWidth { get; set; } = 4;
        public string ClaudeApiKey { get; set; } = "";
        public void Save() { }
    }

    private static (MainViewModel vm, StatusBus status) Build(FakeAnalyzer? analyzer = null)
    {
        var status = new StatusBus();
        var vm = new MainViewModel(analyzer ?? new FakeAnalyzer(), new StubSettings(), status, new FakeFileIO());
        vm.EditorHost = new FakeEditorHost();
        return (vm, status);
    }

    private static OpenDocument Doc(string path) =>
        new() { Path = path, Text = "print(1)\n", IsDirty = true };

    [Fact]
    public void Saved_status_uses_file_name()
    {
        var (vm, status) = Build();
        vm.Active = Doc(@"C:\proj\bug.py");
        vm.Save();
        Assert.Equal("Saved bug.py", status.Text);
    }

    [Fact]
    public void Unknown_extension_run_message_is_verbatim()
    {
        var (vm, status) = Build();
        vm.Active = Doc(@"C:\proj\readme.md");
        vm.Run();
        Assert.Equal("No run configuration for .md", status.Text);
        Assert.Contains("Don't know how to run .md files yet.", vm.Console.Display);
    }

    [Fact]
    public async Task Analyzing_status_uses_ellipsis_character()
    {
        var analyzer = new FakeAnalyzer
        {
            Next = new EngineOutcome(
                new AnalysisResult(new List<Finding>(), new List<GeneratedTest>(),
                    new Summary(0, 0, 0, 0, new OutcomeCounts(0, 0, 0, 0))), null)
        };
        var (vm, status) = Build(analyzer);
        vm.Active = Doc(@"C:\proj\bug.py");

        var pending = vm.AnalyzeAsync();
        // Analyzing status is set synchronously before the awaited engine call resolves.
        Assert.Equal("Analyzing bug.py\u2026", status.Text);
        await pending;
        Assert.Equal("Analysis found 0 issues in bug.py", status.Text);
    }

    [Fact]
    public void Initial_status_is_empty_until_set()
    {
        var status = new StatusBus();
        Assert.Equal("", status.Text);
    }
}
```

> The `Analyzing …` assertion relies on the status being set before the first `await`; `AnalyzeAsync` in Task 3.1 sets `_status.Set($"Analyzing {name}\u2026")` before `await _analyzer.AnalyzeAsync(...)`, and `FakeAnalyzer` returns a completed task, so the synchronous portion runs before `pending` is awaited. If `StatusBus`'s default `Text` is not `""` (e.g. Phase 1 seeded `"Open a file or folder to begin"`), change the last fact to assert that exact seed string instead — do not modify `StatusBus`.

- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp\CodeCrack.Tests\CodeCrack.Tests.csproj --filter "FullyQualifiedName~StatusStringsTests"`. If Tasks 3.1/3.5 are correct these pass immediately; the audit's purpose is to fail loudly (`Assert.Equal` mismatch, e.g. a stray ASCII `...` instead of `\u2026`, or `"issue"` vs `"issues"`) if any string drifted. Expect the initial run to fail only on a real drift.

- [ ] **Step 3: Implement** — no new production code unless an assertion fails. If a string mismatches, edit the corresponding literal in `MainViewModel` to the exact expected value from the test (e.g. ensure `$"Analyzing {name}\u2026"`, `$"Saved {Active.Name}"`, `$"No run configuration for .{ext}"`, `"Don't know how to run .{ext} files yet.\n"`, `$"Analysis found {n} issue{(n == 1 ? "" : "s")} in {name}"`). No placeholders — the literals are exactly as written in Tasks 3.1 and 3.5.

- [ ] **Step 4: Run tests, expect PASS** — full phase gate: `dotnet test winapp\CodeCrack.Tests\CodeCrack.Tests.csproj` (all Phase 3 suites green), then `dotnet build winapp\CodeCrack.sln -c Release` to confirm `TreatWarningsAsErrors` passes across Core + App + Tests.

- [ ] **Step 5: Commit** — `git add winapp/CodeCrack.Tests/StatusStringsTests.cs winapp/CodeCrack.App.Core/ViewModels/MainViewModel.cs` then `git commit -m "test(core): lock ported status strings (Saved/Analyzing/No run config)"`.

---

## Phase 4: Shell, sessions, external-change, settings

> Grounded in the macOS sources (`macapp/Sources/PPIDE/…`) so every ported status string and behavior is verbatim. Phases 1–3 already produced the contract types (`IAppSettings`, `OpenDocument`, `StatusBus`, `IFileIO`, `MainViewModel`, `EngineJson.Options`, etc.). All tasks obey the global constraints (net8.0 Core with `Nullable=enable`, `TreatWarningsAsErrors=true`, `System.Text.Json` only, no WPF types in `CodeCrack.App.Core`). Repo root = `CodeCrack\`; branch = `windows-port`; commit after every green step.
>
> **Shared prerequisite type** — several tasks below consume a tiny persistence abstraction (the Windows analog of `UserDefaults`). It is created first, in Task 4.4, and reused by 4.5. The in-memory fake for it is created alongside.

---

### Task 4.1: MainWindow shell — split layout, menu, InputBindings, CanExecute gates
**Files:**
- Create: `winapp/CodeCrack.App.Core/ViewModels/CommandGates.cs`
- Create: `winapp/CodeCrackApp/MainWindow.xaml`, `winapp/CodeCrackApp/MainWindow.xaml.cs`
- Test: `winapp/CodeCrack.Tests/CommandGatesTests.cs`

**Interfaces:**
Consumes: `OpenDocument` (contract; fields `Path`, `IsDirty`), `MainViewModel(IAnalyzer, IAppSettings, StatusBus, IFileIO)` (contract), AvalonEdit `SearchPanel` (installed by `CodeEditorControl` in Phase 2).
Produces: `public static class CommandGates` with `bool CanSave(OpenDocument?)`, `bool CanRun(OpenDocument?)`, `bool CanAnalyze(OpenDocument?, bool isAnalyzing)`, `bool IsPython(string path)` — the CanExecute predicates the XAML `CommandBinding`s call.

- [ ] **Step 1: Write the failing test**
```csharp
// winapp/CodeCrack.Tests/CommandGatesTests.cs
using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.ViewModels;
using Xunit;

namespace CodeCrack.Tests;

public class CommandGatesTests
{
    private static OpenDocument Doc(string path, bool dirty = false) =>
        new() { Path = path, Text = "", IsDirty = dirty, LastWriteUtc = System.DateTime.UtcNow };

    [Fact] public void Save_Run_Analyze_disabled_with_no_active_document()
    {
        Assert.False(CommandGates.CanSave(null));
        Assert.False(CommandGates.CanRun(null));
        Assert.False(CommandGates.CanAnalyze(null, isAnalyzing: false));
    }

    [Fact] public void Save_and_Run_enabled_with_active_document()
    {
        var d = Doc(@"C:\proj\a.txt");
        Assert.True(CommandGates.CanSave(d));
        Assert.True(CommandGates.CanRun(d));
    }

    [Fact] public void Analyze_requires_python_and_not_already_analyzing()
    {
        Assert.True(CommandGates.CanAnalyze(Doc(@"C:\proj\a.py"), isAnalyzing: false));
        Assert.True(CommandGates.CanAnalyze(Doc(@"C:\proj\a.pyw"), isAnalyzing: false));
        Assert.False(CommandGates.CanAnalyze(Doc(@"C:\proj\a.js"), isAnalyzing: false));
        Assert.False(CommandGates.CanAnalyze(Doc(@"C:\proj\a.py"), isAnalyzing: true));
    }
}
```
- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp/CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~CommandGatesTests"` → fails to compile: `The name 'CommandGates' does not exist`.
- [ ] **Step 3: Implement**
```csharp
// winapp/CodeCrack.App.Core/ViewModels/CommandGates.cs
using System.IO;
using CodeCrack.App.Core.Editor;

namespace CodeCrack.App.Core.ViewModels;

/// CanExecute predicates for the shell commands, kept pure so they are unit-testable
/// off the UI thread. The WPF CommandBindings in MainWindow.xaml call straight into these.
public static class CommandGates
{
    public static bool CanSave(OpenDocument? active) => active is not null;

    public static bool CanRun(OpenDocument? active) => active is not null;

    public static bool CanAnalyze(OpenDocument? active, bool isAnalyzing) =>
        active is not null && !isAnalyzing && IsPython(active.Path);

    /// The engine only supports Python; Analyze is gated on .py/.pyw.
    public static bool IsPython(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".py" || ext == ".pyw";
    }
}
```
Then the shell view (manual-verify; logic already tested above):
```xml
<!-- winapp/CodeCrackApp/MainWindow.xaml -->
<Window x:Class="CodeCrackApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="CodeCrack" Height="720" Width="1100"
        SourceInitialized="OnSourceInitialized"
        Activated="OnActivated"
        LocationChanged="OnFrameChanged" SizeChanged="OnFrameChanged">
    <Window.CommandBindings>
        <CommandBinding x:Name="OpenCmd"     Command="Open"           Executed="OnOpen"/>
        <CommandBinding x:Name="NewCmd"      Command="New"            Executed="OnNew"/>
        <CommandBinding x:Name="SaveCmd"     Command="Save"           Executed="OnSave"     CanExecute="OnCanSave"/>
        <CommandBinding x:Name="SaveAsCmd"   Command="SaveAs"         Executed="OnSaveAs"   CanExecute="OnCanSave"/>
        <CommandBinding x:Name="CloseTabCmd" Command="Close"          Executed="OnCloseTab" CanExecute="OnCanSave"/>
        <CommandBinding x:Name="FindCmd"     Command="Find"           Executed="OnFind"/>
        <CommandBinding x:Name="RunCmd"      Command="{x:Static local:Commands.Run}"     Executed="OnRun"     CanExecute="OnCanRun"
                        xmlns:local="clr-namespace:CodeCrackApp"/>
        <CommandBinding      Command="{x:Static local:Commands.Analyze}" Executed="OnAnalyze" CanExecute="OnCanAnalyze"
                        xmlns:local="clr-namespace:CodeCrackApp"/>
    </Window.CommandBindings>
    <Window.InputBindings>
        <KeyBinding Key="O" Modifiers="Ctrl"       Command="Open"/>
        <KeyBinding Key="N" Modifiers="Ctrl"       Command="New"/>
        <KeyBinding Key="S" Modifiers="Ctrl"       Command="Save"/>
        <KeyBinding Key="S" Modifiers="Ctrl+Shift" Command="SaveAs"/>
        <KeyBinding Key="W" Modifiers="Ctrl"       Command="Close"/>
        <KeyBinding Key="F" Modifiers="Ctrl"       Command="Find"/>
        <KeyBinding Key="R" Modifiers="Ctrl"       Command="{x:Static local:Commands.Run}"     xmlns:local="clr-namespace:CodeCrackApp"/>
        <KeyBinding Key="B" Modifiers="Ctrl"       Command="{x:Static local:Commands.Analyze}" xmlns:local="clr-namespace:CodeCrackApp"/>
    </Window.InputBindings>
    <DockPanel>
        <Menu DockPanel.Dock="Top">
            <MenuItem Header="_File">
                <MenuItem Header="_Open…"      Command="Open"/>
                <MenuItem Header="_New"        Command="New"/>
                <MenuItem x:Name="OpenRecentMenu" Header="Open _Recent"/>
                <Separator/>
                <MenuItem Header="_Save"       Command="Save"/>
                <MenuItem Header="Save _As…"   Command="SaveAs"/>
                <MenuItem Header="_Close Tab"  Command="Close"/>
            </MenuItem>
            <MenuItem Header="_Edit">
                <MenuItem Header="_Find…"      Command="Find"/>
            </MenuItem>
            <MenuItem Header="_Run">
                <MenuItem Header="_Run"        Command="{x:Static local:Commands.Run}"     xmlns:local="clr-namespace:CodeCrackApp"/>
                <MenuItem Header="_Analyze"    Command="{x:Static local:Commands.Analyze}" xmlns:local="clr-namespace:CodeCrackApp"/>
            </MenuItem>
        </Menu>
        <TextBlock x:Name="StatusText" DockPanel.Dock="Bottom" Padding="6,3"
                   TextTrimming="CharacterEllipsis" Text="Open a file or folder to begin"/>
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="240" MinWidth="0"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <ContentControl x:Name="FileTreeHost" Grid.Column="0"/>
            <!-- Zero-width splitter: draggable but invisible (SplitDividerHider analog). -->
            <GridSplitter Grid.Column="1" Width="1" HorizontalAlignment="Center"
                          Background="Transparent" ShowsPreview="False"/>
            <ContentControl x:Name="EditorHostSurface" Grid.Column="2"/>
        </Grid>
    </DockPanel>
</Window>
```
```csharp
// winapp/CodeCrackApp/MainWindow.xaml.cs  (partial — command wiring; frame/activate handlers added in 4.5/4.6)
using System.Windows;
using System.Windows.Input;
using CodeCrack.App.Core.ViewModels;

namespace CodeCrackApp;

/// Custom routed commands with no built-in WPF key (Run, Analyze).
public static class Commands
{
    public static readonly RoutedUICommand Run =
        new("Run", nameof(Run), typeof(Commands));
    public static readonly RoutedUICommand Analyze =
        new("Analyze", nameof(Analyze), typeof(Commands));
}

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow() => InitializeComponent();

    private void OnCanSave(object s, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = CommandGates.CanSave(Vm.Active);
    private void OnCanRun(object s, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = CommandGates.CanRun(Vm.Active);
    private void OnCanAnalyze(object s, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = CommandGates.CanAnalyze(Vm.Active, Vm.IsAnalyzing);

    private void OnOpen(object s, ExecutedRoutedEventArgs e)     => Vm.OpenViaDialog();
    private void OnNew(object s, ExecutedRoutedEventArgs e)      => Vm.NewDocument();
    private void OnSave(object s, ExecutedRoutedEventArgs e)     => Vm.Save();
    private void OnSaveAs(object s, ExecutedRoutedEventArgs e)   => Vm.SaveAs();
    private void OnCloseTab(object s, ExecutedRoutedEventArgs e) => Vm.CloseActive();
    private void OnRun(object s, ExecutedRoutedEventArgs e)      => Vm.Run();
    private void OnAnalyze(object s, ExecutedRoutedEventArgs e)  => Vm.Analyze();
    private void OnFind(object s, ExecutedRoutedEventArgs e)     => Vm.EditorHost.FocusEditor();
    // Ctrl+F: AvalonEdit's SearchInputHandler (installed by CodeEditorControl) opens the
    // SearchPanel itself once the editor has focus.
}
```
- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp/CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~CommandGatesTests"`. **Manual verify:** build `CodeCrackApp`, launch, confirm Ctrl+O/N/S/Ctrl+Shift+S/Ctrl+W/Ctrl+F/Ctrl+R/Ctrl+B fire; Save/Run/Analyze menu items grey out with no open doc; dragging the invisible splitter resizes the tree; status bar reads "Open a file or folder to begin".
- [ ] **Step 5: Commit** — `git add winapp/CodeCrack.App.Core/ViewModels/CommandGates.cs winapp/CodeCrackApp/MainWindow.xaml winapp/CodeCrackApp/MainWindow.xaml.cs winapp/CodeCrack.Tests/CommandGatesTests.cs && git commit -m "feat(winapp): shell layout, menu, input bindings, CanExecute gates"`

---

### Task 4.2: FileTree builder + FileTreeViewModel
**Files:**
- Create: `winapp/CodeCrack.App.Core/ViewModels/FileTreeViewModel.cs`
- Test: `winapp/CodeCrack.Tests/FileTreeTests.cs`
- Create (view, manual): `winapp/CodeCrackApp/FileTree/FileTreeView.xaml`

**Interfaces:**
Consumes: none beyond BCL.
Produces: `public sealed record FileNode(string Path, string Name, bool IsDirectory, IReadOnlyList<FileNode> Children)`; `public sealed class FileTreeViewModel` with `FileNode? Root { get; }`, `void BuildFrom(string fileOrDirPath)` (single file → tree from its parent), `void Refresh()`.

- [ ] **Step 1: Write the failing test**
```csharp
// winapp/CodeCrack.Tests/FileTreeTests.cs
using System.IO;
using System.Linq;
using CodeCrack.App.Core.ViewModels;
using Xunit;

namespace CodeCrack.Tests;

public class FileTreeTests : System.IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cc_tree_" + Path.GetRandomFileName());

    public FileTreeTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "z.py"), "z");
        File.WriteAllText(Path.Combine(_root, "src", "a.py"), "a");
        File.WriteAllText(Path.Combine(_root, "readme.md"), "r");
        File.WriteAllText(Path.Combine(_root, ".secret"), "hidden"); // dotfile -> skipped
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact] public void Build_skips_hidden_dirs_first_then_case_insensitive_names()
    {
        var vm = new FileTreeViewModel();
        vm.BuildFrom(_root);

        var names = vm.Root!.Children.Select(c => c.Name).ToArray();
        Assert.Equal(new[] { "src", "readme.md" }, names); // dir before file; .secret gone
        Assert.False(vm.Root.Children.Any(c => c.Name == ".secret"));

        var srcKids = vm.Root.Children.First(c => c.Name == "src").Children.Select(c => c.Name);
        Assert.Equal(new[] { "a.py", "z.py" }, srcKids); // case-insensitive sort
    }

    [Fact] public void Build_from_single_file_roots_at_its_parent()
    {
        var vm = new FileTreeViewModel();
        vm.BuildFrom(Path.Combine(_root, "readme.md"));
        Assert.Equal(_root, vm.Root!.Path);
        Assert.True(vm.Root.IsDirectory);
    }
}
```
- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp/CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~FileTreeTests"` → `The type or namespace name 'FileTreeViewModel' could not be found`.
- [ ] **Step 3: Implement**
```csharp
// winapp/CodeCrack.App.Core/ViewModels/FileTreeViewModel.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeCrack.App.Core.ViewModels;

/// A node in the project file tree. Leaf files have an empty Children list.
public sealed record FileNode(string Path, string Name, bool IsDirectory, IReadOnlyList<FileNode> Children);

/// Eager project tree. Skips hidden entries, sorts directories first then names
/// case-insensitively — a byte-for-byte port of FileTreeBuilder.build.
public sealed class FileTreeViewModel
{
    private static readonly IReadOnlyList<FileNode> None = Array.Empty<FileNode>();

    public FileNode? Root { get; private set; }

    /// Build the tree. A directory roots there; a single file roots at its parent directory.
    public void BuildFrom(string fileOrDirPath)
    {
        var full = System.IO.Path.GetFullPath(fileOrDirPath);
        var dir = Directory.Exists(full) ? full : System.IO.Path.GetDirectoryName(full)!;
        Root = Build(dir);
    }

    /// Rebuild the current root (after New / Save-As create files on disk).
    public void Refresh()
    {
        if (Root is not null) Root = Build(Root.Path);
    }

    private static FileNode Build(string path)
    {
        if (!Directory.Exists(path))
            return new FileNode(path, System.IO.Path.GetFileName(path), false, None);

        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(path); }
        catch { entries = Array.Empty<string>(); }

        var kids = entries
            .Where(p => !IsHidden(p))
            .Select(p => new { p, dir = Directory.Exists(p), name = System.IO.Path.GetFileName(p) })
            .OrderByDescending(e => e.dir)                                   // dirs first
            .ThenBy(e => e.name, StringComparer.OrdinalIgnoreCase)           // then case-insensitive
            .Select(e => Build(e.p))
            .ToList();

        return new FileNode(path, System.IO.Path.GetFileName(path), true, kids);
    }

    private static bool IsHidden(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        if (name.StartsWith('.')) return true;                              // dotfiles
        try { return (File.GetAttributes(path) & FileAttributes.Hidden) != 0; }
        catch { return false; }
    }
}
```
- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp/CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~FileTreeTests"`. **Manual verify (view):** bind a WPF `TreeView` in `FileTree/FileTreeView.xaml` to `FileTreeViewModel.Root` with a `HierarchicalDataTemplate` on `Children`; confirm hidden files are absent, folders sort above files, and clicking a leaf opens it; confirm Open uses two dialogs (file dialog, then folder dialog) and that opening a single file builds the tree from its parent; confirm the tree rebuilds after New and Save-As via `Refresh()`.
- [ ] **Step 5: Commit** — `git add winapp/CodeCrack.App.Core/ViewModels/FileTreeViewModel.cs winapp/CodeCrack.Tests/FileTreeTests.cs && git commit -m "feat(winapp): eager file tree (skip hidden, dirs-first, case-insensitive)"`

---

### Task 4.3: ProjectSearch — case-insensitive search, case-sensitive replace-all
**Files:**
- Create: `winapp/CodeCrack.App.Core/Search/ProjectSearch.cs`
- Test: `winapp/CodeCrack.Tests/ProjectSearchTests.cs`

**Interfaces:**
Consumes: none beyond BCL.
Produces: `public sealed record SearchHit(string Path, int Line, string Preview)`; `public static class ProjectSearch` with `const int MaxHits = 500`, `IReadOnlyList<SearchHit> Search(string query, string root)`, `IReadOnlyList<string> ReplaceAll(string query, string replacement, string root)`.

- [ ] **Step 1: Write the failing test** — asserts the search/replace asymmetry, the 500 cap, and binary skip.
```csharp
// winapp/CodeCrack.Tests/ProjectSearchTests.cs
using System.IO;
using System.Linq;
using CodeCrack.App.Core.Search;
using Xunit;

namespace CodeCrack.Tests;

public class ProjectSearchTests : System.IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cc_search_" + Path.GetRandomFileName());

    public ProjectSearchTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string name, string text)
    {
        var p = Path.Combine(_root, name);
        File.WriteAllText(p, text);
        return p;
    }

    [Fact] public void Search_is_case_insensitive_and_reports_1_based_lines()
    {
        Write("a.txt", "alpha\nBETA beta\ngamma");
        var hits = ProjectSearch.Search("beta", _root);
        Assert.Single(hits);
        Assert.Equal(2, hits[0].Line);
        Assert.Equal("BETA beta", hits[0].Preview);
    }

    [Fact] public void ReplaceAll_is_case_SENSITIVE()
    {
        var p = Write("a.txt", "Foo foo FOO");
        var changed = ProjectSearch.ReplaceAll("foo", "bar", _root);
        Assert.Single(changed);
        Assert.Equal("Foo bar FOO", File.ReadAllText(p)); // only exact-case "foo" replaced
    }

    [Fact] public void ReplaceAll_makes_no_change_when_only_other_cases_match()
    {
        Write("a.txt", "FOO");
        var changed = ProjectSearch.ReplaceAll("foo", "bar", _root);
        Assert.Empty(changed); // case-insensitive Search would have matched; ReplaceAll must not
    }

    [Fact] public void Search_skips_hidden_and_non_utf8_files()
    {
        Write(".hidden.txt", "needle");
        File.WriteAllBytes(Path.Combine(_root, "bin.dat"), new byte[] { 0xFF, 0xFE, 0x00, 0x6E });
        Write("ok.txt", "needle here");
        var hits = ProjectSearch.Search("needle", _root);
        Assert.Single(hits);
        Assert.EndsWith("ok.txt", hits[0].Path);
    }

    [Fact] public void Search_caps_at_500_hits()
    {
        Write("many.txt", string.Concat(Enumerable.Repeat("x\n", 600)));
        Assert.Equal(ProjectSearch.MaxHits, ProjectSearch.Search("x", _root).Count);
    }
}
```
- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp/CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~ProjectSearchTests"` → `The name 'ProjectSearch' does not exist`.
- [ ] **Step 3: Implement**
```csharp
// winapp/CodeCrack.App.Core/Search/ProjectSearch.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeCrack.App.Core.Search;

/// One search match: a file path, a 1-based line number, and the trimmed line text.
public sealed record SearchHit(string Path, int Line, string Preview);

/// Project-wide text search and replace under a root folder. Skips hidden files and anything
/// that is not valid UTF-8 text. Search is case-INsensitive; ReplaceAll is case-SENSITIVE —
/// the asymmetry is deliberate and mirrors the mac app.
public static class ProjectSearch
{
    public const int MaxHits = 500;

    // Strict decoder: throws on invalid UTF-8 so we can skip binary files.
    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static IReadOnlyList<SearchHit> Search(string query, string root)
    {
        var hits = new List<SearchHit>();
        if (string.IsNullOrEmpty(query)) return hits;

        foreach (var path in TextFiles(root))
        {
            if (hits.Count >= MaxHits) break;
            if (!TryReadUtf8(path, out var content)) continue;

            var line = 0;
            foreach (var raw in content.Split('\n'))
            {
                line++;
                var text = raw.TrimEnd('\r');
                if (text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hits.Add(new SearchHit(path, line, text.Trim()));
                    if (hits.Count >= MaxHits) break;
                }
            }
        }
        return hits;
    }

    /// Replace every case-sensitive occurrence of query with replacement across the project.
    /// Returns the paths of files that actually changed.
    public static IReadOnlyList<string> ReplaceAll(string query, string replacement, string root)
    {
        var changed = new List<string>();
        if (string.IsNullOrEmpty(query)) return changed;

        foreach (var path in TextFiles(root))
        {
            if (!TryReadUtf8(path, out var content)) continue;
            if (!content.Contains(query, StringComparison.Ordinal)) continue;

            var updated = content.Replace(query, replacement, StringComparison.Ordinal);
            if (updated == content) continue;
            File.WriteAllText(path, updated, StrictUtf8); // UTF-8, no BOM
            changed.Add(path);
        }
        return changed;
    }

    private static IEnumerable<string> TextFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            IgnoreInaccessible = true
        };
        foreach (var path in Directory.EnumerateFiles(root, "*", opts))
        {
            if (Path.GetFileName(path).StartsWith('.')) continue; // dotfiles
            yield return path;
        }
    }

    private static bool TryReadUtf8(string path, out string content)
    {
        try { content = File.ReadAllText(path, StrictUtf8); return true; }
        catch { content = ""; return false; } // DecoderFallbackException on binary, or IO error
    }
}
```
- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp/CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~ProjectSearchTests"`. **Manual verify (view glue):** after `ReplaceAll`, the view reloads any changed open buffer (set `Text`, clear `IsDirty`, refresh `LastWriteUtc`), re-runs the search, and sets status `Replaced in N file(s)` (`$"Replaced in {n} file{(n == 1 ? "" : "s")}"`).
- [ ] **Step 5: Commit** — `git add winapp/CodeCrack.App.Core/Search/ProjectSearch.cs winapp/CodeCrack.Tests/ProjectSearchTests.cs && git commit -m "feat(winapp): project search (case-insensitive) + case-sensitive replace-all"`

---

### Task 4.4: Key-value store + RecentFiles
**Files:**
- Create: `winapp/CodeCrack.App.Core/Session/IKeyValueStore.cs`
- Create: `winapp/CodeCrack.App.Core/Session/JsonKeyValueStore.cs`
- Create: `winapp/CodeCrack.App.Core/Session/RecentFiles.cs`
- Create: `winapp/CodeCrack.App.Core/ViewModels/RecentOpen.cs`
- Test: `winapp/CodeCrack.Tests/Fakes/InMemoryKeyValueStore.cs`, `winapp/CodeCrack.Tests/RecentFilesTests.cs`

**Interfaces:**
Consumes: none beyond BCL.
Produces:
- `public interface IKeyValueStore` — `string? GetString(string)`, `void SetString(string,string)`, `string[]? GetStringArray(string)`, `void SetStringArray(string,string[])`, `void Remove(string)` (Windows analog of `UserDefaults`; reused by Task 4.5).
- `public sealed class JsonKeyValueStore : IKeyValueStore` (backed by a JSON file).
- `public sealed class RecentFiles(IKeyValueStore store)` — `const int Cap = 10`, `IReadOnlyList<string> Paths`, `bool IsEmpty`, `void Record(string)`, `void Remove(string)`, `void Clear()`.
- `public static class RecentOpen` — `string MissingStatus(string path)`.

- [ ] **Step 1: Write the failing test**
```csharp
// winapp/CodeCrack.Tests/Fakes/InMemoryKeyValueStore.cs
using System.Collections.Generic;
using CodeCrack.App.Core.Session;

namespace CodeCrack.Tests.Fakes;

public sealed class InMemoryKeyValueStore : IKeyValueStore
{
    private readonly Dictionary<string, object> _d = new();
    public int Writes { get; private set; }

    public string? GetString(string key) => _d.TryGetValue(key, out var v) && v is string s ? s : null;
    public void SetString(string key, string value) { _d[key] = value; Writes++; }
    public string[]? GetStringArray(string key) => _d.TryGetValue(key, out var v) && v is string[] a ? a : null;
    public void SetStringArray(string key, string[] value) { _d[key] = value; Writes++; }
    public void Remove(string key) { if (_d.Remove(key)) Writes++; }
}
```
```csharp
// winapp/CodeCrack.Tests/RecentFilesTests.cs
using System.IO;
using System.Linq;
using CodeCrack.App.Core.Session;
using CodeCrack.App.Core.ViewModels;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public class RecentFilesTests : System.IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "cc_recent_" + Path.GetRandomFileName());

    public RecentFilesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Touch(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "");
        return p;
    }

    [Fact] public void Record_dedups_by_full_path_and_moves_to_front()
    {
        var a = Touch("a.py"); var b = Touch("b.py");
        var r = new RecentFiles(new InMemoryKeyValueStore());
        r.Record(a); r.Record(b); r.Record(a);
        Assert.Equal(new[] { Path.GetFullPath(a), Path.GetFullPath(b) }, r.Paths);
    }

    [Fact] public void Record_caps_at_10_newest_first()
    {
        var r = new RecentFiles(new InMemoryKeyValueStore());
        for (var i = 0; i < 12; i++) r.Record(Touch($"f{i}.py"));
        Assert.Equal(RecentFiles.Cap, r.Paths.Count);
        Assert.EndsWith("f11.py", r.Paths[0]);
    }

    [Fact] public void Load_prunes_missing_entries()
    {
        var store = new InMemoryKeyValueStore();
        var gone = Path.Combine(_dir, "ghost.py");
        var live = Touch("live.py");
        store.SetStringArray("recentDocumentPaths", new[] { gone, live });
        var r = new RecentFiles(store);
        Assert.Equal(new[] { Path.GetFullPath(live) }, r.Paths);
    }

    [Fact] public void Clear_and_IsEmpty()
    {
        var r = new RecentFiles(new InMemoryKeyValueStore());
        r.Record(Touch("a.py"));
        Assert.False(r.IsEmpty);
        r.Clear();
        Assert.True(r.IsEmpty);
    }

    [Fact] public void MissingStatus_is_verbatim()
        => Assert.Equal("\"ghost.py\" is no longer available",
                        RecentOpen.MissingStatus(@"C:\proj\ghost.py"));
}
```
- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp/CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~RecentFilesTests"` → `The type or namespace name 'IKeyValueStore' could not be found`.
- [ ] **Step 3: Implement**
```csharp
// winapp/CodeCrack.App.Core/Session/IKeyValueStore.cs
namespace CodeCrack.App.Core.Session;

/// Minimal string/string-array persistence (the Windows analog of UserDefaults). Backed by a
/// JSON file in the app; faked in-memory for tests. Used by RecentFiles, SessionStore, WindowFrame.
public interface IKeyValueStore
{
    string? GetString(string key);
    void SetString(string key, string value);
    string[]? GetStringArray(string key);
    void SetStringArray(string key, string[] value);
    void Remove(string key);
}
```
```csharp
// winapp/CodeCrack.App.Core/Session/JsonKeyValueStore.cs
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeCrack.App.Core.Session;

/// IKeyValueStore backed by a single JSON object on disk (e.g. %APPDATA%\CodeCrack\session.json).
public sealed class JsonKeyValueStore : IKeyValueStore
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private readonly string _path;
    private readonly JsonObject _root;

    public JsonKeyValueStore(string path)
    {
        _path = path;
        _root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject()
            : new JsonObject();
    }

    public string? GetString(string key) =>
        _root[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public void SetString(string key, string value) { _root[key] = value; Flush(); }

    public string[]? GetStringArray(string key) =>
        _root[key] is JsonArray a ? a.Select(n => n!.GetValue<string>()).ToArray() : null;

    public void SetStringArray(string key, string[] value)
    {
        _root[key] = new JsonArray(value.Select(s => (JsonNode)s!).ToArray());
        Flush();
    }

    public void Remove(string key) { if (_root.Remove(key)) Flush(); }

    private void Flush()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, _root.ToJsonString(Pretty));
    }
}
```
```csharp
// winapp/CodeCrack.App.Core/Session/RecentFiles.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeCrack.App.Core.Session;

/// Most-recently-opened files, newest first, de-duplicated by full path and capped.
/// Entries missing on disk are pruned at load; the Open-Recent path prunes on demand.
public sealed class RecentFiles
{
    public const int Cap = 10;
    private const string Key = "recentDocumentPaths";

    private readonly IKeyValueStore _store;
    private List<string> _paths;

    public RecentFiles(IKeyValueStore store)
    {
        _store = store;
        _paths = (store.GetStringArray(Key) ?? Array.Empty<string>())
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Cap)
            .ToList();
    }

    public IReadOnlyList<string> Paths => _paths;
    public bool IsEmpty => _paths.Count == 0;

    public void Record(string path)
    {
        var full = Path.GetFullPath(path);
        _paths.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        _paths.Insert(0, full);
        if (_paths.Count > Cap) _paths = _paths.Take(Cap).ToList();
        Persist();
    }

    public void Remove(string path)
    {
        var full = Path.GetFullPath(path);
        if (_paths.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase)) > 0)
            Persist();
    }

    public void Clear()
    {
        if (_paths.Count == 0) return;
        _paths.Clear();
        Persist();
    }

    private void Persist() => _store.SetStringArray(Key, _paths.ToArray());
}
```
```csharp
// winapp/CodeCrack.App.Core/ViewModels/RecentOpen.cs
using System.IO;

namespace CodeCrack.App.Core.ViewModels;

/// Verbatim status string for opening a recent file that has vanished from disk.
public static class RecentOpen
{
    public static string MissingStatus(string path) =>
        $"\"{Path.GetFileName(path)}\" is no longer available";
}
```
- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp/CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~RecentFilesTests"`. **Manual verify (menu):** File ▸ Open Recent lists `Paths`, a separator, then Clear Menu; the whole submenu is disabled when `IsEmpty`; a normal Open calls `Record`; picking a now-missing entry calls `Remove` and sets `RecentOpen.MissingStatus`; session restore opens files WITHOUT calling `Record` (bypass — verified in Task 4.5).
- [ ] **Step 5: Commit** — `git add winapp/CodeCrack.App.Core/Session/IKeyValueStore.cs winapp/CodeCrack.App.Core/Session/JsonKeyValueStore.cs winapp/CodeCrack.App.Core/Session/RecentFiles.cs winapp/CodeCrack.App.Core/ViewModels/RecentOpen.cs winapp/CodeCrack.Tests/Fakes/InMemoryKeyValueStore.cs winapp/CodeCrack.Tests/RecentFilesTests.cs && git commit -m "feat(winapp): key-value store + recent files (cap 10, dedup, prune)"`

---

### Task 4.5: SessionStore + WindowFrame
**Files:**
- Create: `winapp/CodeCrack.App.Core/Session/WindowFrame.cs`
- Create: `winapp/CodeCrack.App.Core/Session/SessionStore.cs`
- Test: `winapp/CodeCrack.Tests/SessionStoreTests.cs`

**Interfaces:**
Consumes: `IKeyValueStore` (Task 4.4), `InMemoryKeyValueStore` (Task 4.4 fake).
Produces:
- `public sealed record WindowFrame(double Left, double Top, double Width, double Height)` — `bool IsValid`, `void Save(IKeyValueStore)`, `static WindowFrame? Load(IKeyValueStore)`.
- `public sealed record SessionState(IReadOnlyList<string> OpenPaths, string? ActivePath)`.
- `public sealed class SessionStore(IKeyValueStore store)` — `void Save(IReadOnlyList<string> openPaths, string? activePath)`, `SessionState? Restore()`.

- [ ] **Step 1: Write the failing test**
```csharp
// winapp/CodeCrack.Tests/SessionStoreTests.cs
using System.IO;
using CodeCrack.App.Core.Session;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public class SessionStoreTests : System.IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "cc_session_" + Path.GetRandomFileName());

    public SessionStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Touch(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "");
        return p;
    }

    [Fact] public void Save_is_ignored_until_Restore_runs_the_didRestore_guard()
    {
        var store = new InMemoryKeyValueStore();
        var s = new SessionStore(store);
        s.Save(new[] { Touch("a.py") }, null); // before Restore -> no-op
        Assert.Equal(0, store.Writes);
        Assert.Null(store.GetStringArray("sessionOpenDocumentPaths"));
    }

    [Fact] public void Restore_skips_missing_and_returns_existing_only()
    {
        var store = new InMemoryKeyValueStore();
        var live = Touch("live.py");
        var gone = Path.Combine(_dir, "gone.py");
        store.SetStringArray("sessionOpenDocumentPaths", new[] { gone, live });
        store.SetString("sessionActiveDocumentPath", gone);

        var state = new SessionStore(store).Restore();
        Assert.NotNull(state);
        Assert.Equal(new[] { live }, state!.OpenPaths);
        Assert.Null(state.ActivePath); // active was missing -> dropped
    }

    [Fact] public void Fingerprint_suppresses_redundant_saves()
    {
        var store = new InMemoryKeyValueStore();
        var a = Touch("a.py");
        store.SetStringArray("sessionOpenDocumentPaths", new[] { a });
        store.SetString("sessionActiveDocumentPath", a);

        var s = new SessionStore(store);
        s.Restore();                 // seeds signature + enables saving
        var before = store.Writes;
        s.Save(new[] { a }, a);      // identical set -> no write
        Assert.Equal(before, store.Writes);
    }

    [Fact] public void Empty_tab_set_clears_the_keys()
    {
        var store = new InMemoryKeyValueStore();
        var a = Touch("a.py");
        store.SetStringArray("sessionOpenDocumentPaths", new[] { a });
        var s = new SessionStore(store);
        s.Restore();
        s.Save(System.Array.Empty<string>(), null);
        Assert.Null(store.GetStringArray("sessionOpenDocumentPaths"));
        Assert.Null(store.GetString("sessionActiveDocumentPath"));
    }

    [Fact] public void WindowFrame_round_trips_and_rejects_non_positive_sizes()
    {
        var store = new InMemoryKeyValueStore();
        new WindowFrame(100, 50, 800, 600).Save(store);
        var f = WindowFrame.Load(store);
        Assert.Equal(new WindowFrame(100, 50, 800, 600), f);

        var s2 = new InMemoryKeyValueStore();
        new WindowFrame(0, 0, 0, 600).Save(s2);        // width <= 0 -> not saved
        Assert.Null(WindowFrame.Load(s2));
    }
}
```
- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp/CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~SessionStoreTests"` → `The type or namespace name 'SessionStore' could not be found`.
- [ ] **Step 3: Implement**
```csharp
// winapp/CodeCrack.App.Core/Session/WindowFrame.cs
using System.Globalization;

namespace CodeCrack.App.Core.Session;

/// The main window frame (L/T/W/H). Persisted as four invariant-culture strings.
public sealed record WindowFrame(double Left, double Top, double Width, double Height)
{
    private const string Key = "sessionWindowFrame";

    public bool IsValid => Width > 0 && Height > 0;

    /// Persist — but never persist a degenerate frame (width/height <= 0).
    public void Save(IKeyValueStore store)
    {
        if (!IsValid) return;
        store.SetStringArray(Key, new[]
        {
            Left.ToString(CultureInfo.InvariantCulture),
            Top.ToString(CultureInfo.InvariantCulture),
            Width.ToString(CultureInfo.InvariantCulture),
            Height.ToString(CultureInfo.InvariantCulture),
        });
    }

    public static WindowFrame? Load(IKeyValueStore store)
    {
        var a = store.GetStringArray(Key);
        if (a is not { Length: 4}) return null;
        static double P(string s) => double.Parse(s, CultureInfo.InvariantCulture);
        var f = new WindowFrame(P(a[0]), P(a[1]), P(a[2]), P(a[3]));
        return f.IsValid ? f : null;
    }
}
```
```csharp
// winapp/CodeCrack.App.Core/Session/SessionStore.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeCrack.App.Core.Session;

/// A restored session. OpenPaths are guaranteed to exist on disk (missing ones are dropped).
public sealed record SessionState(IReadOnlyList<string> OpenPaths, string? ActivePath);

/// Deterministic persistence of the open tab set + active tab. A fingerprint (paths + active,
/// not text) gates redundant re-saves; a _didRestore guard blocks saves until Restore has run,
/// so startup tab churn never clobbers the saved session.
public sealed class SessionStore
{
    private const string OpenKey = "sessionOpenDocumentPaths";
    private const string ActiveKey = "sessionActiveDocumentPath";

    private readonly IKeyValueStore _store;
    private bool _didRestore;
    private string? _lastSignature;

    public SessionStore(IKeyValueStore store) => _store = store;

    public void Save(IReadOnlyList<string> openPaths, string? activePath)
    {
        if (!_didRestore) return;                       // guard: no saving before restore
        var sig = Signature(openPaths, activePath);
        if (sig == _lastSignature) return;              // fingerprint: skip identical
        _lastSignature = sig;

        if (openPaths.Count == 0)                        // empty tab set clears the keys
        {
            _store.Remove(OpenKey);
            _store.Remove(ActiveKey);
            return;
        }
        _store.SetStringArray(OpenKey, openPaths.ToArray());
        if (activePath is null) _store.Remove(ActiveKey);
        else _store.SetString(ActiveKey, activePath);
    }

    /// Read the saved session (existing paths only), enabling future saves. Restore opens files
    /// inline in the caller WITHOUT touching RecentFiles — the recents bypass.
    public SessionState? Restore()
    {
        _didRestore = true;
        var open = _store.GetStringArray(OpenKey);
        if (open is null || open.Length == 0) return null;

        var existing = open.Where(File.Exists).ToList();
        if (existing.Count == 0) return null;

        var active = _store.GetString(ActiveKey);
        if (active is not null && !File.Exists(active)) active = null;

        _lastSignature = Signature(existing, active);    // seed: identical post-restore save is a no-op
        return new SessionState(existing, active);
    }

    private static string Signature(IReadOnlyList<string> open, string? active) =>
        string.Join('\u0000', open) + '\u0001' + (active ?? "");
}
```
- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp/CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~SessionStoreTests"`. **Manual verify (view):** in `MainWindow`, restore the frame once in `OnSourceInitialized` via `WindowFrame.Load`; save it on every `LocationChanged`/`SizeChanged`; call `SessionStore.Restore()` on load, opening each returned path inline (no `RecentFiles.Record`) and rebuilding the tree from the active file's dir; call `SessionStore.Save(openPaths, activePath)` whenever the tab set changes.
- [ ] **Step 5: Commit** — `git add winapp/CodeCrack.App.Core/Session/WindowFrame.cs winapp/CodeCrack.App.Core/Session/SessionStore.cs winapp/CodeCrack.Tests/SessionStoreTests.cs && git commit -m "feat(winapp): session store (fingerprint + didRestore guard) and window frame"`

---

### Task 4.6: ExternalChangeWatcher — 4-branch state machine
**Files:**
- Create: `winapp/CodeCrack.App.Core/Services/ExternalChangeWatcher.cs`
- Test: `winapp/CodeCrack.Tests/Fakes/FakeExternalFileProbe.cs`, `winapp/CodeCrack.Tests/ExternalChangeTests.cs`

**Interfaces:**
Consumes: `OpenDocument` (contract; mutates `Text`, `IsDirty`, `LastWriteUtc`), `StatusBus` (contract; `Set(string)`, `Text`).
Produces:
- `public interface IExternalFileProbe` — `bool Exists(string)`, `DateTime LastWriteUtc(string)`, `string ReadText(string)`.
- `public sealed class DiskFileProbe : IExternalFileProbe` (real System.IO impl).
- `public sealed record ExternalChangePrompt(OpenDocument Doc, string DiskText, DateTime DiskWriteUtc)`.
- `public sealed class ExternalChangeWatcher(IExternalFileProbe probe, StatusBus status)` — `bool PromptActive { get; set; }`, `ExternalChangePrompt? Scan(IReadOnlyList<OpenDocument>)`, `void Resolve(ExternalChangePrompt, bool reload)`.

- [ ] **Step 1: Write the failing test** — one doc per branch.
```csharp
// winapp/CodeCrack.Tests/Fakes/FakeExternalFileProbe.cs
using System;
using System.Collections.Generic;
using CodeCrack.App.Core.Services;

namespace CodeCrack.Tests.Fakes;

public sealed class FakeExternalFileProbe : IExternalFileProbe
{
    public Dictionary<string, (DateTime Mtime, string Text, bool Exists)> Files = new();

    public bool Exists(string path) => Files.TryGetValue(path, out var f) && f.Exists;
    public DateTime LastWriteUtc(string path) => Files[path].Mtime;
    public string ReadText(string path) => Files[path].Text;
}
```
```csharp
// winapp/CodeCrack.Tests/ExternalChangeTests.cs
using System;
using System.Collections.Generic;
using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.Services;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public class ExternalChangeTests
{
    private static OpenDocument Doc(string path, string text, bool dirty, DateTime known) =>
        new() { Path = path, Text = text, IsDirty = dirty, LastWriteUtc = known };

    [Fact] public void All_four_branches()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddMinutes(1);

        var unchanged = Doc(@"C:\p\unchanged.txt", "same", false, t0); // branch 1: no newer mtime
        var caughtUp  = Doc(@"C:\p\caughtup.txt",  "same", true,  t0); // branch 2: newer mtime, same text
        var reloaded  = Doc(@"C:\p\clean.txt",     "old",  false, t0); // branch 4: clean + differs
        var prompted  = Doc(@"C:\p\dirty.txt",     "mine", true,  t0); // branch 3: dirty + differs

        var probe = new FakeExternalFileProbe();
        probe.Files[unchanged.Path] = (t0, "same", true);
        probe.Files[caughtUp.Path]  = (t1, "same", true);
        probe.Files[reloaded.Path]  = (t1, "disk", true);
        probe.Files[prompted.Path]  = (t1, "disk", true);

        var status = new CodeCrack.App.Core.Services.StatusBus();
        var w = new ExternalChangeWatcher(probe, status);
        var docs = new List<OpenDocument> { unchanged, caughtUp, reloaded, prompted };

        var prompt = w.Scan(docs);

        Assert.Equal(t0, unchanged.LastWriteUtc);                 // 1: untouched
        Assert.Equal(t1, caughtUp.LastWriteUtc);                  // 2: mtime caught up
        Assert.True(caughtUp.IsDirty);                            //    but edits preserved
        Assert.Equal("disk", reloaded.Text);                     // 4: silently reloaded
        Assert.False(reloaded.IsDirty);
        Assert.Equal("Reloaded clean.txt — changed on disk", status.Text);
        Assert.NotNull(prompt);                                   // 3: dirty -> prompt
        Assert.Equal(prompted.Path, prompt!.Doc.Path);
        Assert.Equal("disk", prompt.DiskText);
    }

    [Fact] public void Scan_is_a_no_op_while_a_prompt_is_active()
    {
        var probe = new FakeExternalFileProbe();
        var doc = Doc(@"C:\p\a.txt", "mine", true, DateTime.UtcNow.AddMinutes(-1));
        probe.Files[doc.Path] = (DateTime.UtcNow, "disk", true);
        var w = new ExternalChangeWatcher(probe, new CodeCrack.App.Core.Services.StatusBus())
        {
            PromptActive = true
        };
        Assert.Null(w.Scan(new List<OpenDocument> { doc }));
    }

    [Fact] public void Resolve_reload_adopts_disk_text_keep_records_new_mtime()
    {
        var t1 = DateTime.UtcNow;
        var doc = Doc(@"C:\p\a.txt", "mine", true, t1.AddMinutes(-1));
        var status = new CodeCrack.App.Core.Services.StatusBus();
        var w = new ExternalChangeWatcher(new FakeExternalFileProbe(), status);

        w.Resolve(new ExternalChangePrompt(doc, "disk", t1), reload: true);
        Assert.Equal("disk", doc.Text);
        Assert.False(doc.IsDirty);
        Assert.Equal(t1, doc.LastWriteUtc);
        Assert.Equal("Reloaded a.txt", status.Text);

        var doc2 = Doc(@"C:\p\b.txt", "mine", true, t1.AddMinutes(-1));
        w.Resolve(new ExternalChangePrompt(doc2, "disk", t1), reload: false);
        Assert.Equal("mine", doc2.Text);          // kept
        Assert.Equal(t1, doc2.LastWriteUtc);      // but mtime recorded so it stops asking
        Assert.Equal("Kept your version of b.txt", status.Text);
    }
}
```
- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp/CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~ExternalChangeTests"` → `The type or namespace name 'ExternalChangeWatcher' could not be found`.
- [ ] **Step 3: Implement**
```csharp
// winapp/CodeCrack.App.Core/Services/ExternalChangeWatcher.cs
using System;
using System.Collections.Generic;
using System.IO;
using CodeCrack.App.Core.Editor;

namespace CodeCrack.App.Core.Services;

/// Filesystem probe the watcher depends on (faked in tests).
public interface IExternalFileProbe
{
    bool Exists(string path);
    DateTime LastWriteUtc(string path);
    string ReadText(string path);
}

/// Real probe over System.IO (UTF-8 read).
public sealed class DiskFileProbe : IExternalFileProbe
{
    public bool Exists(string path) => File.Exists(path);
    public DateTime LastWriteUtc(string path) => File.GetLastWriteTimeUtc(path);
    public string ReadText(string path) => File.ReadAllText(path);
}

/// A pending "file changed on disk" prompt for a dirty document whose disk content differs.
public sealed record ExternalChangePrompt(OpenDocument Doc, string DiskText, DateTime DiskWriteUtc);

/// The 4-branch external-change state machine, run on Window.Activated (not a continuous
/// FileSystemWatcher). One prompt at a time via PromptActive.
public sealed class ExternalChangeWatcher
{
    private readonly IExternalFileProbe _probe;
    private readonly StatusBus _status;

    public ExternalChangeWatcher(IExternalFileProbe probe, StatusBus status)
    {
        _probe = probe;
        _status = status;
    }

    /// True while a "File changed on disk" dialog is open — Scan is then a no-op (single-dialog guard).
    public bool PromptActive { get; set; }

    /// Reconcile each open doc against disk. Clean docs reload silently; a dirty differing doc
    /// returns a prompt (the rest are handled after it resolves). Returns null if nothing prompts.
    public ExternalChangePrompt? Scan(IReadOnlyList<OpenDocument> docs)
    {
        if (PromptActive) return null;                       // (1) don't stack prompts
        foreach (var doc in docs)
        {
            if (!_probe.Exists(doc.Path)) continue;
            var disk = _probe.LastWriteUtc(doc.Path);
            if (disk <= doc.LastWriteUtc) continue;          // (1) no newer mtime

            string diskText;
            try { diskText = _probe.ReadText(doc.Path); }
            catch { continue; }

            if (diskText == doc.Text)                        // (2) same content, newer mtime: catch up
            {
                doc.LastWriteUtc = disk;
                continue;
            }
            if (doc.IsDirty)                                 // (3) differs + dirty: prompt
                return new ExternalChangePrompt(doc, diskText, disk);

            doc.Text = diskText;                            // (4) differs + clean: silent reload
            doc.IsDirty = false;
            doc.LastWriteUtc = disk;
            _status.Set($"Reloaded {doc.Name} — changed on disk");
        }
        return null;
    }

    /// Resolve a prompted change. Reload adopts the disk version and drops edits; Keep retains the
    /// user's text but records the new mtime so it stops asking. Caller lowers PromptActive and re-Scans.
    public void Resolve(ExternalChangePrompt prompt, bool reload)
    {
        if (reload)
        {
            prompt.Doc.Text = prompt.DiskText;
            prompt.Doc.IsDirty = false;
        }
        prompt.Doc.LastWriteUtc = prompt.DiskWriteUtc;
        _status.Set(reload ? $"Reloaded {prompt.Doc.Name}"
                           : $"Kept your version of {prompt.Doc.Name}");
    }
}
```
- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp/CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~ExternalChangeTests"`. **Manual verify (view):** `MainWindow.OnActivated` calls `Scan`; if it returns a prompt, set `PromptActive = true`, show a `MessageBox` titled "File changed on disk" with Reload / Keep My Version, call `Resolve(prompt, reload)`, clear `PromptActive`, then `Scan` again (dispatcher) to drain remaining changes.
- [ ] **Step 5: Commit** — `git add winapp/CodeCrack.App.Core/Services/ExternalChangeWatcher.cs winapp/CodeCrack.Tests/Fakes/FakeExternalFileProbe.cs winapp/CodeCrack.Tests/ExternalChangeTests.cs && git commit -m "feat(winapp): external-change 4-branch watcher with single-dialog guard"`

---

### Task 4.7: JsonAppSettings + SettingsWindow + DPAPI
**Files:**
- Create: `winapp/CodeCrack.App.Core/Settings/JsonAppSettings.cs`
- Test: `winapp/CodeCrack.Tests/JsonAppSettingsTests.cs`
- Create (view/Windows-only): `winapp/CodeCrackApp/Services/Dpapi.cs`, `winapp/CodeCrackApp/Settings/SettingsWindow.xaml`, `winapp/CodeCrackApp/Settings/SettingsWindow.xaml.cs`
- Modify: `winapp/CodeCrackApp/CodeCrackApp.csproj` (add `System.Security.Cryptography.ProtectedData` package)

**Interfaces:**
Consumes: `IAppSettings` (contract — `EnginePathOverride`, `FontSize`, `EditorTheme`, `IndentUsesSpaces`, `IndentWidth`, `ClaudeApiKey`, `Save()`).
Produces: `public sealed class JsonAppSettings : IAppSettings` — `static string DefaultPath`, `JsonAppSettings()`, `JsonAppSettings(string path)`. `public static class Dpapi` — `string Protect(string)`, `string Unprotect(string)`.

Note: `ClaudeApiKey` stores the DPAPI-protected base64 blob (Core just persists the string; the WPF `SettingsWindow` protects on set / unprotects on load via `Dpapi`). This keeps Core free of the Windows-only `ProtectedData` dependency so it still builds on Linux CI.

- [ ] **Step 1: Write the failing test** — defaults, round-trip, and that Core does not decrypt.
```csharp
// winapp/CodeCrack.Tests/JsonAppSettingsTests.cs
using System.IO;
using CodeCrack.App.Core.Settings;
using Xunit;

namespace CodeCrack.Tests;

public class JsonAppSettingsTests : System.IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), "cc_settings_" + Path.GetRandomFileName(), "settings.json");

    public void Dispose()
    {
        var d = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    [Fact] public void Defaults_match_the_contract()
    {
        var s = new JsonAppSettings(_path);
        Assert.Equal(13, s.FontSize);
        Assert.Equal("system", s.EditorTheme);
        Assert.True(s.IndentUsesSpaces);
        Assert.Equal(4, s.IndentWidth);
        Assert.Equal("", s.EnginePathOverride);
        Assert.Equal("", s.ClaudeApiKey);
    }

    [Fact] public void Save_then_reload_persists_all_values()
    {
        var s = new JsonAppSettings(_path)
        {
            FontSize = 18, EditorTheme = "dark", IndentUsesSpaces = false,
            IndentWidth = 2, EnginePathOverride = @"C:\eng", ClaudeApiKey = "BLOB=="
        };
        s.Save();

        var reloaded = new JsonAppSettings(_path);
        Assert.Equal(18, reloaded.FontSize);
        Assert.Equal("dark", reloaded.EditorTheme);
        Assert.False(reloaded.IndentUsesSpaces);
        Assert.Equal(2, reloaded.IndentWidth);
        Assert.Equal(@"C:\eng", reloaded.EnginePathOverride);
        Assert.Equal("BLOB==", reloaded.ClaudeApiKey); // stored verbatim; no decryption in Core
    }

    [Fact] public void On_disk_json_uses_the_contract_keys()
    {
        new JsonAppSettings(_path) { FontSize = 20 }.Save();
        var json = File.ReadAllText(_path);
        Assert.Contains("\"fontSize\": 20", json);
        Assert.Contains("\"claudeAPIKey\"", json);
        Assert.Contains("\"enginePathOverride\"", json);
    }
}
```
- [ ] **Step 2: Run it, expect FAIL** — `dotnet test winapp/CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~JsonAppSettingsTests"` → `The type or namespace name 'JsonAppSettings' could not be found`.
- [ ] **Step 3: Implement**
```csharp
// winapp/CodeCrack.App.Core/Settings/JsonAppSettings.cs
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeCrack.App.Core.Settings;

/// IAppSettings persisted as JSON under %APPDATA%\CodeCrack\settings.json. Keys and defaults match
/// the mac app. claudeAPIKey holds an already-encrypted blob (DPAPI is applied by the WPF layer),
/// so Core has no Windows-only crypto dependency and still builds on Linux CI.
public sealed class JsonAppSettings : IAppSettings
{
    private sealed class Model
    {
        [JsonPropertyName("fontSize")] public int FontSize { get; set; } = 13;
        [JsonPropertyName("editorTheme")] public string EditorTheme { get; set; } = "system";
        [JsonPropertyName("indentUsesSpaces")] public bool IndentUsesSpaces { get; set; } = true;
        [JsonPropertyName("indentWidth")] public int IndentWidth { get; set; } = 4;
        [JsonPropertyName("enginePathOverride")] public string EnginePathOverride { get; set; } = "";
        [JsonPropertyName("claudeAPIKey")] public string ClaudeAPIKey { get; set; } = "";
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Model _m;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CodeCrack", "settings.json");

    public JsonAppSettings() : this(DefaultPath) { }

    public JsonAppSettings(string path)
    {
        _path = path;
        _m = File.Exists(path)
            ? JsonSerializer.Deserialize<Model>(File.ReadAllText(path), Options) ?? new Model()
            : new Model();
    }

    public int FontSize { get => _m.FontSize; set => _m.FontSize = value; }
    public string EditorTheme { get => _m.EditorTheme; set => _m.EditorTheme = value; }
    public bool IndentUsesSpaces { get => _m.IndentUsesSpaces; set => _m.IndentUsesSpaces = value; }
    public int IndentWidth { get => _m.IndentWidth; set => _m.IndentWidth = value; }
    public string EnginePathOverride { get => _m.EnginePathOverride; set => _m.EnginePathOverride = value; }
    public string ClaudeApiKey { get => _m.ClaudeAPIKey; set => _m.ClaudeAPIKey = value; }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_m, Options));
    }
}
```
Windows-only DPAPI wrapper + settings view (manual-verify):
```xml
<!-- winapp/CodeCrackApp/CodeCrackApp.csproj  — add inside an <ItemGroup> -->
<PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />
```
```csharp
// winapp/CodeCrackApp/Services/Dpapi.cs
using System;
using System.Security.Cryptography;
using System.Text;

namespace CodeCrackApp.Services;

/// Encrypt/decrypt the Claude API key with the current-user DPAPI scope. Stored as base64 so it
/// travels safely inside the JSON settings file — never plaintext.
public static class Dpapi
{
    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return "";
        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedBase64), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return ""; } // unreadable (different user/machine) -> treat as unset
    }
}
```
```xml
<!-- winapp/CodeCrackApp/Settings/SettingsWindow.xaml -->
<Window x:Class="CodeCrackApp.Settings.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Preferences" Width="480" Height="300"
        ResizeMode="NoResize" WindowStartupLocation="CenterOwner">
    <DockPanel Margin="12">
        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal"
                    HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button Content="Done" Width="80" IsDefault="True" Click="OnDone"/>
        </StackPanel>
        <TabControl>
            <TabItem Header="Editor">
                <StackPanel Margin="10">
                    <TextBlock Text="Font size"/>
                    <Slider x:Name="FontSlider" Minimum="9" Maximum="24" TickFrequency="1"
                            IsSnapToTickEnabled="True"/>
                    <TextBlock Text="Indent width" Margin="0,10,0,0"/>
                    <ComboBox x:Name="IndentCombo">
                        <ComboBoxItem Content="2"/>
                        <ComboBoxItem Content="4"/>
                        <ComboBoxItem Content="8"/>
                    </ComboBox>
                    <CheckBox x:Name="SpacesCheck" Content="Indent using spaces" Margin="0,10,0,0"/>
                </StackPanel>
            </TabItem>
            <TabItem Header="Engine">
                <StackPanel Margin="10">
                    <TextBlock Text="Engine path override"/>
                    <TextBox x:Name="EngineBox"/>
                </StackPanel>
            </TabItem>
            <TabItem Header="AI">
                <StackPanel Margin="10">
                    <TextBlock Text="Claude API key"/>
                    <PasswordBox x:Name="ApiKeyBox"/>
                </StackPanel>
            </TabItem>
        </TabControl>
    </DockPanel>
</Window>
```
```csharp
// winapp/CodeCrackApp/Settings/SettingsWindow.xaml.cs
using System.Windows;
using System.Windows.Controls;
using CodeCrack.App.Core.Settings;
using CodeCrackApp.Services;

namespace CodeCrackApp.Settings;

public partial class SettingsWindow : Window
{
    private readonly IAppSettings _settings;

    public SettingsWindow(IAppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        FontSlider.Value = settings.FontSize;
        IndentCombo.SelectedIndex = settings.IndentWidth switch { 2 => 0, 8 => 2, _ => 1 };
        SpacesCheck.IsChecked = settings.IndentUsesSpaces;
        EngineBox.Text = settings.EnginePathOverride;
        ApiKeyBox.Password = Dpapi.Unprotect(settings.ClaudeApiKey); // show decrypted (as dots)
    }

    private void OnDone(object sender, RoutedEventArgs e)
    {
        _settings.FontSize = (int)FontSlider.Value;
        _settings.IndentWidth = ((ComboBoxItem)IndentCombo.SelectedItem).Content is "2" ? 2
                              : ((ComboBoxItem)IndentCombo.SelectedItem).Content is "8" ? 8 : 4;
        _settings.IndentUsesSpaces = SpacesCheck.IsChecked == true;
        _settings.EnginePathOverride = EngineBox.Text;
        _settings.ClaudeApiKey = Dpapi.Protect(ApiKeyBox.Password); // encrypt before persisting
        _settings.Save();
        Close();
    }
}
```
- [ ] **Step 4: Run tests, expect PASS** — `dotnet test winapp/CodeCrack.Tests/CodeCrack.Tests.csproj --filter "FullyQualifiedName~JsonAppSettingsTests"`. **Manual verify:** open Preferences (480×300, Editor/Engine/AI tabs); font slider clamps 9–24, indent combo offers 2/4/8; type a Claude key, click Done, open `%APPDATA%\CodeCrack\settings.json` and confirm `claudeAPIKey` is a base64 blob (not the plaintext key); reopen Preferences and confirm the key field is populated (masked).
- [ ] **Step 5: Commit** — `git add winapp/CodeCrack.App.Core/Settings/JsonAppSettings.cs winapp/CodeCrack.Tests/JsonAppSettingsTests.cs winapp/CodeCrackApp/Services/Dpapi.cs winapp/CodeCrackApp/Settings/SettingsWindow.xaml winapp/CodeCrackApp/Settings/SettingsWindow.xaml.cs winapp/CodeCrackApp/CodeCrackApp.csproj && git commit -m "feat(winapp): JSON app settings + tabbed SettingsWindow + DPAPI-protected API key"`

---

## Phase 5: Packaging & CI

These four tasks assume Phases 1–4 exist: the solution builds a WPF app whose published executable is `CodeCrack.exe` (project `winapp/CodeCrackApp/CodeCrackApp.csproj`, `AssemblyName=CodeCrack`, `net8.0-windows`), the engine lives at `engine/codecrack/` with `engine/pyproject.toml`, and `Analyzer`/`PythonInvocation` resolve the interpreter at `AppContext.BaseDirectory\python\python.exe` and the engine at `AppContext.BaseDirectory\Resources\engine`. Phase 5 adds no C#; it produces the shippable artifact.

The PowerShell scripts are written **dot-sourceable**: pure helper functions live at the top and the entry point runs only when the file is executed (not when Pester dot-sources it via `. $path`, where `$MyInvocation.InvocationName -eq '.'`). Tests are Pester v5. Install once per machine/runner:

```powershell
Install-Module Pester -MinimumVersion 5.5.0 -Force -Scope CurrentUser -SkipPublisherCheck
```

Run all Phase-5 tests with:

```powershell
pwsh -NoProfile -Command "Invoke-Pester -Path scripts\tests, winapp\tests -Output Detailed"
```

---

### Task 5.1: `scripts/fetch-python-runtime.ps1` — embedded CPython + pytest fetcher (Windows)
**Files:**
- Create: `scripts/fetch-python-runtime.ps1`
- Test: `scripts/tests/fetch-python-runtime.Tests.ps1`

**Interfaces:**
- Consumes: nothing (top of the packaging chain). Versions **must stay synced** with `scripts/fetch-python-runtime.sh` (`PBS_RELEASE=20260623`, `PY_VERSION=3.12.13`, `PYTEST_SPEC=pytest>=8,<9`).
- Produces: functions `Get-PbsAsset`, `Get-PbsUrl`, `Invoke-FetchRuntime` (dot-sourceable); output layout `build\python-runtime\python\python.exe` + `...\Lib\site-packages\pytest`, tarball cached under `build\python-cache\`, pytest install guarded by stamp `build\python-runtime\python\.codecrack-pytest-installed`. `make-app.ps1` (Task 5.2) invokes this script and copies `build\python-runtime\python` beside the exe.

> **Asset URL verified:** `https://github.com/astral-sh/python-build-standalone/releases/download/20260623/cpython-3.12.13+20260623-x86_64-pc-windows-msvc-install_only.tar.gz` returns `302 Found` → signed CDN (asset exists; 200 on redirect follow). The `install_only` Windows build unpacks a top-level `python\` dir with `python.exe` at its root and bundles `pip`, and `tar` (bsdtar) ships on windows-latest / Win10+.

- [ ] **Step 1: Write the failing test** — create `scripts/tests/fetch-python-runtime.Tests.ps1`:
```powershell
BeforeAll {
    # Dot-source the script under test; its entry point is guarded so nothing runs on import.
    . (Join-Path $PSScriptRoot '..\fetch-python-runtime.ps1')
}

Describe 'fetch-python-runtime helpers' {
    It 'builds the exact pinned Windows x64 install_only asset name' {
        Get-PbsAsset '3.12.13' '20260623' |
            Should -BeExactly 'cpython-3.12.13+20260623-x86_64-pc-windows-msvc-install_only.tar.gz'
    }

    It 'builds the GitHub release download URL from the asset + release tag' {
        $asset = 'cpython-3.12.13+20260623-x86_64-pc-windows-msvc-install_only.tar.gz'
        Get-PbsUrl $asset '20260623' |
            Should -BeExactly "https://github.com/astral-sh/python-build-standalone/releases/download/20260623/$asset"
    }

    It 'exposes an Invoke-FetchRuntime entry point that is not run on dot-source' {
        Get-Command Invoke-FetchRuntime -CommandType Function | Should -Not -BeNullOrEmpty
    }

    It 'keeps the pinned versions in sync with the .sh sibling' {
        $sh = Get-Content (Join-Path $PSScriptRoot '..\fetch-python-runtime.sh') -Raw
        $sh | Should -Match 'PBS_RELEASE="20260623"'
        $sh | Should -Match 'PY_VERSION="3.12.13"'
    }
}
```

- [ ] **Step 2: Run it, expect FAIL** — `pwsh -NoProfile -Command "Invoke-Pester -Path scripts\tests\fetch-python-runtime.Tests.ps1 -Output Detailed"`. Fails in `BeforeAll` with `The term '...\fetch-python-runtime.ps1' is not recognized` / cannot dot-source (file does not exist yet), so all four `It` blocks error.

- [ ] **Step 3: Implement** — create `scripts/fetch-python-runtime.ps1`:
```powershell
#!/usr/bin/env pwsh
# Fetch an embeddable CPython (python-build-standalone) for Windows x64 and install
# pytest into it, producing a self-contained interpreter the Windows app bundles so
# users need no system Python. Mirrors scripts/fetch-python-runtime.sh — KEEP THE
# PINNED VERSIONS BELOW IN SYNC with that file.
#
# Output layout (relative to repo root):
#   build\python-runtime\python\python.exe                     <- the interpreter
#   build\python-runtime\python\Lib\site-packages\pytest\...   <- pytest
#
# Re-runnable and cached: tarball -> build\python-cache\, runtime -> build\python-runtime\,
# pytest install guarded by a stamp file. Delete build\python-runtime\ to force a rebuild.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Pinned versions (bump together with scripts/fetch-python-runtime.sh) ---
$script:PbsRelease = '20260623'
$script:PyVersion  = '3.12.13'
$script:PytestSpec = 'pytest>=8,<9'

function Get-PbsAsset {
    param([Parameter(Mandatory)][string]$PyVersion,
          [Parameter(Mandatory)][string]$PbsRelease)
    "cpython-$PyVersion+$PbsRelease-x86_64-pc-windows-msvc-install_only.tar.gz"
}

function Get-PbsUrl {
    param([Parameter(Mandatory)][string]$Asset,
          [Parameter(Mandatory)][string]$PbsRelease)
    "https://github.com/astral-sh/python-build-standalone/releases/download/$PbsRelease/$Asset"
}

function Invoke-FetchRuntime {
    $repoRoot   = Split-Path -Parent $PSScriptRoot       # scripts\ -> repo root
    $cacheDir   = Join-Path $repoRoot 'build\python-cache'
    $runtimeDir = Join-Path $repoRoot 'build\python-runtime'
    $pyHome     = Join-Path $runtimeDir 'python'
    $pythonBin  = Join-Path $pyHome 'python.exe'
    $stamp      = Join-Path $pyHome '.codecrack-pytest-installed'

    $asset   = Get-PbsAsset $script:PyVersion $script:PbsRelease
    $url     = Get-PbsUrl $asset $script:PbsRelease
    $tarball = Join-Path $cacheDir $asset

    New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null

    if (-not (Test-Path $tarball)) {
        Write-Host "Downloading $asset ..."
        Invoke-WebRequest -Uri $url -OutFile "$tarball.tmp"
        Move-Item -Force "$tarball.tmp" $tarball
    } else {
        Write-Host "Using cached $asset"
    }

    if (-not (Test-Path $pythonBin)) {
        Write-Host "Extracting runtime into $runtimeDir ..."
        if (Test-Path $runtimeDir) { Remove-Item -Recurse -Force $runtimeDir }
        New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null
        tar -xzf $tarball -C $runtimeDir   # unpacks a top-level python\ directory
        if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE" }
    }

    if (-not (Test-Path $stamp)) {
        Write-Host "Installing $script:PytestSpec into the embedded runtime ..."
        & $pythonBin -m pip install --no-warn-script-location --upgrade pip
        if ($LASTEXITCODE -ne 0) { throw "pip upgrade failed ($LASTEXITCODE)" }
        & $pythonBin -m pip install $script:PytestSpec
        if ($LASTEXITCODE -ne 0) { throw "pip install pytest failed ($LASTEXITCODE)" }
        & $pythonBin -c "import pytest; print('pytest', pytest.__version__, 'ready')"
        if ($LASTEXITCODE -ne 0) { throw "pytest import check failed ($LASTEXITCODE)" }
        New-Item -ItemType File -Path $stamp | Out-Null
    } else {
        Write-Host "pytest already installed in the embedded runtime"
    }

    Write-Host "Embedded runtime ready: $pythonBin"
    $pythonBin
}

# Run the entry point only on direct execution. Pester dot-sources this file
# (`. $path`), where InvocationName is '.', so tests import the functions without
# triggering a network download.
if ($MyInvocation.InvocationName -ne '.') {
    Invoke-FetchRuntime
}
```

- [ ] **Step 4: Run tests, expect PASS** — `pwsh -NoProfile -Command "Invoke-Pester -Path scripts\tests\fetch-python-runtime.Tests.ps1 -Output Detailed"` (4 passed). Optional real fetch (network; ~50 MB): `pwsh -NoProfile -File scripts\fetch-python-runtime.ps1` then confirm `Test-Path build\python-runtime\python\python.exe` is `True` and `build\python-runtime\python\python.exe -c "import pytest"` exits 0.

- [ ] **Step 5: Commit** — `git add scripts/fetch-python-runtime.ps1 scripts/tests/fetch-python-runtime.Tests.ps1` then `git commit -m "build(win): add fetch-python-runtime.ps1 for embedded CPython + pytest"`.

---

### Task 5.2: `winapp/make-app.ps1` — self-contained single-file publish + bundle
**Files:**
- Create: `winapp/make-app.ps1`
- Test: `winapp/tests/make-app.Tests.ps1`

**Interfaces:**
- Consumes: `scripts/fetch-python-runtime.ps1` (Task 5.1, invoked to populate `build\python-runtime\python`); the published `CodeCrack.exe` from `winapp/CodeCrackApp/CodeCrackApp.csproj`; the engine at `engine/codecrack` + `engine/pyproject.toml`. Env: `$env:CODECRACK_WINDOWS_CERT` (+ `$env:CODECRACK_WINDOWS_CERT_PASSWORD`) enable signing; `$env:CODECRACK_SKIP_LAUNCH` suppresses launch.
- Produces: functions `Get-SignTool`, `Copy-Engine`, `Invoke-MakeApp` (dot-sourceable); the artifact tree `dist\CodeCrack\` containing `CodeCrack.exe`, `Resources\engine\{codecrack,pyproject.toml}` (no `__pycache__`), and `python\python.exe`. Consumed by CI (Task 5.3) which zips `dist\CodeCrack\*`.

> **Publish flags** (spec §10, verified against `dotnet publish` for WPF): `-c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`, **never** `-p:PublishTrimmed=true` (WPF is not trim-compatible). `PythonInvocation`/`EngineLocator` use `AppContext.BaseDirectory` (not `Assembly.Location`, which is `""` under single-file), so the `python\` and `Resources\engine` folders resolve correctly beside the extracted exe.

- [ ] **Step 1: Write the failing test** — create `winapp/tests/make-app.Tests.ps1`:
```powershell
BeforeAll {
    . (Join-Path $PSScriptRoot '..\make-app.ps1')
}

Describe 'make-app helpers' {
    It 'Get-SignTool returns null when CODECRACK_WINDOWS_CERT is unset' {
        $saved = $env:CODECRACK_WINDOWS_CERT
        Remove-Item Env:CODECRACK_WINDOWS_CERT -ErrorAction SilentlyContinue
        try { Get-SignTool | Should -BeNullOrEmpty }
        finally { if ($null -ne $saved) { $env:CODECRACK_WINDOWS_CERT = $saved } }
    }

    It 'Copy-Engine copies the package + pyproject and prunes __pycache__' {
        $work = Join-Path ([System.IO.Path]::GetTempPath()) ("cc-eng-" + [guid]::NewGuid())
        $src  = Join-Path $work 'engine'
        $dst  = Join-Path $work 'out\Resources\engine'
        New-Item -ItemType Directory -Force -Path (Join-Path $src 'codecrack\__pycache__') | Out-Null
        Set-Content (Join-Path $src 'codecrack\__main__.py')       '# entry'
        Set-Content (Join-Path $src 'codecrack\__pycache__\x.pyc') 'junk'
        Set-Content (Join-Path $src 'pyproject.toml')              '[project]'
        try {
            Copy-Engine $src $dst
            Test-Path (Join-Path $dst 'codecrack\__main__.py') | Should -BeTrue
            Test-Path (Join-Path $dst 'pyproject.toml')        | Should -BeTrue
            Test-Path (Join-Path $dst 'codecrack\__pycache__') | Should -BeFalse
        }
        finally { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
    }

    It 'exposes an Invoke-MakeApp entry point that is not run on dot-source' {
        Get-Command Invoke-MakeApp -CommandType Function | Should -Not -BeNullOrEmpty
    }
}
```

- [ ] **Step 2: Run it, expect FAIL** — `pwsh -NoProfile -Command "Invoke-Pester -Path winapp\tests\make-app.Tests.ps1 -Output Detailed"`. Fails in `BeforeAll` — cannot dot-source `..\make-app.ps1` (does not exist), so all three `It` blocks error.

- [ ] **Step 3: Implement** — create `winapp/make-app.ps1`:
```powershell
#!/usr/bin/env pwsh
# Publish the CodeCrack WPF app as a self-contained single-file Windows x64 build and
# assemble the shippable dist\CodeCrack\ tree: the exe + bundled engine + embedded
# CPython/pytest. Mirrors macapp/make-app.sh.
#
# Code signing is OPTIONAL and guarded: it runs only when $env:CODECRACK_WINDOWS_CERT
# points at a .pfx (with $env:CODECRACK_WINDOWS_CERT_PASSWORD). Launch is suppressed
# when $env:CODECRACK_SKIP_LAUNCH is set (CI / headless).
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SignTool {
    # Returns signtool.exe's path, or $null when signing isn't configured.
    if (-not $env:CODECRACK_WINDOWS_CERT) { return $null }
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $sdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($sdk) { return $sdk.FullName }
    return $null
}

function Copy-Engine {
    # Copy engine\codecrack + pyproject.toml into $Dest, then prune __pycache__.
    param([Parameter(Mandatory)][string]$EngineSrc,
          [Parameter(Mandatory)][string]$Dest)
    if (Test-Path $Dest) { Remove-Item -Recurse -Force $Dest }
    New-Item -ItemType Directory -Force -Path $Dest | Out-Null
    Copy-Item -Recurse (Join-Path $EngineSrc 'codecrack') (Join-Path $Dest 'codecrack')
    Copy-Item (Join-Path $EngineSrc 'pyproject.toml') (Join-Path $Dest 'pyproject.toml')
    Get-ChildItem -Path $Dest -Recurse -Directory -Filter '__pycache__' -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force
}

function Invoke-MakeApp {
    $winapp   = $PSScriptRoot
    $repoRoot = Split-Path -Parent $winapp                # winapp\ -> repo root
    $proj     = Join-Path $winapp 'CodeCrackApp\CodeCrackApp.csproj'
    $publishDir = Join-Path $winapp 'CodeCrackApp\bin\Release\net8.0-windows\win-x64\publish'
    $distDir  = Join-Path $repoRoot 'dist\CodeCrack'

    Write-Host 'Publishing (self-contained, single-file, WPF: no trimming) ...'
    dotnet publish $proj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    if (Test-Path $distDir) { Remove-Item -Recurse -Force $distDir }
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    Copy-Item -Recurse (Join-Path $publishDir '*') $distDir

    Write-Host 'Bundling engine into Resources\engine ...'
    Copy-Engine (Join-Path $repoRoot 'engine') (Join-Path $distDir 'Resources\engine')

    Write-Host 'Preparing embedded Python runtime ...'
    & (Join-Path $repoRoot 'scripts\fetch-python-runtime.ps1')
    if ($LASTEXITCODE -ne 0) { throw "fetch-python-runtime.ps1 failed ($LASTEXITCODE)" }
    $pyRuntime = Join-Path $repoRoot 'build\python-runtime\python'
    $pyDest    = Join-Path $distDir 'python'
    if (Test-Path $pyDest) { Remove-Item -Recurse -Force $pyDest }
    Copy-Item -Recurse $pyRuntime $pyDest
    Get-ChildItem -Path $pyDest -Recurse -Directory -Filter '__pycache__' -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force

    $exe = Join-Path $distDir 'CodeCrack.exe'
    $signtool = Get-SignTool
    if ($signtool) {
        Write-Host "Code signing $exe ..."
        & $signtool sign /fd SHA256 /f $env:CODECRACK_WINDOWS_CERT `
            /p $env:CODECRACK_WINDOWS_CERT_PASSWORD `
            /tr http://timestamp.digicert.com /td SHA256 $exe
        if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
    } else {
        Write-Host 'Code signing skipped: set CODECRACK_WINDOWS_CERT (+ CODECRACK_WINDOWS_CERT_PASSWORD) to sign.'
    }

    Write-Host "Built $distDir"

    if ($env:CODECRACK_SKIP_LAUNCH) {
        Write-Host 'CODECRACK_SKIP_LAUNCH set; not launching.'
        return
    }
    Start-Process $exe
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-MakeApp
}
```

- [ ] **Step 4: Run tests, expect PASS** — `pwsh -NoProfile -Command "Invoke-Pester -Path winapp\tests\make-app.Tests.ps1 -Output Detailed"` (3 passed). Manual E2E (after Phases 1–4 exist): `pwsh -NoProfile -File winapp\make-app.ps1` (set `$env:CODECRACK_SKIP_LAUNCH='1'` to stay headless), then verify `Test-Path dist\CodeCrack\CodeCrack.exe`, `Test-Path dist\CodeCrack\python\python.exe`, and `Test-Path dist\CodeCrack\Resources\engine\codecrack\__main__.py` are all `True`.

- [ ] **Step 5: Commit** — `git add winapp/make-app.ps1 winapp/tests/make-app.Tests.ps1` then `git commit -m "build(win): add make-app.ps1 self-contained publish + engine/python bundle"`.

---

### Task 5.3: `.github/workflows/ci.yml` — `windows-build` job
**Files:**
- Modify: `.github/workflows/ci.yml`
- Test: `winapp/tests/ci-windows-build.Tests.ps1`

**Interfaces:**
- Consumes: `winapp/make-app.ps1` (Task 5.2) run with `CODECRACK_SKIP_LAUNCH=1`; the `dist\CodeCrack\` tree it produces.
- Produces: a `windows-build` job (`windows-latest`) that publishes, bundles, zips `dist\CodeCrack\*` → `CodeCrack-windows.zip`, and uploads it as artifact `CodeCrack-windows` with `if-no-files-found: error`.

- [ ] **Step 1: Write the failing test** — create `winapp/tests/ci-windows-build.Tests.ps1`:
```powershell
BeforeAll {
    $script:Ci = Get-Content (Join-Path $PSScriptRoot '..\..\.github\workflows\ci.yml') -Raw
}

Describe 'ci.yml windows-build job' {
    It 'declares a windows-build job on windows-latest' {
        $script:Ci | Should -Match '(?m)^\s{2}windows-build:'
        $script:Ci | Should -Match 'runs-on:\s*windows-latest'
    }
    It 'pins .NET 8.0.x via setup-dotnet' {
        $script:Ci | Should -Match 'actions/setup-dotnet@v4'
        $script:Ci | Should -Match "dotnet-version:\s*['""]?8\.0\.x"
    }
    It 'runs make-app.ps1 headless via CODECRACK_SKIP_LAUNCH' {
        $script:Ci | Should -Match 'CODECRACK_SKIP_LAUNCH:\s*["'']?1'
        $script:Ci | Should -Match 'make-app\.ps1'
    }
    It 'zips dist\CodeCrack into CodeCrack-windows.zip' {
        $script:Ci | Should -Match 'Compress-Archive'
        $script:Ci | Should -Match 'CodeCrack-windows\.zip'
    }
    It 'uploads the artifact and fails when no files are produced' {
        $script:Ci | Should -Match 'actions/upload-artifact@v4'
        $script:Ci | Should -Match 'if-no-files-found:\s*error'
    }
}
```

- [ ] **Step 2: Run it, expect FAIL** — `pwsh -NoProfile -Command "Invoke-Pester -Path winapp\tests\ci-windows-build.Tests.ps1 -Output Detailed"`. Fails: no `windows-build:` key and no `Compress-Archive`/`CodeCrack-windows.zip` in the current `ci.yml`, so all five `It` blocks fail on `Should -Match`.

- [ ] **Step 3: Implement** — append this job under `jobs:` in `.github/workflows/ci.yml` (after `macos-build`, same indentation as the existing `engine-tests:` / `macos-build:` keys):
```yaml
  windows-build:
    name: Windows app (self-contained + dist artifact)
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      # Publishes the WPF app self-contained/single-file, bundles the engine and an
      # embedded CPython/pytest (scripts/fetch-python-runtime.ps1), and produces
      # dist\CodeCrack\. CODECRACK_SKIP_LAUNCH keeps it headless. Signing is guarded
      # and skips gracefully when the secret is absent (e.g. forks / PRs).
      - name: Build the Windows app bundle
        working-directory: winapp
        shell: pwsh
        env:
          CODECRACK_SKIP_LAUNCH: "1"
          CODECRACK_WINDOWS_CERT: ${{ secrets.CODECRACK_WINDOWS_CERT }}
          CODECRACK_WINDOWS_CERT_PASSWORD: ${{ secrets.CODECRACK_WINDOWS_CERT_PASSWORD }}
        run: ./make-app.ps1

      - name: Package dist\CodeCrack as a zip
        shell: pwsh
        run: Compress-Archive -Path dist\CodeCrack\* -DestinationPath CodeCrack-windows.zip -Force

      - name: Upload distributable artifact
        uses: actions/upload-artifact@v4
        with:
          name: CodeCrack-windows
          path: CodeCrack-windows.zip
          if-no-files-found: error
```

- [ ] **Step 4: Run tests, expect PASS** — `pwsh -NoProfile -Command "Invoke-Pester -Path winapp\tests\ci-windows-build.Tests.ps1 -Output Detailed"` (5 passed). Sanity-check the YAML parses: `pwsh -NoProfile -Command "python -c 'import yaml,sys; yaml.safe_load(open(\".github/workflows/ci.yml\"))'"` exits 0 (any YAML validator is fine).

- [ ] **Step 5: Commit** — `git add .github/workflows/ci.yml winapp/tests/ci-windows-build.Tests.ps1` then `git commit -m "ci: add windows-build job publishing + zipping CodeCrack-windows.zip"`.

---

### Task 5.4: `.gitignore` hardening + `winapp/README.md`
**Files:**
- Modify: `.gitignore`
- Create: `winapp/README.md`
- Test: `winapp/tests/repo-hygiene.Tests.ps1`

**Interfaces:**
- Consumes: the build/run flow established by Tasks 5.1–5.3 (`make-app.ps1`, `dist\CodeCrack\`).
- Produces: `.gitignore` entries `bin/`, `obj/`, `*.user`, `*.suo` (existing `build/`, `dist/` preserved); `winapp/README.md` with build + run instructions. No later task depends on these.

- [ ] **Step 1: Write the failing test** — create `winapp/tests/repo-hygiene.Tests.ps1`:
```powershell
BeforeAll {
    $root = Join-Path $PSScriptRoot '..\..'
    $script:Ignore = Get-Content (Join-Path $root '.gitignore')
    $script:Readme = Join-Path $root 'winapp\README.md'
}

Describe '.gitignore covers .NET build output' {
    It 'ignores bin/ obj/ *.user *.suo' {
        $script:Ignore | Should -Contain 'bin/'
        $script:Ignore | Should -Contain 'obj/'
        $script:Ignore | Should -Contain '*.user'
        $script:Ignore | Should -Contain '*.suo'
    }
    It 'still ignores the existing build/ and dist/ dirs' {
        $script:Ignore | Should -Contain 'build/'
        $script:Ignore | Should -Contain 'dist/'
    }
}

Describe 'winapp/README.md documents build + run' {
    It 'exists' { Test-Path $script:Readme | Should -BeTrue }
    It 'documents make-app.ps1 and the dist output' {
        $text = Get-Content $script:Readme -Raw
        $text | Should -Match 'make-app\.ps1'
        $text | Should -Match 'dist\\CodeCrack'
    }
}
```

- [ ] **Step 2: Run it, expect FAIL** — `pwsh -NoProfile -Command "Invoke-Pester -Path winapp\tests\repo-hygiene.Tests.ps1 -Output Detailed"`. Fails: `.gitignore` lacks `bin/`/`obj/`/`*.user`/`*.suo`, and `winapp/README.md` does not exist (`Test-Path` → `False`).

- [ ] **Step 3: Implement** — append four lines to `.gitignore` (keep existing lines intact):
```gitignore
bin/
obj/
*.user
*.suo
```
Then create `winapp/README.md`:
```markdown
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
```

- [ ] **Step 4: Run tests, expect PASS** — `pwsh -NoProfile -Command "Invoke-Pester -Path winapp\tests\repo-hygiene.Tests.ps1 -Output Detailed"` (all passed).

- [ ] **Step 5: Commit** — `git add .gitignore winapp/README.md winapp/tests/repo-hygiene.Tests.ps1` then `git commit -m "chore(win): ignore .NET build output; document winapp build + run"`.

