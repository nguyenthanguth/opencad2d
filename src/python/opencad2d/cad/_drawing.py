"""The Drawing API, the same over a drawing of the app, a drawing of a running app and a drawing file (D1655 to D1659).

A ``Drawing`` queues what a script does to it (``add_line``, ``modify``, ``erase``, a layer made or changed, an entity's
layer or colour set) and ``commit()`` applies it all as one undo step. What a ``Drawing`` reads and writes it asks of a
backend: ``_local`` (a ``CadDocument``, in the app and headless) or ``_remote`` (a ``CadRemoteDocument``). Reads are
copies: ``Entity.net`` and the styles and blocks are never the live objects of the drawing, so nothing bypasses undo.

    drawing = open_file("plan.dxf")
    drawing.add_line((0, 0), (100, 0), layer="Steel")
    drawing.commit()
"""

from __future__ import annotations

import builtins
import importlib
import math
import os
from collections.abc import Callable, Iterator, Sequence
from dataclasses import dataclass
from typing import TYPE_CHECKING, Any, Generic, NamedTuple, Protocol, TypeVar, cast, overload

if TYPE_CHECKING:
    import typing_extensions
    from GeometryHelper.Dxf import (
        DxfAlignedDimension,
        DxfArc,
        DxfBlockReference,
        DxfCircle,
        DxfCurve,
        DxfDBText,
        DxfDiametricDimension,
        DxfEntity,
        DxfHatch,
        DxfLine,
        DxfMText,
        DxfPoint3AngularDimension,
        DxfPolyline,
        DxfRadialDimension,
        DxfRotatedDimension,
    )

    # The class of an Entity's net; a bare Entity is an Entity[DxfEntity]. The default needs typing_extensions, which only
    # the type checker reads: at run time E is a plain TypeVar and nothing is imported.
    E = typing_extensions.TypeVar("E", bound=DxfEntity, default=DxfEntity, covariant=True)
else:
    E = TypeVar("E", covariant=True)

K = TypeVar("K", bound="DxfEntity")
R = TypeVar("R")

_net: Any = importlib.import_module("System")
_dxf: Any = importlib.import_module("GeometryHelper.Dxf")
_geo: Any = importlib.import_module("GeometryHelper.Geometry")


@dataclass
class Look:
    """What a layer is, as plain values the backends turn into their own layer objects."""

    name: str
    color: int = 7
    true_color: int | None = None
    linetype: str = "Continuous"
    line_weight: int = -3
    description: str = ""
    is_on: bool = True
    is_frozen: bool = False
    is_locked: bool = False
    is_plottable: bool = True


@dataclass
class Op:
    """One queued change: ``insert`` (a copy to add), ``modify`` or ``erase`` of an entity, ``add_layer`` or ``modify_layer``,
    ``text_style``, ``dim_style``, ``linetype`` or ``block`` (a table record to add, with its ``spec``), and ``records``, which is
    what those four come to when the commit is sent: one small drawing (``records``) holding the new table records."""

    kind: str
    entity: Entity | None = None
    copy: Any = None
    layout: str | None = None
    handle: str | None = None
    layer: Layer | None = None
    spec: Any = None
    records: Any = None
    needs: tuple[tuple[str, str], ...] = ()


RECORD_KINDS = ("text_style", "dim_style", "linetype", "block")
"""The kinds of ``Op`` that add a table record."""


@dataclass
class TextStyleSpec:
    """What ``text_styles.add`` was given."""

    name: str
    font: str
    x_scale: float
    height: float


@dataclass
class DimStyleSpec:
    """What ``dimension_styles.add`` was given."""

    name: str
    text_style: str | None
    scale: float
    text_height: float
    arrow_size: float
    variables: dict[str, Any]


@dataclass
class LinetypeSpec:
    """What ``linetypes.load`` was given: the linetype as the file spells it, and the file (None for the built-in set)."""

    name: str
    file: str | None


@dataclass
class BlockSpec:
    """What ``blocks.add`` was given: copies of the objects, and the base point."""

    name: str
    entities: list[Any]
    base_point: Any


class Box(NamedTuple):
    """A box of the drawing, in the units of the drawing: the least corner and the greatest, as ``drawing.view`` gives the part
    of the space the canvas shows and takes the one it is to show.

    Example:
        drawing.zoom_window((0, 0), (10, 10))
        drawing.view                                  # Box(min_x=-3.82022, min_y=0.0, max_x=13.82022, max_y=10.0)
        drawing.view = Box(0.0, 0.0, 10.0, 10.0)      # fits the box with no margin, as ZOOM Window does
    """

    min_x: float
    min_y: float
    max_x: float
    max_y: float


class Backend(Protocol):
    """What a ``Drawing`` asks of the drawing underneath it; ``_local`` and ``_remote`` each give it."""

    @property
    def name(self) -> str: ...
    @property
    def path(self) -> str | None: ...
    @property
    def is_active(self) -> bool: ...
    @property
    def is_modified(self) -> bool: ...
    @property
    def net(self) -> Any: ...
    @property
    def current_layer(self) -> str: ...
    @current_layer.setter
    def current_layer(self, value: str) -> None: ...
    @property
    def layouts(self) -> list[str]: ...
    @property
    def current_layout(self) -> str: ...
    @current_layout.setter
    def current_layout(self, value: str) -> None: ...
    @property
    def text_styles(self) -> list[Any]: ...
    @property
    def dimension_styles(self) -> list[Any]: ...
    @property
    def blocks(self) -> list[Any]: ...
    @property
    def current_dim_style(self) -> str: ...
    @current_dim_style.setter
    def current_dim_style(self, value: str) -> None: ...

    def names(self, kind: str) -> list[str]: ...
    def read(
        self, dxf_type: str | None, layer: str | None, layout: str | None, kind: Any = None
    ) -> list[tuple[str, Any, Any]]: ...
    def read_one(self, handle: str) -> tuple[Any, Any] | None: ...
    def selected(self) -> list[tuple[str, Any, Any]]: ...
    def select(self, handles: list[str]) -> None: ...
    def commit(self, ops: list[Op]) -> list[str | None]: ...
    def edit(
        self, ops: list[Op], how: str, items: list[Entity], args: tuple[Any, ...]
    ) -> tuple[list[str | None], list[tuple[str, Any, Any]]]: ...
    def layers(self) -> list[Look]: ...
    def pick_point(self, message: str, base: Any) -> Any: ...
    def pick_object(self, message: str) -> tuple[str, Any, Any] | None: ...
    def pick_objects(self, message: str) -> list[tuple[str, Any, Any]]: ...
    def save(self) -> None: ...
    def save_as(self, path: str, version: str | None) -> None: ...
    def close(self, discard: bool) -> None: ...

    @property
    def has_window(self) -> bool: ...
    def zoom_extents(self) -> None: ...
    def zoom_window(self, first: Any, second: Any) -> None: ...
    def zoom_objects(self, handles: list[str]) -> None: ...
    def zoom_center(self, center: Any, height: float) -> None: ...
    def zoom_scaled(self, factor: float) -> None: ...
    def zoom_previous(self) -> bool: ...
    def pan(self, displacement: Any) -> None: ...
    def regen(self) -> None: ...
    @property
    def view(self) -> Box: ...
    @view.setter
    def view(self, box: Box) -> None: ...
    @property
    def named_views(self) -> list[str]: ...
    def save_view(self, name: str) -> None: ...
    def restore_view(self, name: str) -> None: ...
    def delete_view(self, name: str) -> None: ...
    def get_variable(self, name: str) -> Any: ...
    def set_variable(self, name: str, value: Any) -> None: ...
    def select_all(self) -> list[tuple[str, Any, Any]]: ...
    def select_window(self, first: Any, second: Any) -> list[tuple[str, Any, Any]]: ...
    def select_crossing(self, first: Any, second: Any) -> list[tuple[str, Any, Any]]: ...


def dxf_type_of(net: Any) -> str:
    """The DXF name of an entity (``LINE``, ``LWPOLYLINE``): its ``DxfName``, the type its record is written under."""
    return str(net.DxfName)


def look_color(look: Look) -> Any:
    """The ``DxfColor`` of a layer look: its true colour where it has one, else its colour of the index."""
    if look.true_color is not None:
        rgb = look.true_color
        return _dxf.DxfColor.FromRgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255)
    return _dxf.DxfColor.FromColorIndex(_dxf.DxfColorMethod.ByAci, look.color)


def point3(value: Any) -> Any:
    """A ``GeoPoint3`` from a ``GeoPoint2``, a ``GeoPoint3`` or a tuple or list of 2 or 3 numbers; z is 0 for a 2D point."""
    if isinstance(value, _geo.GeoPoint3):
        return value
    if isinstance(value, _geo.GeoPoint2):
        return _geo.GeoPoint3(value.X, value.Y, 0.0)
    if isinstance(value, (tuple, list)) and len(value) in (2, 3):  # pyright: ignore[reportUnknownArgumentType]
        numbers: list[Any] = list(value)  # pyright: ignore[reportUnknownArgumentType]
        return _geo.GeoPoint3(float(numbers[0]), float(numbers[1]), float(numbers[2]) if len(numbers) == 3 else 0.0)
    raise ValueError(f"A point is a GeoPoint2, a GeoPoint3 or a tuple of 2 or 3 numbers, not {value!r}.")


def point2(value: Any) -> Any:
    """A ``GeoPoint2`` from anything ``point3`` takes; a z is dropped, as the view and the selection calls are 2D."""
    point = point3(value)
    return _geo.GeoPoint2(point.X, point.Y)


def vector2(value: Any) -> Any:
    """A ``GeoVector2`` from a ``GeoVector2`` or a tuple or list of 2 numbers; a displacement of 3 numbers is refused."""
    if isinstance(value, _geo.GeoVector2):
        return value
    if isinstance(value, (tuple, list)) and len(value) == 2:  # pyright: ignore[reportUnknownArgumentType]
        numbers: list[Any] = list(value)  # pyright: ignore[reportUnknownArgumentType]
        return _geo.GeoVector2(float(numbers[0]), float(numbers[1]))
    raise ValueError(f"A displacement is a GeoVector2 or a tuple of 2 numbers, not {value!r}.")


def finite(what: str, *numbers: float) -> None:
    """``ValueError`` unless every number is finite: an edit sends numbers that are numbers, as the app takes none other."""
    for number in numbers:
        if not math.isfinite(number):
            raise ValueError(f"{what} is a finite number, not {number!r}.")


def positive(what: str, number: float) -> None:
    """``ValueError`` unless the number is finite and above nought (a scale factor, an offset distance)."""
    if not (math.isfinite(number) and number > 0.0):
        raise ValueError(f"{what} is a finite number above nought, not {number!r}.")


def variable_value(name: str, value: Any) -> Any:
    """What ``set_variable`` gives the app for a value, as the .NET object the app tells apart by type (a Python ``int`` would
    reach it as a ``PyInt``): ``Int32``, ``Double`` or ``String``, a sequence of 2 or 3 numbers as an array of doubles;
    ``TypeError`` for any other, a ``bool`` included."""
    if isinstance(value, int) and not isinstance(value, bool):
        return _net.Int32(value)
    if isinstance(value, float):
        return _net.Double(value)
    if isinstance(value, str):
        return _net.String(value)
    if isinstance(value, (tuple, list)) and len(value) in (2, 3):  # pyright: ignore[reportUnknownArgumentType]
        numbers: list[Any] = list(value)  # pyright: ignore[reportUnknownArgumentType]
        return _net.Array[_net.Double]([float(number) for number in numbers])
    raise TypeError(
        f"The variable {name} is set to an int, a float, a str or a tuple of 2 or 3 numbers, not {value!r}."
    )


_cad: Any = importlib.import_module("OpenCAD2D")

_TABLES = {"STYLE": "TextStyleTable", "LTYPE": "LinetypeTable", "DIMSTYLE": "DimStyleTable", "BLOCK": "BlockTable"}
_WORDS = {"STYLE": "text style", "LTYPE": "linetype", "DIMSTYLE": "dimension style", "BLOCK": "block"}
_ADDED_BY = {"STYLE": "text_styles.add", "LTYPE": "linetypes.load", "DIMSTYLE": "dimension_styles.add"}


