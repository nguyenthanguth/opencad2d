"""PW3: the example tool ``src/python/examples/tools/circle_marks`` (snap CTOP, setting textHeight, command MARKTOPS, a ribbon
button), run as the app runs a tool: its process started on the folder, a snap question and a run told over the control pipe.

The snap and the files need nothing of the app's settings. MARKTOPS reads ``setting("textHeight")``, so its test waits on the
server side of the channel as ``test_tool_settings`` does (task C1: the console line ``toolsettings TOKEN JSON`` of the test
server): it is red until C1 has merged and goes green with it.
"""

import json
import time
from collections.abc import Callable
from pathlib import Path
from typing import Any

from tool_process import ToolProcess

from GeometryHelper.Dxf import DxfCircle, DxfDatabase, DxfDBText
from GeometryHelper.Geometry import GeoPoint3, GeoVector3

Started = Callable[..., tuple[ToolProcess, str, str]]
Run = Callable[[ToolProcess, str, str], int]

EXAMPLE = Path(__file__).resolve().parents[3] / "src" / "python" / "examples" / "tools" / "circle_marks"


def _circle(x: float, y: float, radius: float) -> DxfCircle:
    return DxfCircle(GeoPoint3(x, y, 0), GeoVector3(0, 0, 1), radius)


def test_the_plugin_json_declares_the_command_the_ribbon_button_the_snap_and_the_setting() -> None:
    manifest: dict[str, Any] = json.loads((EXAMPLE / "plugin.json").read_text(encoding="utf-8"))

    assert manifest["name"] == "circle_marks"
    assert [command["name"] for command in manifest["commands"]] == ["MARKTOPS"]
    (tab,) = manifest["ribbon"]
    (panel,) = tab["panels"]
    assert [(button["command"], button["size"]) for button in panel["buttons"]] == [("MARKTOPS", "large")]
    (snap,) = manifest["snaps"]
    assert (snap["keyword"], snap["entityTypes"], snap["glyph"], snap["onByDefault"]) == (
        "CTOP",
        ["CIRCLE"],
        "quadrant",
        True,
    )
    (setting,) = manifest["settings"]
    assert (setting["name"], setting["kind"], setting["default"]) == ("textHeight", "number", 2.5)


def test_the_process_binds_marktops_and_ctop(start_tool: Started, drawing: Any) -> None:
    tool, _, _ = start_tool(EXAMPLE)

    ready = tool.receive("ready")

    assert ready["commands"] == ["MARKTOPS"]
    assert ready["snaps"] == ["CTOP"]


def test_ctop_gives_the_top_of_a_circle_r_25_at_100_50_as_100_75(
    start_tool: Started, drawing: Any
) -> None:
    tool, _, _ = start_tool(EXAMPLE)
    tool.receive("ready")
    database = DxfDatabase("AC1027")
    circle = _circle(100, 50, 25)
    database.ModelSpace.AppendEntity(circle)

    tool.send(
        {
            "op": "snap",
            "id": 1,
            "fragment": str(database.ToText()),
            "items": [{"keyword": "CTOP", "ref": str(circle.Handle), "handle": "2A", "blocks": []}],
        }
    )

    assert tool.receive("snapped")["answers"] == [{"points": [[100.0, 75.0]]}]


def test_marktops_writes_one_text_2_5_high_at_the_top_of_the_selected_circle(
    start_tool: Started, run_command: Run, drawing: Any, server: Any
) -> None:
    answer = server.send("tools")
    assert answer.startswith("DONE "), answer
    _, pipe, token = answer.split(" ")
    manifest = json.loads((EXAMPLE / "plugin.json").read_text(encoding="utf-8"))
    told = server.send(f"toolsettings {token} {json.dumps(manifest)}")
    assert told == "DONE", told
    drawing.Insert(_circle(100, 50, 25))
    drawing.CommitChanges()
    tool, _, _ = start_tool(EXAMPLE, {"OPENCAD2D_TOOL_PIPE": pipe, "OPENCAD2D_TOOL_TOKEN": token})
    tool.receive("ready")
    run_command(tool, "MARKTOPS", token)
    time.sleep(2.0)  # the run asks "Select objects:" within milliseconds

    assert server.send("type ALL") == "DONE"
    assert server.send("type ") == "DONE"

    tool.receive("ended")
    texts = [entity for entity in drawing.GetObjects() if isinstance(entity, DxfDBText)]
    assert len(texts) == 1
    assert (texts[0].Position.X, texts[0].Position.Y, texts[0].Height) == (100.0, 75.0, 2.5)
