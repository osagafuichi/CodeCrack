"""The one directional pipeline: source -> findings -> tests."""

from __future__ import annotations

from dataclasses import dataclass

from codecrack.analysis import analyze_source
from codecrack.core.models import Finding, GeneratedTest
from codecrack.generation import generate_tests


@dataclass
class Result:
    findings: list[Finding]
    tests: list[GeneratedTest]


def crack(source: str, *, module: str = "target", filename: str = "<input>") -> Result:
    """Analyze *source* and generate tests for what it finds."""
    findings = analyze_source(source, filename=filename)
    tests = generate_tests(findings, module=module)
    return Result(findings=findings, tests=tests)
