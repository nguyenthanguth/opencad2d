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
import json
import math
import os
from collections.abc import Callable, Iterator, Mapping, Sequence
from dataclasses import dataclass, field
from functools import cached_property
from typing import TYPE_CHECKING, Any, Generic, Literal, NamedTuple, Protocol, TypeVar, cast, overload


if TYPE_CHECKING:
    import numpy as np
    import typing_extensions
    from numpy.typing import NDArray
    from System.Collections.Generic import IEnumerable
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


@dataclass
class RenderOptions:
    """What a render call was given, checked: the part drawn (None for the extents), the picture's size (None for the default),
    the ground as 0xRRGGBB, the layout (None for the space the drawing was left in), and for the labelled renders the class
    numbers to label (None for the default four)."""

    box: Box | None = None
    dpi: float = 96.0
    width: int | None = None
    height: int | None = None
    background: int = 0
    layout: str | None = None
    classes: tuple[int, ...] | None = None


LABEL_CLASSES: tuple[str, ...] = ("words", "dimensions", "lines", "hatches", "blocks")
"""The classes of ``render_masks`` and the categories of ``to_coco``: the name of class number ``i + 1`` is ``LABEL_CLASSES[i]``
(0 is the ground)."""


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
    def render(self, kind: str, path: str | None, options: RenderOptions) -> Any: ...

    def export(
        self, what: str, layout: str | None, tolerance: float, max_edges: int | None = None
    ) -> tuple[str, Any]: ...

    # ---- where things are drawn (PW6.2) ----
    def bounds(self, layout: str | None, handles: list[str] | None) -> tuple[list[str], Any]: ...
    def overlaps(
        self,
        layout: str | None,
        words: list[str] | None,
        over: list[str] | None,
        handles: list[str] | None,
        tolerance: float,
    ) -> tuple[list[str], list[str], Any]: ...

    # ---- measuring before placing (PW7.1) ----
    def measure(self, entity: Any) -> tuple[tuple[float, ...] | None, list[tuple[float, ...]]]: ...
    def measure_leader(
        self,
        style: str | None,
        arrow: tuple[float, float],
        landing: tuple[float, float],
        words: str,
        scale: float | None,
    ) -> tuple[tuple[float, ...] | None, list[tuple[float, ...]]]: ...
    def free_space(
        self,
        layout: str | None,
        frame: tuple[float, float, float, float],
        kinds: list[str] | None,
        margin: float,
        min_width: float,
        min_height: float,
        tolerance: float,
    ) -> list[tuple[float, float, float, float]]: ...
    def fits(
        self,
        layout: str | None,
        candidate: tuple[float, float, float, float, float],
        kinds: list[str] | None,
        margin: float,
        tolerance: float,
    ) -> bool: ...

    # ---- a drawing against a company standard (PW7.2) ----
    def check(
        self, standard: str, model_scale: float, tolerance: float
    ) -> Any: ...

    # ---- a snapshot and reset of a drawing in memory (PW7.3) ----
    def snapshot(self) -> Any: ...
    def reset(self, snapshot: Any) -> None: ...
    def release(self, snapshot: Any) -> None: ...
    def as_drawn(self) -> Any: ...
    def layout_list(self) -> list[dict[str, Any]]: ...
    def layout_add(self, name: str, paper: str | None, landscape: bool) -> dict[str, Any]: ...
    def layout_setup(
        self,
        layout: str,
        paper: str | None,
        orientation: str | None,
        area: str | None,
        scale: str | None,
        plot_style: str | None,
    ) -> dict[str, Any]: ...
    def layout_viewport(
        self,
        layout: str,
        frame: tuple[float, float, float, float],
        scale: str,
        center: tuple[float, float] | None,
        frozen: list[str],
    ) -> str: ...
    def plot_pdf(self, layout: str, path: str, paper: str | None, scale: str | None, fit: bool) -> Any: ...


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


