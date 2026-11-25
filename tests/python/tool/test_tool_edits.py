"""The edit calls inside a Python tool (A8): the calls are requests of the run, so a command that copies, moves and zooms is one
undo step with the run, what is queued goes with the edit, the entities come back refreshed, and a refusal changes nothing and
leaves the edits before it in the run's step.

The app is the headless test server of the remote suite (the console view, 942 by 534 pixels, 10 units high about the origin);
the drawing is ``edit_scenarios.build_plan`` in a drawing of the app, shown, so that the tool's run is in it.
"""

import json
from collections.abc import Callable, Iterator
from pathlib import Path
from typing import Any

import pytest
from tool_process import ToolProcess

import edit_scenarios

Started = Callable[..., tuple[ToolProcess, str, str]]
Make = Callable[[str, str], Path]
Run = Callable[[ToolProcess, str, str], int]


@pytest.fixture
def plan(app: Any) -> Iterator[Any]:
    """The plan of ``edit_scenarios``, in a new drawing of the app, shown."""
    from opencad2d.cad import App

    with App(app) as cad_app:
        drawing = cad_app.new()
        edit_scenarios.build_plan(drawing)
        yield drawing


def _view(drawing: Any) -> tuple[float, ...]:
    return tuple(drawing.view)


def test_a_command_that_copies_moves_and_zooms_is_one_undo_step_with_the_run(
    start_tool: Started, tool_folder: Make, run_command: Run, plan: Any, server: Any
) -> None:
    source = '''
from opencad2d.cad import active_drawing, command

@command("EDITS")
def edits():
    drawing = active_drawing()
    line = drawing.entities("LINE")[0]
    circle = drawing.entities("CIRCLE")[0]
    drawing.copy([line], (0, 50))
    drawing.move([circle], (10, 5))
    drawing.zoom_window((0, 0), (10, 10))
'''
    before = edit_scenarios.snapshot(plan)
    view = _view(plan)
    tool, _, token = start_tool(tool_folder("Edits", source))
    tool.receive("ready")

    run_command(tool, "EDITS", token)

    # One copy of the line more, the circle moved from (200, 0) to (210, 5), the view on the window.
    tool.receive("ended")
    after = edit_scenarios.snapshot(plan)
    assert len(after) == len(before) + 1
    circle = plan.entities("CIRCLE")[0]
    assert (round(circle.net.Center.X, 9), round(circle.net.Center.Y, 9)) == (210.0, 5.0)
    assert _view(plan) == pytest.approx((-3.82022, 0.0, 13.82022, 10.0), abs=1e-4)
    # One U of the command line takes the copy, the move and the zoom back: the run's step is one step.
    assert server.send("type U") == "DONE"
    assert edit_scenarios.differences(before, edit_scenarios.snapshot(plan)) == []
    assert _view(plan) == pytest.approx(view, abs=1e-9)


def test_an_insert_queued_in_a_run_goes_with_the_edit_and_what_the_edit_gives_back_is_read_in_the_run(
    start_tool: Started, tool_folder: Make, run_command: Run, plan: Any, server: Any, tmp_path: Path
) -> None:
    out = tmp_path / "queue.json"
    source = f'''
import json
import math
from opencad2d.cad import active_drawing, command

@command("QUEUED")
def queued():
    drawing = active_drawing()
    line = drawing.entities("LINE")[0]
    added = drawing.add_line((0, 100), (50, 100))
    turned = drawing.rotate([line], (0, 0), math.pi / 2)
    copies = drawing.mirror([line], (0, 0), (10, 0))
    result = {{
        "same": turned[0] is line,
        "real": not added.handle.startswith("~"),
        "end": [round(line.net.EndPoint.X, 9), round(line.net.EndPoint.Y, 9)],
        "copy_end": [round(copies[0].net.EndPoint.X, 9), round(copies[0].net.EndPoint.Y, 9)],
        "copy_is_new": copies[0] is not line and copies[0].handle != line.handle,
        "after": len(drawing.entities()),
    }}
    with open({str(out)!r}, "w", encoding="utf-8") as handle:
        json.dump(result, handle)
'''
    before = edit_scenarios.snapshot(plan)
    tool, _, token = start_tool(tool_folder("Queued", source))
    tool.receive("ready")

    run_command(tool, "QUEUED", token)

    # The line turned a quarter about the origin ends at (0, 100); mirrored across the x axis (the line through (0, 0) and
    # (10, 0)) the copy ends at (0, -100); the added line and the copy make the drawing two objects larger.
    tool.receive("ended")
    read = json.loads(out.read_text(encoding="utf-8"))
    assert read == {
        "same": True,
        "real": True,
        "end": [0.0, 100.0],
        "copy_end": [0.0, -100.0],
        "copy_is_new": True,
        "after": len(before) + 2,
    }
    assert server.send("type U") == "DONE"
    assert edit_scenarios.differences(before, edit_scenarios.snapshot(plan)) == []


