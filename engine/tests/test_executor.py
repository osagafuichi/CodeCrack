"""Sandbox executor: outcome mapping, stdout capture, and timeout kill."""

from __future__ import annotations

import time

from codecrack.core.models import GeneratedTest
from codecrack.execution import SandboxConfig, execute_tests


def _test(name: str, body: str, expects: str = "raises") -> GeneratedTest:
    src = f"def {name}():\n" + "\n".join(f"    {line}" for line in body.splitlines())
    return GeneratedTest(finding_id="F001", test_name=name, source=src + "\n", expects=expects)


def test_pass_and_fail_outcomes_mapped():
    passing = _test("test_pass", "assert 1 + 1 == 2", expects="assertion")
    failing = _test("test_fail", "assert 1 + 1 == 3", expects="assertion")
    execute_tests([passing, failing], module_source="", module="target")

    assert passing.outcome == "passed"
    assert failing.outcome == "failed"
    assert "assert" in failing.detail  # traceback captured
    # For an invariant (assertion) test, FAILING is the proof of a bug.
    assert failing.reproduces_bug()
    assert not passing.reproduces_bug()


def test_raises_test_passing_reproduces_bug():
    module = "def divide(a, b):\n    return a / b\n"
    t = _test(
        "test_zero",
        "import pytest\nwith pytest.raises(ZeroDivisionError):\n    from target import divide\n    divide(1, 0)",
        expects="raises",
    )
    execute_tests([t], module_source=module, module="target")
    assert t.outcome == "passed"
    assert t.reproduces_bug()
    assert t.duration >= 0.0


def test_stdout_captured():
    t = _test("test_prints", "print('hello-from-sandbox')\nassert True", expects="assertion")
    execute_tests([t], module_source="", module="target")
    assert t.outcome == "passed"
    assert "hello-from-sandbox" in t.stdout


def test_infinite_loop_is_killed_without_hanging():
    module = "def loop():\n    while True:\n        pass\n"
    t = _test(
        "test_loop",
        "from target import loop\nloop()",
        expects="raises",
    )
    start = time.monotonic()
    execute_tests(
        [t], module_source=module, module="target", config=SandboxConfig(wall_timeout=3)
    )
    elapsed = time.monotonic() - start

    # The sandbox must kill the process group and return promptly, not hang.
    assert elapsed < 30, f"executor hung for {elapsed:.1f}s"
    assert t.outcome == "error"
    assert "timed out" in t.detail
    assert not t.reproduces_bug()
