"""The tool backend of ``opencad2d.cad``: what its functions do inside the process of a Python tool.

A tool is a folder the app starts in a ``python.exe`` of its own (``python -m opencad2d.tool``). When the user types one of
its commands the app begins a run, a command of the drawing that waits while the tool works, and tells the tool; the tool joins
that run on the private channel of the tools and every call here is a request of it: a prompt is asked on the command line of
the drawing, ``write`` writes a line there, ``cmd`` runs a command inside the run, and what ``commit()`` applies is one undo
step with the run. Esc in the app ends the run with every change taken back and raises ``Cancelled`` here.

Nothing here is public API: ``opencad2d.cad`` calls it where ``in_tool()`` is true, and ``opencad2d.tool`` makes it so.
"""

from __future__ import annotations

import importlib
import io
import threading
from collections.abc import Callable, Sequence
from typing import Any, cast

from ._drawing import Documents, Drawing, Entity, point3
from ._remote import _RemoteBackend  # pyright: ignore[reportPrivateUsage]

_net: Any = importlib.import_module("System")
_geo: Any = importlib.import_module("GeometryHelper.Geometry")
_remote: Any = importlib.import_module("OpenCAD2D.Remote")


class _ToolBackend(_RemoteBackend):
    """A drawing of the app inside a run: what the user is asked is asked as the run's own questions (``get_point`` and the
    others), since the app takes no ``pick`` request inside a run."""

    def pick_point(self, message: str, base: Any) -> Any:
        from . import get_point  # pyright: ignore[reportAttributeAccessIssue]

        point: Any = get_point(message, None if base is None else _geo.GeoPoint2(base.X, base.Y))
        return None if point is None or isinstance(point, str) else point3(point)

    def pick_object(self, message: str) -> tuple[str, Any, Any] | None:
        from . import get_entity  # pyright: ignore[reportAttributeAccessIssue]

        found: Any = get_entity(message)
        if not isinstance(found, Entity):
            return None
        entity = cast(Entity, found)
        return (entity.handle, entity.net, None)

    def pick_objects(self, message: str) -> list[tuple[str, Any, Any]]:
        from . import get_selection  # pyright: ignore[reportAttributeAccessIssue]

        chosen: list[Any] = get_selection()
        return [(e.handle, e.net, None) for e in chosen]


class Command:
    """A command a tool defined with ``@command``: the function and what the window shows of it."""

    def __init__(
        self, name: str, alias: str | None, description: str | None, function: Callable[..., object], uses_selection: bool
    ) -> None:
        self.name = name.upper()
        self.alias = alias.upper() if alias else None
        self.description = description
        self.function = function
        self.uses_selection = uses_selection


class Runtime:
    """The state of one tool process: its commands, its unloaders and the run it is working in."""

    def __init__(self, app: Any) -> None:
        self.app = app
        self.commands: list[Command] = []
        self.unloaders: list[Callable[[], object]] = []
        self.run: Any = None
        self.document: int = 0
        self._drawings: dict[int, Drawing] = {}
        self._queued: list[Drawing] = []

    def find(self, name: str) -> Command | None:
        """The command of that name or alias (compared in capitals), or None."""
        wanted = name.upper()
        for command in self.commands:
            if wanted in (command.name, command.alias):
                return command
        return None

    # ---- the run -----------------------------------------------------------------------------------------------------

    def begin(self, run_id: int, document: int) -> None:
        """Joins the run the app began: every request from here on is of it."""
        self.run = self.app.JoinRun(run_id)
        self.document = document
        self._drawings.clear()
        self._queued.clear()

    def end(self) -> None:
        """Commits what the run's drawings queued, then ends the run well."""
        queued = list(self._queued)
        self._queued.clear()
        for index, drawing in enumerate(queued):
            try:
                drawing.commit()
            except BaseException:
                for rest in queued[index + 1 :]:
                    rest._discard()  # pyright: ignore[reportPrivateUsage]
                raise
        run, self.run = self.run, None
        run.End()

    def abandon(self) -> None:
        """Forgets the run and what its drawings queued; the app has ended it, or the tool failed it."""
        for drawing in self._queued:
            drawing._discard()  # pyright: ignore[reportPrivateUsage]
        self._queued.clear()
        self._drawings.clear()
        self.run = None

    def drawing_of(self, document: Any) -> Drawing:
        """The ``Drawing`` of a ``CadRemoteDocument``: one for each, for the whole run."""
        key = int(document.Id)
        drawing = self._drawings.get(key)
        if drawing is None:
            drawing = Drawing(_ToolBackend(self.app, document), self._remember)
            self._drawings[key] = drawing
        return drawing

    def _remember(self, drawing: Drawing) -> None:
        if not any(drawing is queued for queued in self._queued):
            self._queued.append(drawing)


