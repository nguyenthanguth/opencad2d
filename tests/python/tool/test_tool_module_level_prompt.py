"""A prompt asked at module level of a tool: the tool is not loaded, and its first command says why (PW4)."""

from collections.abc import Callable
from pathlib import Path
from typing import Any

from tool_process import ToolProcess

Started = Callable[..., tuple[ToolProcess, str, str]]
Make = Callable[[str, str], Path]
Run = Callable[[ToolProcess, str, str], int]

SOURCE = '''
from opencad2d.cad import command, get_point, write

where = get_point("At import")

@command("LATE")
def late():
    write("never")
'''


def test_a_prompt_at_module_level_fails_the_tool_import_and_the_first_command_says_so(
    start_tool: Started, tool_folder: Make, run_command: Run, drawing: Any
) -> None:
    tool, _, token = start_tool(tool_folder("Asker", SOURCE))
    ready = tool.receive("ready")
    print("READY", ready)
    run = run_command(tool, "LATE", token)
    ended = tool.receive("ended")
    print("ENDED", ended)
    assert ended["run"] == run
    print("STDERR", tool.stderr)
    assert any("RuntimeError: get_point works only while a command of the tool runs" in line for line in tool.stderr)
