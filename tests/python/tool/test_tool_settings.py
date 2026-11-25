"""PW3: the settings of a Python tool, read and written by ``setting`` and ``set_setting`` over the channel of the tools (spec
sections 4 and 5.3, decision PW3-D19).

These tests wait on the server side of the channel (task C1): the token the tool is started with must name a tool that
declares settings. The headless test server learns them from one console line, ``toolsettings TOKEN JSON``, where JSON is the
``plugin.json`` of the tool (its ``name`` and its ``settings``, as spec 5.1 writes them); the server calls
``CadRemoteServer.SetToolSettings`` for the token with the settings of that manifest, over the store of 5.4. Until C1 has
merged (and the test server has that line) the fixture ``declared`` fails with the server's ``unknown command`` and every test
here is red; none of them fakes the server.
"""

import json
import time
from collections.abc import Callable
from pathlib import Path
from typing import Any

import pytest
from tool_process import ToolProcess

from GeometryHelper.Dxf import DxfCircle, DxfDatabase
from GeometryHelper.Geometry import GeoPoint3, GeoVector3

Started = Callable[..., tuple[ToolProcess, str, str]]
Make = Callable[[str, str], Path]
Run = Callable[[ToolProcess, str, str], int]

MANIFEST: dict[str, Any] = {
    "name": "Circles",
    "settings": [
        {
            "name": "textHeight",
            "label": "Text height",
            "kind": "number",
            "default": 2.5,
            "minimum": 0.1,
            "maximum": 1000,
            "description": "How high the marks are written.",
        },
        {"name": "count", "kind": "integer", "default": 3, "minimum": 1, "maximum": 10},
        {"name": "marks", "kind": "boolean", "default": True},
        {"name": "title", "kind": "text", "default": "Plan"},
        {"name": "side", "kind": "choice", "choices": ["Left", "Right"], "default": "Left"},
    ],
}

SOURCE = '''
import sys
from opencad2d.cad import SnapRequest, command, set_setting, setting, snap
from GeometryHelper.Geometry import GeoPoint2

AT_IMPORT = setting("textHeight")

@snap("CTOP")
def circle_top(request: SnapRequest):
    return [GeoPoint2(setting("textHeight"), 0.0)]

@command("SHOW")
def show():
    print("shown", repr(AT_IMPORT), repr(setting("textHeight")), repr(setting("count")), repr(setting("marks")),
          repr(setting("title")), repr(setting("side")), file=sys.stderr)

@command("CHANGE")
def change():
    set_setting("textHeight", 4.0)
    set_setting("count", 7)
    set_setting("marks", False)
    set_setting("title", "Section")
    set_setting("side", "Right")
    print("changed", file=sys.stderr)

@command("REFUSE")
def refuse():
    for name, value in (("textHeight", 0.05), ("nope", 1), ("count", 11), ("side", "Up")):
        try:
            set_setting(name, value)
            print("accepted", name, file=sys.stderr)
        except ValueError as error:
            print("refused", name, str(error), file=sys.stderr)
    print("kept", repr(setting("textHeight")), file=sys.stderr)
'''


@pytest.fixture
def declared(server: Any) -> Callable[[], tuple[str, str]]:
    """Returns ``declare()``: a token of the channel of the tools, named a tool that declares ``MANIFEST``'s settings, and
    the pipe it belongs to. A tool is started with them in its environment, before it imports."""

    def declare() -> tuple[str, str]:
        answer = server.send("tools")
        assert answer.startswith("DONE "), answer
        _, pipe, token = answer.split(" ")
        told = server.send(f"toolsettings {token} {json.dumps(MANIFEST)}")
        assert told == "DONE", told
        return pipe, token

    return declare


def _line(tool: ToolProcess, start: str, seconds: float = 20.0) -> str:
    """The first line the tool wrote on its standard error that starts with ``start``, waited for."""
    deadline = time.monotonic() + seconds
    while True:
        for line in list(tool.stderr):
            if line.startswith(start):
                return line
        assert time.monotonic() < deadline, f"The tool never wrote {start!r}: {tool.stderr}"
        time.sleep(0.02)


def _start(start_tool: Started, tool_folder: Make, declared: Callable[[], tuple[str, str]]) -> tuple[ToolProcess, str]:
    pipe, token = declared()
    tool, _, _ = start_tool(
        tool_folder("Circles", SOURCE), {"OPENCAD2D_TOOL_PIPE": pipe, "OPENCAD2D_TOOL_TOKEN": token}
    )
    tool.receive("ready")
    return tool, token


def test_setting_gives_the_default_at_import_in_a_command_and_in_a_finder(
    start_tool: Started, tool_folder: Make, declared: Callable[[], tuple[str, str]], run_command: Run, drawing: Any
) -> None:
    tool, token = _start(start_tool, tool_folder, declared)
    run_command(tool, "SHOW", token)
    tool.receive("ended")
    database = DxfDatabase("AC1027")
    circle = DxfCircle(GeoPoint3(0, 0, 0), GeoVector3(0, 0, 1), 1)
    database.ModelSpace.AppendEntity(circle)

    tool.send(
        {
            "op": "snap",
            "id": 1,
            "fragment": str(database.ToText()),
            "items": [{"keyword": "CTOP", "ref": str(circle.Handle), "handle": "1A", "blocks": []}],
        }
    )

    assert _line(tool, "shown") == "shown 2.5 2.5 3 True 'Plan' 'Left'"
    assert tool.receive("snapped")["answers"] == [{"points": [[2.5, 0.0]]}]


def test_set_setting_keeps_each_kind_and_setting_gives_it_back_with_its_python_type(
    start_tool: Started, tool_folder: Make, declared: Callable[[], tuple[str, str]], run_command: Run, drawing: Any
) -> None:
    tool, token = _start(start_tool, tool_folder, declared)
    run_command(tool, "CHANGE", token)
    tool.receive("ended")

    run_command(tool, "SHOW", token)
    tool.receive("ended")

    assert _line(tool, "shown") == "shown 2.5 4.0 7 False 'Section' 'Right'"


def test_a_value_out_of_range_or_a_name_not_declared_is_a_value_error_in_the_apps_words_and_nothing_changes(
    start_tool: Started, tool_folder: Make, declared: Callable[[], tuple[str, str]], run_command: Run, drawing: Any
) -> None:
    tool, token = _start(start_tool, tool_folder, declared)

    run_command(tool, "REFUSE", token)
    tool.receive("ended")

    lines = [line for line in tool.stderr if line.startswith(("refused", "accepted", "kept"))]
    assert [line.split(" ")[0:2] for line in lines] == [
        ["refused", "textHeight"],
        ["refused", "nope"],
        ["refused", "count"],
        ["refused", "side"],
        ["kept", "2.5"],
    ]
    assert "Text height" in lines[0] and "0.1" in lines[0] and "1000" in lines[0]
    assert "nope" in lines[1]
