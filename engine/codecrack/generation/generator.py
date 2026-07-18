"""Generate pytest tests that target Findings.

Each strategy builds a call to the suspect function with edge-case arguments
(0 for a divisor, [] for an indexed arg, None for a dereferenced arg) and an
oracle (``pytest.raises`` / assertion). Where an automatic trigger cannot be
derived, a clearly-marked regression scaffold is emitted instead.
"""

from __future__ import annotations

from codecrack.core.models import Finding, GeneratedTest


def _call_args(params: list[str], trigger: str, trigger_value: str) -> str:
    """Build an argument list: the trigger param gets the edge value, others 1."""
    parts = []
    for p in params:
        parts.append(f"{p}={trigger_value}" if p == trigger else f"{p}=1")
    return ", ".join(parts)


def _raises_test(f: Finding, exc: str, trigger_value: str, module: str) -> GeneratedTest:
    fn = f.evidence.get("function")
    params = f.evidence.get("params", [])
    trigger = f.evidence.get("param", params[0] if params else "")
    args = _call_args(params, trigger, trigger_value)
    name = f"test_{fn}_{f.kind.replace('-', '_')}_{f.id.lower()}"
    src = (
        f"import pytest\n"
        f"from {module} import {fn}\n\n\n"
        f"def {name}():\n"
        f"    # {f.rationale}\n"
        f"    with pytest.raises({exc}):\n"
        f"        {fn}({args})\n"
    )
    return GeneratedTest(finding_id=f.id, test_name=name, source=src, expects="raises")


def _mutable_default_test(f: Finding, module: str) -> GeneratedTest:
    """Trigger the shared-mutable-default bug with a repr-snapshot oracle.

    The naive ``first == second`` check is defeated by aliasing: when the buggy
    function mutates *and returns* its default, ``first`` and ``second`` are the
    same object, so they always compare equal. Instead we snapshot ``repr(first)``
    and assert an independent second call did not mutate it — which it will have,
    because ``first`` *is* the leaked default. Failing this assertion proves the
    bug (``expects="assertion"``).
    """
    fn = f.evidence.get("function")
    required = f.evidence.get("required_params", [])
    call = f"{fn}({', '.join(f'{p}=1' for p in required)})"
    name = f"test_{fn}_mutable_default_leak_{f.id.lower()}"
    src = (
        f"from {module} import {fn}\n\n\n"
        f"def {name}():\n"
        f"    # {f.rationale}\n"
        f"    # Snapshot the first result, then confirm an independent second call\n"
        f"    # did not mutate it (survives aliasing: a leaked default IS this object).\n"
        f"    first = {call}\n"
        f"    before = repr(first)\n"
        f"    {call}\n"
        f"    after = repr(first)\n"
        f"    assert before == after, "
        f"'mutable default argument leaked state between independent calls'\n"
    )
    return GeneratedTest(finding_id=f.id, test_name=name, source=src, expects="assertion")


def _regression_scaffold(f: Finding, module: str) -> GeneratedTest:
    """A clearly-labelled skip for weaknesses with no auto-derivable trigger.

    The skip reason starts with ``needs input`` so the UI can group these as
    "needs input" rather than treating them as failures.
    """
    fn = f.evidence.get("function") or "target"
    name = f"test_{fn}_{f.kind.replace('-', '_')}_{f.id.lower()}"
    reason = f"needs input: no trigger could be auto-derived for {f.kind}"
    src = (
        f"import pytest\n\n\n"
        f"@pytest.mark.skip(reason={reason!r})\n"
        f"def {name}():\n"
        f"    # {f.rationale}\n"
        f"    # CodeCrack flagged this but could not auto-derive a trigger input.\n"
        f"    # Supply a call that exercises the risky path, then assert on it.\n"
        f"    raise AssertionError('unimplemented regression test')\n"
    )
    return GeneratedTest(finding_id=f.id, test_name=name, source=src, expects="regression")


# kind -> (exception, edge value) for the pytest.raises strategy.
_RAISES = {
    "zero-division": ("ZeroDivisionError", "0"),
    "index-error": ("(IndexError, KeyError)", "[]"),
    "none-deref": ("AttributeError", "None"),
}


def generate_tests(findings: list[Finding], *, module: str = "target") -> list[GeneratedTest]:
    """Return one GeneratedTest per Finding that has a viable strategy.

    *module* is the import path the generated tests use for the code under test.
    """
    tests: list[GeneratedTest] = []
    for f in findings:
        if f.kind in _RAISES and f.evidence.get("function"):
            exc, value = _RAISES[f.kind]
            tests.append(_raises_test(f, exc, value, module))
        elif f.kind == "mutable-default-arg" and f.evidence.get("function"):
            tests.append(_mutable_default_test(f, module))
        elif f.kind in {"broad-except", "bare-except"}:
            tests.append(_regression_scaffold(f, module))
        # syntax-error and anything else: no runnable test.
    return tests
