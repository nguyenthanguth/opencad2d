"""PW3: the snaps of a Python tool, on the tool's side of the control pipe (spec section 5.2).

The test plays the app's tool host as ``test_tool_process`` does: it starts the process, reads ``ready`` and, where the app
asks a snap question, writes ``{"op": "snap", ...}`` with a small DXF drawing of the entities (the channel's fragment, each
entity named by its handle in it) and reads ``{"op": "snapped", ...}`` back. A finder is asked on a thread of its own, so
the tests also ask while a command of the tool waits at a prompt and while it computes in a loop of Python.
"""

import time
from collections.abc import Callable
from pathlib import Path
from typing import Any

import pytest
from tool_process import ToolProcess

from GeometryHelper.Dxf import DxfCircle, DxfDatabase, DxfEntity
from GeometryHelper.Geometry import GeoPoint3, GeoVector3

Started = Callable[..., tuple[ToolProcess, str, str]]
Make = Callable[[str, str], Path]
Run = Callable[[ToolProcess, str, str], int]

# The finder of the example of the guide: the top of a circle (a circle r=25 at (100, 50) gives (100, 75)).
CTOP = '''
from opencad2d.cad import SnapRequest, snap
from GeometryHelper.Geometry import GeoPoint2

@snap("ctop")
def circle_top(request: SnapRequest) -> list[GeoPoint2]:
    c = request.entity
    if c.Radius == 0:
        raise ValueError("no circle")
    return [GeoPoint2(c.Center.X, c.Center.Y + c.Radius)]
'''
LATE_ANSWER = 1.0


def _circle(x: float, y: float, radius: float) -> DxfCircle:
    return DxfCircle(GeoPoint3(x, y, 0), GeoVector3(0, 0, 1), radius)


def _fragment(*entities: DxfEntity) -> tuple[str, list[str]]:
    """The DXF text of a small drawing holding the entities, and each one's handle in it."""
    database = DxfDatabase("AC1027")
    for entity in entities:
        database.ModelSpace.AppendEntity(entity)
    return str(database.ToText()), [str(entity.Handle) for entity in entities]


def _item(keyword: str, ref: str, handle: str = "1A", blocks: list[str] | None = None) -> dict[str, Any]:
    return {"keyword": keyword, "ref": ref, "handle": handle, "blocks": blocks or []}


def _ask(tool: ToolProcess, question: int, entities: list[DxfEntity], keywords: list[str]) -> list[dict[str, Any]]:
    """Asks every keyword about every entity in one question and gives the answers, which come in the items' order."""
    fragment, refs = _fragment(*entities)
    items = [_item(keyword, ref) for ref in refs for keyword in keywords]
    tool.send({"op": "snap", "id": question, "fragment": fragment, "items": items})
    reply = tool.receive("snapped")
    assert reply["id"] == question
    assert len(reply["answers"]) == len(items)
    return list(reply["answers"])


def _said(tool: ToolProcess, line: str, seconds: float = 20.0) -> None:
    """Waits until the tool has written the line on its standard error (a command says where it has got to)."""
    deadline = time.monotonic() + seconds
    while line not in tool.stderr:
        assert time.monotonic() < deadline, f"The tool never said {line!r}: {tool.stderr}"
        time.sleep(0.02)


def test_a_snap_function_answers_a_question_with_its_points_in_the_order_it_gave_them(
    start_tool: Started, tool_folder: Make, drawing: Any
) -> None:
    source = '''
from opencad2d.cad import SnapRequest, snap
from GeometryHelper.Geometry import GeoPoint2

@snap("FOUR")
def four(request: SnapRequest) -> list[GeoPoint2]:
    c = request.entity
    return [
        GeoPoint2(c.Center.X + c.Radius, c.Center.Y),
        GeoPoint2(c.Center.X, c.Center.Y + c.Radius),
        GeoPoint2(c.Center.X - c.Radius, c.Center.Y),
        GeoPoint2(c.Center.X, c.Center.Y - c.Radius),
    ]
'''
    tool, _, _ = start_tool(tool_folder("Four", source))
    tool.receive("ready")

    answers = _ask(tool, 7, [_circle(100, 50, 25)], ["FOUR"])

    assert answers == [{"points": [[125.0, 50.0], [100.0, 75.0], [75.0, 50.0], [100.0, 25.0]]}]