def names_of(database: Any, kind: str) -> list[str]:
    """The names in a table of a ``DxfDatabase``: ``kind`` is ``"STYLE"``, ``"LTYPE"``, ``"DIMSTYLE"`` or ``"BLOCK"``, as the
    DXF names the tables."""
    return [str(record.Name) for record in getattr(database, _TABLES[kind])]


_KIND_OF: dict[Any, str] = {}
"""What each class of entity brings besides its linetype, found once: ``"text"`` (a text style), ``"dim"`` (a dimension style),
``"block"`` (a block) or ``""``."""


def needs(entity: Any) -> tuple[tuple[str, str], ...]:
    """The names an entity brings that the drawing it is added to must have, as (table, name) pairs, left out where the
    drawing always has the name: its linetype, its text style (a TEXT or MTEXT), its dimension style (a dimension) and its
    block (an INSERT). ``()`` for the common object that names none."""
    found: list[tuple[str, str]] = []
    linetype = entity.Linetype
    if linetype and str(linetype).upper() not in ("BYLAYER", "BYBLOCK", "CONTINUOUS"):
        found.append(("LTYPE", str(linetype)))
    cls: Any = entity.__class__
    kind = _KIND_OF.get(cls)
    if kind is None:
        if issubclass(cls, (_dxf.DxfDBText, _dxf.DxfMText)):
            kind = "text"
        elif issubclass(cls, _dxf.DxfDimension):
            kind = "dim"
        elif issubclass(cls, _dxf.DxfBlockReference):
            kind = "block"
        else:
            kind = ""
        _KIND_OF[cls] = kind
    if kind == "text":
        style = entity.TextStyleName
        if style and str(style) != "Standard":
            found.append(("STYLE", str(style)))
    elif kind == "dim":
        found.append(("DIMSTYLE", str(entity.DimensionStyleName or "")))
    elif kind == "block":
        found.append(("BLOCK", str(entity.Name or "")))
    return tuple(found)


def provide(database: Any, wanted: tuple[tuple[str, str], ...], has: Callable[[str, str], bool]) -> None:
    """Gives ``database`` a record of each name an entity ``needs`` that it lacks and ``has`` says the drawing it is to go to
    has: its linetype, its text style or its block, which the library asks of a drawing an entity is added to. A record is made
    of its name alone; the drawing the entity is then copied into keeps its own record of the name. A name ``has`` does not
    know is left, and adding the entity refuses it in the library's words."""
    for kind, name in wanted:
        if kind == "DIMSTYLE" or not name:
            continue
        table = getattr(database, _TABLES[kind])
        if not table.Has(name) and has(kind, name):
            record: Any = {
                "LTYPE": _dxf.DxfLinetypeTableRecord,
                "STYLE": _dxf.DxfTextStyleTableRecord,
                "BLOCK": _dxf.DxfBlockTableRecord,
            }[kind]()
            record.Name = name
            table.Add(record)


def _boxed(record: Any, name: str, value: Any) -> Any:
    """``value`` as the .NET object the property ``name`` of ``record`` takes, which the editor sets by reflection: a Python
    ``int`` would reach it as a ``PyInt``, which no property takes. ``TypeError`` for a property of a kind other than a number, a
    bool, a text or an enum."""
    kind = record.GetType().GetProperty(name).PropertyType
    word = str(kind.Name)
    if kind.IsEnum:
        return _net.Enum.ToObject(kind, int(value))
    if word == "Boolean":
        return _net.Boolean(bool(value))
    if word == "String":
        return _net.String(str(value))
    if word in ("Double", "Single"):
        return getattr(_net, word)(float(value))
    if word in ("Int32", "Int16", "Byte"):
        return getattr(_net, word)(int(value))
    raise TypeError(f"The dimension variable {name} is a {word} and is not set from Python.")


class Staging:
    """The new table records of one commit, made in a small drawing of their own (``database``): the text styles, linetypes,
    dimension styles and blocks queued by ``text_styles.add``, ``linetypes.load``, ``dimension_styles.add`` and ``blocks.add``,
    in the order queued. Each backend writes the small drawing's records into the drawing it works on: a record the drawing has
    is refused here, in words, before anything is sent."""

    def __init__(self, backend: Backend) -> None:
        self.database: Any = _dxf.DxfDatabase("AC1027")
        self.added: dict[str, list[str]] = {"STYLE": [], "LTYPE": [], "BLOCK": [], "DIMSTYLE": []}
        """The names of the records the commit adds, in the order made, by table: not the defaults the small drawing is made with,
        nor the record of a name the drawing has that is made so that another can point at it."""
        self._backend = backend
        self._there: dict[str, set[str]] = {}

    def has(self, kind: str, name: str) -> bool:
        """Whether the drawing has a record of the name (either case), or this commit adds one."""
        if kind not in self._there:
            self._there[kind] = {n.upper() for n in self._backend.names(kind)}
        return name.upper() in self._there[kind] or getattr(self.database, _TABLES[kind]).Has(name)

    def _new(self, kind: str, name: str) -> None:
        """Refuses a name the drawing or this commit has."""
        if self.has(kind, name):
            raise RuntimeError(f'The {_WORDS[kind]} "{name}" already exists.')

    def add(self, op: Op) -> None:
        """Makes the record an op queued."""
        if op.kind == "text_style":
            self._text_style(op.spec)
        elif op.kind == "dim_style":
            self._dim_style(op.spec)
        elif op.kind == "linetype":
            self._linetype(op.spec)
        else:
            self._block(op.spec)

    def _text_style(self, spec: TextStyleSpec) -> None:
        self._new("STYLE", spec.name)
        style = _dxf.DxfTextStyleTableRecord()
        style.Name = spec.name
        style.FileName = spec.font
        style.XScale = spec.x_scale
        style.TextSize = spec.height
        self.database.TextStyleTable.Add(style)
        self.added["STYLE"].append(spec.name)

    def _text_style_named(self, name: str) -> None:
        """Makes sure the small drawing has a text style of the name for a record to point at: one of this commit, or a stub of
        the name for one the drawing has, whose own record is kept; ``ValueError`` where neither has it."""
        if self.database.TextStyleTable.Has(name):
            return
        if not self.has("STYLE", name):
            raise ValueError(f'The text style "{name}" is not in the drawing: add it with text_styles.add first.')
        stub = _dxf.DxfTextStyleTableRecord()
        stub.Name = name
        self.database.TextStyleTable.Add(stub)

    def _dim_style(self, spec: DimStyleSpec) -> None:
        self._new("DIMSTYLE", spec.name)
        # What a new style starts with is ISO-25's, as the Dimension Style Manager's New starts with a metric drawing's style.
        values: Any = _cad.CadDimStyleEditor.Read(self.database.DimStyleTable["ISO-25"])
        style = _dxf.DxfDimStyleTableRecord()
        style.Name = spec.name
        self.database.DimStyleTable.Add(style)
        self.added["DIMSTYLE"].append(spec.name)
        if spec.text_style is not None:
            self._text_style_named(spec.text_style)
            values["Dimtxsty"] = _net.String(spec.text_style)
        values["Dimscale"] = _net.Double(spec.scale)
        values["Dimtxt"] = _net.Double(spec.text_height)
        values["Dimasz"] = _net.Double(spec.arrow_size)
        for key, value in spec.variables.items():
            values[key] = _boxed(style, key, value)
        _cad.CadDimStyleEditor.Write(self.database, style, values)

    def _linetype(self, spec: LinetypeSpec) -> None:
        if self.has("LTYPE", spec.name):
            return
        # The units of the drawing the linetype goes into, as LINETYPE takes acad.lin or acadiso.lin by MEASUREMENT.
        self.database.Measurement = _dxf.DxfMeasurementValue(int(self._backend.get_variable("MEASUREMENT")))
        if spec.file is None:
            for found in _cad.CadLinetypeFile.BuiltIn:
                if str(found.Name).upper() == spec.name.upper():
                    _cad.CadLinetypes.Add(self.database, found)
                    break
        else:
            self.database.LoadLineTypeFile(spec.name, spec.file)
        self.added["LTYPE"].append(spec.name)

    def _block(self, spec: BlockSpec) -> None:
        self._new("BLOCK", spec.name)
        block = _dxf.DxfBlockTableRecord()
        block.Name = spec.name
        block.Origin = point3(spec.base_point)
        self.database.BlockTable.Add(block)
        self.added["BLOCK"].append(spec.name)
        for entity in spec.entities:
            provide(self.database, needs(entity), self.has)
            block.AppendEntity(entity)


class Entity(Generic[E]):
    """One object of a drawing, as a copy: ``handle``, ``dxf_type`` and ``net``, the ``DxfEntity`` copy.

    An ``Entity`` knows the class of its ``net`` for a type checker: ``entities(DxfCircle)`` and ``add_circle`` give an
    ``Entity[DxfCircle]``, whose ``net.Radius`` is a ``float``; one read without a class is a plain ``Entity``, whose ``net``
    is a ``DxfEntity`` until ``isinstance(entity.net, DxfCircle)`` or ``match entity.net:`` narrows it. Nothing changes at
    run time: ``net`` is always the copy of its own class.

    ``layer``, ``color`` (the colour index: 256 ByLayer, 0 ByBlock) and ``linetype`` are set here and committed by
    ``Drawing.commit()`` without ``modify``. ``net`` is one copy for the life of the ``Entity``: a change made to it reaches
    the drawing only through ``Drawing.modify(entity)``. An entity just added has the handle ``~1``, ``~2``... until
    ``commit()`` gives it the real one, and its layer, colour and linetype are given to ``add`` until then::

        line = drawing.entities(type="LINE")[0]
        line.layer = "Steel"      # a line from (0, 0) to (100, 0) now on Steel
        drawing.commit()

    Example:
        line = drawing.entities(type="LINE")[0]
        line.layer = "Steel"                          # a line from (0, 0) to (100, 0) now on Steel
        drawing.commit()
    """

    def __init__(self, drawing: Drawing, handle: str, net: E, source: Any = None) -> None:
        self._drawing = drawing
        self._handle = handle
        self._net: E = net
        self._source = source  # the small drawing the copy lives in: the local backend writes a change from it
        self._type = dxf_type_of(net)

    @property
    def handle(self) -> str:
        """The handle of the object in the drawing (``2F``); ``~n`` for one added and not yet committed.

        Example:
            drawing.entities()[0].handle                  # '101'; an object just added has '~1' until commit()
        """
        return self._handle

    @property
    def dxf_type(self) -> str:
        """The DXF name of the object: ``"LINE"``, ``"CIRCLE"``, ``"LWPOLYLINE"``, ``"TEXT"``.

        Example:
            [e.dxf_type for e in drawing.entities()]      # ['LINE', 'CIRCLE', 'ARC', 'LWPOLYLINE', 'TEXT', 'MTEXT']
        """
        return self._type

    @property
    def net(self) -> E:
        """The ``DxfEntity`` copy, of its own class; after changing it call ``drawing.modify(entity)``.

        Example:
            polyline = drawing.entities(DxfPolyline)[0]
            polyline.net.Area                             # 2500.0 for the triangle (0, 0), (100, 0), (100, 50)
            entity = drawing.entities()[0]
            if isinstance(entity.net, DxfLine):           # narrowed: entity.net.Length is a float
                entity.net.Length                         # 100.0 for the line from (0, 0) to (100, 0)
        """
        return self._net

    def _renew(self, net: Any, source: Any) -> None:
        """Reads the object again: the copy an edit gave back for this object's handle, which the object keeps."""
        self._net = net
        self._source = source

    def _set(self, member: str, value: Any) -> None:
        if self._handle.startswith("~"):
            raise ValueError(
                f"The {self._type} {self._handle} is not in the drawing yet: call commit() first, or give {member} to add."
            )
        setattr(self._net, member, value)
        self._drawing._changed(self)  # pyright: ignore[reportPrivateUsage]

    @property
    def layer(self) -> str:
        """The name of the layer the object is on.

        Example:
            line.layer = "Steel"                          # then drawing.commit(); line.layer == "Steel"
        """
        return str(self._net.Layer)

    @layer.setter
    def layer(self, value: str) -> None:
        self._set("Layer", value)

    @property
    def color(self) -> int:
        """The colour index: 1 to 255, 256 for ByLayer, 0 for ByBlock.

        Example:
            line.color = 3                                # green; 256 is ByLayer, 0 ByBlock; then drawing.commit()
        """
        return int(self._net.ColorIndex)

    @color.setter
    def color(self, value: int) -> None:
        self._set("ColorIndex", int(value))

    @property
    def linetype(self) -> str:
        """The name of the linetype: ``"ByLayer"``, ``"ByBlock"`` or one of the drawing's.

        Example:
            line.linetype                                 # "ByLayer" for an object that has none of its own
        """
        return str(self._net.Linetype)

    @linetype.setter
    def linetype(self, value: str) -> None:
        self._set("Linetype", value)

    def __repr__(self) -> str:
        return f"<Entity {self._type} {self._handle} on {self.layer}>"


