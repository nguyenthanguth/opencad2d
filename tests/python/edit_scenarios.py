"""The drawing and the edits the edit-call tests share (A8, Python): ``move``, ``copy``, ``rotate``, ``scale``, ``mirror`` and
``offset`` of a ``Drawing``, each against the command it stands for.

The drawing is ``build_plan``: a line on layer Steel from (0, 0) to (100, 0), a line from (0, 0) to (0, 50), a circle of radius
10 about (200, 0), an arc of radius 20 about (300, 0) from 0 to 90 degrees, an open polyline (0, 100), (100, 100), (100, 150), a
text "NOTE" 5 high at (0, 200), a closed plate (0, 300) to (100, 350) and a line from (0, -50) to (100, -50) on the locked layer
Locked. Two drawings built so are twins: the command is typed in one, the call is made on the other, and ``differences`` says
where their objects part (the same handles, the same DXF groups, a number within 1E-9 of the other).

Every edit of ``EDITS`` is given the objects it names and carries the command line that makes the same drawing: the objects are
selected first (PICKFIRST), then the lines are typed; OFFSET picks its curve by the point ``pick`` on it.
"""

from __future__ import annotations

import math
from collections.abc import Callable
from dataclasses import dataclass
from typing import Any

from GeometryHelper.Dxf import DxfDatabase
from OpenCAD2D import CadCommandSession
from opencad2d.cad import Drawing, Entity

#: The objects every transformation is given: one of each kind, on layers Steel and 0.
KINDS = ("line", "circle", "arc", "polyline", "text", "plate")

EQUAL = 1e-9


def build_plan(drawing: Drawing) -> dict[str, Entity]:
    """Draws the plan described above in ``drawing`` (two commits, the layer Locked locked by the second) and gives its objects
    by name, read back from the drawing."""
    drawing.layers.add("Steel", color=1)
    drawing.layers.add("Locked", color=3)
    drawing.commit()
    drawing.add_line((0, 0), (100, 0), layer="Steel")
    drawing.add_line((0, 0), (0, 50))
    drawing.add_circle((200, 0), 10)
    drawing.add_arc((300, 0), 20, 0.0, math.pi / 2)
    drawing.add_polyline([(0, 100), (100, 100), (100, 150)])
    drawing.add_text("NOTE", (0, 200), 5.0)
    drawing.add_polyline([(0, 300), (100, 300), (100, 350), (0, 350)], closed=True)
    drawing.add_line((0, -50), (100, -50), layer="Locked")
    drawing.commit()
    drawing.layers["Locked"].is_locked = True
    drawing.commit()
    order = ("line", "side", "circle", "arc", "polyline", "text", "plate", "locked")
    return dict(zip(order, drawing.entities(), strict=True))


@dataclass
class Twins:
    """Two drawings built alike: ``typed`` gets the command line, ``called`` the call; each with the objects of its plan by name."""

    typed: Drawing
    called: Drawing
    typed_plan: dict[str, Entity]
    plan: dict[str, Entity]


def make_twins(typed: Drawing, called: Drawing) -> Twins:
    """Builds the plan in the two drawings, ``typed`` first."""
    typed_plan = build_plan(typed)
    return Twins(typed, called, typed_plan, build_plan(called))


def groups(net: Any) -> list[tuple[str, str]]:
    """The DXF groups of one object, as (code, value) pairs: its text in a drawing of its own, less the handle and the owner,
    which that drawing gives."""
    fragment = DxfDatabase("AC1027")
    fragment.ModelSpace.AppendEntity(net.Clone())
    lines = fragment.ToText().replace("\r\n", "\n").split("\n")
    found: list[tuple[str, str]] = []
    inside = False
    for index in range(0, len(lines) - 1, 2):
        code, value = lines[index].strip(), lines[index + 1].strip()
        if inside and code == "0" and value == "ENDSEC":
            break
        if inside:
            if code not in ("5", "330"):
                found.append((code, value))
        elif code == "2" and value == "ENTITIES":
            inside = True
    return found


