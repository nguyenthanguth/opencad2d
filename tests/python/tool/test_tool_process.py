"""The process of a Python tool (S32, S35, S36): ``python -m opencad2d.tool`` over the private channel of the tools.

The test plays the app's tool host: it starts the process the way the app does (a folder, two control pipes, the pipe and the
token of the channel of the tools, ``OPENCAD2D_LIB``), tells it of a run, and reads what comes back. The drawing is the headless
app's; the test reads it through the app's own channel.
"""

import time
from collections.abc import Callable
from pathlib import Path
from typing import Any

import pytest
from tool_process import ToolProcess

Started = Callable[..., tuple[ToolProcess, str, str]]
Make = Callable[[str, str], Path]
Run = Callable[[ToolProcess, str, str], int]

DRAW = '''
from opencad2d.cad import active_drawing, command, write

@command("DRAWIT", alias="DI", description="Draws a line")
def draw_it():
    drawing = active_drawing()
    drawing.add_line((0, 0), (100, 0))
    drawing.commit()
    write("drew a line in " + drawing.name)
    print("printed after write")
'''


def _lines(drawing: Any) -> int:
    return int(drawing.GetObjects().Count)


def test_the_process_says_ready_with_the_commands_it_bound_and_its_pid(
    start_tool: Started, tool_folder: Make, drawing: Any
) -> None:
    tool, _, _ = start_tool(tool_folder("Draw", DRAW))

    ready = tool.receive("ready")

    assert ready["commands"] == ["DRAWIT"]
    assert ready["pid"] == tool.process.pid or ready["pid"] > 0


def test_the_library_is_loaded_from_the_folder_OPENCAD2D_LIB_names(
    start_tool: Started, tool_folder: Make, lib_dir: Path, drawing: Any
) -> None:
    tool, _, _ = start_tool(tool_folder("Draw", DRAW), {"OPENCAD2D_LIB": str(lib_dir)})

    assert tool.receive("ready")["commands"] == ["DRAWIT"]


def test_a_folder_that_holds_no_library_is_a_process_that_does_not_start(
    start_tool: Started, tool_folder: Make, tmp_path: Path, drawing: Any
) -> None:
    empty = tmp_path / "empty"
    empty.mkdir()
    tool, _, _ = start_tool(tool_folder("Draw", DRAW), {"OPENCAD2D_LIB": str(empty)})

    assert tool.ended_within(60) >= 0
    assert tool.process.returncode != 0
    assert any("GeometryHelper" in line for line in tool.stderr)


def test_a_command_draws_in_the_undo_step_of_its_run_and_its_lines_come_in_order(
    start_tool: Started, tool_folder: Make, run_command: Run, drawing: Any, server: Any
) -> None:
    tool, _, token = start_tool(tool_folder("Draw", DRAW))
    tool.receive("ready")

    run = run_command(tool, "DRAWIT", token)

    assert tool.receive("ended")["run"] == run
    assert _lines(drawing) == 1
    # The run ended well: the app's command is over, and the one step is undone by one U.
    assert server.send("type U") == "DONE"
    assert _lines(drawing) == 0


def test_a_command_given_by_its_alias_is_found(start_tool: Started, tool_folder: Make, run_command: Run, drawing: Any) -> None:
    tool, _, token = start_tool(tool_folder("Draw", DRAW))
    tool.receive("ready")

    run_command(tool, "DI", token)

    tool.receive("ended")
    assert _lines(drawing) == 1


def test_a_command_that_raises_fails_its_run_and_the_drawing_is_as_it_was(
    start_tool: Started, tool_folder: Make, run_command: Run, drawing: Any
) -> None:
    source = '''
from opencad2d.cad import active_drawing, command

@command("BOOM")
def boom():
    drawing = active_drawing()
    drawing.add_line((0, 0), (100, 0))
    drawing.commit()
    raise ValueError("no stairs here")
'''
    tool, _, token = start_tool(tool_folder("Boom", source))
    tool.receive("ready")

    run_command(tool, "BOOM", token)

    tool.receive("ended")
    assert _lines(drawing) == 0
    assert any("ValueError: no stairs here" in line for line in tool.stderr)
    # The process is still there for the next command.
    assert tool.process.poll() is None
    run_command(tool, "BOOM", token)
    tool.receive("ended")


def test_a_command_the_tool_does_not_define_fails_its_run_with_a_line_that_says_so(
    start_tool: Started, tool_folder: Make, run_command: Run, drawing: Any
) -> None:
    tool, _, token = start_tool(tool_folder("Draw", DRAW))
    tool.receive("ready")

    run_command(tool, "MISSING", token)

    tool.receive("ended")
    assert tool.process.poll() is None


