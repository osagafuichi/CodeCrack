"""Command-line interface: ``python -m codecrack ...``."""

from __future__ import annotations

import argparse
import sys

from codecrack.pipeline import crack
from codecrack.report import render_json, render_text


def _configure_stdout() -> None:
    """Force UTF-8 output so non-ASCII report glyphs survive OEM/CJK consoles."""
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="backslashreplace")


def _decode(data: bytes) -> str:
    """Decode source bytes tolerantly: real-world Windows files are often not UTF-8.

    Try UTF-8 (with a BOM stripped if present), then CP-1252 (the common Windows
    Latin-1 superset), then a lossy UTF-8 pass so a stray byte never crashes analysis.
    """
    for encoding in ("utf-8-sig", "cp1252"):
        try:
            return data.decode(encoding)
        except UnicodeDecodeError:
            continue
    return data.decode("utf-8", errors="replace")


def _read(path: str) -> str:
    if path == "-":
        return _decode(sys.stdin.buffer.read())
    with open(path, "rb") as fh:
        return _decode(fh.read())


def main(argv: list[str] | None = None) -> int:
    _configure_stdout()
    parser = argparse.ArgumentParser(
        prog="codecrack",
        description="Analyze Python code and generate tests that expose its bugs.",
    )
    sub = parser.add_subparsers(dest="command", required=True)

    an = sub.add_parser("analyze", help="analyze a file and generate tests")
    an.add_argument("path", help="Python file to analyze, or '-' for stdin")
    an.add_argument("--json", action="store_true", help="emit JSON instead of text")
    an.add_argument(
        "--module", default="target",
        help="import path the generated tests should use (default: target)",
    )
    an.add_argument(
        "--no-execute", action="store_true",
        help="skip the sandbox execute stage (analyze + generate only)",
    )

    args = parser.parse_args(argv)

    if args.command == "analyze":
        source = _read(args.path)
        result = crack(
            source, module=args.module, filename=args.path, execute=not args.no_execute
        )
        out = (
            render_json(result.findings, result.tests)
            if args.json
            else render_text(result.findings, result.tests)
        )
        print(out)
        return 0

    parser.error("unknown command")
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