def snapshot(drawing: Drawing) -> list[tuple[str, list[tuple[str, str]]]]:
    """Every object of the drawing, in its order: (handle, DXF groups)."""
    return [(entity.handle, groups(entity.net)) for entity in drawing.entities()]


def _close(a: str, b: str) -> bool:
    if a == b:
        return True
    try:
        return abs(float(a) - float(b)) <= EQUAL
    except ValueError:
        return False


def differences(
    expected: list[tuple[str, list[tuple[str, str]]]], actual: list[tuple[str, list[tuple[str, str]]]]
) -> list[str]:
    """Where two snapshots part, the first six places, in words; empty when they are the same drawing."""
    found: list[str] = []
    if len(expected) != len(actual):
        found.append(f"objects: {len(expected)} against {len(actual)}")
    for (handle_a, groups_a), (handle_b, groups_b) in zip(expected, actual):
        if handle_a != handle_b:
            found.append(f"handle {handle_a} against {handle_b}")
        if len(groups_a) != len(groups_b):
            found.append(f"{handle_a}: {len(groups_a)} groups against {len(groups_b)}")
            continue
        for (code_a, value_a), (code_b, value_b) in zip(groups_a, groups_b):
            if code_a != code_b or not _close(value_a, value_b):
                found.append(f"{handle_a} code {code_a}: {value_a} against {code_b} {value_b}")
    return found[:6]


def same_drawing(expected: Drawing, actual: Drawing) -> list[str]:
    """``differences`` of the objects of two drawings."""
    return differences(snapshot(expected), snapshot(actual))


def locked(drawing: Drawing) -> bool:
    """Whether the layer Locked is locked: the last step ``build_plan`` takes, so that a test can tell, after one U, whether a call
    recorded a step of its own."""
    return [layer.is_locked for layer in drawing.layers if layer.name == "Locked"] == [True]


def type_command(drawing: Drawing, *lines: str) -> None:
    """Types the lines at the command line of a drawing, an empty line being Enter, and checks that the command ended: through
    ``SendCommand`` of a drawing of the app, through a command session of its own on a drawing of a file."""
    net = drawing.net
    if hasattr(net, "SendCommand"):
        result = net.SendCommand("\n".join(lines) + "\n")
        assert result.Completed, list(result.Output)
        return
    session = CadCommandSession(net)
    for line in lines:
        if line:
            session.Feed(line)
        else:
            session.Enter()
    assert not session.IsRunning, session.PromptText


def type_edit(twins: Twins, edit: Edit) -> None:
    """Selects the objects (PICKFIRST; OFFSET picks its curve by a point instead) and types the command of the edit on the
    ``typed`` drawing."""
    if edit.pick is None:
        twins.typed.select([twins.typed_plan[name] for name in edit.names])
    type_command(twins.typed, *edit.command)


def call_edit(twins: Twins, edit: Edit) -> list[Any]:
    """Makes the call of the edit on the ``called`` drawing, given the objects of its plan; gives what it gives back."""
    return edit.call(twins.called, [twins.plan[name] for name in edit.names])


@dataclass(frozen=True)
class Edit:
    """One edit: the plan's objects it is given (by name), the call, the command line that makes the same drawing, whether the
    objects given come back (an edit in place) and the point at which OFFSET picks its curve."""

    name: str
    names: tuple[str, ...]
    call: Callable[[Drawing, list[Any]], list[Any]]
    command: tuple[str, ...]
    in_place: bool
    pick: str | None = None

    def __repr__(self) -> str:
        return self.name


def _offset(distance: float, side: tuple[float, float]) -> Callable[[Drawing, list[Any]], list[Any]]:
    return lambda drawing, items: drawing.offset(items[0], distance, side)