runtime: Runtime | None = None
"""The runtime of this process; set by ``opencad2d.tool`` before the tool is imported, None in any other process."""


def in_tool() -> bool:
    """Say whether this process is a tool the app started."""
    return runtime is not None


def _run(name: str) -> Any:
    """The run going on; ``RuntimeError`` naming the function where none is."""
    if runtime is None or runtime.run is None:
        raise RuntimeError(f"{name} works only while a command of the tool runs.")
    return runtime.run


def _failed(error: Any) -> BaseException:
    """What a refusal of the app is in Python: ``Cancelled`` for Esc, else a ``RuntimeError`` with the app's words."""
    from . import Cancelled

    if bool(error.IsCancelled):
        return Cancelled("Function cancelled")
    return RuntimeError(str(error.Message))


def _call(method: Callable[..., Any], *arguments: object) -> Any:
    """Calls a method of the run, turning what the app refuses into what Python code expects."""
    try:
        return method(*arguments)
    except _remote.CadRemoteException as error:
        raise _failed(error) from None
    except _net.ArgumentException as error:
        raise ValueError(str(error.Message)) from None


def _strings(items: Sequence[str] | None) -> Any:
    return None if items is None else _net.Array[_net.String](list(items))


def _base(point: Any) -> Any:
    """The base point of a prompt, which the channel takes as a ``GeoPoint3``."""
    return None if point is None else _geo.GeoPoint3(point.X, point.Y, 0.0)


def ask(name: str, kind: str, message: str, base: Any, keywords: Sequence[str] | None, default: str | None) -> Any:
    """Asks the user on the command line of the run's drawing and gives the ``CadRemoteAnswer``."""
    run = _run(name)
    return _call(run.Ask, getattr(_remote.CadAskKind, kind), message, _base(base), _strings(keywords), default)


def point_of(answer: Any) -> Any:
    """The ``GeoPoint2`` of a point answer."""
    return _geo.GeoPoint2(answer.Point.X, answer.Point.Y)


def entity_of(handle: str) -> Entity:
    """The ``Entity`` of the run's drawing with the handle."""
    return active_drawing().entity(handle)


def write(text: str) -> None:
    """Writes the lines of the text on the command line of the run's drawing."""
    run = _run("write")
    for line in text.split("\n"):
        _call(run.Write, line)


def cmd(answers: Any) -> None:
    """Runs a command inside the run, with the answers ``cmd`` turned into what the channel takes."""
    run = _run("cmd")
    _call(run.RunCommand, answers[0], _net.Array[_net.Object](list(answers[1:])))


def active_drawing() -> Drawing:
    """The drawing the run was typed in."""
    run = _run("active_drawing")
    assert runtime is not None and run is not None
    for document in runtime.app.Documents:
        if int(document.Id) == runtime.document:
            return runtime.drawing_of(document)
    raise RuntimeError("The drawing the command was typed in is closed.")


def documents() -> Documents:
    """The drawings of the app, in the order it lists them."""
    _run("documents")
    assert runtime is not None
    current = runtime
    return Documents(lambda: [current.drawing_of(document) for document in current.app.Documents])


def open_drawing(path: str) -> Drawing:
    """Opens a drawing file in the app and gives it."""
    _run("open_drawing")
    assert runtime is not None
    return runtime.drawing_of(_call(runtime.app.Open, path))


def new_drawing(template: str | None) -> Drawing:
    """Makes a new drawing in the app and gives it."""
    _run("new_drawing")
    assert runtime is not None
    return runtime.drawing_of(_call(runtime.app.New, template))


class RunOutput(io.TextIOBase):
    """``sys.stdout`` of a tool: a line printed inside a run goes to the run's command line, in order with ``write``; anything
    printed outside one goes to the process's own standard output, which the app writes to its log."""

    def __init__(self, own: Any) -> None:
        self._own = own
        self._lines = ""
        self._lock = threading.Lock()

    def writable(self) -> bool:
        return True

    def write(self, text: str) -> int:
        run = None if runtime is None else runtime.run
        if run is None:
            return int(self._own.write(text))
        with self._lock:
            self._lines += text
            *complete, self._lines = self._lines.split("\n")
        for line in complete:
            _call(run.Write, line)
        return len(text)

    def flush(self) -> None:
        run = None if runtime is None else runtime.run
        if run is None:
            self._own.flush()
            return
        with self._lock:
            rest, self._lines = self._lines, ""
        if rest:
            _call(run.Write, rest)
