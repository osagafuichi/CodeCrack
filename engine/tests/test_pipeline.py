"""End-to-end pipeline + JSON contract shape."""

from __future__ import annotations

import json

from codecrack.pipeline import crack
from codecrack.report import render_json, render_text

MULTI_BUG = """
def divide(a, b):
    return a / b


def first(xs):
    return xs[0]


def accumulate(item, acc=[]):
    acc += [item]
    return acc
"""


def test_crack_finds_and_executes():
    result = crack(MULTI_BUG, module="target", execute=True)
    assert len(result.findings) >= 3
    assert result.tests
    # Every generated test was executed (has an outcome).
    assert all(t.outcome is not None for t in result.tests)
    # At least the three seeded bugs reproduce.
    assert sum(t.reproduces_bug() for t in result.tests) >= 3


def test_execute_false_skips_execution():
    result = crack(MULTI_BUG, module="target", execute=False)
    assert result.tests
    assert all(t.outcome is None for t in result.tests)


def test_json_contract_shape():
    result = crack(MULTI_BUG, module="target", execute=True)
    payload = json.loads(render_json(result.findings, result.tests))

    assert set(payload) == {"findings", "tests", "summary"}

    summary = payload["summary"]
    for key in ("findings", "tests", "executed", "reproduced", "by_outcome"):
        assert key in summary, f"summary missing {key}"
    assert set(summary["by_outcome"]) == {"passed", "failed", "error", "skipped"}
    assert summary["reproduced"] >= 3

    test = payload["tests"][0]
    for key in (
        "finding_id",
        "test_name",
        "source",
        "expects",
        "outcome",
        "detail",
        "stdout",
        "duration",
        "reproduced",
    ):
        assert key in test, f"test entry missing {key}"


def test_render_text_reports_reproduced_count():
    result = crack(MULTI_BUG, module="target", execute=True)
    text = render_text(result.findings, result.tests)
    assert "reproduce a real failure" in text
