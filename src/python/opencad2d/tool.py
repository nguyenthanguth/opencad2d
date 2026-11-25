"""The process of a Python tool: ``python -m opencad2d.tool <folder> <control in> <control out>``, as OpenCAD2D starts it.

A tool is a folder with an ``__init__.py`` that defines commands with ``@command``. Where the first argument is a ``.py`` file
instead of a folder, the process runs that file once as ``python file.py`` does, in the run the app began for PYRUN, and ends. The app starts this module in a
``python.exe`` of the tool's own, with the two ends of an anonymous pipe pair given as handles: the app writes lines of JSON
on the first (``run``, ``cancel``, ``snap``, ``unload``) and the tool answers on the second (``ready``, ``ended``, ``snapped``). Where the tool
drives the app, it does so on the private channel of the tools (``OPENCAD2D_TOOL_PIPE`` and ``OPENCAD2D_TOOL_TOKEN``), through
the functions of ``opencad2d.cad``, which are requests of the run the app began for the command.

Esc in the app ends the run and ``cancel`` arrives: the main thread of the tool gets ``KeyboardInterrupt``, which stops
a loop of Python code. Code the interrupt cannot stop is ended by the app after 5 seconds, and the tool starts again at its
next command.
"""

from __future__ import annotations

import contextlib
import importlib
import json
import os
import queue
import signal
import sys
import threading
import traceback
from pathlib import Path
from typing import IO, Any

from opencad2d.cad import _tool

__all__ = ["main"]


class _Control:
    """The two pipes to the app: lines of JSON read from one and written to the other."""

    def __init__(self, handle_in: int, handle_out: int) -> None:
        import msvcrt

        self.reader: IO[bytes] = os.fdopen(msvcrt.open_osfhandle(handle_in, os.O_RDONLY), "rb")
        self.writer: IO[bytes] = os.fdopen(msvcrt.open_osfhandle(handle_out, 0), "wb", buffering=0)
        self._lock = threading.Lock()

    def send(self, message: dict[str, Any]) -> None:
        """Writes one line; the app has gone when the pipe is broken, and the tool goes with it."""
        data = (json.dumps(message, ensure_ascii=False) + "\n").encode("utf-8")
        try:
            with self._lock:
                self.writer.write(data)
        except OSError:
            os._exit(0)

    def read(self) -> dict[str, Any] | None:
        """Reads one line; None at the end of the pipe, which is the app gone."""
        line = self.reader.readline()
        return None if not line else json.loads(line.decode("utf-8"))


class _Interrupt:
    """Whether a run is going on and the main thread may be interrupted for it."""

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._run = 0
        self._cancelled: set[int] = set()

    def enter(self, run: int) -> bool:
        """Marks the main thread as working in the run; says whether Esc ended the run before it began."""
        with self._lock:
            self._run = run
            return run in self._cancelled

    def leave(self) -> None:
        with self._lock:
            self._run = 0

    def cancel(self, run: int) -> None:
        """Interrupts the main thread when the run is the one it is in; remembers the run otherwise, for it may be about to begin."""
        import _thread

        with self._lock:
            if self._run == run:
                _thread.interrupt_main()
            else:
                self._cancelled.add(run)


def _shielded(action: Any) -> Any:
    """Calls the action again where a late ``KeyboardInterrupt`` (the run was ended as it finished) breaks it."""
    for _ in range(5):
        try:
            return action()
        except KeyboardInterrupt:
            continue
    return action()


def _import_tool(folder: Path) -> str | None:
    """Imports the tool's package; None, or the line that says why it could not be."""
    searched = [str(folder.parent)]
    searched += [p for p in os.environ.get("OPENCAD2D_PYTHONS", "").split(os.pathsep) if p]
    for path in searched:
        if path not in sys.path:
            sys.path.append(path)
    try:
        importlib.import_module(folder.name)
        return None
    except BaseException as error:
        traceback.print_exc()
        return f"{type(error).__name__}: {error}"


def _execute(runtime: _tool.Runtime, interrupt: _Interrupt, control: _Control, message: dict[str, Any], problem: str | None) -> None:
    """Runs one command of the tool in the run the app began: ends it, or fails it, or leaves it to the app where Esc ended it."""
    run_id = int(message["run"])
    name = str(message["command"])
    cancelled = False
    failure: str | None = None
    try:
        try:
            runtime.begin(run_id, int(message["document"]))
            if interrupt.enter(run_id):
                raise KeyboardInterrupt
            command = runtime.find(name)
            if problem is not None:
                failure = f"{name}: the tool could not be loaded: {problem}"
            elif command is None:
                failure = f"{name} is declared by the tool, but it has no @command of that name."
            else:
                command.function()
                _tool_flush()
                interrupt.leave()
                runtime.end()
        except KeyboardInterrupt:
            cancelled = True
        except BaseException as error:
            if _is_cancel(error):
                cancelled = True
            else:
                traceback.print_exc()
                failure = f"{name}: {type(error).__name__}: {error}"
    except KeyboardInterrupt:
        cancelled = True
    finally:
        interrupt.leave()
    with contextlib.suppress(Exception):
        _shielded(lambda: _finish(runtime, failure, cancelled))
    _shielded(lambda: control.send({"op": "ended", "run": run_id}))


