"""T2: every test that asserts how fast something ran carries ``@pytest.mark.timing``.

A hosted CI runner is slower and shared, so the tests that compare an elapsed time with a limit are left out there
(``OPENCAD2D_SKIP_TIMING=1`` makes ``tools/python/verify-python.sh`` pass ``-m "not timing"``) and run on the developer's
machine. This file reads the source of every test in ``tests/python`` (functions, async functions, and the methods of
``Test*`` classes) and fails when a test asserts an upper limit on a time and lacks the marker. A time is a reading of
``time.perf_counter``, ``monotonic`` or ``time`` (also imported by name or under another name), or a ``seconds`` field.

Two kinds of assert are not speed asserts and need no marker. A test that only asserts that something took *at least* so
long (a timeout that was waited out) cannot be broken by a slow host. A hang guard, an upper limit that is a number of
``HANG_GUARD`` seconds or more, says "this did not hang"; no host is that slow, so a functional test with only that is run on
CI as it is.
"""

import ast
from pathlib import Path

import pytest

HERE = Path(__file__).resolve().parent
CLOCKS = {"perf_counter", "monotonic", "time", "perf_counter_ns", "monotonic_ns"}
SECONDS = "seconds"

# An upper limit written as a number of this many seconds or more is a hang guard (a loose bound a slow host never
# reaches), not a speed assert: a limit of 30 s on the move of 2000 lines, or of 60 s on a worker that never starts.
HANG_GUARD = 10.0

# Tests whose time is read in a process of their own, out of sight of the scan: each must carry the marker too, and each
# must be found by the scan (a renamed test fails the guard instead of leaving the list stale).
MEASURED_ELSEWHERE = {
    "test_k1_render_headless.py::test_the_first_call_and_the_median_of_five_after_it_stay_far_below_a_hang",
}


def _names(target: ast.AST) -> list[str]:
    return [node.id for node in ast.walk(target) if isinstance(node, ast.Name)]


def _is_function(node: ast.AST) -> bool:
    return isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))


class _Clocks:
    """How the clocks are written in one module: ``time`` (or ``import time as t``) and ``from time import perf_counter``."""

    def __init__(self, tree: ast.AST) -> None:
        self.modules = {"time"}
        self.names: set[str] = set()
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                self.modules.update(a.asname for a in node.names if a.name == "time" and a.asname)
            elif isinstance(node, ast.ImportFrom) and node.module == "time":
                self.names.update(a.asname or a.name for a in node.names if a.name in CLOCKS)

    def is_clock(self, node: ast.AST) -> bool:
        if not isinstance(node, ast.Call):
            return False
        function = node.func
        if isinstance(function, ast.Name):
            return function.id in self.names
        return (
            isinstance(function, ast.Attribute)
            and function.attr in CLOCKS
            and isinstance(function.value, ast.Name)
            and function.value.id in self.modules
        )


class _Elapsed:
    """Which expressions of one test carry an elapsed time, found by following the names they are put in."""

    def __init__(self, function: ast.AST, clocks: _Clocks) -> None:
        self.function = function
        self.clocks = clocks
        self.tainted: set[str] = set()
        self.clocked_functions = {
            node.name
            for node in ast.walk(function)
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))
            and any(clocks.is_clock(inner) for inner in ast.walk(node))
        }
        changed = True
        while changed:
            before = len(self.tainted)
            for node in ast.walk(function):
                self._follow(node)
            changed = len(self.tainted) != before

    def carries(self, expression: ast.AST) -> bool:
        for node in ast.walk(expression):
            if self.clocks.is_clock(node):
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
        elif isinstance(node, (ast.comprehension, ast.For, ast.AsyncFor)) and self.carries(node.iter):
            self.tainted.update(_names(node.target))
        elif (
            isinstance(node, ast.Call)
            and isinstance(node.func, ast.Attribute)
            and node.func.attr in ("append", "add")
            and isinstance(node.func.value, ast.Name)
            and any(self.carries(argument) for argument in node.args)
        ):
            self.tainted.add(node.func.value.id)

    @staticmethod
    def _is_hang_guard(bound: ast.AST) -> bool:
        return (
            isinstance(bound, ast.Constant)
            and isinstance(bound.value, (int, float))
            and not isinstance(bound.value, bool)
            and bound.value >= HANG_GUARD
        )

    def asserts_a_speed_limit(self) -> bool:
        for node in ast.walk(self.function):
            if not isinstance(node, ast.Assert):
                continue
            for compare in (inner for inner in ast.walk(node.test) if isinstance(inner, ast.Compare)):
                sides = [compare.left, *compare.comparators]
                for left, operator, right in zip(sides, compare.ops, sides[1:]):
                    if isinstance(operator, (ast.Lt, ast.LtE)) and self.carries(left) and not self._is_hang_guard(right):
                        return True
                    if isinstance(operator, (ast.Gt, ast.GtE)) and self.carries(right) and not self._is_hang_guard(left):
                        return True
        return False


