"""Styles, linetypes, dimensions, hatches and blocks in a tool command (PW5): a command that adds a text style, a linetype, a
dimension style and a block and the objects that use them, and commits, gives the app all of it as one undo step with the run:
the same ``Drawing`` calls as on a file or from outside, with the records written before the objects that name them.

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
    """A new drawing of the app, shown."""
    from opencad2d.cad import App

    with App(app) as cad_app:
        yield cad_app.new()


def test_a_command_that_adds_styles_a_linetype_and_a_block_and_uses_them_is_one_undo_step_with_the_run(
    start_tool: Started, tool_folder: Make, run_command: Run, shown: Any, tmp_path: Path
) -> None:
    out = tmp_path / "result.json"
    source = f'''
import json
from GeometryHelper.Dxf import DxfLine
from GeometryHelper.Geometry import GeoPoint3
from opencad2d.cad import active_drawing, command

@command("TABLES")
def tables():
    drawing = active_drawing()
    drawing.text_styles.add("NOTE", font="romans.shx", x_scale=0.8)
    drawing.linetypes.load("DASHED")
    drawing.dimension_styles.add("PLAN", text_style="NOTE", scale=50, text_height=3.5)
    drawing.blocks.add("BOLT", [DxfLine(GeoPoint3(0, 0, 0), GeoPoint3(10, 0, 0))])
    drawing.add_text("Plate", (0, 5), 3.0, text_style="NOTE")
    drawing.add_line((0, 0), (10, 0), linetype="DASHED")
    drawing.add_linear_dim((0, 0), (100, 0), (50, 20), dim_style="PLAN")
    drawing.add_hatch([(0, 0), (100, 0), (100, 50), (0, 50)])
    drawing.add_insert("BOLT", (10, 10))
    drawing.commit()
    with open({str(out)!r}, "w", encoding="utf-8") as handle:
        json.dump(
            [
                [str(s.Name) for s in drawing.text_styles],
                [str(s.Name) for s in drawing.dimension_styles],
                list(drawing.linetypes),
                [str(b.Name) for b in drawing.blocks],
                [e.dxf_type for e in drawing.entities()],
            ],
            handle,
        )
'''
    tool, _, token = start_tool(tool_folder("Tables", source))
    tool.receive("ready")

    run_command(tool, "TABLES", token)

    tool.receive("ended")
    assert json.loads(out.read_text(encoding="utf-8")) == [
        ["NOTE", "Standard"],
        ["ISO-25", "PLAN", "Standard"],
        ["ByBlock", "ByLayer", "Continuous", "DASHED"],
        ["BOLT"],
        ["TEXT", "LINE", "DIMENSION", "HATCH", "INSERT"],
    ]
    names = [str(s.Name) for s in shown.text_styles]
    assert names == ["NOTE", "Standard"]
    shown.net.SendCommand("U\n")
    assert [str(s.Name) for s in shown.text_styles] == ["Standard"]
    assert list(shown.linetypes) == ["ByBlock", "ByLayer", "Continuous"]
    assert shown.entities() == []
