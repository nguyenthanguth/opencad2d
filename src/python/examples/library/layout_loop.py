"""The loop of an AI that places notes on a sheet: place, score, move what offends, repeat while the score rises.
Run: python layout_loop.py OUT_FOLDER [SEED]   (OUT_FOLDER is made if it is not there; SEED is a whole number, 1)
Tags and dimensions go on a plan where an AI would first put them; ``reward`` scores the sheet (overlaps and a standard)
and what offends moves to the nearest free place (``measure_*``, ``free_space``, ``fits``). Prints each round and writes
before.png and after.png. The same seed gives the same sheet and moves. Needs numpy.
"""

import math
import random
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, NamedTuple

from opencad2d.cad import Drawing, Entity, new_file

# A small company standard, as a dict of the schema of docs/schemas/opencad2d-standard-v1.schema.json. The sheet is in
# millimetres drawn 1:1 (model_scale 1), so the heights and gaps are on the paper as they are on the screen.
STANDARD: dict[str, Any] = {
    "schema": "opencad2d.standard",
    "version": 1,
    "name": "Layout loop standard",
    "textHeights": {"paper": [2.5, 3.5, 5.0, 7.0], "tolerance": 0.01},
    "gaps": {"wordLine": 0.5, "wordWord": 0.5},
    "weights": {"text-height": 1, "gap-word-line": 2, "gap-word-word": 2},
}
SHEET = (0.0, 0.0, 420.0, 297.0)  # an A3 sheet, in millimetres
FRAME = (20.0, 20.0, 400.0, 277.0)  # the border drawn on it: what is placed stays inside
MARGIN = 1.0  # the clear space asked of a place, twice the least gap the standard needs
REACH = 60.0  # how far from its anchor a tag may be looked for


class Reward(NamedTuple):
    """What ``reward`` gives: ``value`` from 0 to 1 (higher is better) and the parts it is made of."""

    value: float
    standard: float  # ``check(standard).total``
    clear: float  # the share of the objects with words that no line or word runs through
    overlaps: int  # the pairs ``overlaps()`` found


def reward(drawing: Drawing, standard: dict[str, Any], words: int) -> Reward:
    """The reward of an agent that lays out a sheet: half the score of ``check`` against the standard, half the share
    of the ``words`` objects with words that nothing overlaps, so it reads 1.0 for a sheet within the standard and with
    no word overlapped. The agent knows how many it placed; counting them in the drawing would copy every object.

    It is the call an RL agent makes after each action: ``check`` and ``overlaps`` answer a drawing of 10 000 objects in
    0.05 s and one of hundreds in milliseconds, and the same drawing gives the same number on every run.
    """
    drawing.commit()  # what is queued is not scored
    checked = drawing.check(standard, model_scale=1.0)
    hits = drawing.overlaps()
    clear = 1.0 - len(set(hits.words)) / words if words else 1.0
    return Reward((checked.total + clear) / 2.0, checked.total, clear, len(hits))


@dataclass
class Note:
    """A note to place: a tag (``kind`` ``"text"``) whose insertion point is ``shift`` from its ``anchor``, or a
    dimension (``"dim"``) of ``anchor`` and ``end`` whose line is ``offset`` millimetres above them. ``entity`` is the
    object that stands for it in the drawing."""

    kind: str
    anchor: tuple[float, float]
    text: str
    height: float
    end: tuple[float, float] = (0.0, 0.0)
    offset: float = 0.0
    shift: tuple[float, float] = (0.0, 0.0)
    entity: Entity[Any] | None = None


def put(drawing: Drawing, note: Note) -> None:
    """Adds the note where it says it is and keeps the object that stands for it."""
    if note.kind == "dim":
        middle = ((note.anchor[0] + note.end[0]) / 2.0, note.anchor[1] + note.offset)
        note.entity = drawing.add_linear_dim(note.anchor, note.end, middle)
    else:
        where = (note.anchor[0] + note.shift[0], note.anchor[1] + note.shift[1])
        note.entity = drawing.add_text(note.text, where, note.height)
    drawing.commit()


def generate(seed: int) -> tuple[Drawing, list[Note]]:
    """A plan of a frame with a grid of beams, a tag on twelve of its crossings and two dimensions below it, all put
    where an AI would put them first: a tag with its middle on its crossing, a dimension a little off what it
    measures."""
    chance = random.Random(seed)
    drawing = new_file()
    left, bottom, right, top = FRAME
    drawing.add_polyline([(left, bottom), (right, bottom), (right, top), (left, top)], closed=True)
    columns, rows = [60.0 + 50.0 * i for i in range(7)], [80.0, 140.0, 200.0, 250.0]
    for x in columns:
        drawing.add_line((x, 60.0), (x, 270.0))
    for y in rows:
        drawing.add_line((40.0, y), (390.0, y))
    drawing.commit()
    crossings = sorted(chance.sample([(x, y) for y in rows for x in columns], 12), key=lambda c: (c[1], c[0]))
    notes = [
        Note("text", crossing, f"B{number}", chance.choice([3.5, 3.5, 3.5, 4.0]), shift=(-4.0, -1.75))
        for number, crossing in enumerate(crossings, 1)  # a height of 4.0 is one the standard has not
    ]
    notes.append(Note("dim", (columns[0], 60.0), "", 2.5, (columns[-1], 60.0), -8.0))
    notes.append(Note("dim", (columns[0], 60.0), "", 2.5, (columns[3], 60.0), -4.0))
    for note in notes:
        put(drawing, note)
    return drawing, notes


