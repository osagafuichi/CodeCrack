"""AST-based risk detection.

A deliberately small, low-false-positive set of detectors that each map cleanly
to a concrete, generatable test. Each detector yields ``Finding`` objects and
stashes the context generation needs (enclosing function name + arg names) in
``Finding.evidence``.
"""

from __future__ import annotations

import ast
import itertools

from codecrack.core.models import Finding


_counter = itertools.count(1)


def _fid() -> str:
    return f"F{next(_counter):03d}"


def _func_params(fn: ast.FunctionDef | ast.AsyncFunctionDef) -> list[str]:
    a = fn.args
    names = [p.arg for p in (a.posonlyargs + a.args + a.kwonlyargs)]
    return [n for n in names if n != "self"]


class _Analyzer(ast.NodeVisitor):
    def __init__(self, source: str) -> None:
        self.source = source
        self.findings: list[Finding] = []
        self._func_stack: list[ast.FunctionDef | ast.AsyncFunctionDef] = []

    # --- helpers -----------------------------------------------------------
    def _here(self) -> ast.FunctionDef | ast.AsyncFunctionDef | None:
        return self._func_stack[-1] if self._func_stack else None

    def _target(self) -> str:
        fn = self._here()
        return fn.name if fn else "<module>"

    def _evidence(self) -> dict:
        fn = self._here()
        if fn is None:
            return {"function": None, "params": []}
        return {"function": fn.name, "params": _func_params(fn)}

    def _add(self, kind, node, rationale, severity, extra=None) -> None:
        evidence = self._evidence()
        if extra:
            evidence.update(extra)
        self.findings.append(
            Finding(
                id=_fid(),
                kind=kind,
                target=self._target(),
                location=(getattr(node, "lineno", 0), getattr(node, "col_offset", 0)),
                rationale=rationale,
                severity=severity,
                evidence=evidence,
            )
        )

    # --- traversal ---------------------------------------------------------
    def visit_FunctionDef(self, node: ast.FunctionDef) -> None:
        self._check_mutable_defaults(node)
        self._func_stack.append(node)
        self.generic_visit(node)
        self._func_stack.pop()

    visit_AsyncFunctionDef = visit_FunctionDef  # type: ignore[assignment]

    def _check_mutable_defaults(self, node) -> None:
        a = node.args
        positional = a.posonlyargs + a.args
        # Python binds ``defaults`` to the LAST N positional params; ``kw_defaults``
        # aligns 1:1 with ``kwonlyargs`` (None where there is no default).
        defaulted: dict[str, ast.expr] = {}
        if a.defaults:
            for arg, default in zip(positional[-len(a.defaults):], a.defaults):
                defaulted[arg.arg] = default
        for arg, default in zip(a.kwonlyargs, a.kw_defaults):
            if default is not None:
                defaulted[arg.arg] = default

        # Params generation MUST supply to reach the risky default (everything
        # without its own default, minus the mutable one which we leave defaulted).
        required = [
            arg.arg
            for arg in (positional + a.kwonlyargs)
            if arg.arg not in defaulted and arg.arg != "self"
        ]

        for arg_name, default in defaulted.items():
            if isinstance(default, (ast.List, ast.Dict, ast.Set)):
                self._func_stack.append(node)
                self._add(
                    "mutable-default-arg",
                    default,
                    "Mutable default argument is shared across calls and leaks state "
                    "between invocations.",
                    "high",
                    extra={"param": arg_name, "required_params": required},
                )
                self._func_stack.pop()

    def visit_ExceptHandler(self, node: ast.ExceptHandler) -> None:
        if node.type is None:
            self._add(
                "bare-except",
                node,
                "Bare 'except:' swallows every exception, including bugs and "
                "KeyboardInterrupt.",
                "medium",
            )
        elif isinstance(node.type, ast.Name) and node.type.id in {"Exception", "BaseException"}:
            self._add(
                "broad-except",
                node,
                f"Catching '{node.type.id}' hides unexpected errors and makes bugs "
                "silent.",
                "low",
            )
        self.generic_visit(node)

    def visit_BinOp(self, node: ast.BinOp) -> None:
        # a / b  or  a % b  where b is a parameter -> possible ZeroDivisionError.
        if isinstance(node.op, (ast.Div, ast.Mod, ast.FloorDiv)):
            rhs = node.right
            if isinstance(rhs, ast.Name) and rhs.id in self._evidence()["params"]:
                self._add(
                    "zero-division",
                    node,
                    f"Division by parameter '{rhs.id}' raises ZeroDivisionError when it "
                    "is 0.",
                    "high",
                    extra={"param": rhs.id},
                )
        self.generic_visit(node)

    def visit_Subscript(self, node: ast.Subscript) -> None:
        # param[...] -> possible IndexError/KeyError on empty/missing input.
        val = node.value
        if isinstance(val, ast.Name) and val.id in self._evidence()["params"]:
            self._add(
                "index-error",
                node,
                f"Indexing parameter '{val.id}' can raise IndexError/KeyError for "
                "empty or missing input.",
                "medium",
                extra={"param": val.id},
            )
        self.generic_visit(node)

    def visit_Attribute(self, node: ast.Attribute) -> None:
        # param.attr -> possible AttributeError when the argument is None.
        val = node.value
        if isinstance(val, ast.Name) and val.id in self._evidence()["params"]:
            self._add(
                "none-deref",
                node,
                f"Accessing '.{node.attr}' on parameter '{val.id}' raises "
                "AttributeError if it is None.",
                "medium",
                extra={"param": val.id},
            )
        self.generic_visit(node)


def analyze_source(source: str, *, filename: str = "<input>") -> list[Finding]:
    """Analyze Python *source* and return a list of Findings.

    Syntax errors are surfaced as a single high-severity Finding rather than
    raising, so the caller (CLI/UI) always gets a usable result.
    """
    try:
        tree = ast.parse(source, filename=filename)
    except SyntaxError as exc:  # noqa: BLE001 - reported as a finding
        return [
            Finding(
                id=_fid(),
                kind="syntax-error",
                target="<module>",
                location=(exc.lineno or 0, exc.offset or 0),
                rationale=f"Source does not parse: {exc.msg}.",
                severity="high",
                evidence={"function": None, "params": []},
            )
        ]
    analyzer = _Analyzer(source)
    analyzer.visit(tree)
    return analyzer.findings
