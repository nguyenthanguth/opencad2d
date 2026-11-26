"""Where things are drawn in a tool command (PW6.2): ``entity.bounds``, ``drawing.bounds()`` and ``drawing.overlaps()`` inside a
run read the drawing as committed, the same as from outside the app: the tool backend is the remote one over the channel of the tools.

The app is the headless test server of the remote suite, the drawing a new one of the app, shown, so that the run is in it.
"""

import json
from collections.abc import Callable, Iterator
from pathlib import Path
from typing import Any

import pytest
from tool_process import ToolProcess

Started = Callable[..., tuple[ToolProcess, str, str]]
Make = Callable[[str, str], Path]
Run = Callable[[ToolProcess, str, str], int]


@pytest.fixture
def shown(app: Any) -> Iterator[Any]:
    """A new drawing of the app, shown, with a line and a text with a line through it."""
    from opencad2d.cad import App

    with App(app) as cad_app:
        drawing = cad_app.new()
        drawing.text_styles.add("NOTE", font="romans.shx")
        drawing.add_line((0, 0), (100, 40))
        drawing.add_text("Plate A-12", (10, 60), 5.0, text_style="NOTE")
        drawing.add_line((28, 50), (28, 70))
        drawing.commit()
        yield drawing


def test_a_command_reads_the_boxes_and_the_overlaps_of_the_drawing_it_runs_in(
    start_tool: Started, tool_folder: Make, run_command: Run, shown: Any, tmp_path: Path
) -> None:
    out = tmp_path / "spatial.json"
    source = f'''
import json
from opencad2d.cad import active_drawing, command

@command("SPATIAL")
def spatial():
    drawing = active_drawing()
    table = drawing.bounds()
    hits = drawing.overlaps()
    line = drawing.entities("LINE")[0]
    with open({str(out)!r}, "w", encoding="utf-8") as handle:
        json.dump(
            {{
                "handles": table.handles,
                "shape": list(table.boxes.shape),
                "line": list(line.bounds),
                "words": hits.words,
                "others": hits.others,
            }},
            handle,
        )
'''
    tool, _, token = start_tool(tool_folder("Spatial", source))
    tool.receive("ready")

    run_command(tool, "SPATIAL", token)

    tool.receive("ended")
    got = json.loads(out.read_text(encoding="utf-8"))
    handles = [entity.handle for entity in shown.entities()]
    assert got["handles"] == handles
    assert got["shape"] == [3, 4]
    assert got["line"] == [0.0, 0.0, 100.0, 40.0]
    assert got["words"] == [handles[1]]
    assert got["others"] == [handles[2]]