def test_the_example_finder_gives_a_circle_r_25_at_100_50_the_top_100_75(
    start_tool: Started, tool_folder: Make, drawing: Any
) -> None:
    tool, _, _ = start_tool(tool_folder("Circles", CTOP))
    tool.receive("ready")

    answers = _ask(tool, 1, [_circle(100, 50, 25)], ["CTOP"])

    assert answers == [{"points": [[100.0, 75.0]]}]


def test_a_point_travels_as_a_geopoint2_a_geopoint3_with_z_dropped_or_an_x_y_pair_and_none_and_empty_are_no_points(
    start_tool: Started, tool_folder: Make, drawing: Any
) -> None:
    source = '''
from opencad2d.cad import SnapRequest, snap
from GeometryHelper.Geometry import GeoPoint2, GeoPoint3

@snap("P2")
def p2(request: SnapRequest):
    return [GeoPoint2(1, 2)]

@snap("P3")
def p3(request: SnapRequest):
    return [GeoPoint3(3, 4, 99)]

@snap("TUPLE")
def tup(request: SnapRequest):
    return [(5, 6), (7.5, 8.5)]

@snap("NONE")
def none(request: SnapRequest):
    return None

@snap("EMPTY")
def empty(request: SnapRequest):
    return []
'''
    tool, _, _ = start_tool(tool_folder("Kinds", source))
    tool.receive("ready")

    answers = _ask(tool, 2, [_circle(0, 0, 1)], ["P2", "P3", "TUPLE", "NONE", "EMPTY"])

    assert answers == [
        {"points": [[1.0, 2.0]]},
        {"points": [[3.0, 4.0]]},
        {"points": [[5.0, 6.0], [7.5, 8.5]]},
        {"points": []},
        {"points": []},
    ]


def test_a_finder_that_raises_makes_that_items_error_line_and_the_other_items_are_answered(
    start_tool: Started, tool_folder: Make, drawing: Any
) -> None:
    tool, _, _ = start_tool(tool_folder("Circles", CTOP))
    tool.receive("ready")

    answers = _ask(tool, 3, [_circle(0, 0, 10), _circle(5, 5, 0), _circle(0, 0, 5)], ["CTOP"])

    assert answers == [
        {"points": [[0.0, 10.0]]},
        {"error": "CTOP: ValueError: no circle"},
        {"points": [[0.0, 5.0]]},
    ]
    assert tool.process.poll() is None


def test_ready_lists_the_snaps_bound_in_capitals_in_the_order_they_were_bound(
    start_tool: Started, tool_folder: Make, drawing: Any
) -> None:
    source = '''
from opencad2d.cad import snap

@snap("ctop")
def top(request): return None

@snap("Mid")
def mid(request): return None

@snap("AAA")
def first(request): return None
'''
    tool, _, _ = start_tool(tool_folder("Order", source))

    assert tool.receive("ready")["snaps"] == ["CTOP", "MID", "AAA"]


def test_a_tool_with_no_snap_says_ready_with_no_snaps(start_tool: Started, tool_folder: Make, drawing: Any) -> None:
    tool, _, _ = start_tool(tool_folder("Plain", "from opencad2d.cad import command\n"))

    assert tool.receive("ready")["snaps"] == []


