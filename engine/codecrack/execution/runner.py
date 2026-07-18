"""Run generated pytest files in a subprocess sandbox and map the results back.

The *execute* stage of the Python adapter. Given the generated tests and the
user's module source, it:

1. Materializes a scratch temp dir containing ``<module>.py`` (the code under
   test), one ``test_*.py`` per generated test, and a ``conftest.py`` plugin
   that records per-test outcomes.
2. Runs ``python -m pytest`` there as a **subprocess** — its own process group,
   a wall-clock timeout, CPU/memory ``rlimit``s, a scrubbed environment, and a
   scratch CWD. User code is treated as hostile; nothing runs in-process.
3. Reads the plugin's JSON result file and attaches outcome/detail/stdout/
   duration onto each ``GeneratedTest``.

Isolation is deliberately the *bottom rung* of the design brief's ladder
(subprocess + rlimits). Higher rungs (OS sandbox, containers, microVMs) can wrap
this without changing the public surface: ``execute_tests`` + ``SandboxConfig``.
"""

from __future__ import annotations

import json
import os
import signal
import subprocess
import sys
import tempfile
from dataclasses import dataclass

from codecrack.core.models import GeneratedTest

try:  # POSIX only; rlimits are best-effort and skipped where unsupported.
    import resource
except ImportError:  # pragma: no cover - non-POSIX
    resource = None  # type: ignore[assignment]


# Safe defaults. All are overridable via SandboxConfig; set a limit to ``None``
# to disable it (e.g. on a platform where RLIMIT_AS breaks the interpreter).
DEFAULT_WALL_TIMEOUT = 15.0  # seconds of real time before the group is killed
DEFAULT_CPU_SECONDS = 10  # RLIMIT_CPU: CPU-time ceiling per process
DEFAULT_MEMORY_BYTES = 2 * 1024 * 1024 * 1024  # RLIMIT_AS: 2 GiB address space


@dataclass(frozen=True)
class SandboxConfig:
    """Tunable, safe-by-default limits for a sandboxed test run."""

    wall_timeout: float = DEFAULT_WALL_TIMEOUT
    cpu_seconds: int | None = DEFAULT_CPU_SECONDS
    memory_bytes: int | None = DEFAULT_MEMORY_BYTES


# Inline pytest plugin written into the scratch dir. It captures the decisive
# per-test outcome (setup errors/skips override; otherwise the call phase) plus
# traceback, captured stdout, and duration, and dumps them as JSON to the path
# in $CODECRACK_RESULTS at session end.
_CONFTEST = r'''
import json
import os

_results = {}


def _entry(nodeid):
    return _results.setdefault(
        nodeid,
        {"nodeid": nodeid, "outcome": "passed", "detail": "", "stdout": "", "duration": 0.0},
    )


def pytest_runtest_logreport(report):
    e = _entry(report.nodeid)
    e["duration"] += float(getattr(report, "duration", 0.0) or 0.0)
    if getattr(report, "capstdout", ""):
        e["stdout"] += report.capstdout
    if report.when == "setup":
        if report.failed:
            e["outcome"] = "error"
            e["detail"] = report.longreprtext
        elif report.skipped:
            e["outcome"] = "skipped"
            e["detail"] = report.longreprtext or "skipped"
    elif report.when == "call":
        if report.failed:
            e["outcome"] = "failed"
            e["detail"] = report.longreprtext
        elif report.skipped:
            e["outcome"] = "skipped"
            e["detail"] = report.longreprtext or "skipped"
        else:
            e["outcome"] = "passed"
    elif report.when == "teardown" and report.failed and e["outcome"] == "passed":
        e["outcome"] = "error"
        e["detail"] = report.longreprtext


def pytest_sessionfinish(session, exitstatus):
    path = os.environ.get("CODECRACK_RESULTS")
    if path:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(list(_results.values()), fh)
'''


def _preexec(config: SandboxConfig):
    """Build a preexec_fn that applies rlimits in the forked child (POSIX)."""

    def apply() -> None:
        # New session/process group so a timeout can kill the whole tree.
        os.setsid()
        if resource is None:
            return
        if config.cpu_seconds is not None:
            try:
                resource.setrlimit(
                    resource.RLIMIT_CPU, (config.cpu_seconds, config.cpu_seconds)
                )
            except (ValueError, OSError):
                pass
        if config.memory_bytes is not None and hasattr(resource, "RLIMIT_AS"):
            try:
                resource.setrlimit(
                    resource.RLIMIT_AS, (config.memory_bytes, config.memory_bytes)
                )
            except (ValueError, OSError):
                pass

    return apply


