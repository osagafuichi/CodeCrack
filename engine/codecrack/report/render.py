"""Render analysis results for humans (text) and machines (JSON)."""

from __future__ import annotations

import json

from codecrack.core.models import Finding, GeneratedTest

_SEV_ORDER = {"high": 0, "medium": 1, "low": 2}


def render_text(findings: list[Finding], tests: list[GeneratedTest]) -> str:
    lines: list[str] = []
    lines.append("CodeCrack report")
    lines.append("=" * 40)
    if not findings:
        lines.append("No weaknesses detected.")
        return "\n".join(lines)

    lines.append(f"{len(findings)} finding(s), {len(tests)} generated test(s)\n")
    for f in sorted(findings, key=lambda x: (_SEV_ORDER.get(x.severity, 9), x.location)):
        loc = f"{f.location[0]}:{f.location[1]}"
        lines.append(f"[{f.severity.upper():6}] {f.kind}  ({f.target} @ {loc})")
        lines.append(f"    {f.rationale}")

    lines.append("\nGenerated tests")
    lines.append("-" * 40)
    for t in tests:
        lines.append(f"# {t.test_name}  (targets {t.finding_id}, expects {t.expects})")
        lines.append(t.source.rstrip("\n"))
        lines.append("")
    return "\n".join(lines)


def render_json(findings: list[Finding], tests: list[GeneratedTest]) -> str:
    payload = {
        "findings": [f.to_dict() for f in findings],
        "tests": [t.to_dict() for t in tests],
        "summary": {"findings": len(findings), "tests": len(tests)},
    }
    return json.dumps(payload, indent=2)