def _layout_name(layout: str | None) -> str | None:
    """A layout as a plain ``str``: a ``Layout`` is one, but .NET takes only an exact ``str``."""
    return None if layout is None else str(layout)


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

    @property
    def bounds(self) -> Box | None:
        """The box the object takes as the drawing draws it, as a ``Box`` of (min_x, min_y, max_x, max_y): the letters of an SHX
        or TrueType word and not its insertion point, the picture of a dimension, a block however deep and scaled, the width
        of a wide polyline. ``None`` where the drawing draws nothing of it (an object on a layer that is off or frozen). It is
        the drawing as committed: a change queued and not committed is not in it, and an object just added (``~1``) is a
        ``ValueError`` until ``commit()``.

        Example:
            line = drawing.add_line((0, 0), (100, 40))
            drawing.commit()
            line.bounds                                   # Box(min_x=0.0, min_y=0.0, max_x=100.0, max_y=40.0)
        """
        if self._handle.startswith("~"):
            raise ValueError(f"The {self._type} {self._handle} is not in the drawing yet: call commit() first.")
        return self._drawing._bounds_of(self._handle)  # pyright: ignore[reportPrivateUsage]

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
    """The layers of a drawing: iterable, ``len``, ``[index]``, ``[name]`` (ignoring case, ``KeyError`` when none), ``in``, and ``add``.

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

    def __getitem__(self, name: int | str) -> Layer:
        layers = self._read()
        if isinstance(name, int):
            return layers[name]
        for layer in layers:
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
        self._drafts: list[Any] | None = None
        self._noted = False
        self._layers = Layers(self)
        self._text_styles = TextStyles(self)
        self._dimension_styles = DimensionStyles(self)
        self._linetypes = Linetypes(self)
        self._blocks = Blocks(self)
        self._layouts = Layouts(self)

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
            found = self._backend.read(None if type is None else type.upper(), layer, _layout_name(layout))
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

    # ---- where things are drawn: bounds and overlaps (PW6.2) ------------------------------------------------------

    def _bounds_of(self, handle: str) -> Box | None:
        """The box of one object as drawn, or None where the drawing draws nothing of it."""
        handles, boxes = self._backend.bounds(None, [handle])
        if not handles:
            return None
        one = [float(number) for number in boxes]
        return Box(one[0], one[1], one[2], one[3])

    def bounds(self, layout: str | None = None) -> BoundsTable:
        """The box every object of a space takes as the drawing draws it, as a ``BoundsTable``: ``handles`` and ``boxes``, a
        NumPy array of one row of (min_x, min_y, max_x, max_y) for each, in the order the space draws them. Read from the
        display list, so the box is the one the canvas fills: the letters of SHX and TrueType words, a dimension's lines,
        arrows and words, the contents of a block however deep and scaled, the columns of an MTEXT, the width of a polyline.
        ``layout`` names a layout (model space where None). An object that draws nothing has no row. Needs numpy
        (``pip install opencad2d[numpy]``). A drawing of 100 000 objects answers in 0.3 s the first time (it makes its index) and 0.01 s after.

        Example:
            table = drawing.bounds()
            table.handles                                 # ['101', '102'] for a line and a text
            table.boxes[0]                                # array([0., 0., 100., 40.]) for the line from (0, 0) to (100, 40)
        """
        handles, boxes = self._backend.bounds(_layout_name(layout), None)
        return BoundsTable(handles, _rows(boxes))

    def overlaps(
        self,
        kinds: Sequence[str] | None = None,
        over: Sequence[str] | None = None,
        layout: str | None = None,
        of: Sequence[Entity | str] | None = None,
        tolerance: float = 0.001,
    ) -> OverlapTable:
        """Find the objects whose words overlap what is drawn near them, as an ``OverlapTable``: ``words`` and ``others``, the
        handles of each pair, and ``boxes``, a NumPy array of the box (min_x, min_y, max_x, max_y) of the common part. A
        word is the frame of a text (a TEXT, the runs of an MTEXT line by line so that a gap between columns is no word, the
        words of a dimension), turned as the text is; it overlaps a line, an arc, a polyline, a hatch or a filled area that
        runs through it and the frame of another word it shares an area with. What only touches it within ``tolerance``
        (millimetres, 0.001) does not, and neither does a part of the word's own object: a dimension's text is not over its
        own lines. A pair is one row however many of the parts of the two meet; the box holds all the places they do.

        ``kinds`` are the DXF names whose words are looked at (``"TEXT"``, ``"MTEXT"``, ``"DIMENSION"``, ``"MULTILEADER"``; None
        takes those four), ``over`` the names of what they are looked at against (None takes every kind, words included, so
        two texts that overlap are a row, the one drawn first as ``words``). The words of an INSERT, its attributes and the
        text of its block, are of the kind ``"INSERT"``. ``of`` limits the answer to pairs in which one of these objects
        stands, on either side. ``layout`` names a layout (model space where None). Fast enough for a reward function: 100 000
        objects in about 0.5 s, and one object's pairs in a few milliseconds. Needs numpy.

        Example:
            hits = drawing.overlaps()                     # a text with a line through it
            hits.words, hits.others                       # (['101'], ['102'])
            drawing.overlaps(kinds=["DIMENSION"], over=["LINE", "LWPOLYLINE"])   # where dimension words sit on lines
            drawing.overlaps(of=[line])                   # what the object just moved meets
        """
        if not (math.isfinite(tolerance) and tolerance >= 0.0):
            raise ValueError(f"The tolerance is a finite number of nought or more, not {tolerance!r}.")
        handles = None if of is None else self._handles_of(of)
        words, others, boxes = self._backend.overlaps(
            _layout_name(layout),
            None if kinds is None else [str(kind) for kind in kinds],
            None if over is None else [str(kind) for kind in over],
            handles,
            float(tolerance),
        )
        return OverlapTable(words, others, _rows(boxes))

    # ---- measuring before placing (PW7.1) -------------------------------------------------------------------------

    def _drafted(self, make: Callable[[], Any]) -> Any:
        """The .NET object an ``add_*`` call makes, taken without queueing it: what a measure draws is the very object that
        adding it would add, the same code having made it."""
        made: list[Any] = []
        tickets = self._tickets
        self._drafts = made
        try:
            make()
        finally:
            self._drafts = None
            self._tickets = tickets
        return made[0]

    def _measured(self, answer: tuple[tuple[float, ...] | None, list[tuple[float, ...]]]) -> Measurement:
        box, frames = answer
        return Measurement(None if box is None else Box(*box), [Frame(*frame) for frame in frames])

    def measure_text(
        self,
        text: str,
        at: Any,
        height: float,
        style: str | None = None,
        rotation: float = 0.0,
        justify: str | None = None,
    ) -> Measurement:
        """Measure the single-line text ``add_text`` would add, without adding it: the box it would take and the frame of its
        word, found by the code that draws the text once it is in the drawing, in the drawing's text style and fonts, so that the
        box equals ``bounds`` of the text added to 1e-6. ``at`` is the insertion point, or the point the text stands about
        where ``justify`` is given: ``"left"``, ``"center"``, ``"right"``, ``"middle"`` or an AutoCAD code, ``"TL"``, ``"TC"``,
        ``"TR"``, ``"ML"``, ``"MC"``, ``"MR"``, ``"BL"``, ``"BC"`` or ``"BR"`` (any case). ``rotation`` is in radians. A style the
        drawing lacks is a ``ValueError``. Nothing is queued and nothing is added.

        Example:
            m = drawing.measure_text("Plate A-12", (10, 20), 5.0, style="NOTE")
            m.box                                         # Box(min_x=10.0, min_y=18.5, max_x=40.25, max_y=25.0)
            drawing.fits(m.frames[0], margin=1.0)         # True where the place is free by a millimetre all round
        """
        positive("The height of a text", height)
        finite("The rotation of a text", rotation)
        at2 = point2(at)
        finite("The point of a text", at2.X, at2.Y)
        net = self._drafted(lambda: self.add_text(text, at, height, rotation, style))
        if justify is not None:
            net.Justify = _justification(justify, True)
            net.AlignmentPoint = point3(at)
        return self._measured(self._backend.measure(net))

    def measure_mtext(
        self,
        text: str,
        at: Any,
        height: float,
        width: float = 0.0,
        style: str | None = None,
        rotation: float = 0.0,
        justify: str | None = None,
    ) -> Measurement:
        """Measure the multi-line text ``add_mtext`` would add, without adding it: the box of its lines and a frame for each
        run of a line, as the drawing lays it out in its fonts (wrapped at ``width``, 0 for no wrapping). ``justify`` is the
        attachment, ``"TL"`` (the default), ``"TC"``, ``"TR"``, ``"ML"``, ``"MC"``, ``"MR"``, ``"BL"``, ``"BC"`` or ``"BR"``; ``rotation``
        is in radians. Nothing is added.

        Example:
            m = drawing.measure_mtext("Plate PL10\\PGrade S355", (10, 20), 5.0, width=60)
            len(m.frames)                                 # 2: a frame for each line
        """
        positive("The height of a text", height)
        finite("The width and rotation of a text", width, rotation)
        at2 = point2(at)
        finite("The point of a text", at2.X, at2.Y)
        net = self._drafted(lambda: self.add_mtext(text, at, height, width, style))
        if rotation:
            net.Rotation = float(rotation)
        if justify is not None:
            net.Attachment = _justification(justify, False)
        return self._measured(self._backend.measure(net))

    def measure_dimension(
        self,
        kind: str,
        points: Sequence[Any],
        dim_style: str | None = None,
        text: str | None = None,
        angle: float | None = None,
    ) -> Measurement:
        """Measure the dimension an ``add_*_dim`` call would add, without adding it: the box of its lines, arrows and words, and
        the frame of its words, as the drawing draws its picture in the style ``dim_style`` (the current one where None; a
        style the drawing lacks is a ``ValueError``). ``kind`` and ``points`` are ``"linear"`` and ``(first, second, location)``
        (with ``angle`` of the dimension line, as ``add_linear_dim`` takes it), ``"aligned"`` and ``(first, second, location)``,
        ``"radial"`` and ``"diameter"`` and ``(center, rim, location)``, a point of the circle in place of the radius, and
        ``"angular"`` and ``(vertex, first, second, location)``. ``text`` is the words in place of the measurement.

        Example:
            m = drawing.measure_dimension("linear", [(0, 0), (100, 0), (50, 20)])
            m.box.max_y > 20.0                            # True: the words stand above the dimension line
        """
        count = {"linear": 3, "aligned": 3, "radial": 3, "diameter": 3, "angular": 4}
        name = str(kind).strip().lower()
        if name not in count:
            raise ValueError(f"A dimension to measure is of the kind {', '.join(count)}, not {kind!r}.")
        given = list(points)
        if len(given) != count[name]:
            raise ValueError(f"A {name} dimension is measured from {count[name]} points, not {len(given)}.")

        def make() -> Any:
            if name == "linear":
                return self.add_linear_dim(given[0], given[1], given[2], angle, text, dim_style)
            if name == "aligned":
                return self.add_aligned_dim(given[0], given[1], given[2], text, dim_style)
            if name == "angular":
                return self.add_angular_dim(given[0], given[1], given[2], given[3], text, dim_style)
            centre, rim = point2(given[0]), point2(given[1])
            finite("A point of a dimension", centre.X, centre.Y, rim.X, rim.Y)
            radius = math.hypot(rim.X - centre.X, rim.Y - centre.Y)
            if name == "radial":
                return self.add_radial_dim(given[0], radius, given[2], text, dim_style)
            return self.add_diameter_dim(given[0], radius, given[2], text, dim_style)

        return self._measured(self._backend.measure(self._drafted(make)))

    def measure_leader(
        self, arrow: Any, landing: Any, text: str, style: str | None = None, scale: float | None = None
    ) -> Measurement:
        """Measure the multileader of words the MLEADER command would make with its arrow at ``arrow`` and its landing at
        ``landing``, in the multileader style ``style`` (the current one where None, and Standard, as MLEADER makes it, in a drawing
        with none) at ``scale`` (the one MLEADER gives where None), without adding it: the box of the leader line, its arrowhead, the landing and the words, and the frame of the
        words, laid out as the command lays them. A style the drawing lacks, one of a block or of no content, and an annotative
        one, are a ``ValueError``.

        Example:
            m = drawing.measure_leader((0, 0), (30, 20), "B-12")
            m.box.min_x                                   # 0.0: the arrowhead's point
        """
        start, end = point2(arrow), point2(landing)
        finite("A point of a leader", start.X, start.Y, end.X, end.Y)
        if scale is not None:
            positive("The scale of a leader", scale)
        return self._measured(
            self._backend.measure_leader(
                style,
                (float(start.X), float(start.Y)),
                (float(end.X), float(end.Y)),
                str(text),
                None if scale is None else float(scale),
            )
        )

    def free_space(
        self,
        box: Any,
        kinds: Sequence[str] | None = None,
        margin: float = 0.0,
        min_width: float = 0.0,
        min_height: float = 0.0,
        layout: str | None = None,
        tolerance: float = 0.001,
    ) -> list[Box]:
        """The empty places of a frame: every rectangle, not turned, inside ``box`` that no line or word of the ``kinds`` (DXF
        names, ``"TEXT"``, ``"LINE"``, ``"INSERT"``; every kind where None) runs through or comes within ``margin`` of, and that
        cannot be made larger, as a list of ``Box`` with the largest first (equal areas by min_y, min_x, max_y, max_x). They
        overlap one another, as the empty places of a page do: a box a thing could stand in lies inside one of them, which is the
        test ``fits`` makes exactly. A word is taken by the box of its frame, a slanted line by a staircase of boxes along it, a hatch
        by its box, and ``margin`` is a gap measured along the axes. In a crowded frame the rectangles number in thousands: give the
        width and height of the thing to place as ``min_width`` and ``min_height`` and only the rectangles it could stand in come back,
        which is exact and far faster. ``ValueError`` for a frame with no width or height, a margin or size below nought, and a
        frame too crowded to list every rectangle in a few seconds. 100 000 objects over 3 000 by 3 000: a 300 by 300 frame in 0.4 s, the whole
        at least 20 by 20 in 1 s.

        Example:
            places = drawing.free_space((0, 0, 100, 50), kinds=["LINE"], margin=2.0, min_width=20, min_height=10)
            places[0]                                     # Box(min_x=0.0, min_y=0.0, max_x=68.0, max_y=50.0) beside a line at x 70
        """
        frame = _frame_box(box)
        if not (frame[2] > frame[0] and frame[3] > frame[1]):
            raise ValueError(f"A frame has a width and a height above nought, not {box!r}.")
        for what, value in (("The margin", margin), ("The least width", min_width), ("The least height", min_height)):
            if not (math.isfinite(value) and value >= 0.0):
                raise ValueError(f"{what} is a finite number of nought or more, not {value!r}.")
        _tolerance(tolerance)
        rows = self._backend.free_space(
            _layout_name(layout),
            frame,
            None if kinds is None else [str(kind) for kind in kinds],
            float(margin),
            float(min_width),
            float(min_height),
            float(tolerance),
        )
        return [Box(*row) for row in rows]

    def fits(
        self,
        candidate: Any,
        kinds: Sequence[str] | None = None,
        margin: float = 0.0,
        layout: str | None = None,
        tolerance: float = 0.001,
    ) -> bool:
        """Whether a place is free: nothing of the ``kinds`` (every kind where None) runs through ``candidate`` or comes within
        ``margin`` of it, tested on the real lines, arcs, fills and word frames and not on their boxes, as ``overlaps`` tests a word,
        so a diagonal passing a corner at a distance fits. What only touches it within ``tolerance`` does not count.
        ``candidate`` is a ``Box`` or four numbers (min_x, min_y, max_x, max_y), a ``Frame`` as ``measure_text`` gives (turned as
        the word is), or a ``Measurement`` (its box). A layout agent calls it in a loop: about 0.2 ms a call over 100 000 objects.

        Example:
            drawing.fits((40, 10, 60, 20))                # True where nothing is in the 20 by 10 box
            drawing.fits(Frame(40, 10, 20, 10, 0.5))      # the same size turned half a radian about (40, 10)
        """
        place = _frame_of(candidate)
        if not (math.isfinite(margin) and margin >= 0.0):
            raise ValueError(f"The margin is a finite number of nought or more, not {margin!r}.")
        _tolerance(tolerance)
        return bool(
            self._backend.fits(
                _layout_name(layout),
                (place.x, place.y, place.width, place.height, place.rotation),
                None if kinds is None else [str(kind) for kind in kinds],
                float(margin),
                float(tolerance),
            )
        )

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
        if self._drafts is not None:  # a measure takes the object an add_* makes and queues nothing
            self._drafts.append(clone)
            return entity
        self._queue(Op("insert", entity=entity, copy=dxf_entity.Clone(), layout=_layout_name(layout)))
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
        self,
        name: str,
        insert: Any,
        scale: float = 1.0,
        rotation: float = 0.0,
        layout: str | None = None,
        attributes: Mapping[str, str] | None = None,
        **props: Any,
    ) -> Entity[DxfBlockReference]:
        """Queue an INSERT of the block ``name`` at ``insert``, scaled by ``scale`` and turned ``rotation`` radians, as INSERT
        places one, in model space or in ``layout``. The block is one the drawing has or one ``blocks.add`` makes in the same
        commit; the commit refuses a name neither has. ``attributes`` fills the attributes of the block, as -INSERT asks for
        them: the words by tag (letters of either case the same), a tag left out keeping the default of its definition, and
        ``{}`` an insert with every attribute at its default; the constant attributes belong to the block and are not made. A
        tag the block lacks, and ``attributes`` for a block with no attribute definition, are a ``ValueError`` that names the
        tags the block has. Left out, the insert has no attribute of its own, as before.

        Example:
            drawing.add_insert("BOLT", (100, 50))                      # an INSERT of BOLT at (100, 50)
            drawing.add_insert("BOLT", (200, 50), scale=2.0, rotation=math.pi / 2)   # twice the size, turned a quarter
            drawing.add_insert("TITLE", (0, 0), layout="Layout1", attributes={"DRAWING": "Plate", "SCALE": "1:10"})
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
        if attributes is not None:
            self._fill_attributes(made, record, label, attributes)
        return self.add(self._made(made, props), layout=layout)

    def _fill_attributes(self, made: Any, record: Any, label: str, values: Mapping[str, str]) -> None:
        """Gives an INSERT the attributes its block defines: one for each definition that is not constant, its words the value
        given for its tag or the definition's own, placed as the INSERT places the block."""
        found: list[Any] | None = None
        for op in reversed(self._ops):
            if op.kind == "block" and op.spec.name.upper() == label.upper():
                found = list(op.spec.entities)
                record.Origin = op.spec.base_point
                break
        if found is None:
            for block in self._backend.blocks:
                if str(block.Name).upper() == label.upper():
                    found = list(block)
                    record.Origin = block.Origin
                    break
        if found is None:
            raise ValueError(f"The block {label} is not in the drawing, nor made in this commit: attributes need its definition.")
        definitions = [item for item in found if isinstance(item, _dxf.DxfAttributeDefinition) and not item.Constant]
        tags = [str(definition.Tag) for definition in definitions]
        given = {str(tag).upper(): (str(tag), str(words)) for tag, words in values.items()}
        for key, (tag, _) in given.items():
            if key not in {t.upper() for t in tags}:
                have = ", ".join(tags) if tags else "none"
                raise ValueError(f"The block {label} has no attribute {tag!r}: its tags are {have}.")
        for definition in definitions:
            attribute = _dxf.DxfAttributeReference()
            attribute.SetAttributeFromBlock(definition, made.BlockTransform)
            entry = given.get(str(definition.Tag).upper())
            if entry is not None:
                attribute.TextString = entry[1]
            made.AttributeCollection.AppendAttribute(attribute)

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
    def layouts(self) -> Layouts:
        """The layouts of the drawing as a table, ``Model`` and then the tabs: iterable (each a ``Layout``, which is its name
        as a ``str`` too), ``len``, ``[index]`` or ``[name]``, ``in``, ``add`` and ``current``. A layout is set up and plotted
        from its own members (``paper``, ``scale``, ``add_viewport``, ``plot_pdf``).

        Example:
            [str(layout) for layout in drawing.layouts]   # ['Model', 'Layout1'] in a new drawing
            drawing.layouts["Layout1"].paper              # 'ISO A3'
        """
        return self._layouts

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
        self._backend.current_layout = str(name)

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

    # ---- rendering: pictures with no window -----------------------------------------------------------------------

    def _render_options(
        self,
        box: Box | tuple[float, float, float, float] | None,
        dpi: float,
        width: int | None,
        height: int | None,
        ground: str,
        layout: str | None,
    ) -> RenderOptions:
        """Check what a render call was given and commit what is queued, as ``save`` does."""
        if ground not in ("black", "white"):
            raise ValueError(f"ground is 'black' or 'white', not {ground!r}.")
        positive("dpi", dpi)
        if box is not None:
            finite("box", *box)
        for what, pixels in (("width", width), ("height", height)):
            if pixels is not None and (isinstance(pixels, bool) or not isinstance(pixels, int)):  # pyright: ignore[reportUnnecessaryIsInstance]
                raise ValueError(f"{what} is a whole number of pixels, not {pixels!r}.")
        self.commit()
        corners = None if box is None else Box(min(box[0], box[2]), min(box[1], box[3]), max(box[0], box[2]), max(box[1], box[3]))
        return RenderOptions(corners, float(dpi), width, height, 0xFFFFFF if ground == "white" else 0, _layout_name(layout))

    def render_png(
        self,
        path: str,
        box: Box | tuple[float, float, float, float] | None = None,
        dpi: float = 96.0,
        width: int | None = None,
        height: int | None = None,
        ground: Literal["black", "white"] = "black",
        layout: str | None = None,
    ) -> None:
        """Draw the drawing to a PNG file with no window, as the canvas of the app draws it: the same renderer, fonts and colours
        (the grid, the UCS icon and the grips, which belong to the canvas, are not drawn), on the app's default settings. What is
        queued is committed first, as ``save`` does. The picture shows the extents of the space with the margin ZOOM Extents
        leaves, or ``box`` (a ``Box`` or a tuple of 4 numbers, in the units of the space) fitted with none. The size is ``width``
        by ``height`` pixels; with one of them given the other follows the aspect of the part; with neither, a layout is its sheet
        at ``dpi`` pixels to the inch (A3, 420 by 297 mm, is 1 587 by 1 123 at 96) and model space is 1 600 pixels along its longer
        side. A picture over 16 384 pixels a side or 100 000 000 pixels in all raises ``ValueError``. ``ground`` is the colour of
        model space (the paper of a layout is white), on which white draws colour 7 black. ``layout`` is a layout's name or
        ``"Model"``; None draws the space the drawing was left in. It works on a drawing of a file (``open_file``, ``new_file``) and on
        a drawing of the app: the app's drawing as it is now, changes not saved included, is read into this process and drawn here,
        and its file is never written. One picture at a time per process.

        Example:
            drawing = new_file()
            drawing.add_line((0, 0), (100, 0))
            drawing.add_circle((50, 50), 25)
            drawing.render_png("plan.png")                # 1 600 by 1 200 pixels, white lines on black
            drawing.render_png("sheet.png", layout="Layout1", dpi=150, ground="white")   # the sheet of the layout at 150 pixels to the inch (an A3 sheet: 2 480 by 1 754)
        """
        options = self._render_options(box, dpi, width, height, ground, layout)
        self._backend.render("png", os.path.abspath(path), options)

    def render_svg(
        self,
        path: str,
        box: Box | tuple[float, float, float, float] | None = None,
        dpi: float = 96.0,
        width: int | None = None,
        height: int | None = None,
        ground: Literal["black", "white"] = "black",
        layout: str | None = None,
    ) -> None:
        """Draw the drawing to an SVG file: the picture ``render_png`` makes, in vectors, its ``width`` and ``height`` the pixels of
        that picture and its ``viewBox`` the same, images inlined as PNG data. It is drawn by the renderer of the canvas, so it keeps
        the canvas's level of detail at that size: an item under half a pixel is one dot, a text under 3 pixels high is left out and
        a hatch whose lines come closer than 2 pixels is a tint. ``render_pdf`` is the form with every detail. The arguments and
        the refusals are those of ``render_png``.

        Example:
            drawing.render_svg("plan.svg", width=800)     # 800 by 600 for the line and circle of render_png
        """
        options = self._render_options(box, dpi, width, height, ground, layout)
        self._backend.render("svg", os.path.abspath(path), options)

    def render_pdf(self, path: str, layout: str | None = None) -> None:
        """Plot a layout, or model space, to a PDF file of one page, as EXPORTPDF does: the page setup of the layout (its sheet,
        scale and plot style), lines at the lineweights of the plot, text as text, and for model space the extents. What is queued
        is committed first. An A3 sheet on its side is 1 190.55 by 841.89 points. A space that was never given a sheet (a drawing
        made in code) is fitted to ISO A3. ``layout`` is as in ``render_png``. A space with nothing to plot raises the .NET
        ``InvalidOperationException``. A drawing of the app is read into this process and plotted here, as in ``render_png``.

        Example:
            drawing.render_pdf("plan.pdf")                # one A3 page of the line and circle, fitted to the sheet
            drawing.render_pdf("sheet.pdf", layout="Layout1")
        """
        self.commit()
        self._backend.render("pdf", os.path.abspath(path), RenderOptions(layout=_layout_name(layout)))

    def render_image(
        self,
        box: Box | tuple[float, float, float, float] | None = None,
        dpi: float = 96.0,
        width: int | None = None,
        height: int | None = None,
        ground: Literal["black", "white"] = "black",
        layout: str | None = None,
    ) -> NDArray[np.uint8]:
        """Draw the drawing and give the picture as a numpy array of shape ``(height, width, 3)``, ``uint8``, red, green and blue,
        rows from the top: the pixels ``render_png`` writes, with no file. The arguments are those of ``render_png``. Needs numpy
        (``pip install opencad2d[numpy]``).

        Example:
            image = drawing.render_image(width=400)       # shape (300, 400, 3) for the line and circle of render_png
            image[150, 200]                               # the red, green and blue of the pixel at row 150, column 200
        """
        options = self._render_options(box, dpi, width, height, ground, layout)
        picture: Any = self._backend.render("image", None, options)
        from .. import arrays

        rgb: IEnumerable[int] = picture.Rgb
        return arrays.to_numpy(rgb).reshape(int(picture.Height), int(picture.Width), 3)

    # ---- labelled renders: the matrix, class and instance masks, COCO (PW7.4) -----------------------------------------

    def render_transform(
        self,
        box: Box | tuple[float, float, float, float] | None = None,
        dpi: float = 96.0,
        width: int | None = None,
        height: int | None = None,
        layout: str | None = None,
    ) -> NDArray[np.float64]:
        """Give the matrix from the drawing to the pixels of the picture ``render_png`` and ``render_image`` make for the same
        arguments, as a numpy array of shape ``(3, 3)``: the very view the renderer draws by, so the point ``(x, y)`` is at pixel
        position ``(m @ [x, y, 1])[:2]``. Pixel ``(column, row)`` is the square from ``(column, row)`` to ``(column + 1, row +
        1)``, row 0 at the top and y of the drawing upward, so the pixel a point falls in is the floor of both numbers. The
        inverse, from pixels to the drawing, is ``numpy.linalg.inv(m)``. On a layout it maps the paper in millimetres. The
        arguments and the refusals are those of ``render_png``. Needs numpy (``pip install opencad2d[numpy]``).

        Example:
            drawing = new_file()
            drawing.add_line((0, 0), (400, 300))
            m = drawing.render_transform()                # the picture is 1 600 by 1 200 pixels with ZOOM Extents' margin
            (m @ [0, 0, 1])[:2]                           # array([  80., 1140.]): the line's start, near the bottom left
            (m @ [400, 300, 1])[:2]                       # array([1520.,   60.]): its end, near the top right
        """
        options = self._render_options(box, dpi, width, height, "black", layout)
        return _matrix(self._backend.render("transform", None, options))

    def render_masks(
        self,
        classes: Sequence[str] = LABEL_CLASSES[:4],
        instances: bool = False,
        box: Box | tuple[float, float, float, float] | None = None,
        dpi: float = 96.0,
        width: int | None = None,
        height: int | None = None,
        layout: str | None = None,
    ) -> RenderMasks:
        """Draw the drawing as labels for training a vision model: for each pixel of the picture ``render_png`` makes for the
        same ``box``, ``dpi``, ``width``, ``height`` and ``layout``, the class of what is drawn on it, and with ``instances`` the
        entity. The renderer is the canvas's, drawing each item in a flat colour that is its id, with no antialiasing, in the
        order the drawing draws them, so what is on top in the picture is on top in the masks; a pixel is one class exactly.
        ``classes`` are names of ``LABEL_CLASSES`` (``"words"`` 1, ``"dimensions"`` 2, ``"lines"`` 3, ``"hatches"`` 4,
        ``"blocks"`` 5), by what the item is and the kind of its entity: letters and everything of a TEXT, MTEXT or attribute
        are words, the lines and arrowheads of a DIMENSION dimensions, the pattern and fill of a HATCH, SOLID, TRACE or
        WIPEOUT hatches, and the rest lines. With ``"blocks"`` among the classes every item of an INSERT is class 5; without it
        a block's pieces are classed by what they are. A pixel of a class not asked for is 0, as is the ground. A text under 3
        pixels high, as in the picture, is left out; the paper of a layout, the frame of a viewport and raster images are not
        labelled. Letters are drawn by the font's monochrome rasteriser, so a letter's pixels can sit a pixel off the antialiased
        picture's. The result is a ``RenderMasks``. Needs numpy; it works on a drawing of the app as ``render_png`` does; the other
        refusals are those of ``render_png``, and an empty ``classes`` or a name that is none of the five a ``ValueError``.

        Example:
            drawing = new_file()
            drawing.add_line((0, 0), (400, 300))
            drawing.add_text("Plate A-12", (50, 200), 30.0)
            masks = drawing.render_masks(instances=True)
            masks.classes.shape                           # (1200, 1600): rows from the top, like render_image
            sorted(set(masks.classes.ravel().tolist()))   # [0, 1, 3]: the ground, the words and the line
            masks.handles                                 # ['100', '101']: the instance k is the object handles[k - 1]
        """
        numbers = _class_numbers(classes)
        options = self._render_options(box, dpi, width, height, "black", layout)
        options.classes = numbers
        labels = self._backend.render("masks", None, options)
        return _masks(labels, instances)

    def to_coco(
        self,
        image_path: str,
        classes: Sequence[str] = LABEL_CLASSES[:4],
        segmentation: bool = False,
        box: Box | tuple[float, float, float, float] | None = None,
        dpi: float = 96.0,
        width: int | None = None,
        height: int | None = None,
        ground: Literal["black", "white"] = "black",
        layout: str | None = None,
        image_id: int = 1,
    ) -> dict[str, Any]:
        """Write the picture of the drawing to ``image_path`` as ``render_png`` does and give its annotations in COCO detection
        format, a ``dict`` ready for ``json.dump``: ``images`` (the one picture: ``id`` ``image_id``, ``file_name`` the name of
        the file, ``width``, ``height``), ``annotations`` and ``categories``. The categories are fixed, ids 1 to 5, ``supercategory``
        ``"drawing"``: words, dimensions, lines, hatches, blocks. There is an annotation for each object that shows a pixel in the
        masks of ``classes`` (``render_masks``) and whose own kind is one of them: the kind is words for a TEXT, MTEXT, ATTRIB or
        ATTDEF, dimensions for a DIMENSION, hatches for a HATCH, SOLID, TRACE or WIPEOUT, blocks for an INSERT (so it has an annotation
        only where ``"blocks"`` is asked for) and lines for any other. Its ``bbox`` is ``[x, y, width, height]`` in whole pixels from
        the top left, the box the drawing takes through ``render_transform`` widened to hold every pixel of the object, clipped to the
        picture; ``area`` the object's pixels in the masks; ``iscrowd`` 0; ``id`` the object's number in ``render_masks(...).handles``
        counted from 1 (an id is the same in every call and unique in one image, so renumber when files are joined). The
        annotations are in the order of those numbers. With ``segmentation`` a words object gets the frames of its words, turned as
        they are drawn, each as the 8 numbers of its corners in pixels, and any other object the four corners of its box. The other
        arguments are those of ``render_png`` and ``render_masks``. Needs numpy; it works on a drawing of the app as ``render_png`` does, the labels and the picture of one moment.

        Example:
            drawing = new_file()
            drawing.add_line((0, 0), (400, 300))
            drawing.add_text("Plate A-12", (50, 200), 30.0)
            coco = drawing.to_coco("plan.png", width=800)   # writes plan.png, 800 by 600
            [category["name"] for category in coco["categories"]]   # ['words', 'dimensions', 'lines', 'hatches', 'blocks']
            [(a["category_id"], a["bbox"]) for a in coco["annotations"]]   # [(3, [40, 30, 720, 540]), (1, [130, 155, 327, 72])]
        """
        numbers = _class_numbers(classes)
        options = self._render_options(box, dpi, width, height, ground, layout)
        options.classes = numbers
        labels = self._backend.render("to_coco", None, options)
        self._backend.render("png", os.path.abspath(image_path), options)
        return _coco(labels, image_path, numbers, segmentation, image_id)

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

    # ---- the export for AI pipelines: flatten, JSON, graph (PW6.3) ------------------------------------------------

    def flatten(self, layout: str | None = None) -> list[dict[str, Any]]:
        """The drawing's objects with every block reference replaced by what it draws, nested blocks and arrays of references
        (MINSERT) included, in world coordinates and millimetres, as a ``list`` of ``dict``, read and not changed. Each has
        ``handle`` (that of the reference in the space, for a piece of a block), ``kind``, ``layer``, ``color``, ``linetype``,
        ``layout``, ``box`` ([min x, min y, max x, max y]), ``path`` (the names of the blocks it came out of, outermost first),
        and for a piece of a block ``indexPath`` and ``matrix`` (4 by 4, block coordinates to world millimetres), then its
        geometry and words. A piece on layer 0 or ByBlock has the look of the reference it is in, as BURST gives it, and a
        circle under a scale unlike along its axes comes out an ellipse, as EXPLODE gives it. ``layout`` is one layout's name
        (``"Model"``), or None for model space and every layout. Not committed: what is queued is not in it.

        Example:
            flat = drawing.flatten()                                  # a block BOLT of a circle and a line, inserted twice, gives 4
            [(e["kind"], e["path"]) for e in flat]                    # [('CIRCLE', ['BOLT']), ('LINE', ['BOLT']), ('CIRCLE', ['BOLT']), ...]
            flat[0]["matrix"][0]                                      # [1.0, 0.0, 0.0, 100.0]: the first INSERT at (100, 100), not turned
        """
        entities: list[dict[str, Any]] = self.to_dict(layout)["entities"]
        return entities

    def to_json(self, layout: str | None = None) -> str:
        """The drawing as one JSON document of schema ``opencad2d.drawing``, version 1 (``docs/schemas``), in world millimetres:
        ``units``, ``layouts``, ``layers``, ``textStyles``, ``dimStyles``, ``linetypes``, ``blocks``, ``report`` and ``entities``
        (those of ``flatten``). The same drawing gives the same text on every run and on every backend: entities in the order
        of their handles as numbers, tables by name, numbers rounded to a nanometre. A drawing of no unit (INSUNITS 0) is left
        as it is and ``units.normalised`` says so. Not committed: what is queued is not in it.

        Example:
            text = drawing.to_json()                                  # a str, 3 MB for 10 000 entities
            json.loads(text)["units"]                                 # {'insunits': 'Inches', ..., 'toMillimetres': 25.4, 'normalised': True}
            drawing.to_json("Layout1")                                # the entities of that layout alone
        """
        text, _ = self._backend.export("json", _layout_name(layout), 0.0)
        return text

    def to_dict(self, layout: str | None = None) -> dict[str, Any]:
        """``to_json`` read: the document as a ``dict``, its entities a ``list`` of ``dict``.

        Example:
            document = drawing.to_dict()
            document["entities"][0]["kind"]                           # 'LINE'
            document["units"]["toMillimetres"]                        # 1.0 for a drawing of millimetres
        """
        return _export_document(self.to_json(layout))

    def to_graph(
        self, layout: str | None = None, tolerance: float = 0.001, max_edges: int | None = None
    ) -> dict[str, Any]:
        """The drawing as a graph for a network to read: a node for each entity of ``flatten`` (``nodes``, numbered as
        ``flatten`` lists them) and an edge for each way two are related (``edges``: ``source``, ``target``, ``kind`` and ``at``):
        ``touches`` (two curves that come within ``tolerance`` millimetres of each other: ends that meet, an end on the other, a crossing), ``contains`` (a closed curve that
        holds the other inside it), ``dimensions`` (a dimension and what its association or its points tie it to) and
        ``annotates`` (a leader and what its arrow points at). With numpy installed the dict also holds ``edge_index`` (an array
        of shape (2, E), int64) and ``edge_kind`` (E, int64; an index into ``["touches", "contains", "dimensions",
        "annotates"]``).  The graph is found with a grid over the boxes: 10 000 entities take about 0.25 s.

        ``max_edges`` caps the edges for a drawing so dense that its crossings would run into the millions: the graph keeps
        the first ``max_edges`` edges of the whole one in its order (kind, source, target, the order ``edges`` is sorted in),
        stops looking once it has more, and says so: ``truncated`` is ``True`` and ``edgesFound`` is how many edges it had
        found when it stopped (at least ``max_edges + 1``). A graph that was not cut has ``truncated`` ``False`` and
        ``edgesFound`` the number of its edges. The same on every backend; ``None`` (the default) has no cap.

        Example:
            graph = drawing.to_graph()                                # a plate with a circle in it and a line from its corner
            [(e["source"], e["target"], e["kind"]) for e in graph["edges"]]   # [(0, 2, 'touches'), (2, 3, 'contains'), ...]
            graph["edge_index"].shape                                 # (2, 5): with numpy
            capped = drawing.to_graph(max_edges=2)                    # the first 2 edges of the 5
            (capped["truncated"], len(capped["edges"]))               # (True, 2): capped["edgesFound"] is at least 3
        """
        if not (0.0 < tolerance < math.inf):
            raise ValueError("The tolerance is a number of millimetres above nought.")
        if max_edges is not None and (type(max_edges) is not int or max_edges < 1):
            raise ValueError(f"max_edges is a number of edges of 1 or more, or None, not {max_edges!r}.")
        text, native = self._backend.export("graph", _layout_name(layout), tolerance, max_edges)
        return _export_graph(text, native)

    # ---- checking a drawing against a company standard (PW7.2) ----------------------------------------------------

    def check(
        self,
        standard: str | os.PathLike[str] | Mapping[str, Any],
        model_scale: float | str | None = None,
        tolerance: float = 0.001,
    ) -> CheckResult:
        """Check the drawing against a company standard and score it, as a ``CheckResult``: a score from 0 to 1 for each rule
        the standard runs (``scores``), a ``total`` (the weighted mean, the weights from the standard), and every
        ``Violation`` with its rule, the object (and the second object where the rule is about two), the place as a box, the
        number found and the number allowed, sorted by rule and then by handle, the same on every run and every backend.
        ``standard`` is a JSON file of the schema ``docs/schemas/opencad2d-standard-v1.schema.json`` (a path) or the same as a
        ``dict``; it lists the layers allowed with their colour, linetype and lineweight, the text and dimension styles with
        their fonts, the heights words may have on paper in millimetres, the least gap on paper between words and lines and
        between words, how dimensions of a chain must line up, and whether an object may sit on layer 0 or have a colour or
        linetype of its own. A rule the standard does not mention is not run. A rule scores ``1 - offenders / subjects`` (the
        objects that offend over those it looked at), 1 where it looked at none.

        Words are measured on paper: in a layout as drawn, in model space through each viewport that shows them at the
        scale of the viewport (an annotative object at the annotation scale the viewport draws it at). Where no layout has a
        viewport, or ``model_scale`` is given, model space is measured on its own at that scale: the paper millimetres of one
        unit of the model, ``0.01`` or ``"1:100"`` for a drawing of millimetres plotted at 1:100; a drawing with neither is
        taken as plotted 1:1. ``tolerance`` is the millimetres on paper within which a gap counts as the one allowed. A
        drawing of 10 000 objects is checked in 0.1 to 0.4 s. Not committed: what is queued is not in it. ``boxes`` needs
        numpy and is None without it.

        Example:
            result = drawing.check({"schema": "opencad2d.standard", "version": 1, "textHeights": {"paper": [2.5, 3.5]}})
            result.total                                  # 0.5 when one of two texts is 3.1 high on paper
            result.scores                                 # {'text-height': 0.5}
            result.violations[0].measured                 # '3.1 mm', with .allowed '2.5, 3.5 mm' and .handle '102'
            drawing.check("company.json", model_scale="1:100").total   # a file, model space plotted at 1:100
        """
        if not (math.isfinite(tolerance) and tolerance >= 0.0):
            raise ValueError(f"The tolerance is a finite number of nought or more, not {tolerance!r}.")
        if isinstance(standard, Mapping):
            text = json.dumps(dict(standard))
        else:
            with open(os.fspath(standard), encoding="utf-8") as stream:
                text = stream.read()
        native = self._backend.check(text, _model_scale(model_scale), float(tolerance))
        try:
            numbers: NDArray[np.float64] | None = _rows(native.Boxes)  # pyright: ignore[reportAny]
        except ImportError:
            numbers = None
        rules = [
            RuleScore(str(r.Rule), float(r.Score), int(r.Subjects), int(r.Offenders), float(r.Weight))  # pyright: ignore[reportAny]
            for r in native.Rules  # pyright: ignore[reportAny]
        ]
        return CheckResult(str(native.Name), float(native.Total), rules, numbers, native)  # pyright: ignore[reportAny]

    # ---- comparing two drawings (PW7.6) ---------------------------------------------------------------------------

    def compare(self, other: Drawing | str | os.PathLike[str], tolerance: float = 0.001) -> Diff:
        """Compare this drawing, the state things changed from, with ``other``, the state after, as a ``Diff``: the objects
        ``other`` has and this drawing has not (``added``), those it has and ``other`` has not (``removed``), and those both
        have that differ (``changed``, each with the names of the properties that differ and, where the box kept its size
        and moved, how far). ``other`` is a ``Drawing`` of any backend (a drawing of a running app is read as a copy of its
        database, as the renders do) or the path of a DXF or DWG file. Objects are matched by handle, and a piece of a block
        by its place in the block, so the two must be two states of one drawing: a file and the file after an edit, a drawing
        and the copy saved before an episode and compared after a ``reset``. Two drawings drawn apart share no handles, and
        every object of one is added and of the other removed. Lengths are millimetres, as the JSON export has them, and
        equal where they differ by no more than ``tolerance``; boxes are as the canvas draws them. Not committed: what is
        queued is in neither. A snapshot is not taken as ``other``: ``save_as`` a copy before the episode and compare with that.
        Two drawings of 10 000 objects are compared in about 0.4 s.

        Example:
            drawing.save_as("before.dxf")                 # a copy of the plan as it is
            drawing.move(drawing.entities("CIRCLE"), (10, 0))
            drawing.commit()
            diff = open_file("before.dxf").compare(drawing)   # the copy is the state before, the plan the state after
            diff.moved[0].moved_by                        # (10.0, 0.0): the circle moved 10 mm along x
            len(diff.added), len(diff.removed)            # (0, 0)
        """
        if isinstance(tolerance, bool) or not (math.isfinite(tolerance) and tolerance >= 0.0):  # pyright: ignore[reportUnnecessaryIsInstance]
            raise ValueError(f"The tolerance is a finite number of nought or more, not {tolerance!r}.")
        if not isinstance(other, (Drawing, str, os.PathLike)):  # pyright: ignore[reportUnnecessaryIsInstance]
            raise TypeError(f"compare takes a Drawing or the path of a DXF or DWG file, not {other!r}.")
        from . import _local  # pyright: ignore[reportPrivateUsage]

        second = other._backend.as_drawn() if isinstance(other, Drawing) else _local.open_as_drawn(other)  # pyright: ignore[reportPrivateUsage]
        native = _local.compare_documents(self._backend.as_drawn(), second, float(tolerance))
        return _diff_of(native)

    # ---- a snapshot and reset of the drawing in memory (PW7.3) ----------------------------------------------------

    def snapshot(self) -> Snapshot:
        """Keep the drawing as it stands, to put it back to with ``reset``: for a program that runs an episode of edits on a
        drawing many times from the same start (training a model, trying layouts) and cannot read the file again each time.
        Taking one costs a few milliseconds (the drawing compressed, about 0.2 MB for 10 000 objects, is held until it is
        released or the drawing is let go). Not committed: what is queued is not in it. ``RuntimeError`` while a command runs
        in the drawing, as a tool does, and while the Block Editor is open.

        Example:
            start = drawing.snapshot()                    # the plan as it is now
            drawing.add_line((0, 0), (100, 0))
            drawing.commit()                              # an episode ...
            drawing.reset(start)                          # ... and the plan is back, the line gone
        """
        return Snapshot(self, self._backend.snapshot())

    def reset(self, snapshot: Snapshot) -> None:
        """Put the drawing back as ``snapshot`` kept it, to the byte: its objects, handles (the next object made gets the
        handle it got the first time), layers, styles, blocks, layouts and the header variables, the current layer and styles
        and the handle seed among them. What is queued and not committed is dropped; an ``Entity`` read before is a copy and
        keeps what it had. The steps made since are forgotten, not made one more step: what was done before the snapshot can
        still be undone, and a thousand resets use no more memory than one. The same snapshot can be reset to again and again.
        The display, the spatial tables and the pictures of dimensions are drawn again only where the episode touched them:
        a reset of 10 000 objects after 100 edits takes about 1 ms and the display after it 1 to 3 ms, where opening the file
        and drawing it takes 20 to 40 ms; the first reset to a snapshot takes about 4 ms, as the next one's drawing is read
        while the episode runs. ``ValueError`` for a snapshot of another drawing or one that was released; ``RuntimeError``
        while a command runs in the drawing.

        Example:
            start = drawing.snapshot()
            for episode in range(1000):
                drawing.add_circle((50, 50), 25)
                drawing.commit()                          # the same handle every time, '102'
                drawing.reset(start)
            len(drawing.entities())                       # as many as when the snapshot was taken
        """
        if not isinstance(snapshot, Snapshot):  # pyright: ignore[reportUnnecessaryIsInstance]
            raise ValueError(f"A snapshot is what drawing.snapshot() gives, not {snapshot!r}.")
        if snapshot.drawing is not self:
            raise ValueError("The snapshot is of another drawing.")
        if snapshot._native is None:  # pyright: ignore[reportPrivateUsage]
            raise ValueError("The snapshot was released.")
        self._discard()
        self._backend.reset(snapshot._native)  # pyright: ignore[reportPrivateUsage]

    def __repr__(self) -> str:
        return f"<Drawing {self.name}>"


