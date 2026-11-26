"""Python in OpenCAD2D: commands, prompts, the drawing, and driving the app, from a Python tool or from outside.

Import it anywhere. A Python tool is a folder with a ``requirements.txt``, an ``__init__.py`` that defines commands with
``@command`` and a ``plugin.json`` that names them; the app starts the tool in a ``python.exe`` of its own at its first command, and
the functions that talk to the app (``get_point`` and the other prompts, ``cmd``, ``write``, ``active_drawing``, ``documents``)
are requests of the command that is running. They raise ``RuntimeError`` in any other process; ``open_file`` and ``new_file`` work
on a drawing file with no app, and ``connect`` drives a running app from outside. Esc at a prompt raises ``Cancelled``, which a
bare ``except Exception`` does not swallow. Units are the drawing's; nothing is converted. The ``plugin.json`` of a tool may also
declare object snaps, which ``@snap`` binds to a finder, and settings, which ``setting`` and ``set_setting`` read and write.

    from opencad2d.cad import active_drawing, command, get_distance, get_point

    @command("RING", alias="RG", description="Draws a circle of the radius asked")
    def ring() -> None:
        centre = get_point("Specify centre")
        radius = get_distance("Specify radius", base=centre)
        drawing = active_drawing()
        drawing.add_circle(centre, radius)
        drawing.commit()
"""

from __future__ import annotations

import decimal
import importlib
import math
from collections.abc import Callable, Sequence
from typing import TYPE_CHECKING, Any, TypeVar, cast

from . import _tool

if TYPE_CHECKING:
    from GeometryHelper.Dxf import DxfEntity
    from GeometryHelper.Geometry import GeoPoint2

F = TypeVar("F", bound=Callable[..., object])
S = TypeVar("S", bound=Callable[["SnapRequest"], object])

_net: Any = importlib.import_module("System")
_geo: Any = importlib.import_module("GeometryHelper.Geometry")

PAUSE: str = "\\"
"""The answer that hands one prompt of a command fed by ``cmd`` to the user: ``cmd("CIRCLE", PAUSE, 50)`` lets the user pick
the centre and gives the radius 50. It is one backslash, as AutoLISP's ``pause`` is. Esc at that prompt raises ``Cancelled``."""


class Cancelled(KeyboardInterrupt):
    """Esc at a prompt, or a prompt asked after it: the run is cancelled and the drawing is as before it.

    Example:
        try:
            get_point("Specify centre")      # the user presses Esc
        except Cancelled:                    # not an Exception: a bare 'except Exception' does not catch it
            write("cancelled")               # the run ends cancelled and the drawing is as before it
    """


def _need(name: str) -> None:
    """Raises ``RuntimeError`` naming the function where this process is no Python tool of OpenCAD2D."""
    if not _tool.in_tool():
        raise RuntimeError(f"{name} works inside OpenCAD2D only")


def write(text: str) -> None:
    """Write one line or more on the command line: ``write("12 circles drawn")``.

    Example:
        write("12 circles drawn")            # one line on the command line, as AutoLISP's princ does
    """
    _need("write")
    _tool.write(text)


def _kind(answer: Any) -> str:
    return str(answer.Kind.ToString())


def _as_point(text: str) -> Any:
    parts = text.split(",")
    try:
        return _geo.GeoPoint2(float(parts[0]), float(parts[1])) if len(parts) == 2 else text
    except ValueError:
        return text


def get_point(
    message: str = "Specify point",
    base: GeoPoint2 | None = None,
    keywords: Sequence[str] | None = None,
    default: str | None = None,
) -> GeoPoint2 | str | None:
    """Ask for a point, picked or typed; return a keyword as the prompt writes it, the default on Enter, else None.

        centre = get_point("Specify centre")
        corner = get_point("Specify other corner", base=centre, keywords=["Undo"])

    Example:
        centre = get_point("Specify centre")                  # the user picks (10, 20): GeoPoint2, centre.X == 10.0
        corner = get_point("Other corner", base=centre, keywords=["Close", "Undo"])   # the user types c: 'Close'
    """
    _need("get_point")
    answer = _tool.ask("get_point", "Point", message, base, keywords, default)
    kind = _kind(answer)
    if kind == "Point":
        return _tool.point_of(answer)
    if kind == "Keyword":
        return str(answer.Text)
    return None if default is None else _as_point(default)