class Layer:
    """One layer, as a copy: ``name`` (read only) and the settable ``color`` (colour index 1 to 255), ``linetype``,
    ``description``, ``is_on``, ``is_frozen``, ``is_locked`` and ``is_plottable``, committed with the drawing::

        steel = drawing.layers["Steel"]
        steel.is_locked = True      # Steel is locked once commit() runs
        drawing.commit()

    Example:
        steel = drawing.layers["Steel"]
        steel.is_locked = True                        # Steel is locked once commit() runs
        drawing.commit()
    """

    def __init__(self, drawing: Drawing, look: Look, pending: bool = False) -> None:
        self._drawing = drawing
        self._look = look
        self._pending = pending
        self._dirty = False

    def _touch(self) -> None:
        if not self._pending and not self._dirty:
            self._dirty = True
            self._drawing._layer_changed(self)  # pyright: ignore[reportPrivateUsage]

    @property
    def name(self) -> str:
        """The name of the layer.

        Example:
            drawing.layers["steel"].name                  # "Steel": the spelling of the layer table
        """
        return self._look.name

    @property
    def color(self) -> int:
        """The colour index of the layer, 1 to 255 (a layer of a true colour reads the index nearest it).

        Example:
            drawing.layers["Steel"].color                 # 1 for a layer made with color=1
        """
        return self._look.color

    @color.setter
    def color(self, value: int) -> None:
        if isinstance(value, bool) or not isinstance(value, int) or not 1 <= value <= 255:  # pyright: ignore[reportUnnecessaryIsInstance]
            raise ValueError(f"The colour of layer {self.name} is an index from 1 to 255, not {value!r}.")
        self._look.color = value
        self._look.true_color = None
        self._touch()

    @property
    def linetype(self) -> str:
        """The name of the layer's linetype.

        Example:
            drawing.layers["Steel"].linetype              # "Continuous" for a layer made with the default
        """
        return self._look.linetype

    @linetype.setter
    def linetype(self, value: str) -> None:
        self._look.linetype = value
        self._touch()

    @property
    def description(self) -> str:
        """The description of the layer.

        Example:
            drawing.layers["Steel"].description           # '' until it is set: layer.description = "Structural steel"
        """
        return self._look.description

    @description.setter
    def description(self, value: str) -> None:
        self._look.description = value
        self._touch()

    @property
    def is_on(self) -> bool:
        """Whether the layer is on.

        Example:
            drawing.layers["Steel"].is_on                 # True; layer.is_on = False turns it off at commit()
        """
        return self._look.is_on

    @is_on.setter
    def is_on(self, value: bool) -> None:
        self._look.is_on = bool(value)
        self._touch()

    @property
    def is_frozen(self) -> bool:
        """Whether the layer is frozen.

        Example:
            drawing.layers["Steel"].is_frozen             # False; layer.is_frozen = True freezes it at commit()
        """
        return self._look.is_frozen

    @is_frozen.setter
    def is_frozen(self, value: bool) -> None:
        self._look.is_frozen = bool(value)
        self._touch()

    @property
    def is_locked(self) -> bool:
        """Whether the layer is locked.

        Example:
            drawing.layers["Steel"].is_locked             # False; layer.is_locked = True locks it at commit()
        """
        return self._look.is_locked

    @is_locked.setter
    def is_locked(self, value: bool) -> None:
        self._look.is_locked = bool(value)
        self._touch()

    @property
    def is_plottable(self) -> bool:
        """Whether the layer is plotted.

        Example:
            drawing.layers["Steel"].is_plottable          # True; layer.is_plottable = False keeps it off the plot
        """
        return self._look.is_plottable

    @is_plottable.setter
    def is_plottable(self, value: bool) -> None:
        self._look.is_plottable = bool(value)
        self._touch()

    def __repr__(self) -> str:
        return f"<Layer {self.name}>"


class Layers:
    """The layers of a drawing: iterable, ``len``, ``[name]`` (ignoring case, ``KeyError`` when none), ``in``, and ``add``.

    Each reading asks the drawing, so a layer added shows after ``commit()``; a ``Layer`` changed and not yet committed is
    kept by the drawing and returned as it is::

        "Steel" in drawing.layers    # False
        drawing.layers.add("Steel", color=1)
        drawing.commit()
        "steel" in drawing.layers    # True

    Example:
        len(drawing.layers)                           # 1 in a new drawing: layer 0
        "Steel" in drawing.layers                     # False until Steel is added and committed
        [layer.name for layer in drawing.layers]      # ['0'] in a new drawing, then ['0', 'Steel']
    """

    def __init__(self, drawing: Drawing) -> None:
        self._drawing = drawing
        self._known: dict[str, Layer] = {}

    def _read(self) -> list[Layer]:
        found: list[Layer] = []
        for look in self._drawing._backend.layers():  # pyright: ignore[reportPrivateUsage]
            key = look.name.upper()
            known = self._known.get(key)
            if known is None or not known._dirty:  # pyright: ignore[reportPrivateUsage]
                known = Layer(self._drawing, look)
                self._known[key] = known
            found.append(known)
        return found

    def __iter__(self) -> Iterator[Layer]:
        return iter(self._read())

    def __len__(self) -> int:
        return len(self._read())

    def __contains__(self, name: object) -> bool:
        return isinstance(name, str) and any(layer.name.upper() == name.upper() for layer in self._read())

    def __getitem__(self, name: str) -> Layer:
        for layer in self._read():
            if layer.name.upper() == name.upper():
                return layer
        raise KeyError(f"The drawing {self._drawing.name} has no layer {name!r}.")

    def add(self, name: str, color: int = 7, linetype: str = "Continuous", description: str = "") -> Layer:
        """Queue a new layer; it exists after ``commit()``, and a layer of that name already there refuses the commit.

        Example:
            steel = drawing.layers.add("Steel", color=1)  # queued: len(drawing.layers) is still 1
            drawing.commit()                              # now 2; a second add of 'steel' makes the commit refuse and raise
        """
        if not isinstance(name, str) or not name.strip():  # pyright: ignore[reportUnnecessaryIsInstance]
            raise ValueError(f"A layer needs a name, not {name!r}.")
        layer = Layer(self._drawing, Look(name.strip(), linetype=linetype, description=description), pending=True)
        layer.color = color
        self._drawing._queue(Op("add_layer", layer=layer))  # pyright: ignore[reportPrivateUsage]
        return layer


def _net_of(item: Any) -> Any:
    """The ``DxfEntity`` of an ``Entity``, or the entity itself where one of ``GeometryHelper.Dxf`` is given."""
    if isinstance(item, Entity):
        return cast("Entity[Any]", item).net
    return item


class _Records:
    """What the style, linetype and block tables of a drawing share: iterable, ``len``, ``in`` by name (ignoring case) and
    ``[index]`` or ``[name]``, each reading the drawing when it is used."""

    def __init__(self, drawing: Drawing) -> None:
        self._drawing = drawing

    def _read(self) -> list[Any]:
        raise NotImplementedError

    def _name_of(self, record: Any) -> str:
        return str(record.Name)

    def __iter__(self) -> Iterator[Any]:
        return iter(self._read())

    def __len__(self) -> int:
        return len(self._read())

    def __contains__(self, name: object) -> bool:
        return isinstance(name, str) and any(self._name_of(record).upper() == name.upper() for record in self._read())

    def __getitem__(self, key: int | str) -> Any:
        records = self._read()
        if isinstance(key, int):
            return records[key]
        for record in records:
            if self._name_of(record).upper() == key.upper():
                return record
        raise KeyError(f"The drawing {self._drawing.name} has no {self._word} {key!r}.")

    _word = "record"


class TextStyles(_Records):
    """The text styles of a drawing: iterable (copies of its ``DxfTextStyleTableRecord``, sorted by name ignoring case, then
    ordinal), ``len``, ``[index]`` or ``[name]`` (ignoring case, ``KeyError`` when none), ``in``, and ``add``. A copy changed
    changes nothing in the drawing; a style added shows after ``commit()``::

        drawing.text_styles.add("NOTE", font="romans.shx", x_scale=0.8)
        drawing.commit()
        "note" in drawing.text_styles    # True

    Example:
        [s.Name for s in drawing.text_styles]         # ['Standard'] in a new drawing
        "NOTE" in drawing.text_styles                 # False until NOTE is added and committed
        drawing.text_styles["Standard"].FileName      # 'txt'
    """

    _word = "text style"

    def _read(self) -> list[Any]:
        return self._drawing._backend.text_styles  # pyright: ignore[reportPrivateUsage]

    def add(self, name: str, font: str = "txt", x_scale: float = 1.0, height: float = 0.0) -> str:
        """Queue a new text style, as STYLE makes one: ``font`` is the file of a font (``"romans.shx"``, ``"arial.ttf"``;
        ``"txt"`` is the font of Standard), ``x_scale`` how wide its letters are for their height, and ``height`` the fixed
        height of its texts (0 where each text gives its own). It exists after ``commit()``, and a style of that name already
        there refuses the commit. Returns the name, to give ``add_text(..., text_style=)`` and ``dimension_styles.add``.

        Example:
            drawing.text_styles.add("NOTE", font="romans.shx", x_scale=0.8)   # 'NOTE', queued
            drawing.commit()                                                  # now 2 styles: Standard and NOTE
            drawing.add_text("Plate", (0, 0), 5.0, text_style="NOTE")         # a TEXT in NOTE, in the next commit
        """
        label = _record_name(name, "text style")
        if not isinstance(font, str) or not font.strip():  # pyright: ignore[reportUnnecessaryIsInstance]
            raise ValueError(f"The font of text style {label} is a font file such as 'romans.shx', not {font!r}.")
        positive("The x_scale of a text style", x_scale)
        finite("The height of a text style", height)
        if height < 0.0:
            raise ValueError(f"The height of a text style is nought or above, not {height!r}.")
        spec = TextStyleSpec(label, font.strip(), float(x_scale), float(height))
        self._drawing._queue(Op("text_style", spec=spec))  # pyright: ignore[reportPrivateUsage]
        return label


class DimensionStyles(_Records):
    """The dimension styles of a drawing: iterable (copies of its ``DxfDimStyleTableRecord``, sorted as ``text_styles`` are),
    ``len``, ``[index]`` or ``[name]``, ``in``, and ``add``::

        drawing.dimension_styles.add("PLAN", text_style="NOTE", scale=50, text_height=2.5, arrow_size=2.5)
        drawing.commit()

    Example:
        [s.Name for s in drawing.dimension_styles]    # ['ISO-25', 'Standard'] in a new drawing
        drawing.dimension_styles["iso-25"].Dimtxt     # 2.5
    """

    _word = "dimension style"

    def _read(self) -> list[Any]:
        return self._drawing._backend.dimension_styles  # pyright: ignore[reportPrivateUsage]

    def add(
        self,
        name: str,
        text_style: str | None = None,
        scale: float = 1.0,
        text_height: float = 2.5,
        arrow_size: float = 2.5,
        **variables: Any,
    ) -> str:
        """Queue a new dimension style, as the Dimension Style Manager's New makes one from ISO-25: ``text_style`` the name of a
        text style of the drawing (or one added in the same commit; None keeps ISO-25's), ``scale`` the overall scale
        (DIMSCALE), ``text_height`` the height of the words (DIMTXT) and ``arrow_size`` the size of the arrowheads (DIMASZ).
        ``variables`` set any other variable of the style by the name the library gives it (``Dimdec=2``, ``Dimtad=1``,
        ``Dimexe=1.5``; ``ValueError`` for a name that is none). It exists after ``commit()``, and a style of that name already
        there refuses the commit. Returns the name, to give ``add_linear_dim(..., dim_style=)`` and ``current_dim_style``.

        Example:
            drawing.dimension_styles.add("PLAN", text_style="NOTE", scale=50, text_height=2.5, Dimdec=1)   # 'PLAN', queued
            drawing.commit()                                  # PLAN has Dimscale 50.0, Dimtxt 2.5 and Dimdec 1
        """
        label = _record_name(name, "dimension style")
        if text_style is not None and not isinstance(text_style, str):  # pyright: ignore[reportUnnecessaryIsInstance]
            raise ValueError(f"The text style of {label} is the name of one, not {text_style!r}.")
        positive("The scale of a dimension style", scale)
        positive("The text height of a dimension style", text_height)
        positive("The arrow size of a dimension style", arrow_size)
        known = set(str(known) for known in _cad.CadDimStyleEditor.VariableNames)
        for key in variables:
            if key not in known:
                raise ValueError(f"{key!r} is no dimension variable: give a name such as Dimdec, Dimtad or Dimexe.")
        spec = DimStyleSpec(
            label,
            None if text_style is None else text_style.strip(),
            float(scale),
            float(text_height),
            float(arrow_size),
            dict(variables),
        )
        self._drawing._queue(Op("dim_style", spec=spec))  # pyright: ignore[reportPrivateUsage]
        return label