# ---- what the export for AI pipelines reads (PW6.3) ------------------------------------------------------------------------

_EDGE_KINDS: tuple[str, ...] = ("touches", "contains", "dimensions", "annotates")
"""The kinds of edge of a graph, in the order of their numbers: ``edge_kind`` of ``to_graph`` holds an index into this."""


def _export_document(text: str) -> dict[str, Any]:
    """The JSON document of a drawing (``to_json``), read."""
    return json.loads(text)  # pyright: ignore[reportAny]


def _export_edges(graph: dict[str, Any], native: Any) -> Any:
    """The edges of a graph as an array of shape (E, 3) of float64: source, target and kind of each, or None without numpy.

    A graph made in this process (``native`` is the ``CadDrawingGraph``) hands its edge array over with one copy through
    ``opencad2d.arrays``; one that came as text from the app is built from its edge list.
    """
    try:
        import numpy as np
    except ImportError:
        return None
    if native is not None:
        from .. import arrays

        edge_array: IEnumerable[float] = native.EdgeArray()
        return arrays.to_numpy(edge_array).reshape(-1, 3)
    edges: list[dict[str, Any]] = graph["edges"]
    flat = [number for edge in edges for number in (edge["source"], edge["target"], _EDGE_KINDS.index(edge["kind"]))]
    return np.asarray(flat, dtype=np.float64).reshape(-1, 3)