def test_a_tool_that_cannot_be_imported_fails_each_command_and_stays_up(
    start_tool: Started, tool_folder: Make, run_command: Run, drawing: Any
) -> None:
    tool, _, token = start_tool(tool_folder("Bad", "import no_such_module_anywhere\n"))

    assert tool.receive("ready")["commands"] == []
    run_command(tool, "ANY", token)
    tool.receive("ended")
    assert any("no_such_module_anywhere" in line for line in tool.stderr)


@pytest.mark.timing
def test_a_busy_loop_is_ended_by_the_cancel_message_at_once(
    start_tool: Started, tool_folder: Make, run_command: Run, drawing: Any, server: Any
) -> None:
    source = '''
from opencad2d.cad import active_drawing, command

@command("SPIN")
def spin():
    drawing = active_drawing()
    drawing.add_line((0, 0), (100, 0))
    drawing.commit()
    while True:
        pass
'''
    tool, _, token = start_tool(tool_folder("Spin", source))
    tool.receive("ready")
    run = run_command(tool, "SPIN", token)
    time.sleep(1.0)

    assert server.send("escape") == "DONE"
    tool.send({"op": "cancel", "run": run})
    start = time.perf_counter()

    assert tool.receive("ended", 5.0)["run"] == run
    assert time.perf_counter() - start < 2.0
    assert _lines(drawing) == 0
    assert tool.process.poll() is None


def _typed_until(tool: ToolProcess, server: Any, text: str, marker: str) -> None:
    """Types the text on the command line of the drawing until the tool says (on its standard error) that it took it: typed
    while the run is at its waiting prompt a text is taken by nobody, so it is typed again until the question is up."""
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        server.send("type " + text)
        settled = time.monotonic() + 0.6
        while time.monotonic() < settled:
            if marker in tool.stderr:
                return
            time.sleep(0.02)
    pytest.fail(f"The tool never took {text!r}: stderr {tool.stderr}")


def test_a_get_point_is_answered_on_the_command_line_of_the_drawing(
    start_tool: Started, tool_folder: Make, run_command: Run, drawing: Any, server: Any
) -> None:
    source = '''
import sys
from opencad2d.cad import active_drawing, command, get_distance, get_point

@command("RING")
def ring():
    centre = get_point("Specify centre")
    print("centre", centre.X, centre.Y, file=sys.stderr)
    radius = get_distance("Specify radius", base=centre)
    print("radius", radius, file=sys.stderr)
    drawing = active_drawing()
    drawing.add_circle(centre, radius)
    drawing.commit()
'''
    tool, _, token = start_tool(tool_folder("Ring", source))
    tool.receive("ready")
    run_command(tool, "RING", token)

    _typed_until(tool, server, "5,5", "centre 5.0 5.0")
    _typed_until(tool, server, "12", "radius 12.0")

    tool.receive("ended")
    circles = list(drawing.GetObjects())
    assert len(circles) == 1
    assert circles[0].Radius == 12.0
    assert (circles[0].Center.X, circles[0].Center.Y) == (5.0, 5.0)


def test_cmd_runs_a_command_inside_the_run_as_one_undo_step_with_it(
    start_tool: Started, tool_folder: Make, run_command: Run, drawing: Any, server: Any
) -> None:
    source = '''
from opencad2d.cad import active_drawing, cmd, command

@command("LINES")
def lines():
    cmd("LINE", (0, 0), (50, 0), "")
    cmd("CIRCLE", "10,10", 5)
    active_drawing().add_line((0, 20), (50, 20))
'''
    tool, _, token = start_tool(tool_folder("Lines", source))
    tool.receive("ready")

    run_command(tool, "LINES", token)

    tool.receive("ended")
    assert _lines(drawing) == 3
    assert server.send("type U") == "DONE"
    assert _lines(drawing) == 0


def test_print_goes_to_the_run_and_unload_runs_the_on_unload_functions_and_leaves(
    start_tool: Started, tool_folder: Make, run_command: Run, drawing: Any
) -> None:
    source = '''
from opencad2d.cad import command, on_unload

@command("HELLO")
def hello():
    print("hello from the tool")

@on_unload
def bye():
    print("bye from the tool")
'''
    tool, _, token = start_tool(tool_folder("Hello", source))
    tool.receive("ready")
    run_command(tool, "HELLO", token)
    tool.receive("ended")

    tool.send({"op": "unload"})

    assert tool.ended_within(30) < 30
    assert tool.process.returncode == 0
    assert "bye from the tool" in tool.stdout


def test_the_end_of_the_control_pipe_is_the_end_of_the_process(
    start_tool: Started, tool_folder: Make, drawing: Any
) -> None:
    tool, _, _ = start_tool(tool_folder("Draw", DRAW))
    tool.receive("ready")

    tool.to_tool.close()

    assert tool.ended_within(30) < 30
