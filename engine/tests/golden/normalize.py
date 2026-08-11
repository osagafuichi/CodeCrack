"""Mask volatile fields so the golden JSON is byte-stable across the CI matrix.

Used both to produce the committed golden and by the CI drift gate (Task 0.9), so
the two sides normalize identically. ``duration`` is wall-clock; ``detail`` and
``stdout`` carry pytest traceback text that varies across Python 3.10-3.13. The
contract we assert is the JSON *shape* plus the proven-bug outcome counts, not the
exact traceback prose.
"""

from __future__ import annotations

import json
import sys


def normalize(text: str) -> str:
    data = json.loads(text)
    for test in data.get("tests", []):
        test["duration"] = 0.0
        test["detail"] = ""
        test["stdout"] = ""
    return json.dumps(data, indent=2)


if __name__ == "__main__":
    sys.stdout.write(normalize(sys.stdin.read()))