def _export_graph(text: str, native: Any) -> dict[str, Any]:
    """A graph as a dict: ``tolerance``, ``nodes``, ``edges`` and, with numpy, ``edge_index`` (2, E) int64 and ``edge_kind`` (E,) int64."""
    result = _export_document(text)
    numbers = _export_edges(result, native)
    if numbers is not None:
        result["edge_index"] = numbers[:, :2].T.astype("int64")
        result["edge_kind"] = numbers[:, 2].astype("int64")
    return result


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


@dataclass(frozen=True, eq=False)
class RenderMasks:
    """The labels ``drawing.render_masks()`` gives: ``classes``, a NumPy array of shape (height, width) and type uint8 of the class
    number of each pixel (0 the ground, 1 words, 2 dimensions, 3 lines, 4 hatches, 5 blocks); ``instances``, with ``instances=True``,
    an int32 array of the same shape of the object of each pixel (0 none, else k for the object ``handles[k - 1]``), else None;
    ``handles``, the handles instance numbers stand for, in the order the space draws the objects (those that draw nothing included,
    then the model objects a layout's viewports show); ``transform``, the (3, 3) matrix of ``render_transform``; and ``names``, the
    name of each class number.

    Example:
        masks = drawing.render_masks(instances=True)
        masks.names                                   # ['background', 'words', 'dimensions', 'lines', 'hatches', 'blocks']
        int((masks.classes == 3).sum()) > 0           # True: the pixels of lines
    """

    classes: NDArray[np.uint8]
    instances: NDArray[np.int32] | None
    handles: list[str]
    transform: NDArray[np.float64]
    names: list[str]