def _scrubbed_env(results_path: str) -> dict[str, str]:
    """A minimal environment: enough to import pytest/python, nothing else.

    We deliberately drop the caller's environment (no secrets, no network
    config) but preserve what a stdlib interpreter + pytest need to start.
    """
    src = os.environ
    env: dict[str, str] = {}
    for key in ("PATH", "HOME", "TMPDIR", "LANG", "LC_ALL", "SYSTEMROOT"):
        if key in src:
            env[key] = src[key]
    env.setdefault("PATH", "/usr/bin:/bin")
    env["PYTHONHASHSEED"] = "0"  # determinism
    env["PYTHONDONTWRITEBYTECODE"] = "1"
    env["CODECRACK_RESULTS"] = results_path
    return env


def _safe_stem(name: str) -> str:
    return "".join(c if c.isalnum() or c == "_" else "_" for c in name)


def execute_tests(
    tests: list[GeneratedTest],
    module_source: str,
    *,
    module: str = "target",
    config: SandboxConfig | None = None,
) -> list[GeneratedTest]:
    """Execute *tests* against *module_source* in a sandbox; attach results.

    Mutates and returns the same ``GeneratedTest`` objects (sets ``outcome``,
    ``detail``, ``stdout``, ``duration``). No-op for an empty list.
    """
    config = config or SandboxConfig()
    if not tests:
        return tests

    with tempfile.TemporaryDirectory(prefix="codecrack_exec_") as scratch:
        # Code under test, importable as ``{module}`` from the scratch CWD.
        with open(os.path.join(scratch, f"{module}.py"), "w", encoding="utf-8") as fh:
            fh.write(module_source)
        with open(os.path.join(scratch, "conftest.py"), "w", encoding="utf-8") as fh:
            fh.write(_CONFTEST)

        # One file per test; map its pytest nodeid back to the object.
        by_nodeid: dict[str, GeneratedTest] = {}
        for i, t in enumerate(tests):
            fname = f"test_{i:03d}_{_safe_stem(t.finding_id)}.py"
            with open(os.path.join(scratch, fname), "w", encoding="utf-8") as fh:
                fh.write(t.source)
            by_nodeid[f"{fname}::{t.test_name}"] = t

        results_path = os.path.join(scratch, "_results.json")
        env = _scrubbed_env(results_path)
        cmd = [
            sys.executable,
            "-m",
            "pytest",
            "-p",
            "no:cacheprovider",
            "-q",
            "--no-header",
            scratch,
        ]

        timed_out = False
        proc = subprocess.Popen(
            cmd,
            cwd=scratch,
            env=env,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            preexec_fn=_preexec(config),
        )
        try:
            captured, _ = proc.communicate(timeout=config.wall_timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
            _kill_group(proc)
            captured, _ = proc.communicate()

        _attach_results(tests, by_nodeid, results_path, timed_out, config, captured)

    return tests


def _kill_group(proc: subprocess.Popen) -> None:
    """Kill the child's whole process group so infinite loops can't linger."""
    try:
        os.killpg(os.getpgid(proc.pid), signal.SIGKILL)
    except (ProcessLookupError, PermissionError):
        try:
            proc.kill()
        except ProcessLookupError:
            pass


def _attach_results(
    tests: list[GeneratedTest],
    by_nodeid: dict[str, GeneratedTest],
    results_path: str,
    timed_out: bool,
    config: SandboxConfig,
    captured: str,
) -> None:
    recorded: dict[str, dict] = {}
    if os.path.exists(results_path):
        try:
            with open(results_path, encoding="utf-8") as fh:
                for row in json.load(fh):
                    recorded[row["nodeid"]] = row
        except (json.JSONDecodeError, OSError, KeyError):
            recorded = {}

    for nodeid, test in by_nodeid.items():
        row = recorded.get(nodeid)
        if row is not None:
            test.outcome = row.get("outcome", "error")
            test.detail = row.get("detail", "")
            test.stdout = row.get("stdout", "")
            test.duration = float(row.get("duration", 0.0))
        elif timed_out:
            test.outcome = "error"
            test.detail = (
                f"sandbox timed out after {config.wall_timeout}s and was killed"
                f"\n{_tail(captured)}"
            )
        else:
            # No record and no timeout: pytest crashed/couldn't collect this test.
            test.outcome = "error"
            test.detail = f"no result recorded for {nodeid}\n{_tail(captured)}"


def _tail(text: str, limit: int = 2000) -> str:
    text = text or ""
    return text[-limit:]
