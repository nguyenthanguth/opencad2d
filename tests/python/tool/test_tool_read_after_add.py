"""A read after an add in one tool command (the S12 sandbox check): ``add_line`` queues, so ``entities`` still lists the drawing
as it was; ``commit()`` applies the queue and the next read has the line, and a command that never commits has its line when it
ends. This is how a ``Drawing`` works on every backend ([Python](../../../docs/presenter/python.md#the-drawing-api)).

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
    """A new drawing of the app, shown, with two lines."""
    from opencad2d.cad import App

    with App(app) as cad_app:
        drawing = cad_app.new()
        drawing.add_line((0, 0), (10, 0))
        drawing.add_line((0, 5), (10, 5))
        drawing.commit()
        yield drawing


def test_a_read_after_an_add_in_a_command_lists_the_drawing_as_it_was_until_commit_and_the_line_is_there_when_it_ends(
    start_tool: Started, tool_folder: Make, run_command: Run, shown: Any, tmp_path: Path
) -> None:
    out = tmp_path / "counts.json"
    source = f'''
import json
from opencad2d.cad import active_drawing, command

@command("ADDREAD")
def add_read():
    drawing = active_drawing()
    drawing.add_line((0.0, 0.0), (100.0, 50.0))
    queued = len(drawing.entities(layout="Model"))
    drawing.commit()
    committed = len(drawing.entities(layout="Model"))
    drawing.add_line((0.0, 10.0), (100.0, 60.0))
    with open({str(out)!r}, "w", encoding="utf-8") as handle:
        json.dump([queued, committed], handle)
'''
    tool, _, token = start_tool(tool_folder("AddRead", source))
    tool.receive("ready")

    run_command(tool, "ADDREAD", token)

    # Two lines before; the add is queued (2), commit() applies it (3); the second add is committed with the command's end (4).
    tool.receive("ended")
    assert json.loads(out.read_text(encoding="utf-8")) == [2, 3]
    assert len(shown.entities(layout="Model")) == 4