def _run_script(path: Path) -> None:
    """Runs a file as ``python path`` does: ``__main__``, its folder first on ``sys.path``; ``sys.exit(0)`` ends it well."""
    import runpy

    saved = sys.argv
    sys.argv = [str(path)]
    sys.path.insert(0, str(path.parent))
    try:
        runpy.run_path(str(path), run_name="__main__")
    except SystemExit as stop:
        if stop.code not in (None, 0):
            raise
    finally:
        sys.argv = saved
        if str(path.parent) in sys.path:
            sys.path.remove(str(path.parent))


def _tool_flush() -> None:
    """Sends what the tool printed and left on a line without its end."""
    sys.stdout.flush()


def _is_cancel(error: BaseException) -> bool:
    """Whether the app refused a request because Esc ended the run."""
    remote = importlib.import_module("OpenCAD2D.Remote")
    refused: Any = error
    return isinstance(error, remote.CadRemoteException) and bool(refused.IsCancelled)


def _finish(runtime: _tool.Runtime, failure: str | None, cancelled: bool) -> None:
    """Leaves the run: a failed one is told to the app (which writes the line and takes the changes back); a cancelled one has
    been ended by the app already, and where it has not (the tool raised ``KeyboardInterrupt`` itself) is ended the same way."""
    run = runtime.run
    if run is not None:
        text = failure or "Cancelled."
        runtime.abandon()
        with contextlib.suppress(Exception):
            run.Fail(text)
    else:
        runtime.abandon()


def main(argv: list[str]) -> int:
    """Runs the tool in the folder ``argv[1]``, controlled through the handles ``argv[2]`` (read) and ``argv[3]`` (write)."""
    if len(argv) != 4:
        print("usage: python -m opencad2d.tool <folder> <control in handle> <control out handle>", file=sys.stderr)
        return 2
    folder = Path(argv[1]).resolve()
    signal.signal(signal.SIGINT, signal.default_int_handler)
    control = _Control(int(argv[2]), int(argv[3]))
    remote = importlib.import_module("OpenCAD2D.Remote")
    app = remote.CadApplication(os.environ["OPENCAD2D_TOOL_PIPE"], os.environ["OPENCAD2D_TOOL_TOKEN"])
    runtime = _tool.Runtime(app)
    _tool.runtime = runtime
    sys.stdout = _tool.RunOutput(sys.stdout)  # pyright: ignore[reportAttributeAccessIssue]
    script = folder.is_file()
    if script:
        runtime.commands.append(_tool.Command("PYRUN", None, None, lambda: _run_script(folder), False))
        problem = None
    else:
        problem = _import_tool(folder)
    control.send(
        {
            "op": "ready",
            "pid": os.getpid(),
            "commands": [command.name for command in runtime.commands],
            "snaps": list(runtime.snaps),
        }
    )

    interrupt = _Interrupt()
    work: queue.Queue[dict[str, Any] | None] = queue.Queue()
    questions: queue.Queue[dict[str, Any] | None] = queue.Queue()

    def listen() -> None:
        while True:
            try:
                message = control.read()
            except (OSError, ValueError):
                message = None
            if message is None:
                os._exit(0)
            op = message.get("op")
            if op == "cancel":
                interrupt.cancel(int(message["run"]))
            elif op == "snap":
                questions.put(message)
            elif op == "unload":
                questions.put(None)
                work.put(None)
                return
            else:
                work.put(message)

    def answer_snaps() -> None:
        """Answers the snap questions in the order they came, on a thread of this tool's own, never the main thread or the
        control listener: a finder may take as long as it likes and the listener must stay free for ``cancel``."""
        _tool.finder_thread()
        while True:
            question = questions.get()
            if question is None:
                return
            answers = _tool.answer_snap(runtime.snaps, question)
            control.send({"op": "snapped", "id": question["id"], "answers": answers})

    threading.Thread(target=answer_snaps, name="opencad2d.tool snaps", daemon=True).start()
    threading.Thread(target=listen, name="opencad2d.tool control", daemon=True).start()
    while True:
        try:
            message = work.get()
            if message is None:
                break
            if message.get("op") == "run":
                _execute(runtime, interrupt, control, message, problem)
                if script:
                    break
        except KeyboardInterrupt:
            continue
    for unloader in runtime.unloaders:
        try:
            unloader()
        except BaseException:
            traceback.print_exc()
    return 0


if __name__ == "__main__":
    code = main(sys.argv)
    sys.stdout.flush()
    os._exit(code)
