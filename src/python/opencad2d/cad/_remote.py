"""The Drawing API over a running OpenCAD2D: ``connect()`` and ``App``, over ``CadApplication``.

A script written for the app, or for a file, runs here with only ``connect()`` changed. The queue is the ``Drawing``'s own and
reaches the app at ``commit()`` through ``CadRemoteDocument.Insert``, ``Modify``, ``Delete`` and ``CommitChanges``, which
the app applies as one undo step; what a script leaves queued when it ends is lost.

    with connect() as app:
        drawing = app.new()
        drawing.add_circle((50, 50), 25)
        drawing.commit()
"""

from __future__ import annotations

import importlib
from types import TracebackType
from typing import Any

from ._drawing import Backend, Box, Documents, Drawing, Entity, Look, Op, RenderOptions, look_color

_net: Any = importlib.import_module("System")
_dxf: Any = importlib.import_module("GeometryHelper.Dxf")
_geo: Any = importlib.import_module("GeometryHelper.Geometry")
_remote: Any = importlib.import_module("OpenCAD2D.Remote")


def _layer_of(look: Look) -> Any:
    """The ``CadRemoteLayer`` a look stands for."""
    layer = _remote.CadRemoteLayer(look.name)
    layer.IsOn = look.is_on
    layer.IsFrozen = look.is_frozen
    layer.IsLocked = look.is_locked
    layer.IsPlottable = look.is_plottable
    layer.Color = look_color(look)
    layer.Linetype = look.linetype
    layer.LineWeight = _dxf.DxfLineWeight(look.line_weight)
    layer.Description = look.description
    return layer


