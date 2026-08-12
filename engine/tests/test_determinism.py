"""crack() + render_json must be reproducible run-to-run (volatile fields masked).

Determinism is a property of the findings/tests STRUCTURE and outcomes, not of the
wall-clock ``duration`` or the pytest traceback text in ``detail``/``stdout`` — the
latter embed a per-run temp-dir path (e.g. macOS ``/var/folders/...codecrack_exec_XXXX``)
that legitimately varies each run. Mask all three, exactly like the golden normalizer.
"""

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
        test["detail"] = ""
        test["stdout"] = ""
    return payload


def test_render_json_is_stable_across_runs():
    source = FIXTURE.read_text(encoding="utf-8")
    first = _render(source)
    second = _render(source)
    assert first == second
    assert first["summary"]["reproduced"] == 3
