"""The view, the selection by area and the system variables inside a Python tool (A6): the calls are requests of the run, so a
command that draws and zooms is one undo step, and what it reads (the view, the selection, a variable) is read in the run.

The app is the headless test server of the remote suite (the console view, 942 by 534 pixels, 10 units high about the
origin); the fixtures are those of ``conftest.py``.
"""

import json
from collections.abc import Callable
from pathlib import Path
from typing import Any

from tool_process import ToolProcess

Started = Callable[..., tuple[ToolProcess, str, str]]
Make = Callable[[str, str], Path]
Run = Callable[[ToolProcess, str, str], int]


def _view(drawing: Any) -> tuple[float, float, float, float]:
    box = drawing.CurrentView
    return (box.Min.X, box.Min.Y, box.Max.X, box.Max.Y)


def _close(actual: tuple[float, ...], expected: tuple[float, ...], places: float = 1e-4) -> bool:
    return all(abs(a - b) <= places for a, b in zip(actual, expected, strict=True))


def test_a_command_that_draws_and_zooms_is_one_undo_step_with_the_run(
    start_tool: Started, tool_folder: Make, run_command: Run, drawing: Any, server: Any
) -> None:
    source = '''
from opencad2d.cad import active_drawing, command

@command("DRAWZOOM")
def draw_zoom():
    drawing = active_drawing()
    drawing.add_line((0, 0), (100, 0))
    drawing.commit()
    drawing.zoom_window((0, 0), (10, 10))
'''
    before = _view(drawing)
    tool, _, token = start_tool(tool_folder("DrawZoom", source))
    tool.receive("ready")

    run_command(tool, "DRAWZOOM", token)

    tool.receive("ended")
    assert drawing.GetObjects().Count == 1
    assert _close(_view(drawing), (-3.82022, 0.0, 13.82022, 10.0))
    assert server.send("type U") == "DONE"
    assert drawing.GetObjects().Count == 0
    assert _close(_view(drawing), before, 1e-9)


def test_what_a_command_reads_of_the_view_the_selection_and_the_variables_is_read_in_the_run(
    start_tool: Started, tool_folder: Make, run_command: Run, drawing: Any, tmp_path: Path
) -> None:
    out = tmp_path / "read.json"
    source = f'''
import json
from opencad2d.cad import active_drawing, command

@command("READALL")
def read_all():
    drawing = active_drawing()
    for y in (0, 10, 20):
        drawing.add_line((0, y), (100, y))
    drawing.commit()
    drawing.zoom_extents()
    drawing.save_view("All")
    drawing.set_variable("LUNITS", 4)
    result = {{
        "view": list(drawing.view),
        "named": drawing.named_views,
        "lunits": drawing.get_variable("LUNITS"),
        "all": len(drawing.select_all()),
        "crossing": len(drawing.select_crossing((50, 5), (60, 15))),
        "window": drawing.has_window,
    }}
    with open({str(out)!r}, "w", encoding="utf-8") as handle:
        json.dump(result, handle)
'''
    tool, _, token = start_tool(tool_folder("ReadAll", source))
    tool.receive("ready")

    run_command(tool, "READALL", token)

    tool.receive("ended")
    read = json.loads(out.read_text(encoding="utf-8"))
    assert read["named"] == ["All"]
    assert read["lunits"] == 4
    assert read["all"] == 3
    assert read["crossing"] == 1
    assert read["window"] is False
    assert len(read["view"]) == 4
    assert read["view"][0] < 0.0 < read["view"][2]
