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
import math
import threading
import traceback
from collections.abc import Callable, Iterable, Sequence
from typing import Any, cast

from ._drawing import Documents, Drawing, Entity, point3
from ._remote import _RemoteBackend  # pyright: ignore[reportPrivateUsage]

_net: Any = importlib.import_module("System")
_geo: Any = importlib.import_module("GeometryHelper.Geometry")
_remote: Any = importlib.import_module("OpenCAD2D.Remote")
_dxf: Any = importlib.import_module("GeometryHelper.Dxf")

MOST_POINTS = 1000
"""The most points a finder may give for one entity; more is an error of that item (PW3-D10)."""


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
    """The state of one tool process: its commands, its snap finders, its unloaders and the run it is working in."""

    def __init__(self, app: Any) -> None:
        self.app = app
        self.commands: list[Command] = []
        self.snaps: dict[str, Callable[[Any], object]] = {}
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


_finder = threading.local()
"""Set on the thread that answers snap questions: a finder works on what it is given, never in the run of a command."""


def finder_thread() -> None:
    """Marks the calling thread as the one that answers snap questions, for good."""
    _finder.active = True


def _run(name: str) -> Any:
    """The run going on; ``RuntimeError`` naming the function where none is, as it does on the thread of a snap finder."""
    if runtime is None or runtime.run is None or getattr(_finder, "active", False):
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


def _refused(error: Any) -> ValueError:
    """What the app's refusal of a setting is in Python: a ``ValueError`` with the app's words."""
    return ValueError(str(error.Message))


def get_setting(name: str) -> Any:
    """The value of a setting of this tool, asked of the app over the channel of the tools; it works in or out of a run."""
    assert runtime is not None
    try:
        return runtime.app.GetToolSetting(name)
    except (_remote.CadRemoteException, _net.ArgumentException) as error:
        raise _refused(error) from None


def put_setting(name: str, value: object) -> None:
    """Sets a setting of this tool through the app, which checks it by the setting's own rules."""
    assert runtime is not None
    if value is not None and not isinstance(value, (bool, int, float, str)):
        raise TypeError(f"A setting is a number, a whole number, a bool or text, not {type(value).__name__}.")
    # A Python int would reach the app as a PyInt it cannot read, so each kind is made the .NET value it is.
    wire: Any = value
    if isinstance(value, bool):
        wire = _net.Boolean(value)
    elif isinstance(value, int):
        wire = _net.Int64(value)
    elif isinstance(value, float):
        wire = _net.Double(value)
    elif isinstance(value, str):
        wire = _net.String(value)
    try:
        runtime.app.SetToolSetting(name, wire)
    except (_remote.CadRemoteException, _net.ArgumentException) as error:
        raise _refused(error) from None


def _finite(x: float, y: float) -> list[float]:
    """The point as the channel carries it; ``ValueError`` where a coordinate is not finite."""
    if not (math.isfinite(x) and math.isfinite(y)):
        raise ValueError(f"a point is not finite: ({x}, {y})")
    return [x, y]


def _point_of(value: Any) -> list[float]:
    """One point a finder gave as the two numbers the channel carries: a ``GeoPoint2``, a ``GeoPoint3`` (Z dropped) or ``(x, y)``."""
    kind = type(value).__name__
    if isinstance(value, (_geo.GeoPoint2, _geo.GeoPoint3)):
        point: Any = value
        return _finite(float(point.X), float(point.Y))
    if isinstance(value, (tuple, list)):
        pair = cast("Sequence[Any]", value)
        if len(pair) == 2:
            return _finite(float(pair[0]), float(pair[1]))
    raise TypeError(f"a point is a GeoPoint2, a GeoPoint3 or (x, y), not {kind}")


def _points_of(result: object) -> list[list[float]]:
    """The points of what a finder returned: None for none, else a list of points, at most ``MOST_POINTS``."""
    if result is None:
        return []
    try:
        points = list(cast(Iterable[object], result))
    except TypeError:
        raise TypeError(f"a finder returns a list of points or None, not {type(result).__name__}") from None
    if len(points) > MOST_POINTS:
        raise ValueError(f"{len(points)} points for one entity, at most {MOST_POINTS} are taken")
    return [_point_of(point) for point in points]


def answer_snap(snaps: dict[str, Callable[[Any], object]], message: dict[str, Any]) -> list[dict[str, Any]]:
    """The answers to a ``snap`` question, one for each of its items in their order: ``{"points": [[x, y], ...]}`` or
    ``{"error": "<line>"}``. Each item is the finder of its keyword given the entity of the fragment, so an error in one
    answers that item only."""
    from . import SnapRequest

    items: list[dict[str, Any]] = list(message["items"])
    try:
        by_reference: dict[str, Any] = {}
        for entity in _dxf.DxfDatabase.Parse(str(message["fragment"])).ModelSpace:
            if entity.Handle:
                by_reference[str(entity.Handle)] = entity
    except BaseException as error:
        return [
            {"error": f"{str(item['keyword']).upper()}: the entities could not be read: {type(error).__name__}: {error}"}
            for item in items
        ]
    answers: list[dict[str, Any]] = []
    for item in items:
        keyword = str(item["keyword"]).upper()
        finder = snaps.get(keyword)
        if finder is None:
            answers.append({"error": f"{keyword} has no @snap in the tool."})
            continue
        try:
            entity = by_reference.get(str(item["ref"]))
            if entity is None:
                raise LookupError(f"the entity {item['ref']} is not in the fragment")
            request = SnapRequest(entity, str(item["handle"]), tuple(str(b) for b in item["blocks"]))
            answers.append({"points": _points_of(finder(request))})
        except BaseException as error:
            traceback.print_exc()
            answers.append({"error": f"{keyword}: {type(error).__name__}: {error}"})
    return answers


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
        run = None if runtime is None or getattr(_finder, "active", False) else runtime.run
        if run is None:
            return int(self._own.write(text))
        with self._lock:
            self._lines += text
            *complete, self._lines = self._lines.split("\n")
        for line in complete:
            _call(run.Write, line)
        return len(text)

    def flush(self) -> None:
        run = None if runtime is None or getattr(_finder, "active", False) else runtime.run
        if run is None:
            self._own.flush()
            return
        with self._lock:
            rest, self._lines = self._lines, ""
        if rest:
            _call(run.Write, rest)