def _number_or(answer: Any, default: str | None, convert: Callable[[float], float]) -> float | str | None:
    kind = _kind(answer)
    if kind == "Number":
        return float(answer.Number)
    if kind == "Keyword":
        return str(answer.Text)
    if default is None:
        return None
    try:
        return convert(float(default))
    except ValueError:
        return default


def get_distance(
    message: str = "Specify distance",
    base: GeoPoint2 | None = None,
    keywords: Sequence[str] | None = None,
    default: str | None = None,
) -> float | str | None:
    """Ask for a distance, typed or as two points; return a keyword as the prompt writes it, the default on Enter, else None.

        radius = get_distance("Specify radius", base=centre)   # the user types 50 or picks a point 50 mm away

    Example:
        radius = get_distance("Specify radius", base=centre)  # the user types 5: 5.0; a second point 5 away gives 5.0 too
    """
    _need("get_distance")
    return _number_or(_tool.ask("get_distance", "Distance", message, base, keywords, default), default, float)


def get_angle(
    message: str = "Specify angle",
    base: GeoPoint2 | None = None,
    keywords: Sequence[str] | None = None,
    default: str | None = None,
) -> float | str | None:
    """Ask for an angle, typed in degrees or as a direction; return radians, a keyword, the default (degrees, as the prompt shows it) on Enter, else None.

        turn = get_angle("Specify rotation")   # the user types 90 and gets 1.5707963267948966

    Example:
        turn = get_angle("Specify rotation")                  # the user types 90: 1.5707963267948966 (radians)
    """
    _need("get_angle")
    return _number_or(_tool.ask("get_angle", "Angle", message, base, keywords, default), default, math.radians)


def get_integer(
    message: str = "Enter an integer", keywords: Sequence[str] | None = None, default: int | None = None
) -> int | str | None:
    """Ask for a whole number, asking again after text that is none; return a keyword, the default on Enter, else None.

        count = get_integer("How many", default=3)

    Example:
        count = get_integer("How many", default=3)            # the user types 7: 7; Enter gives the default, 3
    """
    _need("get_integer")
    answer = _tool.ask("get_integer", "Integer", message, None, keywords, None if default is None else str(default))
    kind = _kind(answer)
    if kind == "Number":
        return int(answer.Number)
    return str(answer.Text) if kind == "Keyword" else default


def get_real(
    message: str = "Enter a number", keywords: Sequence[str] | None = None, default: float | None = None
) -> float | str | None:
    """Ask for a number, asking again after text that is none; return a keyword, the default on Enter, else None.

        factor = get_real("Scale factor", ["Auto"], 1.0)

    Example:
        factor = get_real("Scale factor", ["Auto"], 1.0)      # the user types 12.5: 12.5; "Auto" gives 'Auto'; Enter gives 1.0
    """
    _need("get_real")
    answer = _tool.ask("get_real", "Real", message, None, keywords, None if default is None else str(default))
    kind = _kind(answer)
    if kind == "Number":
        return float(answer.Number)
    return str(answer.Text) if kind == "Keyword" else default


def get_string(message: str = "Enter text", default: str | None = None, spaces: bool = True) -> str | None:
    """Ask for a text; with ``spaces`` false it ends at the first space. Return the default on Enter, else None.

        name = get_string("Name of the sheet", "A1", spaces=False)

    Example:
        name = get_string("Name")                      # the user types two words: 'two words'; Enter gives None
        name = get_string("Name", "Anon")              # Enter gives 'Anon'
    """
    _need("get_string")
    answer = _tool.ask("get_string", "String", message, None, None, default)
    if _kind(answer) != "Text":
        return default
    text = str(answer.Text)
    return text if spaces or not text.strip() else text.split(None, 1)[0]


