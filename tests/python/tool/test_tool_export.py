"""The export for AI pipelines in a tool command (PW6.3): a command that reads the drawing the run is in with ``flatten``,
``to_json`` and ``to_graph`` gets what a file and a program outside the app get, the one request ``export`` of the app.

The app is the headless test server of the remote suite, the drawing one of the app, opened and shown, so that the run is in it.
"""

import json
from collections.abc import Callable, Iterator
from pathlib import Path
from typing import Any

import pytest
from tool_process import ToolProcess

import export_scenarios

Started = Callable[..., tuple[ToolProcess, str, str]]
Make = Callable[[str, str], Path]
Run = Callable[[ToolProcess, str, str], int]


@pytest.fixture
def shown(app: Any, tmp_path: Path) -> Iterator[Any]:
    """The drawing of ``export_scenarios``, opened in the app and shown."""
    from opencad2d.cad import App

    path = tmp_path / "plan.dxf"
    export_scenarios.write_file(str(path))
    with App(app) as cad_app:
        yield cad_app.open(str(path))


def test_a_command_reads_the_drawing_it_is_run_in_as_json_and_as_a_graph(
    start_tool: Started, tool_folder: Make, run_command: Run, shown: Any, tmp_path: Path
) -> None:
    out = tmp_path / "result.json"
    scenarios = Path(export_scenarios.__file__).resolve().parent
    source = f'''
import json
import sys
sys.path.insert(0, {str(scenarios)!r})
import export_scenarios
from opencad2d.cad import active_drawing, command

@command("EXPORT")
def export():
    facts = export_scenarios.facts(active_drawing())
    with open({str(out)!r}, "w", encoding="utf-8") as handle:
        json.dump(facts, handle, default=lambda value: "array")
'''
    tool, _, token = start_tool(tool_folder("Export", source))
    tool.receive("ready")

    run_command(tool, "EXPORT", token)

    tool.receive("ended")
    expected = json.loads(json.dumps(export_scenarios.EXPECTED))
    assert json.loads(out.read_text(encoding="utf-8")) == expected
