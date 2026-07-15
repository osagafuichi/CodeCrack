"""Command-line interface: ``python -m codecrack ...``."""

from __future__ import annotations

import argparse
import sys

from codecrack.pipeline import crack
from codecrack.report import render_json, render_text


def _read(path: str) -> str:
    if path == "-":
        return sys.stdin.read()
    with open(path, "r", encoding="utf-8") as fh:
        return fh.read()


def main(argv: list[str] | None = None) -> int:
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

    args = parser.parse_args(argv)

    if args.command == "analyze":
        source = _read(args.path)
        result = crack(source, module=args.module, filename=args.path)
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