def get_keyword(message: str, keywords: Sequence[str], default: str | None = None) -> str | None:
    """Ask for one of the keywords; return it as the prompt writes it, or None on Enter with no default.

        mode = get_keyword("Mode", ["Yes", "No"], "No")

    Example:
        mode = get_keyword("Mode", ["Yes", "No"], "No")       # the user types n: 'No'; Enter gives the default, 'No'
        mode = get_keyword("Mode", ["Yes", "No"])             # Enter gives None
    """
    _need("get_keyword")
    answer = _tool.ask("get_keyword", "Keyword", message, None, keywords, default)
    return str(answer.Text) if _kind(answer) == "Keyword" else None


def get_entity(message: str = "Select object", keywords: Sequence[str] | None = None) -> Entity | str | None:
    """Ask for one object; return its ``Entity``, a keyword as the prompt writes it, or None on Enter.

        line = get_entity("Select a line")
        if line is not None:
            print(line.handle, line.dxf_type)   # 2B LINE

    Example:
        line = get_entity("Select a line")                    # the user picks it: an Entity, line.dxf_type == "LINE"
        if line is not None:                                   # Enter gives None
            line.layer = "Steel"
    """
    _need("get_entity")
    answer = _tool.ask("get_entity", "Entity", message, None, keywords, None)
    kind = _kind(answer)
    if kind == "Selection" and answer.Handles.Count > 0:
        return _tool.entity_of(str(answer.Handles[0]))
    return str(answer.Text) if kind == "Keyword" else None


def get_selection() -> list[Entity]:
    """Ask "Select objects:" until Enter; return the ``Entity`` objects, or an empty list where none was selected.

    In a command made with ``@command(..., uses_selection=True)`` it returns the objects selected before the command started
    without asking, as MOVE does, and asks only when none were.

        entities = get_selection()
        print(len(entities), "selected")   # 3 selected

    Example:
        entities = get_selection()                             # the user selects three objects and presses Enter
        write(f"{len(entities)} selected")                     # 3 selected
    """
    _need("get_selection")
    answer = _tool.ask("get_selection", "Selection", "Select objects", None, None, None)
    return [_tool.entity_of(str(handle)) for handle in answer.Handles]


def _number_text(value: float) -> str:
    text = repr(float(value))
    if "inf" in text or "nan" in text:
        raise ValueError(f"{value} cannot be typed at a prompt.")
    return format(decimal.Decimal(text), "f") if "e" in text else text


def _answer(value: object) -> Any:
    """Turns one answer of ``cmd`` into what the host feeds."""
    if isinstance(value, str):
        return value
    if isinstance(value, bool):
        raise TypeError("cmd takes text, numbers, points and lists of handles, not a bool.")
    if isinstance(value, int):
        return str(value)
    if isinstance(value, float):
        return _number_text(value)
    if isinstance(value, tuple):
        numbers: tuple[float, ...] = tuple(value)  # pyright: ignore[reportUnknownVariableType, reportUnknownArgumentType]
        if len(numbers) == 2:
            return _geo.GeoPoint2(float(numbers[0]), float(numbers[1]))
        if len(numbers) == 3:
            return f"{_number_text(numbers[0])},{_number_text(numbers[1])}"
        raise ValueError("A point is (x, y) or (x, y, z).")
    if isinstance(value, list):
        handles = cast(list[Any], value)
        return _net.Array[_net.String]([str(getattr(handle, "handle", handle)) for handle in handles])
    if isinstance(value, _geo.GeoPoint3):
        point: Any = value
        return f"{_number_text(point.X)},{_number_text(point.Y)}"
    return value


