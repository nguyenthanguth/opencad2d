"""Seeded random drawings of steel plates, with their ground truth, for training and testing a program that lays drawings out.

A drawing is one plate detail on an A3 sheet in millimetres: a steel plate with a grid of holes, a notch cut from a corner
now and then, a weld line (the outline offset inward), a chain of dimensions between the holes and the overall width and height,
notes, and a title block on a layout with the plate shown through a viewport at 1:5 or 1:10. Layers, text styles and the
dimension style are the small standard ``STANDARD`` names, so that ``check`` has something to score against.

``crowding`` (0 to 1) is how much clutter is added: the plate and its dimensions are the same for a seed whatever the
crowding, and each step of it puts more notes across the holes and the plate's lines, so that words overlap what they stand on
and a program that moves them has work to do. At 0 nothing is added, and the words of a plate overlap nothing. The same seed
and crowding give the same drawing, byte for byte in ``to_json``, on every run; the random numbers are ``random.Random``'s,
whose sequence for a seed is fixed within a Python version; across versions only ``random()`` is promised, so a dataset that
must be reproduced bit for bit keeps its Python.

Needs numpy where ``generate`` makes the picture and labels (``image``), as ``to_coco`` does.
"""

from __future__ import annotations

import os
import random
from dataclasses import dataclass
from typing import Any

from opencad2d.cad import Drawing, new_file

SHEET = "Plate A3"
"""The name of the layout of every drawing."""

LAYERS: dict[str, int] = {
    "S-PLATE": 7,
    "S-HOLE": 7,
    "S-CENTRE": 1,
    "S-WELD": 3,
    "S-DIM": 2,
    "S-NOTE": 5,
    "S-SHEET": 7,
}
"""The layers of a drawing and their colours (AutoCAD numbers): the plate's outline, its holes, their centre marks, the weld,
the dimensions, the notes, and the border and title block of the sheet."""

STANDARD: dict[str, Any] = {
    "schema": "opencad2d.standard",
    "version": 1,
    "name": "Synthetic plates",
    "layers": {
        "allowed": [{"name": name, "color": color} for name, color in LAYERS.items()]
        + [{"name": "Defpoints"}, {"name": "0"}]  # 0: the viewport of a layout stands on it
    },
    "byLayer": {"color": True, "linetype": True},
    "textStyles": [{"name": "SYN"}, {"name": "Standard"}],
    "textHeights": {"paper": [2.5, 3.5, 5.0]},
    "gaps": {"wordLine": 0.5, "wordWord": 0.5},
    "dimensionChains": {},
}
"""The standard every drawing is made to and ``generate`` checks it against: the layers of ``LAYERS``, the text style SYN,
words 2.5, 3.5 or 5 millimetres high on paper, at least half a millimetre between words and lines and between words, and
dimensions of a chain in line."""

_NOTES = ("TYP", "SEE NOTE 3", "BURR", "R5", "CHK", "DO NOT SCALE", "FLAME CUT")


@dataclass(frozen=True)
class Sample:
    """A drawing and what is known of it: the ground truth of a training example.

    ``json`` is ``drawing.to_json()`` of model space; ``score`` and ``scores`` the total and the rules of ``check(STANDARD)``;
    ``overlaps`` the number of pairs of words and what they cross (``drawing.overlaps()``); ``coco`` the annotations of the
    picture ``generate`` wrote, or None where it wrote none.
    """

    seed: int
    crowding: float
    drawing: Drawing
    json: str
    score: float
    scores: dict[str, float]
    overlaps: int
    coco: dict[str, Any] | None


def _box(width: float, height: float, at: tuple[float, float]) -> list[Any]:
    """The four sides of a rectangle as LINEs, for the border of the sheet and the title block."""
    from GeometryHelper.Dxf import DxfLine
    from GeometryHelper.Geometry import GeoPoint3

    x, y = at
    corners = [(x, y), (x + width, y), (x + width, y + height), (x, y + height)]
    return [DxfLine(GeoPoint3(*corners[i], 0.0), GeoPoint3(*corners[(i + 1) % 4], 0.0)) for i in range(4)]


def _toward(start: tuple[float, float], end: tuple[float, float]) -> tuple[float, float]:
    """The unit step from ``start`` to ``end``, a side that runs along an axis."""
    return (float(end[0] > start[0]) - float(end[0] < start[0]), float(end[1] > start[1]) - float(end[1] < start[1]))


