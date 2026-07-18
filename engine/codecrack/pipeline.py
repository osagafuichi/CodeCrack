"""The one directional pipeline: source -> findings -> tests -> results."""

from __future__ import annotations

from dataclasses import dataclass

from codecrack.analysis import analyze_source
from codecrack.core.models import Finding, GeneratedTest
from codecrack.execution import SandboxConfig, execute_tests
from codecrack.generation import generate_tests


@dataclass
class Result:
    findings: list[Finding]
    tests: list[GeneratedTest]


def crack(
    source: str,
    *,
    module: str = "target",
    filename: str = "<input>",
    execute: bool = True,
    config: SandboxConfig | None = None,
) -> Result:
    """Analyze *source*, generate tests, and (by default) execute them.

    When *execute* is true the generated tests are run in the sandbox and their
    pass/fail results are attached in place. Pass ``execute=False`` to stop after
    generation (e.g. for fast, side-effect-free analysis).
    """
    findings = analyze_source(source, filename=filename)
    tests = generate_tests(findings, module=module)
    if execute and tests:
        execute_tests(tests, source, module=module, config=config)
    return Result(findings=findings, tests=tests)