def cmd(*answers: object) -> None:
    """Run a command to its end inside the run's undo step; the first answer is its name, the others are fed in order.

    An empty text is Enter, a text is typed, a number is typed, a ``GeoPoint2`` or ``(x, y)`` is a point picked, a
    ``GeoPoint3`` or ``(x, y, z)`` is typed as ``x,y``, and a list of handles (or of entities) answers "Select objects:".
    ``PAUSE`` hands that one prompt to the user. A command that still asks after the last answer is cancelled and
    ``RuntimeError`` is raised.

        cmd("CIRCLE", (10, 20), 50)        # a circle of radius 50 at 10,20
        cmd("CIRCLE", PAUSE, 50)           # the user picks the centre

    Example:
        cmd("CIRCLE", (10, 20), 50)           # a circle of radius 50 about (10, 20), inside the run's undo step
        cmd("CIRCLE", PAUSE, 50)              # the user picks the centre; the radius is 50
    """
    _need("cmd")
    _tool.cmd([_answer(answer) for answer in answers])


def command(
    name: str,
    *,
    alias: str | None = None,
    description: str | None = None,
    uses_selection: bool = False,
) -> Callable[[F], F]:
    """Make the decorated function a command of the tool, which the tool's ``plugin.json`` names too.

    The command is typed at the command line (or started from a ribbon button) by ``name`` or by ``alias``; it runs as one
    undo step named after it. Outside a tool process the decorator changes nothing, so the module can be imported and checked. ``uses_selection=True`` makes ``get_selection()`` return the objects selected before the
    command started, without asking.

        @command("RING", alias="RG", description="Draws a circle", uses_selection=False)
        def ring() -> None:
            ...

    Example:
        @command("RECTC", alias="RC", description="A rectangle about a centre")
        def rectangle_by_centre() -> None:
            centre = get_point("Specify centre")      # RECTC, then centre (500, 300), width 200, height 100
            ...                                       # draws one closed polyline of 20 000 square units, one undo step
    """
    if not name or any(character.isspace() for character in name):
        raise ValueError(f"A command name is one word, not {name!r}.")

    def decorate(function: F) -> F:
        if _tool.runtime is not None:
            _tool.runtime.commands.append(_tool.Command(name, alias, description, function, uses_selection))
        return function

    return decorate


def on_unload(function: F) -> F:
    """Run the decorated function when the tool is unloaded, loaded again or the app closes: the twin of a plugin's
    ``Terminate``. An error in it is one line on the command line and the unload goes on.

        @on_unload
        def bye() -> None:
            print("the tool is gone")

    Example:
        @on_unload
        def bye() -> None:
            write("the tool is gone")       # written when the tool is unloaded, loaded again, or the app closes
    """
    if _tool.runtime is not None:
        _tool.runtime.unloaders.append(function)
    return function


class SnapRequest:
    """What a snap finder is given: the entity, its handle and the block references it is in.

    The app builds it for each entity it asks about; a tool reads it and does not make one. A finder gets nothing of the
    cursor, so a point it gives is the same wherever the cursor is.

    Example:
        @snap("CTOP")
        def circle_top(request: SnapRequest) -> list[GeoPoint2]:
            c = request.entity                     # a DxfCircle of radius 25 about (100, 50)
            return [GeoPoint2(c.Center.X, c.Center.Y + c.Radius)]   # (100, 75)
    """

    def __init__(self, entity: DxfEntity, handle: str, block_references: tuple[str, ...]) -> None:
        self._entity = entity
        self._handle = handle
        self._block_references = block_references

    @property
    def entity(self) -> DxfEntity:
        """The entity, a ``GeometryHelper.Dxf`` entity. Inside a block it is the copy placed where the references draw it.

        Example:
            request.entity.Radius      # 20.0 for a circle of radius 10 in a block inserted at scale 2
        """
        return self._entity

    @property
    def handle(self) -> str:
        """The entity's own handle, in its block for an entity in a block.

        Example:
            request.handle             # '2F'
        """
        return self._handle

    @property
    def block_references(self) -> tuple[str, ...]:
        """The handles of the block references the entity is in, outermost first; empty for an entity of the space.

        Example:
            request.block_references   # ('2B', '3C'): in the block drawn by 3C, itself in the block drawn by 2B
        """
        return self._block_references


