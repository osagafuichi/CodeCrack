"""cli._configure_stdout must switch stdout to UTF-8 when possible, else no-op."""

from __future__ import annotations

import json
import subprocess
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


def test_decode_falls_back_to_cp1252():
    # 0xE9 is 'é' in CP-1252 but an invalid lone UTF-8 byte.
    assert cli._decode(b"# caf\xe9\n") == "# café\n"


def test_decode_strips_utf8_bom():
    assert cli._decode("x = 1\n".encode("utf-8-sig")) == "x = 1\n"


def test_analyze_non_utf8_file_succeeds(tmp_path):
    # A CP-1252 (non-UTF-8) source with a real bug must analyze cleanly, not crash.
    src = "def divide(a, b):\n    # café handling\n    return a / b\n"
    f = tmp_path / "latin1.py"
    f.write_bytes(src.encode("cp1252"))
    proc = subprocess.run(
        [sys.executable, "-m", "codecrack", "analyze", str(f), "--json"],
        capture_output=True,
        text=True,
    )
    assert proc.returncode == 0, proc.stderr
    data = json.loads(proc.stdout)
    assert data["findings"], "expected at least one finding (zero-division)"