class Linetypes(_Records):
    """The linetypes of a drawing, by name, in the order of its table: iterable, ``len``, ``in`` (ignoring case), ``[index]``, and
    ``load`` to bring one in from the built-in set or a .lin file. ``Continuous``, ``ByBlock`` and ``ByLayer`` are in every drawing::

        drawing.linetypes.load("DASHED")
        drawing.layers.add("Hidden", linetype="DASHED")      # the same commit: the linetype is made first
        drawing.commit()

    Example:
        list(drawing.linetypes)                       # ['ByBlock', 'ByLayer', 'Continuous'] in a new drawing
        "dashed" in drawing.linetypes                 # False until DASHED is loaded and committed
    """

    _word = "linetype"

    def _read(self) -> list[Any]:
        return [str(name) for name in self._drawing._backend.names("LTYPE")]  # pyright: ignore[reportPrivateUsage]

    def _name_of(self, record: Any) -> str:
        return str(record)

    def load(self, name: str, file: str | None = None) -> str:
        """Queue the loading of a linetype, as LINETYPE Load does: ``name`` as the built-in set (``OpenCAD2D.lin``: BORDER,
        CENTER, DASHDOT, DASHED, DIVIDE, DOT, HIDDEN and PHANTOM, each with its 2 and X2, GAS_LINE and the ISO ones) or the .lin
        file ``file`` spells it, ignoring case; the lengths are those of the drawing's units (MEASUREMENT). ``ValueError`` where
        the set or the file has none of that name. A linetype the drawing has already is left as it is, as loading leaves it. It
        is there after ``commit()``, before the layers and objects of that commit, which can use it. Returns the name as the
        set or file spells it.

        Example:
            drawing.linetypes.load("hidden")              # 'HIDDEN', queued; after commit() a layer can have linetype="HIDDEN"
            drawing.linetypes.load("MINE", file="mine.lin")   # from a .lin file of your own
        """
        if not isinstance(name, str) or not name.strip():  # pyright: ignore[reportUnnecessaryIsInstance]
            raise ValueError(f"A linetype needs a name, not {name!r}.")
        if file is None:
            found = [str(d.Name) for d in _cad.CadLinetypeFile.BuiltIn if str(d.Name).upper() == name.strip().upper()]
            if not found:
                raise ValueError(f"The linetype {name!r} is not in the built-in set {_cad.CadLinetypeFile.BuiltInName}.")
            spelled, path = found[0], None
        else:
            path = os.path.abspath(file)
            found = [
                str(d.Name) for d in _cad.CadLinetypeFile.Read(path) if str(d.Name).upper() == name.strip().upper()
            ]
            if not found:
                raise ValueError(f"The linetype {name!r} is not in the file {path}.")
            spelled = found[0]
        self._drawing._queue(Op("linetype", spec=LinetypeSpec(spelled, path)))  # pyright: ignore[reportPrivateUsage]
        return spelled


class Blocks(_Records):
    """The blocks of a drawing: iterable (copies of its ``DxfBlockTableRecord`` with their objects, sorted as ``text_styles``
    are; the blocks of the layouts and the anonymous ones left out), ``len``, ``[index]`` or ``[name]``, ``in``, and ``add``.
    An external reference attached and loaded brings the blocks of its drawing, as AutoCAD lists them: ``grid|TAG`` for the
    block ``TAG`` of ``grid``, its ``IsDependent`` true. They are not inserted or changed::

        drawing.blocks.add("BOLT", [DxfLine(GeoPoint3(0, 0, 0), GeoPoint3(10, 0, 0))])
        drawing.add_insert("BOLT", (100, 50))
        drawing.commit()

    Example:
        [b.Name for b in drawing.blocks]              # [] in a new drawing: no block of its own yet
    """

    _word = "block"

    def _read(self) -> list[Any]:
        return self._drawing._backend.blocks  # pyright: ignore[reportPrivateUsage]

    def add(self, name: str, entities: Sequence[Entity[Any] | Any], base_point: Any = (0, 0)) -> str:
        """Queue a new block made of copies of ``entities`` (``Entity`` objects or ``GeometryHelper.Dxf`` entities, which may
        be ones just added and not committed: the block takes a copy, and they stay where they are), its base point
        ``base_point``, as BLOCK makes one. It exists after ``commit()``, before the objects of that commit, so that
        ``add_insert`` can place it in the same one; a block of that name already there refuses the commit. Returns the name.

        Example:
            drawing.blocks.add("BOLT", [bolt_circle, bolt_line], base_point=(0, 0))  # 'BOLT': two copies, queued
            drawing.commit()                              # [b.Name for b in drawing.blocks] is now ['BOLT']
        """
        label = _record_name(name, "block")
        copies: list[Any] = []
        for item in entities:
            copies.append(_net_of(item).Clone())
        if not copies:
            raise ValueError(f"The block {label} needs at least one object.")
        spec = BlockSpec(label, copies, point3(base_point))
        self._drawing._queue(Op("block", spec=spec))  # pyright: ignore[reportPrivateUsage]
        return label


def _record_name(name: object, what: str) -> str:
    """The name of a table record, without the spaces at its ends; ``ValueError`` for none, or one the tables do not take (a
    name that holds one of < > / \\ " : ; ? * | , = `)."""
    if not isinstance(name, str) or not name.strip():
        raise ValueError(f"A {what} needs a name, not {name!r}.")
    label = name.strip()
    bad = [c for c in '<>/\\":;?*|,=`' if c in label]
    if bad:
        raise ValueError(f"The name of a {what} holds {bad[0]!r}, which a name of the drawing does not.")
    return label