def _class_numbers(classes: Sequence[str]) -> tuple[int, ...]:
    """The class numbers of names of ``LABEL_CLASSES``, in the order of the numbers; ``ValueError`` for an empty list or a name that is none."""
    if isinstance(classes, str) or not classes:  # pyright: ignore[reportUnnecessaryIsInstance]
        raise ValueError(f"classes is a list of names of {list(LABEL_CLASSES)}, not {classes!r}.")
    for name in classes:
        if name not in LABEL_CLASSES:
            raise ValueError(f"A class is one of {list(LABEL_CLASSES)}, not {name!r}.")
    return tuple(sorted({LABEL_CLASSES.index(name) + 1 for name in classes}))


def _bytes(values: Any) -> NDArray[np.uint8]:
    """A .NET array of bytes as a NumPy array."""
    arrays: Any = importlib.import_module("opencad2d.arrays")
    return arrays.to_numpy(values)


def _ints(values: Any) -> NDArray[np.int32]:
    """A .NET array of Int32 as a NumPy array, copied by one block as bytes (``opencad2d.arrays`` takes bytes and doubles)."""
    system: Any = importlib.import_module("System")
    raw = system.Array[system.Byte](4 * len(values))
    system.Buffer.BlockCopy(values, 0, raw, 0, 4 * len(values))
    return _bytes(raw).view("<i4").astype("int32", copy=False)


def _matrix(numbers: Any) -> NDArray[np.float64]:
    """The nine numbers of ``CadRender.Transform`` as a (3, 3) array."""
    arrays: Any = importlib.import_module("opencad2d.arrays")
    return arrays.to_numpy(numbers).reshape(3, 3)


def _masks(labels: Any, instances: bool) -> RenderMasks:
    """The ``RenderMasks`` of the ``CadRenderLabels`` of a call."""
    shape = (int(labels.Height), int(labels.Width))
    return RenderMasks(
        _bytes(labels.Classes).reshape(shape),
        _ints(labels.Instances).reshape(shape) if instances else None,
        [str(handle) for handle in labels.Handles],
        _matrix(labels.Transform),
        ["background", *LABEL_CLASSES],
    )


def _coco(labels: Any, image_path: str, classes: tuple[int, ...], segmentation: bool, image_id: int) -> dict[str, Any]:
    """The COCO dict of the ``CadRenderLabels`` of a call (see ``Drawing.to_coco``)."""
    arrays: Any = importlib.import_module("opencad2d.arrays")
    np: Any = importlib.import_module("numpy")
    kinds = _bytes(labels.Categories)
    areas = _ints(labels.Areas)
    boxes = arrays.to_numpy(labels.Boxes).reshape(-1, 4)
    frames: dict[int, list[list[float]]] = {}
    if segmentation:
        numbers = _ints(labels.FrameInstances)
        corners = arrays.to_numpy(labels.FrameCorners).reshape(-1, 8)
        for number, frame in zip(numbers.tolist(), corners.tolist()):
            frames.setdefault(number, []).append(frame)
    annotations: list[dict[str, Any]] = []
    for k in np.flatnonzero((areas > 0) & np.isin(kinds, classes)).tolist():
        x0, y0, x1, y1 = (int(number) for number in boxes[k])
        annotation: dict[str, Any] = {
            "id": k + 1,
            "image_id": image_id,
            "category_id": int(kinds[k]),
            "bbox": [x0, y0, x1 - x0, y1 - y0],
            "area": float(areas[k]),
            "iscrowd": 0,
        }
        if segmentation:
            rectangle = [float(number) for number in (x0, y0, x1, y0, x1, y1, x0, y1)]
            annotation["segmentation"] = frames.get(k + 1, [rectangle]) if int(kinds[k]) == 1 else [rectangle]
        annotations.append(annotation)
    return {
        "images": [
            {
                "id": image_id,
                "file_name": os.path.basename(image_path),
                "width": int(labels.Width),
                "height": int(labels.Height),
            }
        ],
        "annotations": annotations,
        "categories": [{"id": i + 1, "name": name, "supercategory": "drawing"} for i, name in enumerate(LABEL_CLASSES)],
    }


def _rows(boxes: Any) -> NDArray[np.float64]:
    """A .NET array of doubles, four to a box, as a NumPy array of shape (n, 4)."""
    arrays: Any = importlib.import_module("opencad2d.arrays")
    return arrays.to_numpy(boxes).reshape(-1, 4)


@dataclass(frozen=True, eq=False)
class BoundsTable:
    """The boxes ``drawing.bounds()`` gives: ``handles``, the handle of each object, and ``boxes``, a NumPy array of shape
    (n, 4) whose row ``i`` is (min_x, min_y, max_x, max_y) of the object ``handles[i]``, in the units of the drawing.

    Example:
        table = drawing.bounds()
        len(table)                                    # 2
        dict(zip(table.handles, table.boxes.tolist()))   # {'101': [0.0, 0.0, 100.0, 40.0], '102': [10.0, 20.0, 31.2, 22.5]}
    """

    handles: list[str]
    boxes: NDArray[np.float64]

    def __len__(self) -> int:
        return len(self.handles)