def _outline(width: float, height: float, notch: tuple[int, float] | None) -> list[tuple[float, float]]:
    """The corners of the plate, counter-clockwise from the origin; the corner ``notch[0]`` is cut away by a square of side
    ``notch[1]``."""
    corners = [(0.0, 0.0), (width, 0.0), (width, height), (0.0, height)]
    if notch is None:
        return corners
    points: list[tuple[float, float]] = []
    for index, corner in enumerate(corners):
        if index != notch[0]:
            points.append(corner)
            continue
        back, along = _toward(corner, corners[index - 1]), _toward(corner, corners[(index + 1) % 4])
        size = notch[1]
        points += [
            (corner[0] + size * back[0], corner[1] + size * back[1]),
            (corner[0] + size * (back[0] + along[0]), corner[1] + size * (back[1] + along[1])),
            (corner[0] + size * along[0], corner[1] + size * along[1]),
        ]
    return points


def make_drawing(seed: int, crowding: float = 0.0) -> Drawing:
    """Make the drawing of ``seed``: a new drawing, committed, with the plate in model space and the A3 layout ``SHEET`` with
    its border, title block and a viewport at 1:5 or 1:10. ``crowding`` from 0 to 1 adds up to eight notes across the holes
    and lines of the plate, so that at 0 no word overlaps anything and at 1 several do (``ValueError`` outside 0 to 1). A plate
    is 220 to 780 by 220 to 480 millimetres with 4 to 12 holes of 16 to 30, and the same seed gives the same plate whatever the
    crowding. The overall width and height of a plate with a corner notch run to the corner as if it were not cut (a virtual
    sharp, as a drafter dimensions it), and the notes do not repeat the size of the notch.

    Example:
        drawing = make_drawing(7)                     # a plate with its holes, dimensions and notes, and the sheet
        len(drawing.overlaps())                       # 0: nothing is across anything at crowding 0
        len(make_drawing(7, crowding=1.0).overlaps()) # 42: eight more notes, across holes and lines
    """
    if not 0.0 <= crowding <= 1.0:
        raise ValueError(f"crowding is from 0 to 1, not {crowding!r}.")
    rng = random.Random(seed)
    scale = rng.choice((5, 10))  # model units to a millimetre on paper: the viewport is 1:scale
    columns, rows = rng.randrange(2, 5), rng.randrange(2, 4)
    edge, pitch_x, pitch_y = rng.choice((60, 75, 90)), rng.choice((100, 120, 150, 200)), rng.choice((100, 120, 150))
    radius = rng.choice((8, 10, 12, 15))
    width, height = 2 * edge + (columns - 1) * pitch_x, 2 * edge + (rows - 1) * pitch_y
    notch = (rng.randrange(4), rng.choice((25, 30))) if rng.random() < 0.6 else None
    weld = rng.choice((4, 5, 6)) if rng.random() < 0.7 else None
    thickness = rng.choice((8, 10, 12, 16, 20, 25))
    number = f"A-{rng.randrange(100, 1000)}"
    word, line = 3.5 * scale, 8.0 * scale  # the height of a note, and eight millimetres on paper

    drawing = new_file()
    for name, color in LAYERS.items():
        drawing.layers.add(name, color=color)
    drawing.text_styles.add("SYN", font="txt")
    drawing.dimension_styles.add("SYN-DIM", text_style="SYN", scale=float(scale), text_height=2.5, arrow_size=2.5)
    drawing.commit()
    drawing.current_dim_style = "SYN-DIM"
    holes = [(edge + i * pitch_x, edge + j * pitch_y) for j in range(rows) for i in range(columns)]

    plate = drawing.add_polyline(_outline(width, height, notch), closed=True, layer="S-PLATE")
    drawing.commit()
    if weld is not None:
        for seam in drawing.offset(plate, float(weld), (width / 2.0, height / 2.0)):
            seam.layer = "S-WELD"
    for x, y in holes:
        drawing.add_circle((x, y), float(radius), layer="S-HOLE")
        drawing.add_line((x - radius - 5, y), (x + radius + 5, y), layer="S-CENTRE")
        drawing.add_line((x, y - radius - 5), (x, y + radius + 5), layer="S-CENTRE")
    for i in range(columns - 1):
        drawing.add_linear_dim(holes[i], holes[i + 1], (edge + i * pitch_x + pitch_x / 2.0, -line), layer="S-DIM")
    for j in range(rows - 1):
        first, second = holes[j * columns], holes[(j + 1) * columns]
        drawing.add_linear_dim(first, second, (-line, edge + j * pitch_y + pitch_y / 2.0), layer="S-DIM")
    drawing.add_linear_dim((0, 0), (width, 0), (width / 2.0, -2.0 * line), layer="S-DIM")
    drawing.add_linear_dim((0, 0), (0, height), (-2.0 * line, height / 2.0), layer="S-DIM")
    notes = [f"PL{thickness} S355", f"{len(holes)} HOLES D{2 * radius}", f"EDGE DISTANCE {edge}"]
    if weld is not None:
        notes.append(f"FILLET WELD {weld} ALL ROUND")
    for index, text in enumerate(notes):
        drawing.add_text(text, (0.0, height + line + index * 2.0 * word), word, text_style="SYN", layer="S-NOTE")
    drawing.commit()

    for _ in range(round(8 * crowding)):
        text = rng.choice(_NOTES)
        if rng.random() < 0.6:
            x, y = rng.choice(holes)
            at = (x - radius, y + rng.uniform(-0.5, 0.5) * radius)  # a note that starts on a hole's rim runs across it
        else:
            at = (rng.uniform(0.0, width * 0.6), rng.uniform(0.0, height))
        drawing.add_text(text, at, word, text_style="SYN", layer="S-NOTE")
    drawing.commit()

    sheet = drawing.layouts.add(SHEET, paper="ISO_A3")
    for side in [*_box(400.0, 277.0, (10.0, 10.0)), *_box(160.0, 35.0, (250.0, 10.0))]:
        side.Layer = "S-SHEET"
        drawing.add(side, layout=sheet)
    _title_block(drawing, sheet.name, {"DRAWING": f"PLATE PL{thickness}", "NUMBER": number, "SCALE": f"1:{scale}"})
    sheet.add_viewport((20, 55, 400, 277), f"1:{scale}", center=(width / 2.0 - line, height / 2.0 + line))
    return drawing