class _RemoteBackend:
    """A ``CadRemoteDocument`` as the backend of a ``Drawing``."""

    def __init__(self, app: Any, document: Any) -> None:
        self._app = app
        self._document = document

    def _refresh(self) -> None:
        """Reads the list of drawings, which updates which is shown and which has changes."""
        for _ in self._app.Documents:
            pass

    @property
    def name(self) -> str:
        return str(self._document.Name)

    @property
    def path(self) -> str | None:
        path = str(self._document.Path)
        return path if path else None

    @property
    def is_active(self) -> bool:
        self._refresh()
        return bool(self._document.IsActive)

    @property
    def is_modified(self) -> bool:
        self._refresh()
        return bool(self._document.IsModified)

    @property
    def net(self) -> Any:
        return self._document

    def _pairs(self, entities: Any) -> list[tuple[str, Any, Any]]:
        return [(str(self._document.HandleOf(entity)), entity, None) for entity in entities]

    def read(
        self, dxf_type: str | None, layer: str | None, layout: str | None, kind: Any = None
    ) -> list[tuple[str, Any, Any]]:
        if layer is None and layout is None and kind is None:
            return self._pairs(self._document.GetObjects())
        selector = _remote.CadObjectFilter()
        selector.Kind = kind  # the app finds the class and its subclasses, as isinstance does
        selector.Layer = layer
        selector.Layout = layout
        return self._pairs(self._document.GetObjects(selector))

    def read_one(self, handle: str) -> tuple[Any, Any] | None:
        found = self._document.GetObject(handle)
        return None if found is None else (found, None)

    def selected(self) -> list[tuple[str, Any, Any]]:
        return self._pairs(self._document.GetSelectedObjects())

    def select(self, handles: list[str]) -> None:
        self._document.Select(_net.Array[_net.String](handles))

    def _enqueue(self, ops: list[Op]) -> list[str]:
        """Queues the ops in the document, which sends them with the next commit or edit; the tickets of the inserts."""
        document = self._document
        tickets: list[str] = []
        for op in ops:
            if op.kind == "insert":
                tickets.append(str(document.Insert(op.copy, op.layout)))
            elif op.kind == "modify":
                assert op.entity is not None
                document.Modify(op.entity.net)
            elif op.kind == "erase":
                document.Delete(op.handle)
            elif op.kind == "records":
                document.AddRecords(op.records)
            elif op.kind == "add_layer":
                assert op.layer is not None
                document.AddLayer(_layer_of(op.layer._look))  # pyright: ignore[reportPrivateUsage]
            else:
                assert op.layer is not None
                document.ModifyLayer(_layer_of(op.layer._look))  # pyright: ignore[reportPrivateUsage]
        return tickets

    def commit(self, ops: list[Op]) -> list[str | None]:
        added = len(self._enqueue(ops))
        handles: list[str | None] = [str(handle) for handle in self._document.CommitChanges()]
        return handles + [None] * (added - len(handles))

    def edit(
        self, ops: list[Op], how: str, items: list[Entity], args: tuple[Any, ...]
    ) -> tuple[list[str | None], list[tuple[str, Any, Any]]]:
        """What is queued and an edit call of the document as one commit: one undo step for the app (REMOTE, or the run's own).
        The handles of the inserts are read from their tickets, which the commit's answer filled: no request is made for them."""
        document = self._document
        tickets = self._enqueue(ops)
        given: Any = _net.Array[_dxf.DxfEntity]([item.net for item in items])
        if how == "move":
            found = document.Move[_dxf.DxfEntity](given, args[0])
        elif how == "copy":
            found = document.Copy[_dxf.DxfEntity](given, args[0])
        elif how == "rotate":
            found = document.Rotate[_dxf.DxfEntity](given, args[0], args[1])
        elif how == "scale":
            found = document.Scale[_dxf.DxfEntity](given, args[0], args[1])
        elif how == "mirror":
            found = document.Mirror[_dxf.DxfEntity](given, args[0], args[1], args[2])
        else:
            found = document.Offset(items[0].net, args[0], args[1])
        return [self._made(ticket) for ticket in tickets], self._pairs(found)

    def _made(self, ticket: str) -> str | None:
        """The handle an insert of the commit has in the drawing, from its ticket; None where the app made no object of it."""
        handle = self._document.HandleOfTicket(ticket)
        return None if handle is None else str(handle)

    def layers(self) -> list[Look]:
        looks: list[Look] = []
        for layer in self._document.Layers:
            color = layer.Color
            looks.append(
                Look(
                    name=str(layer.Name),
                    color=int(color.ColorIndex),
                    true_color=int(color.ColorValue) if color.IsByColor else None,
                    linetype=str(layer.Linetype),
                    line_weight=int(layer.LineWeight),
                    description=str(layer.Description),
                    is_on=bool(layer.IsOn),
                    is_frozen=bool(layer.IsFrozen),
                    is_locked=bool(layer.IsLocked),
                    is_plottable=bool(layer.IsPlottable),
                )
            )
        return looks

    @property
    def current_layer(self) -> str:
        return str(self._document.CurrentLayer)

    @current_layer.setter
    def current_layer(self, value: str) -> None:
        self._document.CurrentLayer = value

    @property
    def current_dim_style(self) -> str:
        return str(self._document.CurrentDimensionStyle)

    @current_dim_style.setter
    def current_dim_style(self, value: str) -> None:
        self._document.CurrentDimensionStyle = value

    def names(self, kind: str) -> list[str]:
        if kind == "LTYPE":
            return [str(name) for name in self._document.Linetypes]
        records: Any = {
            "STYLE": self._document.TextStyles,
            "DIMSTYLE": self._document.DimensionStyles,
            "BLOCK": self._document.Blocks,
        }[kind]
        return [str(record.Name) for record in records]

    @property
    def layouts(self) -> list[str]:
        return [str(name) for name in self._document.Layouts]

    @property
    def current_layout(self) -> str:
        return str(self._document.CurrentLayout)

    @current_layout.setter
    def current_layout(self, value: str) -> None:
        self._document.CurrentLayout = value

    @property
    def text_styles(self) -> list[Any]:
        return list(self._document.TextStyles)

    @property
    def dimension_styles(self) -> list[Any]:
        return list(self._document.DimensionStyles)

    @property
    def blocks(self) -> list[Any]:
        return list(self._document.Blocks)

    def pick_point(self, message: str, base: Any) -> Any:
        return self._document.PickPoint(message, base)

    def pick_object(self, message: str) -> tuple[str, Any, Any] | None:
        entity = self._document.PickObject(message)
        return str(self._document.HandleOf(entity)), entity, None

    def pick_objects(self, message: str) -> list[tuple[str, Any, Any]]:
        return self._pairs(self._document.PickObjects(message))

    def save(self) -> None:
        self._document.Save()

    def save_as(self, path: str, version: str | None) -> None:
        self._document.SaveAs(path, version)

    def close(self, discard: bool) -> None:
        self._document.Close(discard)

    # ---- the view, the selection by area, the variables -------------------------------------------------------------

    @property
    def has_window(self) -> bool:
        return bool(self._document.HasWindow)

    def zoom_extents(self) -> None:
        self._document.ZoomExtents()

    def zoom_window(self, first: Any, second: Any) -> None:
        self._document.ZoomWindow(first, second)

    def zoom_objects(self, handles: list[str]) -> None:
        self._document.ZoomObjects(_net.Array[_net.String](handles))

    def zoom_center(self, center: Any, height: float) -> None:
        self._document.ZoomCenter(center, height)

    def zoom_scaled(self, factor: float) -> None:
        self._document.ZoomScaled(factor)

    def zoom_previous(self) -> bool:
        return bool(self._document.ZoomPrevious())

    def pan(self, displacement: Any) -> None:
        self._document.Pan(displacement)

    def regen(self) -> None:
        self._document.Regen()

    @property
    def view(self) -> Box:
        shown = self._document.CurrentView
        return Box(float(shown.Min.X), float(shown.Min.Y), float(shown.Max.X), float(shown.Max.Y))

    @view.setter
    def view(self, box: Box) -> None:
        self._document.SetCurrentView(_geo.GeoPoint2(box.min_x, box.min_y), _geo.GeoPoint2(box.max_x, box.max_y))

    @property
    def named_views(self) -> list[str]:
        return [str(name) for name in self._document.NamedViews]

    def save_view(self, name: str) -> None:
        self._document.SaveView(name)

    def restore_view(self, name: str) -> None:
        self._document.RestoreView(name)

    def delete_view(self, name: str) -> None:
        self._document.DeleteView(name)

    def get_variable(self, name: str) -> Any:
        return self._document.GetVariable(name)

    def set_variable(self, name: str, value: Any) -> None:
        self._document.SetVariable(name, value)

    def select_all(self) -> list[tuple[str, Any, Any]]:
        return self._pairs(self._document.SelectAll())

    def select_window(self, first: Any, second: Any) -> list[tuple[str, Any, Any]]:
        return self._pairs(self._document.SelectWindow(first, second))

    def select_crossing(self, first: Any, second: Any) -> list[tuple[str, Any, Any]]:
        return self._pairs(self._document.SelectCrossingWindow(first, second))

    def render(self, kind: str, path: str | None, options: RenderOptions) -> Any:
        """Refuses: the drawing is in the app, and the Python process holds no ``CadDocument`` to draw. Save it
        (``save_as``) and render ``open_file`` of the copy."""
        name = kind if kind == "to_coco" else f"render_{kind}"
        raise RuntimeError(
            f"{name} works on a drawing of a file: this drawing is in the app. Call save_as(path) and "
            "render open_file(path) instead."
        )

    # ---- where things are drawn (PW6.2) -----------------------------------------------------------------------------

    def bounds(self, layout: str | None, handles: list[str] | None) -> tuple[list[str], Any]:
        if handles is not None:
            table = self._document.GetBoundsOf(_net.Array[_net.String](handles))
        else:
            table = self._document.GetBounds(layout)
        return [str(handle) for handle in table.Handles], table.Boxes

    def overlaps(
        self,
        layout: str | None,
        words: list[str] | None,
        over: list[str] | None,
        handles: list[str] | None,
        tolerance: float,
    ) -> tuple[list[str], list[str], Any]:
        def names(items: list[str] | None) -> Any:
            return None if items is None else _net.Array[_net.String](items)

        table = self._document.GetOverlaps(layout, names(words), names(over), names(handles), tolerance)
        return [str(w) for w in table.Words], [str(o) for o in table.Others], table.Boxes

    # ---- the export for AI pipelines (PW6.3) ------------------------------------------------------------------------

    def export(self, what: str, layout: str | None, tolerance: float) -> tuple[str, Any]:
        """The JSON text of the drawing or of its graph, as the app writes it; there is no .NET graph on this side."""
        if what == "graph":
            return str(self._document.ExportGraph(layout, tolerance)), None
        return str(self._document.ExportJson(layout)), None