@dataclass(frozen=True, eq=False)
class OverlapTable:
    """The pairs ``drawing.overlaps()`` gives: ``words``, the handle of the object whose words meet ``others``, the handle of the
    object they meet (the object drawn first where both are words), and ``boxes``, a NumPy array of shape (n, 4) whose row
    ``i`` is (min_x, min_y, max_x, max_y) of the places the pair meets. The rows are in the order the space draws the objects.

    Example:
        hits = drawing.overlaps()
        list(zip(hits.words, hits.others))            # [('101', '102')]: the text 101 has the line 102 through it
        hits.boxes[0]                                 # array([20., 20.001, 20., 24.999]): the part of the line inside the frame
    """

    words: list[str]
    others: list[str]
    boxes: NDArray[np.float64]

    def __len__(self) -> int:
        return len(self.words)


# ---- measuring before placing: what the calls give and take (PW7.1) -----------------------------------------------------


@dataclass(frozen=True)
class Frame:
    """A rectangle that may be turned, the frame of a word on the page: its first corner (``x``, ``y``), the one at the foot of
    the start of the line, its ``width`` along the line, its ``height`` across it, and the ``rotation`` in radians the line runs
    at. A word 20 long and 5 high set at (10, 30) and turned a quarter turn has its corners at (10, 30), (10, 50), (5, 50) and
    (5, 30).

    Example:
        frame = Frame(10, 30, 20, 5, math.pi / 2)
        frame.box                                     # Box(min_x=5.0, min_y=30.0, max_x=10.0, max_y=50.0), within rounding
    """

    x: float
    y: float
    width: float
    height: float
    rotation: float = 0.0

    @property
    def corners(self) -> list[tuple[float, float]]:
        """The four corners, counter-clockwise from the first.

        Example:
            Frame(0, 0, 4, 2).corners                     # [(0.0, 0.0), (4.0, 0.0), (4.0, 2.0), (0.0, 2.0)]
        """
        c, s = math.cos(self.rotation), math.sin(self.rotation)
        return [
            (self.x + a * c - b * s, self.y + a * s + b * c)
            for a, b in ((0.0, 0.0), (self.width, 0.0), (self.width, self.height), (0.0, self.height))
        ]

    @property
    def box(self) -> Box:
        """The box that holds the four corners.

        Example:
            Frame(0, 0, 4, 2).box                         # Box(min_x=0.0, min_y=0.0, max_x=4.0, max_y=2.0)
        """
        xs = [x for x, _ in self.corners]
        ys = [y for _, y in self.corners]
        return Box(min(xs), min(ys), max(xs), max(ys))


@dataclass(frozen=True)
class Measurement:
    """What ``drawing.measure_text`` and its kin give: ``box``, the box of everything the thing would draw (``None`` where it draws
    nothing, a text of no words), and ``frames``, the ``Frame`` of each of its words in the order they are drawn: one for a text,
    one for each run of a line of an MTEXT, those of a dimension's words.

    Example:
        m = drawing.measure_text("Plate", (0, 0), 5.0)
        m.box is not None and len(m.frames) == 1      # True
    """

    box: Box | None
    frames: list[Frame]


def _justification(name: str, single: bool) -> Any:
    """The ``DxfAttachmentPoint`` a justify name stands for: for a TEXT the words ``left``, ``center``, ``right`` and ``middle`` as
    well as the codes; for an MTEXT the codes alone. ``ValueError`` for another."""
    codes = {
        "TL": "TopLeft",
        "TC": "TopCenter",
        "TR": "TopRight",
        "ML": "MiddleLeft",
        "MC": "MiddleCenter",
        "MR": "MiddleRight",
        "BL": "BottomLeft",
        "BC": "BottomCenter",
        "BR": "BottomRight",
    }
    words = {"LEFT": "BaseLeft", "CENTER": "BaseCenter", "RIGHT": "BaseRight", "MIDDLE": "MiddleCenter"}
    key = str(name).strip().upper()
    found = codes.get(key) or (words.get(key) if single else None)
    if found is None:
        allowed = [*codes, *(word.lower() for word in words)] if single else list(codes)
        raise ValueError(f"A justification is one of {', '.join(allowed)}, not {name!r}.")
    return getattr(_dxf.DxfAttachmentPoint, found)


def _tolerance(tolerance: float) -> None:
    if not (math.isfinite(tolerance) and tolerance >= 0.0):
        raise ValueError(f"The tolerance is a finite number of nought or more, not {tolerance!r}.")


def _frame_box(box: Any) -> tuple[float, float, float, float]:
    """The four numbers of a ``Box`` or a sequence of four, which must be finite."""
    if not isinstance(box, (tuple, list)) or len(box) != 4:  # pyright: ignore[reportUnknownArgumentType]
        raise ValueError(f"A frame is a Box or four numbers (min_x, min_y, max_x, max_y), not {box!r}.")
    numbers = [float(n) for n in box]  # pyright: ignore[reportUnknownVariableType, reportUnknownArgumentType]
    finite("A number of a frame", *numbers)
    return (numbers[0], numbers[1], numbers[2], numbers[3])


def _frame_of(candidate: Any) -> Frame:
    """The ``Frame`` a candidate stands for: a ``Frame``, a ``Measurement`` (its box), a ``Box`` or four numbers."""
    if isinstance(candidate, Frame):
        place = candidate
    else:
        if isinstance(candidate, Measurement):
            if candidate.box is None:
                raise ValueError("The measurement has no box: the thing measured draws nothing.")
            candidate = candidate.box
        low_x, low_y, high_x, high_y = _frame_box(candidate)
        place = Frame(low_x, low_y, high_x - low_x, high_y - low_y, 0.0)
    finite("A number of a candidate", place.x, place.y, place.width, place.height, place.rotation)
    if not (place.width > 0.0 and place.height > 0.0):
        raise ValueError("A candidate has a width and a height above nought.")
    return place


# ---- what a check of a drawing against a standard gives (PW7.2) --------------------------------------------------------------


def _model_scale(scale: float | str | None) -> float:
    """The paper millimetres of one model unit a ``model_scale`` means: a number as it is, ``"1:100"`` as 0.01, ``"50:1"`` as 50,
    None as 0 (none given)."""
    if scale is None:
        return 0.0
    if isinstance(scale, str):
        paper, colon, model = scale.partition(":")
        try:
            number = float(paper) / float(model) if colon else float(paper)
        except (ValueError, ZeroDivisionError):
            raise ValueError(f"A model scale is a number or a ratio such as '1:100', not {scale!r}.") from None
    else:
        number = float(scale)
    if not (math.isfinite(number) and number > 0.0):
        raise ValueError(f"A model scale is a number above nought or a ratio such as '1:100', not {scale!r}.")
    return number


@dataclass(frozen=True)
class RuleScore:
    """How one rule of a standard fared (``drawing.check().rules``): ``rule``, its id (``"text-height"``, ``"gap-word-line"``,
    ``"layer-name"``, ``"layer-properties"``, ``"layer-zero"``, ``"by-layer-color"``, ``"by-layer-linetype"``,
    ``"by-layer-lineweight"``, ``"text-style"``, ``"text-font"``, ``"dim-style"``, ``"dim-font"``, ``"gap-word-word"`` or
    ``"dim-chain"``), ``score`` from 0 to 1, ``subjects`` (how many things it looked at), ``offenders`` (how many of them
    offend) and ``weight`` (its weight in the total).

    Example:
        score = drawing.check(standard).rules[0]
        (score.rule, score.subjects, score.offenders, score.score)   # ('text-height', 4, 1, 0.75)
    """

    rule: str
    score: float
    subjects: int
    offenders: int
    weight: float


@dataclass(frozen=True)
class Violation:
    """One place a drawing breaks a rule of a standard (``drawing.check().violations``): ``rule``, ``handle`` (the object, or the
    layer or style record of a rule about a table), ``other`` (the second object of a rule about two, a line a word is too
    near or the dimension a chain follows; else None), ``space`` (``"Model"``, a layout, or ``""`` for a record), ``through``
    (the handle of the viewport a model object was measured through, else None), ``box`` ((min_x, min_y, max_x, max_y) in the
    space, or None for a record), ``measured`` and ``allowed`` (sentences: ``'3.1 mm'`` against ``'2.5, 3.5, 5, 7 mm'``) and
    ``value`` and ``limit`` (the number found and the number allowed where the rule is about a number, millimetres on paper,
    else NaN).

    Example:
        found = drawing.check(standard).violations[0]
        (found.rule, found.handle, found.measured, found.allowed)    # ('text-height', '102', '3.1 mm', '2.5, 3.5, 5, 7 mm')
        (found.value, found.limit)                                   # (3.1, 3.5)
    """

    rule: str
    handle: str
    other: str | None
    space: str
    through: str | None
    box: tuple[float, float, float, float] | None
    measured: str
    allowed: str
    value: float
    limit: float


@dataclass(frozen=True, eq=False)
class CheckResult:
    """What ``drawing.check()`` gives: ``name`` (of the standard), ``total`` (0 to 1), ``rules`` (a ``RuleScore`` for each rule
    run), ``violations`` (sorted) and ``boxes``, a NumPy array of shape (n, 4) whose row ``i`` is the box of
    ``violations[i]`` (NaN for a violation with none), or None without numpy. ``scores`` is the score of each rule by id.

    Example:
        result = drawing.check("company.json")
        result.total                                  # 0.82
        result.scores["gap-word-line"]                # 0.6: two of five words are too near a line
        result.boxes[0]                               # array([10., 20., 31.2, 22.5]): where the first violation is
    """

    name: str
    total: float
    rules: list[RuleScore]
    boxes: NDArray[np.float64] | None
    _native: Any = field(repr=False)

    @property
    def scores(self) -> dict[str, float]:
        """The score of each rule the standard runs, by id, in the order of the rules.

        Example:
            result.scores                                 # {'layer-name': 1.0, 'text-height': 0.75}
        """
        return {each.rule: each.score for each in self.rules}

    @cached_property
    def violations(self) -> list[Violation]:
        """The violations, sorted by rule, then by handle, then by the other handle. They are made from the core's answer when first
        asked for, so a reward that reads ``total`` and ``scores`` alone does not pay for the thousands of objects a bad drawing has.

        Example:
            [(v.rule, v.handle) for v in result.violations]   # [('layer-name', '102'), ('text-height', '103')]
        """
        found: list[Violation] = []
        for v in self._native.Violations:  # pyright: ignore[reportAny]
            box = v.Box  # pyright: ignore[reportAny]
            found.append(
                Violation(
                    str(v.Rule),  # pyright: ignore[reportAny]
                    str(v.Handle),  # pyright: ignore[reportAny]
                    None if v.Other is None else str(v.Other),  # pyright: ignore[reportAny]
                    str(v.Space),  # pyright: ignore[reportAny]
                    None if v.Through is None else str(v.Through),  # pyright: ignore[reportAny]
                    None if box is None else (float(box[0]), float(box[1]), float(box[2]), float(box[3])),  # pyright: ignore[reportAny]
                    str(v.Measured),  # pyright: ignore[reportAny]
                    str(v.Allowed),  # pyright: ignore[reportAny]
                    float(v.Value),  # pyright: ignore[reportAny]
                    float(v.Limit),  # pyright: ignore[reportAny]
                )
            )
        return found

    def __len__(self) -> int:
        return int(self._native.Violations.Count)  # pyright: ignore[reportAny]