def _marks_timing(decorators: list[ast.expr]) -> bool:
    for decorator in decorators:
        node = decorator.func if isinstance(decorator, ast.Call) else decorator
        if isinstance(node, ast.Attribute) and node.attr == "timing":
            return True
    return False


def _module_is_marked(tree: ast.Module) -> bool:
    return any(
        isinstance(node, ast.Assign)
        and any(isinstance(target, ast.Name) and target.id == "pytestmark" for target in node.targets)
        and _marks_timing([node.value])
        for node in tree.body
    )


def _scan(tree: ast.Module, relative: str) -> list[tuple[str, bool, bool]]:
    """Every test of the module as ``(name, carries the marker, asserts a speed limit)``, in source order."""
    clocks = _Clocks(tree)
    module_marked = _module_is_marked(tree)
    found: list[tuple[str, bool, bool]] = []
    for node in tree.body:
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)) and node.name.startswith("test_"):
            candidates = [(f"{relative}::{node.name}", node, module_marked)]
        elif isinstance(node, ast.ClassDef) and node.name.startswith("Test"):
            class_marked = module_marked or _marks_timing(node.decorator_list)
            candidates = [
                (f"{relative}::{node.name}::{inner.name}", inner, class_marked)
                for inner in node.body
                if isinstance(inner, (ast.FunctionDef, ast.AsyncFunctionDef)) and inner.name.startswith("test_")
            ]
        else:
            continue
        for name, function, inherited in candidates:
            marked = inherited or _marks_timing(function.decorator_list)
            found.append((name, marked, _Elapsed(function, clocks).asserts_a_speed_limit()))
    return found


def _scan_suite() -> list[tuple[str, bool, bool]]:
    found: list[tuple[str, bool, bool]] = []
    for path in sorted(HERE.rglob("test_*.py")):
        relative = path.relative_to(HERE)
        if relative.parts[0] == "typecheck":
            continue
        tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
        found.extend(_scan(tree, relative.as_posix()))
    return found


def test_every_test_that_asserts_a_speed_limit_carries_the_timing_marker() -> None:
    suite = _scan_suite()
    names = {name for name, _, _ in suite}

    assert sorted(MEASURED_ELSEWHERE - names) == [], "a test of MEASURED_ELSEWHERE is not in the suite: renamed?"
    unmarked = [name for name, marked, limited in suite if not marked and (limited or name in MEASURED_ELSEWHERE)]
    assert unmarked == []


def test_the_scan_sees_a_clock_a_derived_name_and_a_seconds_field_and_lets_a_floor_and_a_hang_guard_pass() -> None:
    source = """
import time
import time as clock
from time import perf_counter, monotonic as tick

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
    assert 3.0 <= outcome.seconds < 5.0

def test_d():
    start = time.perf_counter()
    waited = time.perf_counter() - start
    assert waited >= 0.25

def test_e():
    start = perf_counter()
    assert perf_counter() - start < 1.0

def test_f():
    start = tick()
    assert tick() - start < 1.0

def test_g():
    start = clock.perf_counter()
    assert clock.perf_counter() - start < 1.0

async def test_h():
    start = time.perf_counter()
    assert time.perf_counter() - start < 1.0

class TestInClass:
    def test_i(self):
        start = time.perf_counter()
        assert time.perf_counter() - start < 1.0

    @pytest.mark.timing
    def test_j(self):
        start = time.perf_counter()
        assert time.perf_counter() - start < 1.0

def test_k():
    start = time.perf_counter()
    assert time.perf_counter() - start < 10.0

def test_l(outcome):
    assert 3.0 <= outcome.seconds < 10.0

def test_m():
    start = time.perf_counter()
    assert time.perf_counter() - start < 9.9
"""
    scanned = {name.removeprefix("x.py::"): flags for name, *flags in _scan(ast.parse(source), "x.py")}

    assert scanned == {
        "test_a": [False, True],
        "test_b": [False, True],
        "test_c": [False, True],
        "test_d": [False, False],
        "test_e": [False, True],
        "test_f": [False, True],
        "test_g": [False, True],
        "test_h": [False, True],
        "TestInClass::test_i": [False, True],
        "TestInClass::test_j": [True, True],
        "test_k": [False, False],
        "test_l": [False, False],
        "test_m": [False, True],
    }


def test_the_timing_marker_is_registered(pytestconfig: pytest.Config) -> None:
    assert any(line.startswith("timing:") for line in pytestconfig.getini("markers"))