class Drawing:
    """One drawing: read it, queue changes, and ``commit()`` them as one undo step.

    The same members work on a drawing of the app (``active_drawing()``), of a running app (``connect()``) and of a file
    (``open_file``, ``new_file``). ``net`` is the ``CadDocument``, or the ``CadRemoteDocument`` outside the app. A point is a
    ``GeoPoint2``, a ``GeoPoint3`` or a tuple of 2 or 3 numbers; angles are in radians; units are the drawing's::

        drawing.add_line((0, 0), (100, 0))
        drawing.add_circle((50, 50), 25)
        handles = drawing.commit()       # two handles, one undo step

    Example:
        drawing = new_file()
        drawing.add_line((0, 0), (100, 0))
        drawing.add_circle((50, 50), 25)
        drawing.commit()                              # ['101', '102']: two handles, one undo step
    """

    def __init__(self, backend: Backend, on_queue: Callable[[Drawing], None] | None = None) -> None:
        self._backend = backend
        self._on_queue = on_queue
        self._ops: list[Op] = []
        self._changed_entities: list[Entity] = []
        self._changed_layers: list[Layer] = []
        self._tickets = 0
        self._noted = False
        self._layers = Layers(self)
        self._text_styles = TextStyles(self)
        self._dimension_styles = DimensionStyles(self)
        self._linetypes = Linetypes(self)
        self._blocks = Blocks(self)

    # ---- the queue ------------------------------------------------------------------------------------------------

    def _note(self) -> None:
        if self._on_queue is not None and not self._noted:
            self._noted = True
            self._on_queue(self)

    def _queue(self, op: Op) -> None:
        self._ops.append(op)
        self._note()

    def _changed(self, entity: Entity) -> None:
        if not any(entity is known for known in self._changed_entities):
            self._changed_entities.append(entity)
        self._note()

    def _layer_changed(self, layer: Layer) -> None:
        self._changed_layers.append(layer)
        self._note()

    def _discard(self) -> None:
        """Drops what is queued; a changed layer or entity keeps the value it was given."""
        for layer in self._changed_layers:
            layer._dirty = False  # pyright: ignore[reportPrivateUsage]
        self._ops.clear()
        self._changed_entities.clear()
        self._changed_layers.clear()
        self._layers._known.clear()  # pyright: ignore[reportPrivateUsage]
        self._noted = False

    # ---- what the drawing is --------------------------------------------------------------------------------------

    @property
    def name(self) -> str:
        """The name of the drawing, as its tab shows it: ``plan.dxf``.

        Example:
            open_file(r"C:\\plans\\plan.dxf").name          # 'plan.dxf'; new_file().name is 'Drawing'
        """
        return self._backend.name

    @property
    def path(self) -> str | None:
        """The full path of the file, or None for a drawing never saved.

        Example:
            new_file().path                               # None: never saved
            open_file(r"C:\\plans\\plan.dxf").path          # 'C:\\\\plans\\\\plan.dxf'
        """
        return self._backend.path

    @property
    def is_active(self) -> bool:
        """Whether this is the drawing shown (always true for a drawing of a file).

        Example:
            new_file().is_active                          # True: a drawing of a file is always the one shown
        """
        return self._backend.is_active

    @property
    def is_modified(self) -> bool:
        """Whether the drawing differs from the file it was opened from or last saved as.

        Example:
            drawing = new_file()
            drawing.add_line((0, 0), (100, 0))
            drawing.commit()
            drawing.is_modified                           # True; False again after drawing.save_as(path)
        """
        return self._backend.is_modified

    @property
    def net(self) -> Any:
        """The ``CadDocument`` of the drawing, or the ``CadRemoteDocument`` outside the app.

        Example:
            new_file().net.EntityCount                    # 0: the CadDocument; a CadRemoteDocument from connect()
        """
        return self._backend.net

    # ---- reading --------------------------------------------------------------------------------------------------

    @overload
    def entities(
        self, type: builtins.type[K], layer: str | None = None, layout: str | None = None
    ) -> list[Entity[K]]: ...
    @overload
    def entities(
        self, type: str | None = None, layer: str | None = None, layout: str | None = None
    ) -> list[Entity]: ...
    def entities(
        self, type: builtins.type[Any] | str | None = None, layer: str | None = None, layout: str | None = None
    ) -> list[Entity[Any]]:
        """The objects of the drawing, as copies in the order the drawing lists them, of one DXF ``type`` (``"line"``, ignoring
        case, or a class of ``GeometryHelper.Dxf``), one ``layer`` and one ``layout`` where given. A class asks for its
        subclasses too (``DxfCurve`` is lines, arcs, circles, polylines...) and gives ``Entity[DxfCircle]`` for a type checker,
        whose ``net`` needs no narrowing; a class that is no ``DxfEntity`` raises ``TypeError``.

        Example:
            drawing.entities()                            # six objects: LINE, CIRCLE, ARC, LWPOLYLINE, TEXT, MTEXT
            drawing.entities(type="line", layer="steel")  # the one LINE on layer Steel: layer names ignore case
            drawing.entities(DxfCircle)[0].net.Radius     # 25.0: the circle, typed, with no isinstance
            len(drawing.entities(DxfCurve))               # 4: the line, the circle, the arc and the polyline
        """
        if type is None or isinstance(type, str):
            found = self._backend.read(None if type is None else type.upper(), layer, layout)
            entities = [Entity(self, handle, net, source) for handle, net, source in found]
            if type is None:
                return entities
            return [entity for entity in entities if entity.dxf_type.upper() == type.upper()]
        if not isinstance(type, builtins.type) or not issubclass(type, _dxf.DxfEntity):  # pyright: ignore[reportUnnecessaryIsInstance]
            raise TypeError(f"entities takes the name of a DXF type or a class of DxfEntity, not {type!r}.")
        found = self._backend.read(None, layer, layout, type)
        return [Entity(self, handle, net, source) for handle, net, source in found]

    def entity(self, handle: str) -> Entity:
        """The object of a handle; ``KeyError`` when the drawing has none: ``drawing.entity("2F")``.

        Example:
            drawing.entity("101").dxf_type                # "LINE"
            drawing.entity("FFFF")                        # KeyError: The drawing Drawing has no object 'FFFF'.
        """
        found = None if handle.startswith("~") else self._backend.read_one(handle)
        if found is None:
            raise KeyError(f"The drawing {self.name} has no object {handle!r}.")
        return Entity(self, handle, found[0], found[1])

    def selection(self) -> list[Entity]:
        """The objects selected now, in the order they were made.

        Example:
            drawing.select(["101"])
            [e.handle for e in drawing.selection()]       # ['101']
        """
        return [Entity(self, handle, net, source) for handle, net, source in self._backend.selected()]

    def select(self, items: Sequence[Entity | str]) -> None:
        """Select these objects (entities or handles) and nothing else: ``drawing.select([line, "2F"])``.

        Example:
            drawing.select([drawing.entities()[0]])       # the first object, as a user would select it
            len(drawing.selection())                      # 1
        """
        self._backend.select(self._handles_of(items))

    @staticmethod
    def _handles_of(items: Sequence[Entity | str]) -> list[str]:
        """The handles of entities or handles; ``ValueError`` for one not committed yet."""
        handles: list[str] = []
        for item in items:
            handle = item.handle if isinstance(item, Entity) else str(item)
            if handle.startswith("~"):
                raise ValueError(f"The object {handle} is not in the drawing yet: call commit() first.")
            handles.append(handle)
        return handles

    # ---- the view, the selection by area, the variables -----------------------------------------------------------

    def zoom_extents(self) -> None:
        """Zoom to the extents, as ZOOM Extents does: what the space shown draws, fitted with a margin of 5 %. One undo step,
        ZOOM, and ``zoom_previous()`` goes back to the view before. Like every call that moves the view it works on the
        drawing the app shows (``CadRemoteException`` otherwise), and ``RuntimeError`` on a drawing of a file, which has none.

        Example:
            drawing.zoom_extents()                        # a line (0, 0)-(100, 0) and a circle about (50, 50) of radius 25 fill
            drawing.view                                  # Box(min_x=-23.50187, min_y=-4.16667, max_x=123.50187, max_y=79.16667)
        """
        self._backend.zoom_extents()

    def zoom_window(self, first: Any, second: Any) -> None:
        """Zoom to a window, as ZOOM Window does: the two corners (points of any kind, in either order) fitted with no margin.

        Example:
            drawing.zoom_window((0, 0), (10, 10))         # in a 942 by 534 pixel view: view is (-3.82022, 0)-(13.82022, 10)
        """
        self._backend.zoom_window(point2(first), point2(second))

    def zoom_objects(self, items: Sequence[Entity | str]) -> None:
        """Zoom to objects, as ZOOM Object does: the box of the ones drawn in the space shown, fitted with a margin of 5 %;
        none drawn there leaves the view as it was. ``items`` are entities or handles, committed ones.

        Example:
            circle = drawing.entities("CIRCLE")[0]        # a circle of radius 25 about (50, 50)
            drawing.zoom_objects([circle])                # the view is 55.556 high about (50, 50)
        """
        self._backend.zoom_objects(self._handles_of(items))

    def zoom_center(self, center: Any, height: float) -> None:
        """Zoom to a height about a centre, as ZOOM Center does: the view shows ``height`` and as much width as the canvas has.

        Example:
            drawing.zoom_center((50, 50), 20)             # in a 942 by 534 pixel view: view is (32.35955, 40)-(67.64045, 60)
        """
        self._backend.zoom_center(point2(center), float(height))

    def zoom_scaled(self, factor: float) -> None:
        """Scale the view about its centre, as ZOOM nX does: 2 shows half the width and half the height, 0.5 twice as much.

        Example:
            drawing.zoom_scaled(2)                        # a view 40 wide shows 20 wide about the same centre
        """
        self._backend.zoom_scaled(float(factor))

    def zoom_previous(self) -> bool:
        """Go back to the view before the last zoom or pan, as ZOOM Previous does; False, and nothing written, where none is
        saved.

        Example:
            drawing.zoom_extents()
            drawing.zoom_previous()                       # True: the view before zoom_extents(); False once none is left
        """
        return self._backend.zoom_previous()

    def pan(self, displacement: Any) -> None:
        """Pan so that the drawing moves by the displacement, as -PAN does: ``(10, 0)`` moves what is drawn 10 to the right, so
        the view shows 10 less at the left. A ``GeoVector2`` or a tuple of 2 numbers; one of 3 raises ``ValueError``.

        Example:
            drawing.zoom_window((0, 0), (10, 10))         # view (-3.82022, 0)-(13.82022, 10)
            drawing.pan((10, 0))                          # view (-13.82022, 0)-(3.82022, 10)
        """
        self._backend.pan(vector2(displacement))

    def regen(self) -> None:
        """Regenerate the drawing, as REGEN does without its line on the command line: the fields are evaluated again and the
        temporary graphics cleared. It is no undo step and works on a drawing of a file too.

        Example:
            drawing.regen()                               # nothing the script reads changes; a field now shows its new value
        """
        self._backend.regen()

    @property
    def view(self) -> Box:
        """The part of the space shown that the canvas shows, as a ``Box``: the whole canvas, so wider or taller than the box
        last given where the shape differs. Setting it fits a box with no margin, as ``zoom_window`` does.

        Example:
            drawing.view = Box(0.0, 0.0, 10.0, 10.0)     # one undo step, ZOOM
            drawing.view                                  # Box(min_x=-3.82022, min_y=0.0, max_x=13.82022, max_y=10.0)
        """
        return self._backend.view

    @view.setter
    def view(self, box: Box) -> None:
        self._backend.view = Box(*box)

    @property
    def has_window(self) -> bool:
        """Whether the app shows the drawing in a window. False for a drawing of a file and where the app has no window (a test
        of the app): the view calls then work on a view of 942 by 534 pixels showing 10 units of height about the origin.

        Example:
            new_file().has_window                         # False: a drawing of a file has no window
        """
        return self._backend.has_window

    @property
    def named_views(self) -> list[str]:
        """The names of the named views, as -VIEW ? lists them: in ordinal order, letters of either case alike.

        Example:
            drawing.save_view("Corner")
            drawing.named_views                           # ['Corner']
        """
        return self._backend.named_views

    def save_view(self, name: str) -> None:
        """Save the view on screen under a name, as -VIEW Save does; a name that is a view's already redefines it. One undo
        step, VIEW. A name is not empty, 255 letters at most, and has no space at either end.

        Example:
            drawing.zoom_window((0, 0), (40, 30))
            drawing.save_view("Corner")                   # drawing.named_views is now ['Corner']
        """
        self._backend.save_view(name)

    def restore_view(self, name: str) -> None:
        """Show a named view, as -VIEW Restore does; the name ignores case, and one the drawing lacks is refused.

        Example:
            drawing.restore_view("corner")                # the view saved as Corner is shown again
        """
        self._backend.restore_view(name)

    def delete_view(self, name: str) -> None:
        """Erase a named view, as -VIEW Delete does; one the drawing lacks is refused.

        Example:
            drawing.delete_view("Corner")                 # drawing.named_views no longer lists it
        """
        self._backend.delete_view(name)

    def select_all(self) -> list[Entity]:
        """Take every object of the space the drawing works in, as the All option of "Select objects:" does, as copies in the
        order the space lists them. The objects ``select`` selected stay selected; narrow by layer or type with ``entities``.

        Example:
            [e.dxf_type for e in drawing.select_all()]   # ['LINE', 'CIRCLE'] for a line and a circle
        """
        return [Entity(self, handle, net, source) for handle, net, source in self._backend.select_all()]

    def select_window(self, first: Any, second: Any) -> list[Entity]:
        """Take the objects that lie wholly inside a window, as the Window option of "Select objects:" does: where the app has
        a window, only those its view shows. The objects ``select`` selected stay selected.

        Example:
            drawing.select_window((-1, 5), (101, 35))     # the lines at y 10, 20 and 30 of five lines at y 0, 10, 20, 30, 40
        """
        return [
            Entity(self, handle, net, source)
            for handle, net, source in self._backend.select_window(point2(first), point2(second))
        ]

    def select_crossing(self, first: Any, second: Any) -> list[Entity]:
        """Take the objects a window lies across or contains, as the Crossing option of "Select objects:" does, held to the
        view as ``select_window`` is.

        Example:
            drawing.select_crossing((50, 15), (60, 25))   # the line at y 20 of five lines at y 0, 10, 20, 30, 40
        """
        return [
            Entity(self, handle, net, source)
            for handle, net, source in self._backend.select_crossing(point2(first), point2(second))
        ]

    def get_variable(self, name: str) -> int | float | str | tuple[float, ...]:
        """The value of a system variable of the drawing as AutoLISP's ``getvar`` gives it: an ``int`` for a whole number or a
        switch, a ``float`` (an angle in radians), a ``str``, a tuple of 2 or 3 numbers for a point. ``name`` ignores case.

        Example:
            drawing.get_variable("LUNITS")                # 2 in a new drawing
            drawing.get_variable("INSBASE")               # (0.0, 0.0, 0.0)
        """
        value: Any = self._backend.get_variable(name)
        if isinstance(value, (int, float, str)):
            return value
        return tuple(float(number) for number in value)

    def set_variable(self, name: str, value: int | float | str | Sequence[float]) -> None:
        """Set a system variable as AutoLISP's ``setvar`` does; one the drawing keeps is one undo step, SETVAR. A name the
        app lacks, a read-only variable and a value it refuses are refused in the app's words; a ``bool`` is a ``TypeError``.

        Example:
            drawing.set_variable("LUNITS", 4)             # then drawing.get_variable("LUNITS") is 4
            drawing.set_variable("INSBASE", (1.5, 2.5, 0.0))
        """
        self._backend.set_variable(name, variable_value(name, value))

    # ---- adding ---------------------------------------------------------------------------------------------------

    def _made(self, entity: K, props: dict[str, Any]) -> K:
        for key, value in props.items():
            if key == "layer":
                entity.Layer = str(value)
            elif key == "color":
                entity.ColorIndex = int(value)
            elif key == "linetype":
                entity.Linetype = str(value)
            else:
                raise TypeError(f"{key!r} is not a property an object is made with: give layer, color or linetype.")
        return entity

    def add(self, dxf_entity: K, layout: str | None = None) -> Entity[K]:
        """Queue a copy of any ``GeometryHelper.Dxf`` entity, in model space or in ``layout``; the ``Entity`` returned has
        the handle ``~1``, ``~2``... until ``commit()``. The entity given is not kept: a later change to it adds nothing.

        Example:
            from GeometryHelper.Dxf import DxfCircle
            added = drawing.add(DxfCircle(GeoPoint3(0, 0, 0), GeoVector3(0, 0, 1), 5))
            added.handle                                  # '~1' until drawing.commit() returns ['101']
        """
        self._tickets += 1
        clone: Any = dxf_entity.Clone()
        entity: Entity[K] = Entity(self, f"~{self._tickets}", clone)
        self._queue(Op("insert", entity=entity, copy=dxf_entity.Clone(), layout=layout))
        return entity

    def add_line(self, start: Any, end: Any, **props: Any) -> Entity[DxfLine]:
        """Queue a line: ``drawing.add_line((0, 0), (100.5, 0), layer="Steel")``. ``props``: ``layer``, ``color``, ``linetype``.

        Example:
            drawing.add_line((0, 0), (100, 0), layer="Steel")   # a line of length 100 on layer Steel (made first if need be)
        """
        return self.add(self._made(_dxf.DxfLine(point3(start), point3(end)), props))

    def add_circle(self, center: Any, radius: float, **props: Any) -> Entity[DxfCircle]:
        """Queue a circle: ``drawing.add_circle((50, 50), 25)`` is a circle of radius 25 about (50, 50).

        Example:
            drawing.add_circle((50, 50), 25)              # a circle of radius 25 about (50, 50): net.Radius == 25.0
        """
        circle = _dxf.DxfCircle()
        circle.Center = point3(center)
        circle.Radius = float(radius)
        return self.add(self._made(circle, props))

    def add_arc(self, center: Any, radius: float, start_angle: float, end_angle: float, **props: Any) -> Entity[DxfArc]:
        """Queue an arc, counter-clockwise from ``start_angle`` to ``end_angle`` in **radians**:
        ``drawing.add_arc((0, 0), 10, 0.0, math.pi / 2)`` is the quarter of a circle of radius 10 in the first quadrant.

        Example:
            drawing.add_arc((0, 0), 10, 0.0, math.pi / 2)   # a quarter of a circle of radius 10, 15.708 long
        """
        arc = _dxf.DxfArc(point3(center), float(radius), float(start_angle), float(end_angle))
        return self.add(self._made(arc, props))

    def add_polyline(self, points: Sequence[Any], closed: bool = False, **props: Any) -> Entity[DxfPolyline]:
        """Queue a polyline through the points (x and y; the z of the first is its elevation), closed where asked:
        ``drawing.add_polyline([(0, 0), (100, 0), (100, 50)], closed=True)`` is a triangle of 2500 square units.

        Example:
            drawing.add_polyline([(0, 0), (100, 0), (100, 50)], closed=True)   # net.Area == 2500.0
        """
        corners = [point3(point) for point in points]
        if not corners:
            raise ValueError("A polyline needs at least one point.")
        polyline = _dxf.DxfPolyline()
        for index, corner in enumerate(corners):
            polyline.AddVertexAt(index, _geo.GeoPoint2(corner.X, corner.Y), 0.0, 0.0, 0.0)
        polyline.Elevation = float(corners[0].Z)
        polyline.Closed = bool(closed)
        return self.add(self._made(polyline, props))

    def add_text(
        self,
        text: str,
        insert: Any,
        height: float,
        rotation: float = 0.0,
        text_style: str | None = None,
        **props: Any,
    ) -> Entity[DxfDBText]:
        """Queue a single-line text at ``insert``, ``height`` high, turned ``rotation`` radians, in the text style
        ``text_style`` (a style of the drawing, or one added with ``text_styles.add`` in the same commit; None is Standard):
        ``drawing.add_text("Plate", (0, 100), 5.0)``.

        Example:
            drawing.add_text("Plate", (0, 100), 5.0)      # a TEXT 5 high at (0, 100), in Standard
            drawing.add_text("Plate", (0, 100), 5.0, text_style="NOTE")   # in NOTE, which the drawing has or a commit adds
        """
        made = _dxf.DxfDBText()
        made.TextString = str(text)
        made.Position = point3(insert)
        made.Height = float(height)
        made.Rotation = float(rotation)
        if text_style is not None:
            made.TextStyleName = str(text_style)
        return self.add(self._made(made, props))

    def add_mtext(
        self, text: str, insert: Any, height: float, width: float = 0.0, text_style: str | None = None, **props: Any
    ) -> Entity[DxfMText]:
        """Queue a multi-line text at ``insert``, ``height`` high, wrapped at ``width`` (0 for no wrapping), in the text style
        ``text_style`` as ``add_text`` takes it: ``drawing.add_mtext("Plate PL10", (10, 20), 5.0)``.

        Example:
            drawing.add_mtext("Plate PL10", (10, 20), 5.0, width=60)   # an MTEXT 5 high, wrapped at 60
            drawing.add_mtext("Plate PL10", (10, 20), 5.0, text_style="NOTE")   # in the text style NOTE
        """
        made = _dxf.DxfMText()
        made.Contents = str(text)
        made.Location = point3(insert)
        made.TextHeight = float(height)
        if width:
            made.Width = float(width)
        if text_style is not None:
            made.TextStyleName = str(text_style)
        return self.add(self._made(made, props))

    # ---- dimensions, hatches, inserts -----------------------------------------------------------------------------

    def _dimension(self, made: K, dim_style: str | None, props: dict[str, Any]) -> K:
        """A dimension made in code, put in the style ``dim_style`` where one is given and else the drawing's current one, as
        DIM does, then added as ``add`` adds an object: the app draws its picture (the ``*D`` block) as DIM would, in the style it
        names, once ``commit()`` adds it."""
        made.DimensionStyleName = self.current_dim_style if dim_style is None else str(dim_style)  # pyright: ignore[reportAttributeAccessIssue]
        return self._made(made, props)

    def add_linear_dim(
        self,
        first: Any,
        second: Any,
        location: Any,
        angle: float | None = None,
        text: str | None = None,
        dim_style: str | None = None,
        **props: Any,
    ) -> Entity[DxfRotatedDimension]:
        """Queue a linear dimension of the distance between two points, as DIMLINEAR makes one: its dimension line runs
        through ``location`` along ``angle`` (radians; None takes level where ``location`` lies above or below the two points
        and upright where it lies to their left or right, as DIMLINEAR does from where the line is put). ``text`` is the words
        in place of the measurement (``"<>"`` stands for the measurement), ``dim_style`` the style it is drawn in (the drawing's
        current one where None). The picture is drawn when ``commit()`` adds it, as DIM draws it.

        Example:
            drawing.add_linear_dim((0, 0), (100, 0), (50, 20))        # a level dimension of 100 with its line at y 20
            drawing.add_linear_dim((0, 0), (100, 0), (50, 20), text="<> TYP", dim_style="PLAN")   # its words read "100 TYP"
        """
        one, two, put = point2(first), point2(second), point2(location)
        finite("A point of a dimension", one.X, one.Y, two.X, two.Y, put.X, put.Y)
        if angle is None:
            out_x = max(min(one.X, two.X) - put.X, put.X - max(one.X, two.X))
            out_y = max(min(one.Y, two.Y) - put.Y, put.Y - max(one.Y, two.Y))
            angle = math.pi / 2.0 if out_x > out_y else 0.0
        finite("The angle of a dimension", angle)
        along = (math.cos(angle), math.sin(angle))
        if abs((two.X - one.X) * along[0] + (two.Y - one.Y) * along[1]) <= 1e-9:
            raise ValueError("The two points of a linear dimension are no distance apart along its direction.")
        foot = (two.X - put.X) * along[0] + (two.Y - put.Y) * along[1]
        line = _geo.GeoPoint3(put.X + along[0] * foot, put.Y + along[1] * foot, 0.0)
        made = _dxf.DxfRotatedDimension(
            float(angle), point3(first), point3(second), line, "" if text is None else str(text), None
        )
        return self.add(self._dimension(made, dim_style, props))

    def add_aligned_dim(
        self, first: Any, second: Any, location: Any, text: str | None = None, dim_style: str | None = None, **props: Any
    ) -> Entity[DxfAlignedDimension]:
        """Queue an aligned dimension of the distance between two points, as DIMALIGNED makes one: its dimension line is
        parallel to the two points and runs through ``location``. ``text`` and ``dim_style`` are as ``add_linear_dim`` takes
        them.

        Example:
            drawing.add_aligned_dim((0, 0), (30, 40), (35, 5))        # an aligned dimension of 50, its line through (35, 5)
        """
        one, two, put = point2(first), point2(second), point2(location)
        finite("A point of a dimension", one.X, one.Y, two.X, two.Y, put.X, put.Y)
        length = math.hypot(two.X - one.X, two.Y - one.Y)
        if length <= 1e-9:
            raise ValueError("The two points of an aligned dimension are the same point.")
        along = ((two.X - one.X) / length, (two.Y - one.Y) / length)
        foot = (two.X - put.X) * along[0] + (two.Y - put.Y) * along[1]
        line = _geo.GeoPoint3(put.X + along[0] * foot, put.Y + along[1] * foot, 0.0)
        made = _dxf.DxfAlignedDimension(point3(first), point3(second), line, "" if text is None else str(text), None)
        return self.add(self._dimension(made, dim_style, props))

    def _rim(self, center: Any, radius: float, location: Any, what: str) -> tuple[Any, Any, Any]:
        """The centre, the point of the rim the line of a radius or diameter dimension meets and the point opposite it: the line
        runs from the centre toward ``location`` (to the right where ``location`` is the centre)."""
        middle, put = point2(center), point2(location)
        finite(f"A point of a {what} dimension", middle.X, middle.Y, put.X, put.Y)
        positive(f"The radius of a {what} dimension", radius)
        toward = (put.X - middle.X, put.Y - middle.Y)
        length = math.hypot(toward[0], toward[1])
        unit = (1.0, 0.0) if length <= 1e-9 else (toward[0] / length, toward[1] / length)
        near = _geo.GeoPoint3(middle.X + unit[0] * radius, middle.Y + unit[1] * radius, 0.0)
        far = _geo.GeoPoint3(middle.X - unit[0] * radius, middle.Y - unit[1] * radius, 0.0)
        return _geo.GeoPoint3(middle.X, middle.Y, 0.0), near, far

    def add_radial_dim(
        self,
        center: Any,
        radius: float,
        location: Any,
        text: str | None = None,
        dim_style: str | None = None,
        **props: Any,
    ) -> Entity[DxfRadialDimension]:
        """Queue a radius dimension of the circle or arc about ``center`` of ``radius``, as DIMRADIUS makes one: its line runs
        from the centre toward ``location``, which also sets where its words stand. The words read ``R`` and the radius unless
        ``text`` gives others. ``dim_style`` is as ``add_linear_dim`` takes it.

        Example:
            drawing.add_radial_dim((50, 50), 25, (80, 70))            # a radius dimension reading R25 toward (80, 70)
        """
        middle, near, _ = self._rim(center, radius, location, "radius")
        made = _dxf.DxfRadialDimension(middle, near, 0.0, "" if text is None else str(text), None)
        return self.add(self._dimension(made, dim_style, props))

    def add_diameter_dim(
        self,
        center: Any,
        radius: float,
        location: Any,
        text: str | None = None,
        dim_style: str | None = None,
        **props: Any,
    ) -> Entity[DxfDiametricDimension]:
        """Queue a diameter dimension of the circle or arc about ``center`` of ``radius``, as DIMDIAMETER makes one: its line
        runs across the circle along the line from the centre through ``location``. The words read the diameter after the
        sign unless ``text`` gives others. ``dim_style`` is as ``add_linear_dim`` takes it.

        Example:
            drawing.add_diameter_dim((50, 50), 25, (80, 70))          # a diameter dimension reading ∅50
        """
        _, near, far = self._rim(center, radius, location, "diameter")
        made = _dxf.DxfDiametricDimension(near, far, 0.0, "" if text is None else str(text), None)
        return self.add(self._dimension(made, dim_style, props))

    def add_angular_dim(
        self,
        vertex: Any,
        first: Any,
        second: Any,
        location: Any,
        text: str | None = None,
        dim_style: str | None = None,
        **props: Any,
    ) -> Entity[DxfPoint3AngularDimension]:
        """Queue an angular dimension of the angle at ``vertex`` between the rays through ``first`` and ``second``, as DIMANGULAR
        makes one from three points: its arc passes through ``location``, which says the radius of the arc and which of the
        four angles the two rays make is measured. ``text`` and ``dim_style`` are as ``add_linear_dim`` takes them.

        Example:
            drawing.add_angular_dim((0, 0), (100, 0), (0, 100), (50, 50))   # 90 degrees, its arc through (50, 50)
        """
        corner, one, two, put = point2(vertex), point2(first), point2(second), point2(location)
        finite("A point of an angular dimension", corner.X, corner.Y, one.X, one.Y, two.X, two.Y, put.X, put.Y)
        for ray in (one, two):
            if math.hypot(ray.X - corner.X, ray.Y - corner.Y) <= 1e-9:
                raise ValueError("A ray of an angular dimension starts and ends at its vertex.")
        made = _dxf.DxfPoint3AngularDimension(
            point3(vertex), point3(first), point3(second), point3(location), "" if text is None else str(text), None
        )
        return self.add(self._dimension(made, dim_style, props))

    @staticmethod
    def _loops(boundary: Any) -> list[tuple[list[Any], list[float]]]:
        """The loops of a hatch boundary: a sequence of points is one loop; an ``Entity`` or ``GeometryHelper.Dxf`` entity, or a
        sequence of them, is a loop for each (a closed polyline or a circle). Each loop is its corners and the bulge of each."""
        items: list[Any] = [boundary] if isinstance(boundary, Entity) else list(boundary)
        if not items:
            raise ValueError("A hatch boundary needs at least three points, or a closed polyline or a circle.")
        if not any(isinstance(item, Entity) or isinstance(item, _dxf.DxfEntity) for item in items):
            corners = [point2(item) for item in items]
            if len(corners) < 3:
                raise ValueError("A hatch boundary of points needs at least three of them.")
            return [(corners, [0.0] * len(corners))]
        loops: list[tuple[list[Any], list[float]]] = []
        for item in items:
            net = _net_of(item)
            if isinstance(net, _dxf.DxfCircle):
                x, y, r = float(net.Center.X), float(net.Center.Y), float(net.Radius)
                loops.append(([_geo.GeoPoint2(x + r, y), _geo.GeoPoint2(x - r, y)], [1.0, 1.0]))
            elif isinstance(net, _dxf.DxfPolyline) and bool(net.Closed):
                count = int(net.NumberOfVertices)
                loops.append(
                    ([net.GetPoint2dAt(i) for i in range(count)], [float(net.GetBulgeAt(i)) for i in range(count)])
                )
            else:
                raise ValueError(
                    f"A hatch boundary is points, a closed polyline or a circle, not {getattr(net, 'DxfName', net)!r}."
                )
        return loops

    def add_hatch(
        self, boundary: Any, pattern: str = "ANSI31", scale: float = 1.0, angle: float = 0.0, **props: Any
    ) -> Entity[DxfHatch]:
        """Queue a hatch of the area ``boundary`` encloses, as HATCH makes one with a pattern of the built-in set
        (``OpenCAD2D.pat``: ANSI31 to ANSI38, BRICK, EARTH, HONEY, STEEL, ISO02W100 to ISO15W100, AR-CONC and the rest, ignoring
        case, and ``"SOLID"`` for a fill), at ``scale`` and ``angle`` (radians).
        ``boundary`` is the corners of a closed outline (three or more points), or a closed polyline or a circle (an ``Entity``
        or a ``GeometryHelper.Dxf`` entity), or a list of them: the first is the outer boundary and the others lie inside it as
        islands. The hatch follows no object: it is the loops as they are now. ``ValueError`` for a pattern the set has not.

        Example:
            drawing.add_hatch([(0, 0), (100, 0), (100, 50), (0, 50)])                  # ANSI31 in a 100 by 50 box, area 5000
            drawing.add_hatch([(0, 0), (40, 0), (40, 40)], pattern="SOLID", color=1)   # a red solid triangle
        """
        loops = self._loops(boundary)
        positive("The scale of a hatch", scale)
        finite("The angle of a hatch", angle)
        name = str(pattern).strip().upper()
        found = None if name == "SOLID" else _cad.CadPatternFile.FindBuiltIn(name)
        if name != "SOLID" and found is None:
            raise ValueError(f"The pattern {pattern!r} is not in the built-in set {_cad.CadPatternFile.BuiltInName}.")
        made = _dxf.DxfHatch()
        made.SetHatchPattern(_dxf.DxfHatchPatternType.PreDefined, name)
        if found is not None:
            made.PatternAngle = float(angle)
            made.PatternScale = float(scale)
            made.SetPatternDefinitions(found.Place(math.degrees(angle), float(scale), 0.0, 0.0))
        for index, (corners, bulges) in enumerate(loops):
            kind = _dxf.DxfHatchLoopTypes.External if index == 0 else _dxf.DxfHatchLoopTypes.Default
            made.AppendLoop(kind, _net.Array[_geo.GeoPoint2](corners), _net.Array[_net.Double](bulges))
        return self.add(self._made(made, props))

    def add_insert(
        self, name: str, insert: Any, scale: float = 1.0, rotation: float = 0.0, **props: Any
    ) -> Entity[DxfBlockReference]:
        """Queue an INSERT of the block ``name`` at ``insert``, scaled by ``scale`` and turned ``rotation`` radians, as INSERT
        places one. The block is one the drawing has or one ``blocks.add`` makes in the same commit; the commit refuses
        a name neither has.

        Example:
            drawing.add_insert("BOLT", (100, 50))                      # an INSERT of BOLT at (100, 50)
            drawing.add_insert("BOLT", (200, 50), scale=2.0, rotation=math.pi / 2)   # twice the size, turned a quarter
        """
        label = _record_name(name, "block")
        positive("The scale of an insert", scale)
        finite("The rotation of an insert", rotation)
        carrier = _dxf.DxfDatabase("AC1027")
        record = _dxf.DxfBlockTableRecord()
        record.Name = label
        carrier.BlockTable.Add(record)
        made = _dxf.DxfBlockReference(point3(insert), record)
        made.ScaleFactors = _geo.GeoVector3(float(scale), float(scale), float(scale))
        made.Rotation = float(rotation)
        return self.add(self._made(made, props))

    # ---- changing -------------------------------------------------------------------------------------------------

    def _own(self, entity: Entity, verb: str) -> None:
        if not isinstance(entity, Entity) or entity._drawing is not self:  # pyright: ignore[reportUnnecessaryIsInstance, reportPrivateUsage]
            raise ValueError(f"{verb} takes an Entity read from the drawing {self.name}, not {entity!r}.")
        if entity.handle.startswith("~"):
            raise ValueError(f"The {entity.dxf_type} {entity.handle} is not in the drawing yet: call commit() first.")

    def modify(self, entity: Entity) -> None:
        """Queue the changes made to ``entity.net``; not needed for ``layer``, ``color`` and ``linetype``, which commit alone.

        Example:
            circle = drawing.entities("CIRCLE")[0]        # radius 25
            circle.net.Radius = 30.0
            drawing.modify(circle)
            drawing.commit()                              # the circle in the drawing has radius 30.0
        """
        self._own(entity, "modify")
        self._queue(Op("modify", entity=entity))

    def erase(self, entity_or_handle: Entity | str) -> None:
        """Queue the erasing of an object, given as an ``Entity`` or its handle: ``drawing.erase("2F")``.

        Example:
            drawing.erase(drawing.entities("TEXT")[0])
            drawing.commit()                              # [] (nothing added); one object fewer
        """
        if isinstance(entity_or_handle, Entity):
            self._own(entity_or_handle, "erase")
            handle = entity_or_handle.handle
        else:
            handle = str(entity_or_handle)
            if handle.startswith("~"):
                raise ValueError(f"The object {handle} is not in the drawing yet: call commit() first.")
        self._queue(Op("erase", handle=handle))

    # ---- editing: the commands as calls ----------------------------------------------------------------------------

    def _edit(self, how: str, items: Sequence[Entity[Any]], args: tuple[Any, ...], in_place: bool) -> list[Entity[Any]]:
        """Sends what is queued and the edit as one commit and reads what it gave back: the objects given, read again, for an
        edit in place, else new ``Entity`` objects for what it made."""
        entities = list(items)
        for entity in entities:
            self._own(entity, how)
        if not entities:
            return []
        ops, changed_layers = self._take()
        try:
            handles, found = self._backend.edit(self._staged(ops), how, entities, args)
        finally:
            self._released(changed_layers)
        self._settled(ops, handles)
        if not in_place:
            return [Entity(self, handle, net, source) for handle, net, source in found]
        latest = {handle: (net, source) for handle, net, source in found}
        for entity in entities:
            if entity.handle in latest:
                entity._renew(*latest[entity.handle])  # pyright: ignore[reportPrivateUsage]
        return entities

    def move(self, items: Sequence[Entity[K]], displacement: Any) -> list[Entity[K]]:
        """Move objects by a displacement, as MOVE does, in the objects themselves: the ``Entity`` objects given are read
        again and come back in the order given. What is queued is applied first and with it, as one undo step (``REMOTE`` from
        outside the app, ``PYTHON`` for a file, the run's own in a tool); a drawing that refuses applies none of it and
        raises. The displacement is a ``GeoVector2`` or a tuple of 2 numbers, in the world, not the UCS.

        Example:
            line = drawing.entities(DxfLine)[0]           # from (0, 0) to (100, 0)
            drawing.move([line], (10, 5))                 # [line]; line.net now runs from (10, 5) to (110, 5)
        """
        vector = vector2(displacement)
        finite("The displacement", vector.X, vector.Y)
        return self._edit("move", items, (vector,), True)

    def copy(self, items: Sequence[Entity[K]], displacement: Any) -> list[Entity[K]]:
        """Copy objects by a displacement, as COPY does with a displacement typed: the objects stay and the copies come back
        as new ``Entity`` objects, of the class of their sources and in the order given, an object given twice copied once.
        Queued changes go with it, as ``move`` says.

        Example:
            copies = drawing.copy([line, circle], (0, 50))  # two new objects 50 above the line and the circle
            copies[0].net.StartPoint.Y                    # 50.0 for the line from (0, 0) to (100, 0)
        """
        vector = vector2(displacement)
        finite("The displacement", vector.X, vector.Y)
        return self._edit("copy", items, (vector,), False)

    def rotate(self, items: Sequence[Entity[K]], base: Any, angle: float) -> list[Entity[K]]:
        """Turn objects about a base point, as ROTATE does, in the objects themselves: the angle is in radians,
        counter-clockwise, whatever ANGDIR and ANGBASE say. Queued changes go with it, as ``move`` says.

        Example:
            drawing.rotate([line], (0, 0), math.pi / 2)   # the line from (0, 0) to (100, 0) now runs to (0, 100)
        """
        point = point2(base)
        finite("The base point", point.X, point.Y)
        finite("The angle", angle)
        return self._edit("rotate", items, (point, float(angle)), True)

    def scale(self, items: Sequence[Entity[K]], base: Any, factor: float) -> list[Entity[K]]:
        """Scale objects about a base point, as SCALE does, in the objects themselves: the factor is above nought. Queued
        changes go with it, as ``move`` says.

        Example:
            drawing.scale([circle], (0, 0), 2.0)          # a circle of radius 25 about (50, 50): radius 50 about (100, 100)
        """
        point = point2(base)
        finite("The base point", point.X, point.Y)
        positive("A scale factor", factor)
        return self._edit("scale", items, (point, float(factor)), True)

    def mirror(
        self, items: Sequence[Entity[K]], first: Any, second: Any, erase_source: bool = False
    ) -> list[Entity[K]]:
        """Mirror objects across the line through two points, as MIRROR does: a text stays readable under MIRRTEXT 0, a hatch
        keeps its pattern under MIRRHATCH 0. The mirrored copies come back as new ``Entity`` objects; with ``erase_source`` the
        objects themselves are mirrored where they stand and come back, read again, as ``move`` does. Queued changes go with
        it, as ``move`` says.

        Example:
            mirrored = drawing.mirror([line], (0, 0), (10, 0))   # a line from (0, 0) to (0, 50) gives one to (0, -50)
        """
        one, two = point2(first), point2(second)
        finite("A point of the mirror line", one.X, one.Y, two.X, two.Y)
        return self._edit("mirror", items, (one, two, bool(erase_source)), bool(erase_source))

    def offset(self, curve: Entity[DxfCurve], distance: float, side: Any) -> list[Entity[DxfCurve]]:
        """Offset a curve to the side of a point, as one pick of OFFSET with a distance typed: the copies go on the layer of the
        curve, which stays. A line, arc, circle, ellipse, polyline, spline, xline or ray; ``ValueError("Cannot offset that
        object.")`` for any other object, and ``RuntimeError`` where the offset makes no curve (a circle of radius 10 offset
        inward by 15). Queued changes go with it, as ``move`` says.

        Example:
            drawing.offset(line, 5.0, (50, 10))           # [the line from (0, 5) to (100, 5)] for one from (0, 0) to (100, 0)
        """
        self._own(curve, "offset")
        if not isinstance(curve.net, _dxf.DxfCurve):
            raise ValueError("Cannot offset that object.")
        point = point2(side)
        finite("The side point", point.X, point.Y)
        positive("An offset distance", distance)
        return self._edit("offset", [curve], (float(distance), point), False)

    def _take(self) -> tuple[list[Op], list[Layer]]:
        """What is queued as the ops a backend applies, the queue emptied; with the layers that were changed."""
        ops = list(self._ops)
        for entity in self._changed_entities:
            ops.append(Op("modify", entity=entity))
        for layer in self._changed_layers:
            ops.append(Op("modify_layer", layer=layer))
        changed_layers = self._changed_layers[:]
        self._ops.clear()
        self._changed_entities.clear()
        self._changed_layers.clear()
        self._noted = False
        return ops, changed_layers

    def _named(self, ops: list[Op]) -> None:
        """``ValueError`` for a text style, dimension style or linetype that an object queued names and the drawing lacks and
        the commit does not add: the same words on every backend, before anything is sent. What each object names is kept on its
        op, for the backend to give the small drawing it adds the object to."""
        wanted: dict[str, set[str]] = {"STYLE": set(), "DIMSTYLE": set(), "LTYPE": set()}
        for op in ops:
            if op.kind != "insert":
                continue
            op.needs = needs(op.copy)
            for kind, name in op.needs:
                if kind in wanted:
                    wanted[kind].add(name)
        added = {"text_style": "STYLE", "dim_style": "DIMSTYLE", "linetype": "LTYPE"}
        for kind, names in wanted.items():
            if not names:
                continue
            have = {n.upper() for n in self._backend.names(kind)}
            have |= {op.spec.name.upper() for op in ops if added.get(op.kind) == kind}
            for name in sorted(names, key=lambda n: (n.upper(), n)):
                if name and name.upper() not in have:
                    raise ValueError(f'The {_WORDS[kind]} "{name}" is not in the drawing: add it with {_ADDED_BY[kind]} first.')

    def _staged(self, ops: list[Op]) -> list[Op]:
        """The ops a backend is given: the table records queued (styles, linetypes, blocks) made in one small drawing and put
        first as one ``records`` op, the rest as they are. A record the drawing has, or that two of them make, raises here, as
        does an object that names a style or linetype nobody has."""
        self._named(ops)
        records = [op for op in ops if op.kind in RECORD_KINDS]
        if not records:
            return ops
        staging = Staging(self._backend)
        for op in records:
            staging.add(op)
        return [Op("records", spec=staging.added, records=staging.database)] + [
            op for op in ops if op.kind not in RECORD_KINDS
        ]

    def _released(self, changed_layers: list[Layer]) -> None:
        """Ends a send, well or not: a layer is no longer waiting to be written and the layers are read again."""
        for layer in changed_layers:
            layer._dirty = False  # pyright: ignore[reportPrivateUsage]
        self._layers._known.clear()  # pyright: ignore[reportPrivateUsage]

    @staticmethod
    def _settled(ops: list[Op], handles: list[str | None]) -> list[str]:
        """Gives the objects added their real handles; the handles, in the order added."""
        added = [op for op in ops if op.kind == "insert"]
        for op in ops:
            if op.kind == "add_layer" and op.layer is not None:
                op.layer._pending = False  # pyright: ignore[reportPrivateUsage]
        made: list[str] = []
        for op, handle in zip(added, handles + [None] * (len(added) - len(handles))):
            if handle is not None and op.entity is not None:
                op.entity._handle = handle  # pyright: ignore[reportPrivateUsage]
                made.append(handle)
        return made

    def commit(self) -> list[str]:
        """Apply everything queued as one undo step, and return the handles of the objects added, in the order they were
        added; ``[]`` when nothing was queued. The layers are made first, so an object can be put on a layer made in the same
        commit. A commit the drawing refuses changes nothing and raises.

        Example:
            drawing.add_line((0, 0), (100, 0))
            drawing.add_circle((50, 50), 25)
            drawing.commit()                              # ['101', '102']; a second commit with nothing queued gives []
        """
        ops, changed_layers = self._take()
        if not ops:
            return []
        try:
            handles = self._backend.commit(self._staged(ops))
        finally:
            self._released(changed_layers)
        return self._settled(ops, handles)

    # ---- layers, styles, blocks, layouts --------------------------------------------------------------------------

    @property
    def layers(self) -> Layers:
        """The layers: ``drawing.layers["Steel"]``, ``drawing.layers.add("Steel", color=1)``.

        Example:
            drawing.layers.add("Steel", color=1)          # queued; Steel exists after drawing.commit()
            drawing.layers["steel"].name                  # "Steel": names ignore case; len(drawing.layers) is 2 with layer 0
        """
        return self._layers

    @property
    def current_layer(self) -> str:
        """The name of the layer new objects go on; setting it takes effect at once, not at ``commit()``.

        Example:
            drawing.current_layer                         # '0' in a new drawing
            drawing.current_layer = "Steel"               # takes effect at once; the next object made by a command is on Steel
        """
        return self._backend.current_layer

    @current_layer.setter
    def current_layer(self, name: str) -> None:
        self._backend.current_layer = name

    @property
    def text_styles(self) -> TextStyles:
        """The text styles: copies of the drawing's ``DxfTextStyleTableRecord``, sorted by name ignoring case, then ordinal;
        changing one changes nothing in the drawing. ``drawing.text_styles.add("NOTE", font="romans.shx")`` queues a new one.

        Example:
            [s.Name for s in drawing.text_styles]         # ['Standard'] in a new drawing
            drawing.text_styles.add("NOTE", font="romans.shx", x_scale=0.8)   # queued; NOTE exists after drawing.commit()
        """
        return self._text_styles

    @property
    def current_text_style(self) -> str:
        """The name of the text style new texts take (TEXTSTYLE); setting it takes effect at once, as one undo step, not at
        ``commit()``, and a style the drawing has not is refused.

        Example:
            drawing.current_text_style                    # 'Standard' in a new drawing
            drawing.current_text_style = "NOTE"           # at once; NOTE is in the drawing after the commit that added it
        """
        return str(self.get_variable("TEXTSTYLE"))

    @current_text_style.setter
    def current_text_style(self, name: str) -> None:
        self.set_variable("TEXTSTYLE", str(name))

    @property
    def dimension_styles(self) -> DimensionStyles:
        """The dimension styles: copies of the drawing's ``DxfDimStyleTableRecord``, sorted as ``text_styles`` are.
        ``drawing.dimension_styles.add("PLAN", scale=50)`` queues a new one.

        Example:
            [s.Name for s in drawing.dimension_styles]    # ['ISO-25', 'Standard'] in a new drawing
            drawing.dimension_styles.add("PLAN", scale=50)   # queued; PLAN exists after drawing.commit()
        """
        return self._dimension_styles

    @property
    def current_dim_style(self) -> str:
        """The name of the dimension style new dimensions are drawn in (DIMSTYLE); setting it takes effect at once, as one
        undo step, not at ``commit()``, and a style the drawing has not is refused (``RuntimeError``).

        Example:
            drawing.current_dim_style                     # 'ISO-25' in a new drawing
            drawing.current_dim_style = "PLAN"            # at once; PLAN is in the drawing after the commit that added it
        """
        return self._backend.current_dim_style

    @current_dim_style.setter
    def current_dim_style(self, name: str) -> None:
        self._backend.current_dim_style = name

    @property
    def linetypes(self) -> Linetypes:
        """The linetypes, by name; ``drawing.linetypes.load("DASHED")`` queues one of the built-in set or a .lin file.

        Example:
            list(drawing.linetypes)                       # ['ByBlock', 'ByLayer', 'Continuous'] in a new drawing
            drawing.linetypes.load("DASHED")              # queued; DASHED exists after drawing.commit()
        """
        return self._linetypes

    @property
    def blocks(self) -> Blocks:
        """The blocks: copies of the drawing's ``DxfBlockTableRecord`` with their objects, sorted as ``text_styles`` are; the
        blocks of the layouts (``*Model_Space``) and the anonymous ones (``*D1``) are left out. An external reference attached
        and loaded brings the blocks of its drawing, as AutoCAD lists them: ``grid|TAG`` for the block ``TAG`` of ``grid``, its
        ``IsDependent`` true, its objects the drawing's own copies. They are not inserted or changed.
        ``drawing.blocks.add("BOLT", entities)`` queues a new block, and ``add_insert`` places one.

        Example:
            [b.Name for b in drawing.blocks]              # [] in a new drawing: no block of its own yet
        """
        return self._blocks

    @property
    def layouts(self) -> list[str]:
        """The names of the layouts, ``["Model", "Layout1"]``.

        Example:
            drawing.layouts                               # ['Model', 'Layout1'] in a new drawing
        """
        return self._backend.layouts

    @property
    def current_layout(self) -> str:
        """The name of the layout shown; setting it shows that layout at once.

        Example:
            drawing.current_layout                        # 'Model'
            drawing.current_layout = "Layout1"            # Layout1 is shown
        """
        return self._backend.current_layout

    @current_layout.setter
    def current_layout(self, name: str) -> None:
        self._backend.current_layout = name

    # ---- the user -------------------------------------------------------------------------------------------------

    def pick_point(self, message: str, base: Any = None) -> Any:
        """Ask the user for a point, a ``GeoPoint3``: the prompt of the command line in the app, a wait on the app from
        outside; ``RuntimeError`` on a drawing of a file. ``base`` draws a rubber band from that point.

        Example:
            centre = drawing.pick_point("Pick the centre")                  # in the app, the user picks (120, 80): GeoPoint3, Z 0
            drawing.pick_point("Pick the end", base=centre)                 # a rubber band from centre
        """
        return self._backend.pick_point(message, None if base is None else point3(base))

    def pick_object(self, message: str) -> Entity | None:
        """Ask the user for one object and return it, or None where the user gave none; ``RuntimeError`` on a file.

        Example:
            line = drawing.pick_object("Pick a line")     # an Entity; None where the user gave none
        """
        found = self._backend.pick_object(message)
        return None if found is None else Entity(self, found[0], found[1], found[2])

    def pick_objects(self, message: str) -> list[Entity]:
        """Ask the user for a selection and return its objects; ``RuntimeError`` on a drawing of a file.

        Example:
            entities = drawing.pick_objects("Select objects")   # the objects selected, as a list of Entity
        """
        return [Entity(self, handle, net, source) for handle, net, source in self._backend.pick_objects(message)]

    # ---- the file -------------------------------------------------------------------------------------------------

    def save(self) -> None:
        """Commit what is queued, then save to the file the drawing came from or was last saved as. A save inside a group of
        edits (an ``undo_group``, or the run of a tool) saves what the group has made so far and goes on: what follows it is one
        undo step with what came before it, and one undo takes back both, the saved part too.

        Example:
            drawing = open_file("plan.dxf")
            drawing.add_line((0, 0), (100, 0))
            drawing.save()                                # commits first, then writes plan.dxf; is_modified is False
        """
        self.commit()
        self._backend.save()

    def save_as(self, path: str, version: str | None = None) -> None:
        """Commit what is queued, then save as ``path``: a DWG where it ends in .dwg, else a DXF, of ``version`` (``"AC1027"``)
        where given, else the drawing's own.

        Example:
            drawing.save_as("plan.dxf")                   # a DXF of the drawing's own version (AC1027 in a new drawing)
            drawing.save_as("plan.dwg", version="AC1027") # a DWG; drawing.path and drawing.name now name plan.dwg
        """
        self.commit()
        self._backend.save_as(os.path.abspath(path), version)

    def close(self, discard: bool = False) -> None:
        """Close the drawing; one with changes not saved is refused unless ``discard`` is true. What is queued is dropped.
        A drawing of a file is only let go.

        Example:
            drawing.close()                               # refused (CadRemoteException) from outside while it has unsaved changes
            drawing.close(discard=True)                   # closed without saving; a drawing of a file is only let go
        """
        self._discard()
        self._backend.close(discard)

    def __repr__(self) -> str:
        return f"<Drawing {self.name}>"


class Documents:
    """The drawings of an app, in the order it lists them: iterable, ``len`` and ``[name_or_path]`` (``KeyError`` naming the
    key where none is open)::

        for drawing in documents():
            print(drawing.name, drawing.is_active)
        plan = documents()["plan.dxf"]

    Example:
        for drawing in documents():
            print(drawing.name, drawing.is_active)    # plan.dxf False, then Drawing2.dxf True
        plan = documents()["plan.dxf"]                # KeyError naming "plan.dxf" where none is open
    """

    def __init__(self, lister: Callable[[], list[Drawing]]) -> None:
        self._lister = lister

    def __iter__(self) -> Iterator[Drawing]:
        return iter(self._lister())

    def __len__(self) -> int:
        return len(self._lister())

    def __getitem__(self, key: str) -> Drawing:
        drawings = self._lister()
        if os.path.isabs(key):
            full = os.path.normcase(os.path.normpath(key))
            for drawing in drawings:
                if drawing.path is not None and os.path.normcase(os.path.normpath(drawing.path)) == full:
                    return drawing
        for drawing in drawings:
            if drawing.name.upper() == key.upper():
                return drawing
        raise KeyError(f"No drawing named {key!r} is open.")