def _title_block(drawing: Drawing, layout: str, values: dict[str, str]) -> None:
    """Defines the block TITLE (a box 160 by 35 with an attribute for each of ``values``, 5 millimetres high) and puts it
    in the corner of ``layout`` with the words of ``values``."""
    from GeometryHelper.Dxf import DxfAttributeDefinition, DxfLine
    from GeometryHelper.Geometry import GeoPoint3

    parts: list[Any] = [*_box(160.0, 35.0, (0.0, 0.0)), DxfLine(GeoPoint3(0.0, 17.5, 0.0), GeoPoint3(160.0, 17.5, 0.0))]
    style = drawing.text_styles["SYN"]
    for tag, at in zip(values, ((4.0, 21.0), (4.0, 5.0), (84.0, 5.0))):
        definition = DxfAttributeDefinition(GeoPoint3(at[0], at[1], 0.0), "", tag, tag.title(), style)
        definition.Height = 5.0
        parts.append(definition)
    for part in parts:
        part.Layer = "S-SHEET"
    drawing.blocks.add("TITLE", parts)
    drawing.add_insert("TITLE", (250.0, 10.0), layout=layout, attributes=values, layer="S-SHEET")
    drawing.commit()


def generate(
    seed: int, crowding: float = 0.0, image: str | os.PathLike[str] | None = None, width: int = 1280
) -> Sample:
    """Make the drawing of ``seed`` with ``make_drawing`` and its ground truth: the JSON of model space, the score of
    ``STANDARD`` and its rules, the number of overlaps, and, where ``image`` names a PNG file to write, the picture of model
    space ``width`` pixels wide and its COCO annotations (``to_coco``: boxes in whole pixels from the top left, inside the
    picture; needs numpy). The result is the same for the same ``seed`` and ``crowding`` on every run, the picture's bytes
    included. Takes 41 ms without a picture and 76 ms with one on .NET 8 (21 and 58 ms on .NET Framework 4.8), medians of 40 drawings at crowding 0.5 and 1280 pixels.

    Example:
        sample = generate(7, crowding=0.5, image="plate7.png")
        sample.score                                  # 0.952: the total of the standard
        sample.overlaps                               # 16: the pairs of words across holes or lines
        sample.coco["images"][0]["width"]             # 1280
        generate(7).json == generate(7).json          # True
    """
    drawing = make_drawing(seed, crowding)
    checked = drawing.check(STANDARD)
    coco = None if image is None else drawing.to_coco(os.fspath(image), width=width)
    return Sample(
        seed, crowding, drawing, drawing.to_json(), checked.total, dict(checked.scores), len(drawing.overlaps()), coco
    )