def test_an_edit_refused_in_a_run_changes_nothing_and_the_edits_before_it_keep_to_the_runs_step(
    start_tool: Started, tool_folder: Make, run_command: Run, plan: Any, server: Any, tmp_path: Path
) -> None:
    out = tmp_path / "refused.json"
    source = f'''
import json
from opencad2d.cad import active_drawing, command

@command("REFUSED")
def refused():
    drawing = active_drawing()
    line = drawing.entities("LINE")[0]
    locked = drawing.entities(layer="Locked")[0]
    drawing.move([line], (10, 5))
    drawing.add_line((0, 100), (50, 100))
    try:
        drawing.move([locked], (10, 5))
        words = None
    except Exception as error:
        words = str(error)
    with open({str(out)!r}, "w", encoding="utf-8") as handle:
        json.dump({{"words": words}}, handle)
'''
    before = edit_scenarios.snapshot(plan)
    tool, _, token = start_tool(tool_folder("Refused", source))
    tool.receive("ready")

    run_command(tool, "REFUSED", token)

    # The move of the line is the run's; the move of the locked line is refused with the line queued with it (not applied,
    # not kept for the end of the run), and one U takes the run's step back whole.
    tool.receive("ended")
    read = json.loads(out.read_text(encoding="utf-8"))
    assert "is on the locked layer Locked." in read["words"]
    after = edit_scenarios.snapshot(plan)
    assert len(after) == len(before)
    assert after != before
    assert server.send("type U") == "DONE"
    assert edit_scenarios.differences(before, edit_scenarios.snapshot(plan)) == []


def test_a_save_in_a_tool_saves_the_edits_so_far_and_the_run_stays_one_undo_step(
    start_tool: Started, tool_folder: Make, run_command: Run, plan: Any, server: Any, tmp_path: Path
) -> None:
    saved = tmp_path / "saved.dxf"
    source = f'''
from opencad2d.cad import active_drawing, command

@command("SAVES")
def saves():
    drawing = active_drawing()
    drawing.add_line((0, 100), (50, 100))
    drawing.save_as({str(saved)!r})
    drawing.add_line((0, 150), (50, 150))
    drawing.save()
    drawing.add_line((0, 200), (50, 200))
'''
    before = edit_scenarios.snapshot(plan)
    tool, _, token = start_tool(tool_folder("Saves", source))
    tool.receive("ready")

    run_command(tool, "SAVES", token)

    tool.receive("ended")
    from GeometryHelper.Dxf import DxfDatabase

    # The file has the plan and the two lines added before the last save; the third line, added after it, is the drawing's only.
    assert sum(1 for _ in DxfDatabase.Open(str(saved)).ModelSpace) == len(before) + 2
    assert len(edit_scenarios.snapshot(plan)) == len(before) + 3
    # One U takes the whole run back, the saved part too: a save does not split the run's step.
    assert server.send("type U") == "DONE"
    assert edit_scenarios.differences(before, edit_scenarios.snapshot(plan)) == []
