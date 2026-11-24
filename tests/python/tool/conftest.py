"""Fixtures of the tool suite: the process of a Python tool (``python -m opencad2d.tool``) driven as the app drives it.

The tool process runs on coreclr only (the app starts every tool with ``OPENCAD2D_RUNTIME=coreclr``), so the suite is skipped
on the other runtime. The app is the headless test server of the remote suite: it opens the private channel of the tools
(``tools``) and begins the run of a command (``run``); the test plays the part of the app's tool host, which writes the
control messages (``run``, ``cancel``, ``unload``) on the anonymous pipe the process was started with and reads what it says
back (``ready``, ``ended``). The server, its clients and the drawing are the remote suite's own fixtures.
"""

import contextlib
import importlib.util
import msvcrt
import os
import subprocess
import sys
from collections.abc import Callable, Iterator
from pathlib import Path
from typing import Any

import pytest
from tool_process import ToolProcess

_REMOTE = Path(__file__).resolve().parent.parent / "remote" / "conftest.py"
_spec = importlib.util.spec_from_file_location("remote_fixtures", _REMOTE)
assert _spec is not None and _spec.loader is not None
_remote = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_remote)

# The fixtures of the remote suite this one stands on: the headless app, a client of its own channel, a new drawing.
server_exe = _remote.server_exe
server = _remote.server
connect = _remote.connect
app = _remote.app
drawing = _remote.drawing


def pytest_collection_modifyitems(config: pytest.Config, items: list[pytest.Item]) -> None:
    """The tool process runs on coreclr whatever the library under test: it is skipped for netfx."""
    if os.environ.get("OPENCAD2D_RUNTIME") == "coreclr":
        return
    skip = pytest.mark.skip(reason="a tool runs on coreclr only (OPENCAD2D_RUNTIME=coreclr)")
    for item in items:
        if Path(str(item.fspath)).parent == Path(__file__).resolve().parent:
            item.add_marker(skip)


@pytest.fixture
def tool_folder(tmp_path: Path) -> Callable[[str, str], Path]:
    """Returns ``make(name, source)``: a tool folder in the test's own, its ``__init__.py`` the source."""

    def make(name: str, source: str) -> Path:
        folder = tmp_path / "Pythons" / name
        folder.mkdir(parents=True)
        (folder / "__init__.py").write_text(source, encoding="utf-8")
        return folder

    return make


@pytest.fixture
def start_tool(server: Any) -> Iterator[Callable[..., tuple[ToolProcess, str, str]]]:
    """Returns ``start(folder, env=None)``: the tool process of a folder, started as the app starts it, and the pipe and token
    of the channel of the tools it was given. The process is ended with the test."""
    started: list[ToolProcess] = []

    def start(folder: Path, env: dict[str, str] | None = None) -> tuple[ToolProcess, str, str]:
        answer = server.send("tools")
        assert answer.startswith("DONE "), answer
        _, pipe, token = answer.split(" ")
        to_read, to_write = os.pipe()
        from_read, from_write = os.pipe()
        handles = [msvcrt.get_osfhandle(to_read), msvcrt.get_osfhandle(from_write)]
        for handle in handles:
            os.set_handle_inheritable(handle, True)
        child_env = dict(os.environ)
        child_env.update(
            {
                "PYTHONUTF8": "1",
                "PYTHONUNBUFFERED": "1",
                "OPENCAD2D_RUNTIME": "coreclr",
                "OPENCAD2D_TOOL_PIPE": pipe,
                "OPENCAD2D_TOOL_TOKEN": token,
            }
        )
        child_env.update(env or {})
        startup = subprocess.STARTUPINFO(lpAttributeList={"handle_list": handles})  # type: ignore[attr-defined]
        process = subprocess.Popen(
            [sys.executable, "-I", "-X", "utf8", "-u", "-m", "opencad2d.tool", str(folder), str(handles[0]), str(handles[1])],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
            env=child_env,
            cwd=str(folder),
            close_fds=True,
            startupinfo=startup,
        )
        os.close(to_read)
        os.close(from_write)
        tool = ToolProcess(process, to_write, from_read)
        started.append(tool)
        return tool, pipe, token

    yield start
    for tool in started:
        with contextlib.suppress(OSError):
            tool.process.kill()
        with contextlib.suppress(OSError, subprocess.TimeoutExpired):
            tool.process.wait(timeout=10)


@pytest.fixture
def run_command(server: Any) -> Callable[[ToolProcess, str, str], int]:
    """Returns ``run(tool, name, token)``: the app begins the run of a command in the shown drawing and tells the tool;
    gives the run's number."""

    def run(tool: ToolProcess, name: str, token: str) -> int:
        answer = server.send(f"run {name} {token}")
        assert answer.startswith("DONE "), answer
        _, run_id, document = answer.split(" ")
        tool.send({"op": "run", "run": int(run_id), "command": name, "document": int(document)})
        return int(run_id)

    return run