@pytest.mark.timing
def test_a_question_is_answered_while_a_command_waits_at_get_point(
    start_tool: Started, tool_folder: Make, run_command: Run, drawing: Any, server: Any
) -> None:
    source = CTOP + '''
import sys
from opencad2d.cad import command, get_point

@command("WAIT")
def wait():
    print("waiting", file=sys.stderr)
    get_point("Where")
'''
    tool, _, token = start_tool(tool_folder("Wait", source))
    tool.receive("ready")
    run = run_command(tool, "WAIT", token)
    _said(tool, "waiting")
    fragment, refs = _fragment(_circle(100, 50, 25))

    start = time.perf_counter()
    tool.send({"op": "snap", "id": 5, "fragment": fragment, "items": [_item("CTOP", refs[0])]})
    reply = tool.receive("snapped", LATE_ANSWER)

    assert time.perf_counter() - start < LATE_ANSWER
    assert reply["answers"] == [{"points": [[100.0, 75.0]]}]
    assert server.send("escape") == "DONE"
    tool.send({"op": "cancel", "run": run})
    assert tool.receive("ended")["run"] == run


@pytest.mark.timing
def test_a_question_is_answered_while_a_command_computes_in_a_python_loop(
    start_tool: Started, tool_folder: Make, run_command: Run, drawing: Any, server: Any
) -> None:
    source = CTOP + '''
import sys
from opencad2d.cad import command

@command("SPIN")
def spin():
    print("spinning", file=sys.stderr)
    while True:
        pass
'''
    tool, _, token = start_tool(tool_folder("Spin", source))
    tool.receive("ready")
    run = run_command(tool, "SPIN", token)
    _said(tool, "spinning")
    fragment, refs = _fragment(_circle(100, 50, 25))

    start = time.perf_counter()
    tool.send({"op": "snap", "id": 6, "fragment": fragment, "items": [_item("CTOP", refs[0])]})
    reply = tool.receive("snapped", LATE_ANSWER)

    assert time.perf_counter() - start < LATE_ANSWER
    assert reply["answers"] == [{"points": [[100.0, 75.0]]}]
    assert server.send("escape") == "DONE"
    tool.send({"op": "cancel", "run": run})
    assert tool.receive("ended", 5.0)["run"] == run


def test_questions_are_answered_in_the_order_they_came(start_tool: Started, tool_folder: Make, drawing: Any) -> None:
    tool, _, _ = start_tool(tool_folder("Circles", CTOP))
    tool.receive("ready")
    for question in (11, 12, 13):
        fragment, refs = _fragment(_circle(0, 0, question))
        tool.send({"op": "snap", "id": question, "fragment": fragment, "items": [_item("CTOP", refs[0])]})

    replies = [tool.receive("snapped") for _ in range(3)]

    assert [(r["id"], r["answers"]) for r in replies] == [
        (11, [{"points": [[0.0, 11.0]]}]),
        (12, [{"points": [[0.0, 12.0]]}]),
        (13, [{"points": [[0.0, 13.0]]}]),
    ]


def test_a_keyword_the_tool_has_no_snap_for_is_an_error_that_says_so_and_the_other_items_are_answered(
    start_tool: Started, tool_folder: Make, drawing: Any
) -> None:
    tool, _, _ = start_tool(tool_folder("Circles", CTOP))
    tool.receive("ready")

    answers = _ask(tool, 4, [_circle(0, 0, 10)], ["CTOP", "NOPE", "ctop"])

    assert answers == [
        {"points": [[0.0, 10.0]]},
        {"error": "NOPE has no @snap in the tool."},
        {"points": [[0.0, 10.0]]},
    ]