def snap(keyword: str) -> Callable[[S], S]:
    """Make the decorated function the finder of the snap the tool's ``plugin.json`` declares with that keyword.

    The function takes a ``SnapRequest`` and returns the points (``GeoPoint2``, ``GeoPoint3`` with Z dropped, or ``(x, y)``)
    as a list, or None for none, in world coordinates of the space shown. It runs on a thread of its own, may run while a
    command runs, and is asked once for each entity until the drawing changes; it must only read what it is given, so
    ``get_point`` and the other functions of a command raise ``RuntimeError`` in it. More than 1000 points for one
    entity, or a point that is not finite, is an error of that entity. The keyword is one word, kept in capitals. Outside a
    tool process the decorator changes nothing.

        @snap("CTOP")
        def circle_top(request: SnapRequest) -> list[GeoPoint2]:
            ...

    Example:
        @snap("CTOP")                                 # plugin.json: "snaps": [{"keyword": "CTOP", "name": "Circle top"}]
        def circle_top(request: SnapRequest) -> list[GeoPoint2]:
            c = request.entity                        # a circle of radius 25 about (100, 50)
            return [GeoPoint2(c.Center.X, c.Center.Y + c.Radius)]   # (100, 75) is the point CTOP snaps to
    """
    if not keyword or any(character.isspace() for character in keyword):
        raise ValueError(f"A snap keyword is one word, not {keyword!r}.")
    wanted = keyword.upper()

    def decorate(function: S) -> S:
        if _tool.runtime is not None:
            _tool.runtime.snaps[wanted] = function
        return function

    return decorate


def setting(name: str) -> float | int | bool | str | None:
    """Read a setting the tool's ``plugin.json`` declares: the value kept in the settings file, else its default.

    It works at import, in a command and in a snap finder. ``ValueError`` names a setting the tool does not declare, and
    ``RuntimeError`` is raised in a process that is no tool.

        height = setting("textHeight")

    Example:
        height = setting("textHeight")        # 2.5 until the user changes it in Load/Unload Applications, Settings...
    """
    _need("setting")
    return cast("float | int | bool | str | None", _tool.get_setting(name))


def set_setting(name: str, value: float | int | bool | str | None) -> None:
    """Set a setting the tool's ``plugin.json`` declares, as the user would in Load/Unload Applications, Settings...

    The app checks the value by the setting's own rules and keeps it in the settings file; ``ValueError`` carries its words
    for a value it refuses and for a name the tool does not declare, and nothing changes. ``RuntimeError`` is raised in a
    process that is no tool.

        set_setting("textHeight", 4.0)

    Example:
        set_setting("textHeight", 4.0)        # the next setting("textHeight") gives 4.0
        set_setting("textHeight", 0.05)       # ValueError: The setting Text height lies from 0.1 to 1000.
    """
    _need("set_setting")
    _tool.put_setting(name, value)


# The Drawing API (D1655 to D1658).
from ._drawing import (  # noqa: E402
    Blocks as Blocks,
    BoundsTable as BoundsTable,
    Box as Box,
    DimensionStyles as DimensionStyles,
    Documents as Documents,
    Drawing as Drawing,
    Entity as Entity,
    Layer as Layer,
    Linetypes as Linetypes,
    OverlapTable as OverlapTable,
    TextStyles as TextStyles,
)
from ._local import (  # noqa: E402
    active_drawing as active_drawing,
    documents as documents,
    new_drawing as new_drawing,
    new_file as new_file,
    open_drawing as open_drawing,
    open_file as open_file,
)
from ._remote import App as App, connect as connect  # noqa: E402
