"""T2: every test that asserts how fast something ran carries ``@pytest.mark.timing``.

A hosted CI runner is slower and shared, so the tests that compare an elapsed time with a limit are left out there
(``OPENCAD2D_SKIP_TIMING=1`` makes ``tools/python/verify-python.sh`` pass ``-m "not timing"``) and run on the developer's
machine. This file reads the source of every test in ``tests/python`` and fails when a test asserts an upper limit on a
time (a ``perf_counter``, ``monotonic`` or ``time`` reading, or a ``seconds`` field) and lacks the marker. A test that
only asserts that something took *at least* so long (a timeout that was waited out) cannot be broken by a slow host and
needs no marker.
"""

import ast
from pathlib import Path

import pytest

HERE = Path(__file__).resolve().parent
CLOCKS = {"perf_counter", "monotonic", "time", "perf_counter_ns", "monotonic_ns"}
SECONDS = "seconds"

# Tests whose time is read in a process of their own, out of sight of the scan: each must carry the marker too.
MEASURED_ELSEWHERE = {
    "test_k1_render_headless.py::test_the_first_call_and_the_median_of_five_after_it_stay_far_below_a_hang",
}


def _is_clock(node: ast.AST) -> bool:
    return (
        isinstance(node, ast.Call)
        and isinstance(node.func, ast.Attribute)
        and node.func.attr in CLOCKS
        and isinstance(node.func.value, ast.Name)
        and node.func.value.id == "time"
    )


def _names(target: ast.AST) -> list[str]:
    return [node.id for node in ast.walk(target) if isinstance(node, ast.Name)]


class _Elapsed:
    """Which expressions of one test function carry an elapsed time, found by following the names they are put in."""

    def __init__(self, function: ast.AST) -> None:
        self.function = function
        self.tainted: set[str] = set()
        self.clocked_functions = {
            node.name
            for node in ast.walk(function)
            if isinstance(node, ast.FunctionDef) and any(_is_clock(inner) for inner in ast.walk(node))
        }
        changed = True
        while changed:
            before = len(self.tainted)
            for node in ast.walk(function):
                self._follow(node)
            changed = len(self.tainted) != before

    def carries(self, expression: ast.AST) -> bool:
        for node in ast.walk(expression):
            if _is_clock(node):
                return True
            if isinstance(node, ast.Name) and node.id in self.tainted:
                return True
            if isinstance(node, ast.Attribute) and node.attr == SECONDS:
                return True
            if isinstance(node, ast.Constant) and node.value == SECONDS:
                return True
            if isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id in self.clocked_functions:
                return True
        return False

    def _follow(self, node: ast.AST) -> None:
        if isinstance(node, ast.Assign) and self.carries(node.value):
            for target in node.targets:
                self.tainted.update(_names(target))
        elif isinstance(node, (ast.AugAssign, ast.AnnAssign)) and node.value is not None and self.carries(node.value):
            self.tainted.update(_names(node.target))
        elif isinstance(node, (ast.comprehension, ast.For)) and self.carries(node.iter):
            self.tainted.update(_names(node.target))
        elif (
            isinstance(node, ast.Call)
            and isinstance(node.func, ast.Attribute)
            and node.func.attr in ("append", "add")
            and isinstance(node.func.value, ast.Name)
            and any(self.carries(argument) for argument in node.args)
        ):
            self.tainted.add(node.func.value.id)

    def asserts_an_upper_limit(self) -> bool:
        for node in ast.walk(self.function):
            if not isinstance(node, ast.Assert):
                continue
            for compare in (inner for inner in ast.walk(node.test) if isinstance(inner, ast.Compare)):
                sides = [compare.left, *compare.comparators]
                for left, operator, right in zip(sides, compare.ops, sides[1:]):
                    if isinstance(operator, (ast.Lt, ast.LtE)) and self.carries(left):
                        return True
                    if isinstance(operator, (ast.Gt, ast.GtE)) and self.carries(right):
                        return True
        return False


def _is_marked(function: ast.FunctionDef) -> bool:
    for decorator in function.decorator_list:
        node = decorator.func if isinstance(decorator, ast.Call) else decorator
        if isinstance(node, ast.Attribute) and node.attr == "timing":
            return True
    return False


def _unmarked_timing_tests() -> list[str]:
    found: list[str] = []
    for path in sorted(HERE.rglob("test_*.py")):
        relative = path.relative_to(HERE)
        if relative.parts[0] == "typecheck":
            continue
        tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
        for node in tree.body:
            if isinstance(node, ast.FunctionDef) and node.name.startswith("test_"):
                name = f"{relative.as_posix()}::{node.name}"
                if not _is_marked(node) and (name in MEASURED_ELSEWHERE or _Elapsed(node).asserts_an_upper_limit()):
                    found.append(name)
    return found


def test_every_test_that_asserts_an_upper_limit_on_a_time_carries_the_timing_marker() -> None:
    assert _unmarked_timing_tests() == []


def test_the_scan_sees_a_clock_a_derived_name_and_a_seconds_field_and_lets_a_floor_pass() -> None:
    source = """
import time

def test_a():
    start = time.perf_counter()
    taken = time.perf_counter() - start
    assert taken < 1.0

def test_b():
    started = time.monotonic()
    waits = []
    waits.append(time.monotonic() - started)
    assert max(waits) <= 2.0

def test_c(outcome):
    assert 3.0 <= outcome.seconds < 10.0

def test_d():
    start = time.perf_counter()
    waited = time.perf_counter() - start
    assert waited >= 0.25
"""
    functions = [node for node in ast.parse(source).body if isinstance(node, ast.FunctionDef)]
    limited = {function.name: _Elapsed(function).asserts_an_upper_limit() for function in functions}

    assert limited == {"test_a": True, "test_b": True, "test_c": True, "test_d": False}


def test_the_timing_marker_is_registered(pytestconfig: pytest.Config) -> None:
    assert any(line.startswith("timing:") for line in pytestconfig.getini("markers"))
