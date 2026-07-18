"""One test per detector: the finding is produced AND (where a trigger is
derivable) the generated test actually reproduces the bug when executed."""

from __future__ import annotations

import pytest

from codecrack.pipeline import crack

# fixture file -> (expected finding kind, expectation about its generated test)
#   "reproduce" -> executing the generated test proves the bug
#   "needs-input" -> a clearly-labelled skipped scaffold
#   "no-test"    -> a finding with no runnable test (e.g. syntax error)
CASES = [
    ("zero_division.py", "zero-division", "reproduce"),
    ("index_error.py", "index-error", "reproduce"),
    ("none_deref.py", "none-deref", "reproduce"),
    ("mutable_default.py", "mutable-default-arg", "reproduce"),
    ("bare_except.py", "bare-except", "needs-input"),
    ("broad_except.py", "broad-except", "needs-input"),
    ("syntax_error.py", "syntax-error", "no-test"),
]


@pytest.mark.parametrize("fixture,kind,expectation", CASES)
def test_detector(read_fixture, fixture, kind, expectation):
    result = crack(read_fixture(fixture), module="target", execute=True)

    kinds = {f.kind for f in result.findings}
    assert kind in kinds, f"{fixture}: expected a {kind} finding, got {kinds}"

    finding = next(f for f in result.findings if f.kind == kind)
    tests = [t for t in result.tests if t.finding_id == finding.id]

    if expectation == "reproduce":
        assert tests, f"{kind}: expected a generated test"
        assert any(
            t.reproduces_bug() for t in tests
        ), f"{kind}: generated test did not reproduce the bug: {[t.outcome for t in tests]}"
    elif expectation == "needs-input":
        assert tests, f"{kind}: expected a skip scaffold"
        for t in tests:
            assert t.outcome == "skipped"
            assert not t.reproduces_bug()
            assert "needs input" in t.source
    elif expectation == "no-test":
        assert result.tests == []