class Snapshot:
    """A drawing as it stood when ``drawing.snapshot()`` kept it, to ``drawing.reset(snapshot)`` it to. Hold it as long as the
    episodes go on and ``release()`` it after: on a drawing of a file it is let go with the drawing; the app holds one until it
    is released or the drawing is closed.

    Example:
        start = drawing.snapshot()
        start.drawing is drawing                      # True
        drawing.reset(start)                          # as often as wanted
        start.release()                               # the app forgets it; resetting to it is then a ValueError
    """

    def __init__(self, drawing: Drawing, native: Any) -> None:
        self._drawing = drawing
        self._native: Any = native

    @property
    def drawing(self) -> Drawing:
        """The drawing the snapshot is of.

        Example:
            drawing.snapshot().drawing is drawing         # True
        """
        return self._drawing

    def release(self) -> None:
        """Let the snapshot go: the app forgets the drawing it kept, and a drawing of a file lets go of the drawing it read ahead
        for the next reset; one already released is no error.

        Example:
            start = drawing.snapshot()
            start.release()
            drawing.reset(start)                          # ValueError: The snapshot was released.
        """
        native, self._native = self._native, None
        if native is not None:
            self._drawing._backend.release(native)  # pyright: ignore[reportPrivateUsage]

    def __repr__(self) -> str:
        return f"<Snapshot of {self._drawing.name}{'' if self._native is not None else ', released'}>"


# ---- comparing two drawings (PW7.6) ------------------------------------------------------------------------------------------


@dataclass(frozen=True)
class Change:
    """One object of a comparison of two drawings: where it is, what it is, and for a changed one what differs. A block
    reference and the pieces it draws share its handle, so the pieces of a changed block come as several ``Change`` of one
    handle, told apart by ``properties`` and ``box_before``.

    Example:
        change = diff.changed[0]
        change.handle, change.kind                    # ('101', 'LINE')
        change.properties                             # ('box', 'end'): what differs, in alphabetical order
        change.moved_by                               # None: its end moved, so its box changed its size
    """

    handle: str
    """The handle, as in the JSON export: an object's own, or the handle of the block reference a drawn piece belongs to."""
    kind: str
    """The DXF name of the kind, as ``LINE`` or ``MTEXT``."""
    layout: str
    """The layout it is drawn in, or ``Model``; of the first drawing for a removed object, else of the second."""
    layer: str
    """The layer; of the first drawing for a removed object, else of the second."""
    properties: tuple[str, ...]
    """The names of what differs (``layer``, ``color``, ``box``, ``start``, ``text`` ...); empty for an added or removed object."""
    box_before: Box | None
    """The box it took in the first drawing, in millimetres; None for an added object or one that draws nothing."""
    box_after: Box | None
    """The box it takes in the second drawing; None for a removed object or one that draws nothing."""
    moved_by: tuple[float, float] | None
    """How far its box moved, ``(dx, dy)`` in millimetres, where it kept its width and height; None where it did not move, or changed
    its size, or has no box in one of the drawings."""


def _box_of(numbers: Any) -> Box | None:
    if numbers is None:
        return None
    values = [float(n) for n in numbers]
    return Box(values[0], values[1], values[2], values[3])


def _change_of(native: Any) -> Change:
    moved = None if native.MovedBy is None else (float(native.MovedBy[0]), float(native.MovedBy[1]))
    return Change(
        str(native.Handle),
        str(native.Kind),
        str(native.Layout),
        str(native.Layer),
        tuple(str(name) for name in native.Properties),
        _box_of(native.BoxBefore),
        _box_of(native.BoxAfter),
        moved,
    )


class Diff:
    """What ``drawing.compare(other)`` found: the objects only ``other`` has (``added``), those only the drawing has
    (``removed``), and those both have that differ (``changed``), each list in the order of the handles; ``moved`` is the
    changed ones whose box kept its size and moved; ``unchanged`` counts the rest. ``len(diff)`` is the number of objects
    that differ, so ``if diff:`` asks whether the two drawings differ. The boxes come as NumPy arrays too (``None`` without
    numpy): one row of ``[min_x, min_y, max_x, max_y]`` for each ``Change`` of the list, ``nan`` for an object with no box.

    Example:
        diff = before.compare(after)
        (len(diff.added), len(diff.removed), len(diff.changed), diff.unchanged)   # (1, 0, 2, 40)
        diff.is_same                                  # False
        diff.moved_by.shape                           # (1, 2): one object moved
    """

    def __init__(self, added: list[Change], removed: list[Change], changed: list[Change], unchanged: int) -> None:
        self._added = added
        self._removed = removed
        self._changed = changed
        self._unchanged = unchanged

    @property
    def added(self) -> list[Change]:
        """The objects the other drawing has and this one has not.

        Example:
            [(c.handle, c.kind) for c in diff.added]      # [('10A', 'CIRCLE')]
        """
        return list(self._added)

    @property
    def removed(self) -> list[Change]:
        """The objects this drawing has and the other has not.

        Example:
            [(c.handle, c.kind) for c in diff.removed]    # [('101', 'LINE')]
        """
        return list(self._removed)

    @property
    def changed(self) -> list[Change]:
        """The objects both drawings have whose properties differ beyond the tolerance.

        Example:
            [(c.handle, c.properties) for c in diff.changed]   # [('102', ('box', 'center'))]
        """
        return list(self._changed)

    @property
    def moved(self) -> list[Change]:
        """The changed objects whose box kept its width and height and moved: what ``move`` and ``copy`` leave, where ``changed``
        also holds what was resized or recoloured.

        Example:
            [c.moved_by for c in diff.moved]              # [(10.0, 0.0)]
        """
        return [change for change in self._changed if change.moved_by is not None]

    @property
    def unchanged(self) -> int:
        """How many objects both drawings have that do not differ.

        Example:
            diff.unchanged                                # 40
        """
        return self._unchanged

    @property
    def is_same(self) -> bool:
        """Whether the two drawings hold the same objects with the same properties.

        Example:
            drawing.compare(drawing).is_same              # True
        """
        return not (self._added or self._removed or self._changed)

    def __len__(self) -> int:
        return len(self._added) + len(self._removed) + len(self._changed)

    @staticmethod
    def _rows(changes: list[Change], after: bool) -> NDArray[np.float64] | None:
        try:
            import numpy as np
        except ImportError:
            return None
        none = (float("nan"),) * 4
        rows = [(change.box_after if after else change.box_before) or none for change in changes]
        return np.array(rows, dtype=np.float64).reshape(len(rows), 4)

    @property
    def added_boxes(self) -> NDArray[np.float64] | None:
        """The boxes of ``added``, ``(n, 4)`` of ``[min_x, min_y, max_x, max_y]`` as they are drawn; None without numpy.

        Example:
            diff.added_boxes.shape                        # (1, 4)
        """
        return self._rows(self._added, True)

    @property
    def removed_boxes(self) -> NDArray[np.float64] | None:
        """The boxes of ``removed``, as ``added_boxes`` has them: where they were; None without numpy.

        Example:
            diff.removed_boxes.shape                      # (0, 4)
        """
        return self._rows(self._removed, False)

    @property
    def before_boxes(self) -> NDArray[np.float64] | None:
        """The boxes of ``changed`` in this drawing, ``nan`` for an object that draws nothing; None without numpy.

        Example:
            diff.before_boxes.shape                       # (2, 4)
        """
        return self._rows(self._changed, False)

    @property
    def after_boxes(self) -> NDArray[np.float64] | None:
        """The boxes of ``changed`` in the other drawing, row for row with ``before_boxes``; None without numpy.

        Example:
            diff.after_boxes.shape                        # (2, 4)
        """
        return self._rows(self._changed, True)

    @property
    def moved_by(self) -> NDArray[np.float64] | None:
        """How far each of ``moved`` moved, ``(n, 2)`` of ``[dx, dy]`` in millimetres; None without numpy.

        Example:
            diff.moved_by.tolist()                        # [[10.0, 0.0]]
        """
        try:
            import numpy as np
        except ImportError:
            return None
        rows = [change.moved_by for change in self.moved if change.moved_by is not None]
        return np.array(rows, dtype=np.float64).reshape(len(rows), 2)

    def __repr__(self) -> str:
        return (
            f"<Diff {len(self._added)} added, {len(self._removed)} removed, {len(self._changed)} changed, "
            f"{self._unchanged} unchanged>"
        )


def _diff_of(native: Any) -> Diff:
    return Diff(
        [_change_of(c) for c in native.Added],  # pyright: ignore[reportAny]
        [_change_of(c) for c in native.Removed],  # pyright: ignore[reportAny]
        [_change_of(c) for c in native.Changed],  # pyright: ignore[reportAny]
        int(native.Unchanged),
    )


# ---- sheets from code (PW7.7) --------------------------------------------------------------------------------------------------


def _scale_text(scale: float | str) -> str:
    """A scale as the core reads it: ``"1:50"`` as it is, a number of paper millimetres to the drawing unit as decimal digits."""
    if isinstance(scale, str):
        return scale
    if isinstance(scale, bool) or not (math.isfinite(scale) and scale > 0.0):  # pyright: ignore[reportUnnecessaryIsInstance]
        raise ValueError(f"A scale is a number above nought, as 0.02, or text, as '1:50': not {scale!r}.")
    return f"{scale:.15f}".rstrip("0").rstrip(".")


class PlotInfo(NamedTuple):
    """What ``layout.plot_pdf`` wrote: the file, the sheet in millimetres, the scale as ``'1:50'`` and the part of the drawing
    plotted, as the plot laid it on the sheet.

    Example:
        info = drawing.layouts["Layout1"].plot_pdf("sheet.pdf")
        info.sheet                                    # (420.0, 297.0)
        info.scale                                    # '1:1'
    """

    path: str
    sheet: tuple[float, float]
    scale: str | None
    area: Box | None


