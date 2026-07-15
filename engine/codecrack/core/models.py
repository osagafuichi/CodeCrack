"""The stable data contract shared across analysis, generation, and reporting.

Analysis produces ``Finding`` objects; generation consumes them and produces
``GeneratedTest`` objects. Neither side imports the other — both depend only on
this module.
"""

from __future__ import annotations

from dataclasses import dataclass, field, asdict
from typing import Any


SEVERITIES = ("low", "medium", "high")


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
    """A runnable pytest test targeting a specific ``Finding``."""

    finding_id: str
    test_name: str
    source: str  # full pytest test source code
    expects: str  # "raises" | "assertion" | "regression"

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)
