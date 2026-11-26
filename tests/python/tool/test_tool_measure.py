"""Measuring before placing in a tool command (PW7.1): ``measure_text``, ``free_space`` and ``fits`` inside a run read the drawing as
committed, the same as from outside the app: the tool backend is the remote one over the channel of the tools.

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
    """A new drawing of the app, shown, with a line at x 70."""
    from opencad2d.cad import App

    with App(app) as cad_app:
        drawing = cad_app.new()
        drawing.text_styles.add("NOTE", font="romans.shx")
        drawing.add_line((70, 0), (70, 50))
        drawing.commit()
        yield drawing


def test_a_command_measures_a_text_and_asks_where_it_fits(
    start_tool: Started, tool_folder: Make, run_command: Run, shown: Any, tmp_path: Path
) -> None:
    out = tmp_path / "measure.json"
    source = f'''
import json
from opencad2d.cad import active_drawing, command

@command("MEASURE")
def measure():
    drawing = active_drawing()
    word = drawing.measure_text("Plate A-12", (10, 10), 5.0, style="NOTE")
    places = drawing.free_space((0, 0, 100, 50))
    with open({str(out)!r}, "w", encoding="utf-8") as handle:
        json.dump(
            {{
                "box": list(word.box),
                "frames": len(word.frames),
                "places": [list(place) for place in places],
                "beside": drawing.fits((40, 20, 60, 30)),
                "across": drawing.fits((60, 20, 80, 30)),
                "word": drawing.fits(word),
            }},
            handle,
        )
'''
    tool, _, token = start_tool(tool_folder("Measure", source))
    tool.receive("ready")

    run_command(tool, "MEASURE", token)

    tool.receive("ended")
    got = json.loads(out.read_text(encoding="utf-8"))
    assert got["box"][0] == pytest.approx(10.0, abs=1e-6)
    assert got["frames"] == 1
    assert got["places"] == [[0.0, 0.0, 70.0, 50.0], [70.0, 0.0, 100.0, 50.0]]
    assert got["beside"] is True
    assert got["across"] is False
    assert got["word"] is True
    assert len(shown.entities()) == 1