EDITS: tuple[Edit, ...] = (
    Edit("move", KINDS, lambda d, e: d.move(e, (10, 5)), ("MOVE", "0,0", "10,5"), True),
    Edit("copy", KINDS, lambda d, e: d.copy(e, (0, 50)), ("COPY", "0,0", "0,50", ""), False),
    Edit("rotate_quarter", KINDS, lambda d, e: d.rotate(e, (0, 0), math.pi / 2), ("ROTATE", "0,0", "90"), True),
    Edit("rotate_thirty", KINDS, lambda d, e: d.rotate(e, (50, 20), math.radians(30)), ("ROTATE", "50,20", "30"), True),
    Edit("scale_two", KINDS, lambda d, e: d.scale(e, (0, 0), 2.0), ("SCALE", "0,0", "2"), True),
    Edit("scale_half", KINDS, lambda d, e: d.scale(e, (10, 10), 0.5), ("SCALE", "10,10", "0.5"), True),
    Edit(
        "mirror_slanted",
        KINDS,
        lambda d, e: d.mirror(e, (20, 5), (30, 20)),
        ("MIRROR", "20,5", "30,20", ""),
        False,
    ),
    Edit(
        "mirror_upright",
        KINDS,
        lambda d, e: d.mirror(e, (0, 0), (0, 10), erase_source=False),
        ("MIRROR", "0,0", "0,10", "N"),
        False,
    ),
    Edit(
        "mirror_slanted_and_erase",
        KINDS,
        lambda d, e: d.mirror(e, (20, 5), (30, 20), True),
        ("MIRROR", "20,5", "30,20", "Y"),
        True,
    ),
    Edit(
        "offset_line", ("line",), _offset(5.0, (50, 10)), ("OFFSET", "5", "50,0", "50,10", ""), False, pick="50,0"
    ),
    Edit(
        "offset_line_the_other_way",
        ("line",),
        _offset(5.0, (50, -10)),
        ("OFFSET", "5", "50,0", "50,-10", ""),
        False,
        pick="50,0",
    ),
    Edit(
        "offset_arc_outward",
        ("arc",),
        _offset(5.0, (350, 50)),
        ("OFFSET", "5", "314.1421356237,14.1421356237", "350,50", ""),
        False,
        pick="314.1421356237,14.1421356237",
    ),
    Edit(
        "offset_circle_inward",
        ("circle",),
        _offset(4.0, (205, 0)),
        ("OFFSET", "4", "210,0", "205,0", ""),
        False,
        pick="210,0",
    ),
    Edit(
        "offset_open_polyline",
        ("polyline",),
        _offset(10.0, (50, 110)),
        ("OFFSET", "10", "50,100", "50,110", ""),
        False,
        pick="50,100",
    ),
    Edit(
        "offset_plate_outward",
        ("plate",),
        _offset(10.0, (50, 290)),
        ("OFFSET", "10", "50,300", "50,290", ""),
        False,
        pick="50,300",
    ),
    Edit(
        "offset_plate_inward",
        ("plate",),
        _offset(10.0, (50, 325)),
        ("OFFSET", "10", "50,300", "50,325", ""),
        False,
        pick="50,300",
    ),
)

#: The edits that make a drawing out of the objects they are given and read back as what they were, for a table of refusals.
REFUSED_CALLS: tuple[tuple[str, Callable[[Drawing, list[Any]], object]], ...] = (
    ("move", lambda d, e: d.move(e, (10, 5))),
    ("copy", lambda d, e: d.copy(e, (10, 5))),
    ("rotate", lambda d, e: d.rotate(e, (0, 0), 1.0)),
    ("scale", lambda d, e: d.scale(e, (0, 0), 2.0)),
    ("mirror", lambda d, e: d.mirror(e, (0, 0), (0, 10))),
    ("mirror_and_erase", lambda d, e: d.mirror(e, (0, 0), (0, 10), erase_source=True)),
    ("offset", lambda d, e: d.offset(e[0], 5.0, (50, 10))),
)
