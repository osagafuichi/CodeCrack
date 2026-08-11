"""The committed JSON golden must match a fresh CLI regeneration (volatile fields masked).

The golden is a *shape* contract (field names, nesting, types, and the proven-bug
outcome counts), consumed by the Windows app's C# DTOs. ``duration`` is wall-clock
volatile; ``detail``/``stdout`` carry pytest traceback text that differs across the
Python 3.10-3.13 matrix (fine-grained error locations landed in 3.11), so all three
are masked before comparison. Outcomes and ``reproduced`` stay asserted.
"""

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
        t["detail"] = ""
        t["stdout"] = ""
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