class Layout(str):
    """A layout of a drawing: its name, as a ``str`` (so it goes where a layout name goes: ``render_png(layout=...)``,
    ``add(..., layout=...)``), and its page setup and what a sheet needs: ``paper``, ``landscape``, ``plot_area``, ``scale``,
    ``plot_style``, ``add_viewport`` and ``plot_pdf``. Members read the drawing each time and a setter writes it at once, as
    PAGESETUP does, one undo step; what is queued is committed first, so that the order of a script is the order of the
    drawing. Sheets are in millimetres. ``drawing.layouts`` gives them.

    Example:
        sheet = drawing.layouts.add("Detail", paper="ISO_A3")
        sheet.paper, sheet.size                       # ('ISO A3', (420.0, 297.0))
        sheet.scale = "1:50"                          # plotted at 1:50; 0.02 is the same
    """

    _drawing: Drawing

    def __new__(cls, drawing: Drawing, name: str) -> Layout:
        made = super().__new__(cls, name)
        made._drawing = drawing
        return made

    def _info(self) -> dict[str, Any]:
        for info in self._drawing._backend.layout_list():  # pyright: ignore[reportPrivateUsage]
            if str(info["name"]).upper() == self.upper():
                return info
        raise KeyError(f"The drawing {self._drawing.name} has no layout {str(self)!r}.")

    def _setup(
        self,
        paper: str | None = None,
        orientation: str | None = None,
        area: str | None = None,
        scale: str | None = None,
        plot_style: str | None = None,
    ) -> None:
        self._drawing.commit()
        self._drawing._backend.layout_setup(self._info()["name"], paper, orientation, area, scale, plot_style)  # pyright: ignore[reportPrivateUsage]

    @property
    def name(self) -> str:
        """The name of the layout, as on its tab: ``Model`` or ``Layout1``.

        Example:
            drawing.layouts[1].name                       # 'Layout1'
        """
        return str(self._info()["name"])

    @property
    def is_model(self) -> bool:
        """Whether this is model space, which has a page setup too but no sheet to put viewports on.

        Example:
            drawing.layouts["Model"].is_model             # True
        """
        return bool(self._info()["model"])

    @property
    def is_current(self) -> bool:
        """Whether this is the layout the drawing shows; ``drawing.layouts.current = layout`` shows another.

        Example:
            drawing.layouts["Model"].is_current           # True in a new drawing
        """
        return self._drawing.current_layout.upper() == self.upper()

    @property
    def paper(self) -> str:
        """The sheet as the plotter names it, ``'ISO A3'``; set it with the name as ``'ISO_A3'``, ``'ISO A3'`` or ``'A3'`` (ISO A0
        to A4, ANSI A to E). ``ValueError`` names the sheets there are for another.

        Example:
            sheet = drawing.layouts["Layout1"]
            sheet.paper                                   # 'ISO A3'
            sheet.paper = "A2"                            # the sheet becomes 594 by 420
        """
        return str(self._info()["paper"])

    @paper.setter
    def paper(self, value: str) -> None:
        self._setup(paper=str(value))

    @property
    def size(self) -> tuple[float, float]:
        """The sheet as it is plotted, ``(width, height)`` in millimetres: ``(420.0, 297.0)`` for A3 on its side.

        Example:
            drawing.layouts["Layout1"].size               # (420.0, 297.0)
        """
        info = self._info()
        return (float(info["width"]), float(info["height"]))

    @property
    def landscape(self) -> bool:
        """Whether the long side of the sheet runs across; false turns the sheet on its end.

        Example:
            sheet = drawing.layouts["Layout1"]
            sheet.landscape = False                       # a sheet 297 wide and 420 high
            sheet.size                                    # (297.0, 420.0)
        """
        return bool(self._info()["landscape"])

    @landscape.setter
    def landscape(self, value: bool) -> None:
        self._setup(orientation="landscape" if value else "portrait")

    @property
    def units(self) -> str:
        """The unit of the sheet: ``'mm'``, as a sheet made here and by the app is; ``'in'`` for a drawing of another program.

        Example:
            drawing.layouts["Layout1"].units              # 'mm'
        """
        return str(self._info()["units"])

    @property
    def plot_area(self) -> str:
        """What is plotted: ``'layout'`` (the sheet), ``'extents'``, ``'display'`` or ``'limits'``; ``'window'`` and ``'view'`` are read but
        not set from code. Setting it takes one of the first four.

        Example:
            drawing.layouts["Layout1"].plot_area          # 'layout'
        """
        return str(self._info()["area"])

    @plot_area.setter
    def plot_area(self, value: str) -> None:
        self._setup(area=str(value))

    @property
    def scale(self) -> str:
        """The plot scale as ``'1:50'`` or ``'2:1'`` (paper to drawing), or ``'fit'`` where the plot is fitted to the sheet. Set
        it with the same text, or the paper millimetres to a drawing unit: ``0.02`` is ``'1:50'``.

        Example:
            sheet = drawing.layouts["Layout1"]
            sheet.scale = 0.02
            sheet.scale                                   # '1:50'
            sheet.scale = "fit"
        """
        text = self._info()["scale"]
        return "fit" if text is None else str(text)

    @scale.setter
    def scale(self, value: float | str) -> None:
        self._setup(scale=_scale_text(value))

    @property
    def plot_style(self) -> str:
        """The plot style table by the name of its file, ``'monochrome.ctb'``; empty for none.

        Example:
            sheet = drawing.layouts["Layout1"]
            sheet.plot_style = "acad.ctb"
            sheet.plot_style                              # 'acad.ctb'
        """
        return str(self._info()["plotStyle"])

    @plot_style.setter
    def plot_style(self, value: str) -> None:
        self._setup(plot_style=str(value))

    @property
    def viewports(self) -> int:
        """How many viewports show model space on the sheet; the one of the sheet itself is not counted.

        Example:
            drawing.layouts["Layout1"].viewports          # 0 in a new drawing
        """
        return int(self._info()["viewports"])

    def add_viewport(
        self,
        frame: Box | Sequence[float],
        scale: float | str,
        center: Sequence[float] | None = None,
        frozen_layers: Sequence[str] = (),
    ) -> str:
        """Add a viewport that shows model space at ``scale`` and return its handle. ``frame`` is its rectangle on the sheet,
        ``[x1, y1, x2, y2]`` in millimetres; ``scale`` is ``'1:10'`` or paper millimetres to the unit (``0.1``), so a frame 200 by
        100 at ``'1:10'`` shows 2 000 by 1 000 units. ``center`` is the point of model space at the middle of the frame; left
        out, the middle of what model space draws. ``frozen_layers`` are frozen in this viewport only. What is queued is
        committed first, so model space is as the script has drawn it. One undo step; ``ValueError`` for model space, a
        frame with no width or height, a scale that is not above nought and a layer the drawing lacks.

        Example:
            sheet = drawing.layouts["Layout1"]
            top = sheet.add_viewport((20, 120, 220, 280), "1:10", center=(50, 30))   # the handle, as '10A'
            sheet.viewports                               # 1
            sheet.add_viewport((240, 120, 400, 280), 0.2, frozen_layers=["Notes"])   # 1:5, without the layer Notes
        """
        corners = tuple(float(n) for n in frame)
        if len(corners) != 4 or not all(math.isfinite(n) for n in corners):
            raise ValueError("A frame is four finite numbers, [x1, y1, x2, y2] in millimetres.")
        middle: tuple[float, float] | None = None
        if center is not None:
            point = tuple(float(n) for n in center)
            if len(point) != 2 or not all(math.isfinite(n) for n in point):
                raise ValueError("The centre is two finite numbers, [x, y].")
            middle = (point[0], point[1])
        if isinstance(frozen_layers, str):
            raise ValueError("frozen_layers is a list of layer names, not one text.")
        names = [str(name) for name in frozen_layers]
        text = _scale_text(scale)
        self._drawing.commit()
        return self._drawing._backend.layout_viewport(  # pyright: ignore[reportPrivateUsage]
            self._info()["name"], (corners[0], corners[1], corners[2], corners[3]), text, middle, names
        )

    def plot_pdf(
        self,
        path: str | os.PathLike[str],
        paper: str | None = None,
        scale: float | str | None = None,
        fit: bool = False,
    ) -> PlotInfo:
        """Plot the layout to a PDF file of one page through the plot pipeline, as PLOT to the PDF plotter does: the page setup of
        the layout (its sheet and scale), lines at their lineweights, the plot style table, text as text. The text of an SHX
        font is also written as invisible text over its strokes, so that a reader's search finds the words. ``paper``,
        ``scale`` and ``fit`` change the setup for this plot only and are not written into the drawing: ``paper`` is a sheet as
        in the property (the plot is then fitted to it unless a ``scale`` is given), ``scale`` as ``'1:10'`` or ``0.1``, ``fit``
        fills the sheet; ``ValueError`` for a ``scale`` with ``fit``. A drawing of the app is read as a copy and plotted in this
        process, as the renders do. What is queued is committed first. ``RuntimeError`` where there is nothing to plot (model
        space with nothing in it). An A3 sheet on its side is 1 190.55 by 841.89 points.

        Example:
            sheet = drawing.layouts["Layout1"]
            sheet.scale = "1:50"
            info = sheet.plot_pdf("sheet.pdf")            # one A3 page at 1:50
            info.sheet, info.scale                        # ((420.0, 297.0), '1:50')
            sheet.plot_pdf("small.pdf", paper="A4")       # fitted to A4 for this plot; the layout itself stays A3
        """
        if scale is not None and fit:
            raise ValueError("Give a scale or fit, not both: a scale is how big the drawing is plotted.")
        text = None if scale is None else _scale_text(scale)
        self._drawing.commit()
        page = self._drawing._backend.plot_pdf(  # pyright: ignore[reportPrivateUsage]
            self._info()["name"], os.path.abspath(path), paper, text, bool(fit)
        )
        area = page.Area
        return PlotInfo(
            os.path.abspath(path),
            (float(page.SheetWidth), float(page.SheetHeight)),
            _page_scale(page),
            None if bool(area.IsEmpty) else Box(float(area.MinX), float(area.MinY), float(area.MaxX), float(area.MaxY)),
        )

    def __repr__(self) -> str:
        return f"<Layout {str(self)}>"


def _page_scale(page: Any) -> str | None:
    """The scale a plot page has as ``'1:50'``; None where it has none."""
    text: Any = importlib.import_module("OpenCAD2D").CadLayouts.ScaleText(float(page.Scale))
    return None if text is None else str(text)


class Layouts:
    """The layouts of a drawing, ``Model`` first and then the tabs in their order: iterable (each a ``Layout``), ``len``,
    ``[index]``, ``[name]`` (ignoring case, ``KeyError`` when none), ``in``, ``add`` and ``current``. Each reading asks the
    drawing. A ``Layout`` is its own name, so ``drawing.layouts[1]`` still goes where a layout name does::

        sheet = drawing.layouts.add("Plate A3", paper="ISO_A3")
        sheet.add_viewport((10, 10, 410, 287), "1:10")
        sheet.plot_pdf("plate.pdf")

    Example:
        [str(layout) for layout in drawing.layouts]   # ['Model', 'Layout1'] in a new drawing
        "layout1" in drawing.layouts                  # True
        drawing.layouts.current                       # 'Model'
    """

    def __init__(self, drawing: Drawing) -> None:
        self._drawing = drawing

    def _read(self) -> list[Layout]:
        return [Layout(self._drawing, str(name)) for name in self._drawing._backend.layouts]  # pyright: ignore[reportPrivateUsage]

    def __iter__(self) -> Iterator[Layout]:
        return iter(self._read())

    def __len__(self) -> int:
        return len(self._read())

    def __contains__(self, name: object) -> bool:
        return isinstance(name, str) and any(layout.upper() == name.upper() for layout in self._read())

    def __getitem__(self, name: int | str) -> Layout:
        layouts = self._read()
        if isinstance(name, int):
            return layouts[name]
        for layout in layouts:
            if layout.upper() == name.upper():
                return layout
        raise KeyError(f"The drawing {self._drawing.name} has no layout {name!r}.")

    def __repr__(self) -> str:
        return repr([str(layout) for layout in self._read()])

    @property
    def current(self) -> Layout:
        """The layout the drawing shows; setting it, with a ``Layout`` or a name, shows that layout at once.

        Example:
            drawing.layouts.current = "Layout1"           # Layout1 is shown
            drawing.layouts.current.is_current            # True
        """
        return self[self._drawing.current_layout]

    @current.setter
    def current(self, value: str) -> None:
        self._drawing.current_layout = str(value)

    def add(self, name: str, paper: str = "ISO_A3", units: str = "mm") -> Layout:
        """Add a layout on a sheet, plotted at 1:1, as LAYOUT New and the page setup do, and return it; it does not become the
        layout shown (``current``). ``paper`` is a sheet as in ``Layout.paper``; ``units`` is ``"mm"``, the unit of the sheets the
        page setup writes (inches are a ``ValueError``). What is queued is committed first. One undo step. ``ValueError`` for
        a name that is empty, ``Model``, one the drawing has (either case), over 255 characters or with one of the characters
        AutoCAD keeps out of layout names (``< > / \\ " : ; ? * | , =`` and the grave accent), and for a sheet the plotter lacks.

        Example:
            sheet = drawing.layouts.add("Plate A3", paper="ISO_A3")
            sheet.name, sheet.paper, sheet.scale          # ('Plate A3', 'ISO A3', '1:1')
        """
        if not isinstance(name, str) or not name.strip():  # pyright: ignore[reportUnnecessaryIsInstance]
            raise ValueError(f"A layout needs a name, not {name!r}.")
        if str(units).strip().lower() not in ("mm", "millimetres", "millimeters"):
            raise ValueError(f"A sheet is in millimetres, as the page setup writes it: units is 'mm', not {units!r}.")
        self._drawing.commit()
        made = self._drawing._backend.layout_add(name.strip(), str(paper), True)  # pyright: ignore[reportPrivateUsage]
        return Layout(self._drawing, str(made["name"]))
