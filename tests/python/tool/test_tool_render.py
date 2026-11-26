"""The render calls in a tool command: a command that draws the drawing the run is in, with ``render_png``, ``render_image`` and
``to_coco``, gets the drawing as the app has it, the line the command added and has not committed included, and the file of the
drawing is not written.

The app is the headless test server of the remote suite, the drawing one of the app, opened and shown, so that the run is in it.
"""

import json
from collections.abc import Callable, Iterator
from pathlib import Path
from typing import Any

import numpy as np
import pytest
from tool_process import ToolProcess

from opencad2d.cad import new_file, open_file

Started = Callable[..., tuple[ToolProcess, str, str]]
Make = Callable[[str, str], Path]
Run = Callable[[ToolProcess, str, str], int]


@pytest.fixture
def shown(app: Any, tmp_path: Path) -> Iterator[Path]:
    """A drawing of a line and a circle, saved as ``plan.dxf`` and opened in the app, shown; the path of the file."""
    from opencad2d.cad import App

    path = tmp_path / "plan.dxf"
    drawing = new_file()
    drawing.add_line((0, 0), (400, 300))
    drawing.add_circle((200, 150), 60, color=1)
    drawing.add_text("Plate A-12", (50, 200), 30.0)
    drawing.save_as(str(path))
    with App(app) as cad_app:
        cad_app.open(str(path))
        yield path


def test_a_command_draws_the_drawing_it_is_run_in_with_what_it_added_and_did_not_commit(
    start_tool: Started, tool_folder: Make, run_command: Run, shown: Path, tmp_path: Path
) -> None:
    out = tmp_path / "out"
    out.mkdir()
    source = f'''
import json
import numpy as np
from opencad2d.cad import active_drawing, command

@command("DRAW")
def draw():
    drawing = active_drawing()
    drawing.add_line((0, 300), (400, 0))                  # queued: the render commits it
    drawing.render_png({str(out / "tool.png")!r}, width=800)
    np.save({str(out / "image.npy")!r}, drawing.render_image(width=800))
    coco = drawing.to_coco({str(out / "coco.png")!r}, width=800)
    with open({str(out / "coco.json")!r}, "w", encoding="utf-8") as handle:
        json.dump(coco, handle)
'''
    before = (shown.read_bytes(), shown.stat().st_mtime_ns)
    tool, _, token = start_tool(tool_folder("Draw", source))
    tool.receive("ready")

    run_command(tool, "DRAW", token)

    tool.receive("ended")
    expected = open_file(str(shown))
    expected.add_line((0, 300), (400, 0))
    expected.render_png(str(out / "file.png"), width=800)
    assert (out / "tool.png").read_bytes() == (out / "file.png").read_bytes()
    assert np.array_equal(np.load(out / "image.npy"), expected.render_image(width=800))
    coco = json.loads((out / "coco.json").read_text(encoding="utf-8"))
    from_file = expected.to_coco(str(out / "coco-file.png"), width=800)
    assert coco["annotations"] == from_file["annotations"]
    assert coco["categories"] == from_file["categories"]
    assert coco["images"] == [{"id": 1, "file_name": "coco.png", "width": 800, "height": 600}]
    assert len(coco["annotations"]) == 4
    assert (out / "coco.png").read_bytes() == (out / "coco-file.png").read_bytes()
    assert (shown.read_bytes(), shown.stat().st_mtime_ns) == before