def dimension_offset(drawing: Drawing, note: Note) -> float:
    """The offset nearest the dimension's own, on either side, at which its words fit with ``MARGIN`` to spare (its
    own where none does)."""
    steps = sorted((k * 6.0 * side for k in range(0, 11) for side in (1, -1) if k or side == 1), key=abs)
    for step in steps:
        middle = ((note.anchor[0] + note.end[0]) / 2.0, note.anchor[1] + note.offset + step)
        frames = drawing.measure_dimension("linear", [note.anchor, note.end, middle]).frames
        if all(drawing.fits(frame, margin=MARGIN) for frame in frames):
            return note.offset + step
    return note.offset


def tag_shift(drawing: Drawing, note: Note) -> tuple[float, float]:
    """The shift from its anchor to the place nearest it where a tag's words fit with ``MARGIN`` to spare (its own where
    there is none). The tag is measured, ``free_space`` lists the empty rectangles round the anchor that are big enough,
    and the tag goes to the point of one of them nearest the anchor, the corner of its words on it."""
    ax, ay = note.anchor
    frame = drawing.measure_text(note.text, (ax, ay), note.height).frames[0]  # its corner is off the insertion point
    left, bottom, right, top = FRAME
    window = (max(left, ax - REACH), max(bottom, ay - REACH), min(right, ax + REACH), min(top, ay + REACH))
    best: tuple[float, float, float] | None = None  # the distance, then the corner: a tie breaks the same way every run
    for room in drawing.free_space(window, margin=MARGIN, min_width=frame.width, min_height=frame.height):
        x = min(max(ax, room.min_x), room.max_x - frame.width)
        y = min(max(ay, room.min_y), room.max_y - frame.height)
        found = (math.hypot(x - ax, y - ay), x, y)
        if best is None or found < best:
            best = found
    return note.shift if best is None else (best[1] - frame.x, best[2] - frame.y)


def refine(drawing: Drawing, notes: list[Note], standard: dict[str, Any]) -> int:
    """One round of refining: every note the drawing scores down (a word over a line or another word, a gap under the
    least, a height off the standard) is taken out and put at the nearest place where its words fit, at the nearest
    height the standard has. Gives how many notes were moved."""
    heights: list[float] = standard["textHeights"]["paper"]
    wrong = {v.handle for v in drawing.check(standard, model_scale=1.0).violations} | set(drawing.overlaps().words)
    moved = 0
    for note in sorted(notes, key=lambda each: (each.anchor[1], each.anchor[0], each.kind, each.end)):
        if note.entity is None or note.entity.handle not in wrong:
            continue
        drawing.erase(note.entity)  # out of the drawing first, so that the note does not count against itself
        drawing.commit()
        note.height = min(heights, key=lambda height: (abs(height - note.height), height))
        if note.kind == "dim":
            note.offset = dimension_offset(drawing, note)
        else:
            note.shift = tag_shift(drawing, note)
        put(drawing, note)
        moved += 1
    return moved


def describe(number: int, now: Reward, moved: int | None = None) -> str:
    """The line printed for a round: how many notes it moved, the reward and its parts."""
    made = "" if moved is None else f"moved {moved}, "
    return (
        f"round {number}: {made}reward {now.value:.3f} "
        f"(standard {now.standard:.3f}, clear {now.clear:.3f}, {now.overlaps} overlaps)"
    )


def main(argv: list[str]) -> int:
    if len(argv) not in (1, 2) or (len(argv) == 2 and not argv[1].isdecimal()):
        print("usage: python layout_loop.py OUT_FOLDER [SEED]", file=sys.stderr)
        return 2
    folder = Path(argv[0])
    folder.mkdir(parents=True, exist_ok=True)
    drawing, notes = generate(int(argv[1]) if len(argv) == 2 else 1)
    drawing.render_png(str(folder / "before.png"), box=SHEET, width=1200, ground="white")
    first = best = reward(drawing, STANDARD, len(notes))
    print(describe(0, first))
    for number in range(1, 11):
        moved = refine(drawing, notes, STANDARD)
        now = reward(drawing, STANDARD, len(notes))
        print(describe(number, now, moved))
        if now.value <= best.value + 1e-9:  # the score has stopped rising
            break
        best = now
    drawing.render_png(str(folder / "after.png"), box=SHEET, width=1200, ground="white")
    print(f"reward {first.value:.3f} -> {best.value:.3f}; before.png and after.png written to {folder.name}")
    return 0 if best.overlaps == 0 else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
