"""Render analysis results for humans (text) and machines (JSON).

The JSON shape is a **stable contract** consumed by the macOS app and later
milestones (M2/M3/M4). Field reference:

``findings[]``  — see ``Finding.to_dict`` (id, kind, target, location, rationale,
                  severity, evidence).
``tests[]``     — see ``GeneratedTest.to_dict``:
    ``finding_id``  str   which finding this test targets.
    ``test_name``   str   the pytest function name.
    ``source``      str   full generated pytest source.
    ``expects``     str   oracle style: "raises" | "assertion" | "regression".
    ``outcome``     str?  execution result: "passed" | "failed" | "error" |
                          "skipped", or null if the test was not executed.
    ``detail``      str   traceback / failure message / skip reason.
    ``stdout``      str   stdout captured while the test ran.
    ``duration``    float wall-clock seconds for the test's call phase.
    ``reproduced``  bool  **did this test prove the bug?** Encapsulates the
                          inverted semantic: a passing ``raises`` test and a
                          failing ``assertion`` test both mean "bug proven".
``summary``     — counts:
    ``findings``   int  number of findings.
    ``tests``      int  number of generated tests.
    ``executed``   int  tests that actually ran (outcome not null).
    ``reproduced`` int  **tests that reproduce a real failure** (headline metric).
    ``by_outcome`` dict count per outcome: passed/failed/error/skipped.
"""

from __future__ import annotations

import json

from codecrack.core.models import Finding, GeneratedTest, OUTCOMES

_SEV_ORDER = {"high": 0, "medium": 1, "low": 2}


def _summary(findings: list[Finding], tests: list[GeneratedTest]) -> dict:
    by_outcome = {o: 0 for o in OUTCOMES}
    executed = 0
    reproduced = 0
    for t in tests:
        if t.outcome is not None:
            executed += 1
            by_outcome[t.outcome] = by_outcome.get(t.outcome, 0) + 1
        if t.reproduces_bug():
            reproduced += 1
    return {
        "findings": len(findings),
        "tests": len(tests),
        "executed": executed,
        "reproduced": reproduced,
        "by_outcome": by_outcome,
    }


def render_text(findings: list[Finding], tests: list[GeneratedTest]) -> str:
    lines: list[str] = []
    lines.append("CodeCrack report")
    lines.append("=" * 40)
    if not findings:
        lines.append("No weaknesses detected.")
        return "\n".join(lines)

    summary = _summary(findings, tests)
    lines.append(
        f"{summary['findings']} finding(s), {summary['tests']} generated test(s)"
    )
    lines.append(
        f"{summary['reproduced']} test(s) reproduce a real failure "
        f"(executed {summary['executed']}/{summary['tests']})\n"
    )
    for f in sorted(findings, key=lambda x: (_SEV_ORDER.get(x.severity, 9), x.location)):
        loc = f"{f.location[0]}:{f.location[1]}"
        lines.append(f"[{f.severity.upper():6}] {f.kind}  ({f.target} @ {loc})")
        lines.append(f"    {f.rationale}")

    lines.append("\nGenerated tests")
    lines.append("-" * 40)
    for t in tests:
        status = _status_label(t)
        lines.append(
            f"# {t.test_name}  (targets {t.finding_id}, expects {t.expects}) -> {status}"
        )
        lines.append(t.source.rstrip("\n"))
        lines.append("")
    return "\n".join(lines)


def _status_label(t: GeneratedTest) -> str:
    if t.outcome is None:
        return "not executed"
    if t.reproduces_bug():
        return f"{t.outcome} — BUG REPRODUCED"
    if t.outcome == "skipped":
        return "skipped (needs input)"
    return t.outcome


def render_json(findings: list[Finding], tests: list[GeneratedTest]) -> str:
    payload = {
        "findings": [f.to_dict() for f in findings],
        "tests": [t.to_dict() for t in tests],
        "summary": _summary(findings, tests),
    }
    return json.dumps(payload, indent=2)