def test_the_entity_is_read_from_the_fragment_with_its_own_handle_and_the_block_references(
    start_tool: Started, tool_folder: Make, drawing: Any
) -> None:
    source = '''
from opencad2d.cad import SnapRequest, snap
from GeometryHelper.Dxf import DxfCircle

@snap("INFO")
def info(request: SnapRequest):
    c = request.entity
    assert isinstance(c, DxfCircle)
    references = request.block_references
    return [
        (c.Radius, c.Center.X),
        (float(int(request.handle, 16)), float(isinstance(references, tuple))),
        (float(len(references)), float(int(references[0], 16)) if references else -1.0),
        (float(int(references[1], 16)) if len(references) > 1 else -1.0, 0.0),
    ]
'''
    tool, _, _ = start_tool(tool_folder("Info", source))
    tool.receive("ready")
    fragment, refs = _fragment(_circle(7, 0, 20), _circle(9, 0, 5))
    items = [_item("INFO", refs[1], handle="2F", blocks=["2B", "3C"]), _item("INFO", refs[0], handle="1A")]

    tool.send({"op": "snap", "id": 8, "fragment": fragment, "items": items})
    reply = tool.receive("snapped")

    assert reply["answers"] == [
        {"points": [[5.0, 9.0], [47.0, 1.0], [2.0, 43.0], [60.0, 0.0]]},
        {"points": [[20.0, 7.0], [26.0, 1.0], [0.0, -1.0], [-1.0, 0.0]]},
    ]


def test_more_than_1000_points_or_a_point_not_finite_is_that_items_error_and_1000_is_not(
    start_tool: Started, tool_folder: Make, drawing: Any
) -> None:
    source = '''
from opencad2d.cad import snap

@snap("MANY")
def many(request):
    return [(float(n), 0.0) for n in range(1001)]

@snap("LIMIT")
def limit(request):
    return [(float(n), 0.0) for n in range(1000)]

@snap("NAN")
def nan(request):
    return [(float("nan"), 0.0)]

@snap("INF")
def inf(request):
    return [(1.0, float("inf"))]
'''
    tool, _, _ = start_tool(tool_folder("Limits", source))
    tool.receive("ready")

    many, limit, nan, inf = _ask(tool, 9, [_circle(0, 0, 1)], ["MANY", "LIMIT", "NAN", "INF"])

    assert "points" not in many
    assert many["error"].startswith("MANY: ") and "1000" in many["error"]
    assert len(limit["points"]) == 1000
    assert nan["error"].startswith("NAN: ") and "finite" in nan["error"]
    assert inf["error"].startswith("INF: ") and "finite" in inf["error"]


def test_get_point_in_a_finder_raises_runtime_error_that_is_the_items_error(
    start_tool: Started, tool_folder: Make, drawing: Any
) -> None:
    source = '''
from opencad2d.cad import get_point, snap

@snap("ASK")
def ask(request):
    return [get_point("Where")]
'''
    tool, _, _ = start_tool(tool_folder("Ask", source))
    tool.receive("ready")

    answers = _ask(tool, 10, [_circle(0, 0, 1)], ["ASK"])

    assert answers == [{"error": "ASK: RuntimeError: get_point works only while a command of the tool runs."}]


def test_get_point_in_a_finder_does_not_take_the_prompt_of_the_command_that_is_running(
    start_tool: Started, tool_folder: Make, run_command: Run, drawing: Any, server: Any
) -> None:
    source = '''
import sys
from opencad2d.cad import command, get_point, snap

@snap("ASK")
def ask(request):
    return [get_point("From the finder")]

@command("WAIT")
def wait():
    print("waiting", file=sys.stderr)
    get_point("From the command")
    print("answered", file=sys.stderr)
'''
    tool, _, token = start_tool(tool_folder("Ask", source))
    tool.receive("ready")
    run = run_command(tool, "WAIT", token)
    _said(tool, "waiting")

    answers = _ask(tool, 10, [_circle(0, 0, 1)], ["ASK"])

    assert answers[0]["error"].startswith("ASK: RuntimeError: get_point works only while a command of the tool runs")
    assert "answered" not in tool.stderr
    assert server.send("escape") == "DONE"
    tool.send({"op": "cancel", "run": run})
    assert tool.receive("ended")["run"] == run