class App:
    """A running OpenCAD2D, to use as ``with connect() as app``: ``documents``, ``active_drawing``, ``open``,
    ``new`` and ``run_command``. Leaving the ``with`` ends the connection and drops what the drawings still have queued; a call
    on the app or on one of its drawings after that raises ``System.ObjectDisposedException`` (the connection was closed by the
    end of the ``with`` block: make a new ``connect()``), and does not open a new connection.

    ``App(cad_application)`` takes a ``CadApplication`` already made, which ``connect()`` makes for you::

        with connect() as app:
            print([drawing.name for drawing in app.documents])   # ['plan.dxf', 'Drawing2.dxf']

    Example:
        with connect() as app:
            [d.name for d in app.documents]   # ['Drawing1.dxf', 'Drawing2.dxf'] in the order the app lists them
    """

    def __init__(self, cad_application: Any) -> None:
        self._app = cad_application
        self._drawings: dict[int, Drawing] = {}

    def __enter__(self) -> App:
        return self

    def __exit__(
        self, error_type: type[BaseException] | None, error: BaseException | None, traceback: TracebackType | None
    ) -> None:
        for drawing in self._drawings.values():
            drawing._discard()  # pyright: ignore[reportPrivateUsage]
        self._app.Dispose()

    def _drawing(self, document: Any) -> Drawing:
        key = int(document.Id)
        drawing = self._drawings.get(key)
        if drawing is None:
            backend: Backend = _RemoteBackend(self._app, document)
            drawing = Drawing(backend)
            self._drawings[key] = drawing
        return drawing

    def _listed(self) -> list[Drawing]:
        return [self._drawing(document) for document in self._app.Documents]

    @property
    def documents(self) -> Documents:
        """The drawings the app has open, in its order.

        Example:
            app.documents["Drawing2.dxf"].is_active   # True when that drawing is the one shown
        """
        return Documents(self._listed)

    @property
    def active_drawing(self) -> Drawing | None:
        """The drawing shown, or None where the app has none open.

        Example:
            drawing = app.active_drawing          # the drawing shown; None where the app has none open
        """
        document = self._app.ActiveDocument
        return None if document is None else self._drawing(document)

    def open(self, path: str) -> Drawing:
        """Open a drawing file in the app, which shows it: ``app.open("plan.dxf")``.

        Example:
            drawing = app.open("plan.dxf")        # shown in the app; drawing.name == "plan.dxf"
        """
        return self._drawing(self._app.Open(path))

    def new(self, template: str | None = None) -> Drawing:
        """Make a new drawing in the app, from a template file where one is given, and show it.

        Example:
            drawing = app.new()                   # a blank drawing; its name is 'Drawing1.dxf' in a fresh app
        """
        return self._drawing(self._app.New(template))

    def run_command(self, drawing: Drawing, text: str) -> Any:
        """Type ``text`` at the command line of a drawing, as a user types it (``"\\n"`` is Enter), and return the
        ``CadRemoteCommandResult``: ``app.run_command(drawing, "ZOOM E\\n")``.

        Example:
            app.run_command(drawing, "ZOOM E\\n")  # typed on that drawing's command line; "\\n" is Enter
        """
        return drawing.net.SendCommand(text)


def connect(pid: int | None = None, drawing: str | None = None) -> App:
    """Connect to a running OpenCAD2D: the only one, the one with process id ``pid``, or the one that has the file
    ``drawing`` open. It needs "Allow other programs to connect" on in the app's Global settings::

        with connect() as app:
            app.new().add_line((0, 0), (100, 0))

    Example:
        with connect() as app:                # the only OpenCAD2D running
            drawing = app.new()
            drawing.add_line((0, 0), (100, 0))
            drawing.commit()                  # ['101']
    """
    CadApplication = _remote.CadApplication
    if pid is not None and drawing is not None:
        raise ValueError(f"Give the process id ({pid}) or the drawing ({drawing!r}), not both.")
    if drawing is not None:
        return App(CadApplication.ForDocument(drawing))
    if pid is None:
        return App(CadApplication())
    for running in CadApplication.GetRunning():
        if int(running.ProcessId) == pid:
            return App(CadApplication(running))
    raise RuntimeError(f"No running OpenCAD2D has the process id {pid}.")
