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
