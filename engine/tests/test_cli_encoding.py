"""cli._configure_stdout must switch stdout to UTF-8 when possible, else no-op."""

from __future__ import annotations

import sys

from codecrack import cli


class _FakeStdout:
    def __init__(self) -> None:
        self.kwargs: dict | None = None

    def reconfigure(self, **kwargs) -> None:
        self.kwargs = kwargs


def test_configure_stdout_reconfigures_to_utf8(monkeypatch):
    fake = _FakeStdout()
    monkeypatch.setattr(sys, "stdout", fake)
    cli._configure_stdout()
    assert fake.kwargs == {"encoding": "utf-8", "errors": "backslashreplace"}


def test_configure_stdout_is_noop_without_reconfigure(monkeypatch):
    class _Bare:
        pass

    monkeypatch.setattr(sys, "stdout", _Bare())
    cli._configure_stdout()  # must not raise
