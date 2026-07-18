"""Sandboxed execution: run generated tests and attach pass/fail results.

Never runs user or generated code in-process — everything goes through a
subprocess sandbox (see ``runner.py``). This is the Python adapter's
implementation of the design brief's *Execute* + *Map results* contract steps.
"""

from codecrack.execution.runner import SandboxConfig, execute_tests

__all__ = ["SandboxConfig", "execute_tests"]
