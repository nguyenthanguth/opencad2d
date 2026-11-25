"""I1: ``snap``, ``setting`` and ``set_setting`` of ``opencad2d.cad`` in a process that is no tool (PW3 spec section 4).

The decorator changes nothing there and still checks its keyword; the two functions raise ``RuntimeError``. Inside a tool the
decorator registers the function under its keyword in capitals: the test stands a runtime in for the tool's process, the
process itself being the business of the tool suite (``tool/test_tool_snaps.py``), which runs on coreclr only.
"""

from collections.abc import Iterator
from typing import Any

import pytest

from opencad2d import cad
from opencad2d.cad import _tool  # pyright: ignore[reportPrivateUsage]


@pytest.fixture
def in_a_tool() -> Iterator[Any]:
    """A runtime in the place of the tool's, as ``opencad2d.tool`` makes it, taken away at the end of the test."""
    runtime = _tool.Runtime(None)
    _tool.runtime = runtime
    yield runtime
    _tool.runtime = None


def _finder(request: object) -> None:
    return None


def test_snap_outside_a_tool_gives_the_function_back_unchanged() -> None:
    assert cad.snap("CTOP")(_finder) is _finder


@pytest.mark.parametrize("keyword", ["", "two words", " ", "tab\tkeyword"])
def test_a_snap_keyword_that_is_empty_or_holds_a_space_raises_value_error(keyword: str) -> None:
    with pytest.raises(ValueError, match="keyword"):
        cad.snap(keyword)


def test_setting_outside_a_tool_raises_runtime_error() -> None:
    with pytest.raises(RuntimeError, match="setting works inside OpenCAD2D only"):
        cad.setting("textHeight")


def test_set_setting_outside_a_tool_raises_runtime_error() -> None:
    with pytest.raises(RuntimeError, match="set_setting works inside OpenCAD2D only"):
        cad.set_setting("textHeight", 3.0)


def test_snap_inside_a_tool_registers_the_function_under_its_keyword_in_capitals_in_the_order_bound(
    in_a_tool: Any,
) -> None:
    def other(request: object) -> None:
        return None

    cad.snap("ctop")(_finder)
    cad.snap("Mid")(other)

    assert list(in_a_tool.snaps) == ["CTOP", "MID"]
    assert in_a_tool.snaps["CTOP"] is _finder


def test_a_second_snap_of_one_keyword_replaces_the_first(in_a_tool: Any) -> None:
    def second(request: object) -> None:
        return None

    cad.snap("CTOP")(_finder)
    cad.snap("ctop")(second)

    assert list(in_a_tool.snaps) == ["CTOP"]
    assert in_a_tool.snaps["CTOP"] is second
