"""The stable data contract shared across analysis, generation, and reporting.

Analysis produces ``Finding`` objects; generation consumes them and produces
``GeneratedTest`` objects. Neither side imports the other — both depend only on
this module.
"""

from __future__ import annotations

from dataclasses import dataclass, field, asdict
from typing import Any


SEVERITIES = ("low", "medium", "high")

# Execution outcomes a GeneratedTest can carry after the execute stage runs it.
# ``None`` means "not executed yet". These mirror pytest's own vocabulary plus an
# explicit ``error`` (collection/setup/teardown crash or a sandbox kill/timeout).
OUTCOMES = ("passed", "failed", "error", "skipped")


@dataclass
class Finding:
    """A single suspected weakness in the analyzed source."""

    id: str
    kind: str  # e.g. "mutable-default-arg", "broad-except", "zero-division"
    target: str  # dotted-ish path to the function/module under suspicion
    location: tuple[int, int]  # (lineno, col)
    rationale: str  # why this is risky
    severity: str  # one of SEVERITIES
    # Extra context used by generation (kept optional so the contract stays stable).
    evidence: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


@dataclass
class GeneratedTest:
    """A runnable pytest test targeting a specific ``Finding``.

    The ``expects`` field encodes the *oracle style*, which determines what
    outcome proves the bug (see ``reproduces_bug``):

    - ``"raises"``     — asserts the risky call raises; **passing proves the bug**.
    - ``"assertion"``  — asserts an invariant that a correct program upholds;
                          **failing proves the bug** (the invariant was violated).
    - ``"regression"`` — a clearly-labelled ``skip`` scaffold (needs input); it can
                          never prove a bug on its own.

    The result fields below are filled in by the execute stage
    (``codecrack.execution``). They default to the "not executed yet" state so
    existing producers/consumers that ignore execution stay valid.
    """

    finding_id: str
    test_name: str
    source: str  # full pytest test source code
    expects: str  # "raises" | "assertion" | "regression"

    # --- execution results (populated by the execute stage) ----------------
    outcome: str | None = None  # one of OUTCOMES, or None if not executed
    detail: str = ""  # failure/error traceback or skip reason
    stdout: str = ""  # stdout captured while the test ran
    duration: float = 0.0  # wall-clock seconds the test's call phase took

    def reproduces_bug(self) -> bool:
        """Did executing this test actually demonstrate the suspected bug?

        This is the load-bearing semantic for the whole product: a generated
        test that *passes* its ``pytest.raises`` oracle has **proven** the bug,
        not shown the code is fine. Invariant tests invert (a failure is the
        proof). Not-yet-executed and skip scaffolds never count.
        """
        if self.outcome is None:
            return False
        if self.expects == "raises":
            return self.outcome == "passed"
        if self.expects == "assertion":
            return self.outcome == "failed"
        return False

    def to_dict(self) -> dict[str, Any]:
        d = asdict(self)
        # Derived, contract-stable convenience flag for consumers (macOS app,
        # later milestones) so they don't re-implement the inverted semantics.
        d["reproduced"] = self.reproduces_bug()
        return d
