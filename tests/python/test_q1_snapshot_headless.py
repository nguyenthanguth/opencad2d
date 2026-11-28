"""Q1: a snapshot and reset of a drawing in memory, on a file with no app (PW7.3): ``drawing.snapshot()`` and ``drawing.reset()`` over a
``CadDocument``.

After an episode of edits the drawing is as it was: its JSON export byte for byte, its handles and the handle the next object gets,
its bounds; what is queued is dropped; a snapshot of another drawing or one released is refused in words. A drawing of 10 000
objects is reset in a fraction of the time it takes to open it, and hundreds of episodes leave the memory where it was.
"""

import gc
import random
import time
from pathlib import Path
from typing import Any

import numpy as np
import pytest

from opencad2d.cad import Drawing, Snapshot, new_file, open_file


def _plan() -> Drawing:
    """A plan with a layer, a line, a circle, a text and a dimension tied to a line."""
    drawing = new_file()
    drawing.layers.add("Steel", color=1)
    drawing.commit()
    drawing.add_line((0, 0), (100, 0), layer="Steel")
    drawing.add_circle((50, 50), 25)
    drawing.add_text("NOTE", (0, 100), 5.0)
    drawing.add_linear_dim((0, 0), (100, 0), (50, -20))
    drawing.commit()
    return drawing


def _state(drawing: Drawing) -> tuple[str, list[str], list[Any]]:
    """What a reset must give back: the JSON export, the handles in order, the bounds of every entity."""
    table = drawing.bounds()
    return drawing.to_json(), [e.handle for e in drawing.entities()], [list(table.handles), np.asarray(table.boxes).tolist()]


def _episode(drawing: Drawing, generator: random.Random, edits: int) -> None:
    """Random edits, each its own commit: an object added, moved, erased, put on another layer or given a colour."""
    for _ in range(edits):
        found = drawing.entities("LINE") + drawing.entities("CIRCLE")
        kind = generator.randrange(10)
        if kind < 3 or not found:
            drawing.add_line((generator.uniform(0, 500), generator.uniform(0, 500)), (generator.uniform(0, 500), 5))
        elif kind < 6:
            drawing.move([generator.choice(found)], (generator.uniform(-9, 9), generator.uniform(-9, 9)))
        elif kind < 8:
            drawing.erase(generator.choice(found))
        elif kind < 9:
            generator.choice(found).layer = "Steel"
        else:
            generator.choice(found).color = generator.randrange(1, 8)
        drawing.commit()


def test_an_episode_of_random_edits_is_gone_after_a_reset_to_the_byte() -> None:
    drawing = _plan()
    before = _state(drawing)
    modified = drawing.is_modified
    start = drawing.snapshot()
    generator = random.Random(3)

    for _ in range(5):
        _episode(drawing, generator, 100)
        assert _state(drawing) != before
        drawing.reset(start)
        assert _state(drawing) == before

    assert isinstance(start, Snapshot)
    assert start.drawing is drawing
    assert drawing.is_modified == modified


def test_the_handles_repeat_from_one_episode_to_the_next() -> None:
    drawing = _plan()
    start = drawing.snapshot()
    first: list[str] = []

    for episode in range(3):
        drawing.add_line((0, 0), (1, 1))
        drawing.add_circle((5, 5), 2)
        made = drawing.commit()
        first = first or made
        assert made == first
        drawing.reset(start)


def test_what_is_queued_is_dropped_and_what_was_read_before_stays_a_copy() -> None:
    drawing = _plan()
    start = drawing.snapshot()
    line = drawing.entities("LINE")[0]
    drawing.add_line((0, 0), (5, 5))
    line.color = 3

    drawing.reset(start)

    assert drawing.commit() == []
    assert drawing.entities("LINE")[0].color != 3
    assert line.color == 3


def test_the_current_layer_and_the_layers_come_back() -> None:
    drawing = _plan()
    start = drawing.snapshot()
    drawing.layers.add("Extra", color=2)
    drawing.commit()
    drawing.current_layer = "Extra"

    drawing.reset(start)

    assert drawing.current_layer == "0"
    assert "Extra" not in drawing.layers
    assert "Steel" in drawing.layers


def test_a_drawing_opened_from_a_file_is_reset_as_well(tmp_path: Path) -> None:
    path = tmp_path / "plan.dxf"
    _plan().save_as(str(path))
    drawing = open_file(str(path))
    before = _state(drawing)
    start = drawing.snapshot()

    _episode(drawing, random.Random(5), 30)
    drawing.reset(start)

    assert _state(drawing) == before
    assert not drawing.is_modified


def test_a_snapshot_of_another_drawing_or_a_released_one_or_no_snapshot_is_refused_in_words() -> None:
    drawing, other = _plan(), _plan()
    start = drawing.snapshot()

    with pytest.raises(ValueError, match="another drawing"):
        other.reset(start)
    with pytest.raises(ValueError, match="what drawing.snapshot\\(\\) gives"):
        drawing.reset("start")  # type: ignore[arg-type]
    start.release()
    start.release()
    with pytest.raises(ValueError, match="released"):
        drawing.reset(start)
    assert "released" in repr(start)


def test_a_snapshot_taken_while_a_command_runs_is_refused() -> None:
    drawing = _plan()
    start = drawing.snapshot()
    drawing.net.BeginGroup("LINE")
    try:
        with pytest.raises(RuntimeError, match="a command is running"):
            drawing.snapshot()
        with pytest.raises(RuntimeError, match="a command is running"):
            drawing.reset(start)
    finally:
        drawing.net.CancelGroup()
    drawing.reset(start)


def test_two_snapshots_are_reset_to_in_either_order() -> None:
    drawing = _plan()
    first = _state(drawing)
    one = drawing.snapshot()
    drawing.add_line((7, 7), (8, 8))
    drawing.commit()
    second = _state(drawing)
    two = drawing.snapshot()
    drawing.add_line((9, 9), (10, 10))
    drawing.commit()

    drawing.reset(one)
    assert _state(drawing) == first
    drawing.reset(two)
    assert _state(drawing) == second
    drawing.reset(one)
    assert _state(drawing) == first


def _big(count: int) -> Drawing:
    drawing = new_file()
    generator = random.Random(1)
    for i in range(count):
        x, y = generator.uniform(0, 1000), generator.uniform(0, 1000)
        if i % 4 == 0:
            drawing.add_text("PLATE", (x, y), 2.5)
        elif i % 4 == 1:
            drawing.add_circle((x, y), 1 + generator.uniform(0, 5))
        else:
            drawing.add_line((x, y), (x + generator.uniform(-20, 20), y + generator.uniform(-20, 20)))
    drawing.commit()
    return drawing


@pytest.mark.timing
def test_ten_thousand_objects_are_reset_in_a_fraction_of_the_time_to_open_the_file(tmp_path: Path, capsys: Any) -> None:
    drawing = _big(10_000)
    path = tmp_path / "big.dxf"
    drawing.save_as(str(path))
    clock = time.perf_counter()
    opened = open_file(str(path))
    opened.bounds()
    opening = time.perf_counter() - clock

    start = opened.snapshot()
    generator = random.Random(2)
    resets: list[float] = []
    for _ in range(6):
        _episode(opened, generator, 100)
        opened.bounds()
        clock = time.perf_counter()
        opened.reset(start)
        opened.bounds()
        resets.append(time.perf_counter() - clock)

    typical = sorted(resets[1:])[len(resets[1:]) // 2]
    with capsys.disabled():
        print(f"\n10 000 objects: open the file and read its bounds {opening * 1000:.1f} ms; reset and read the bounds {typical * 1000:.1f} ms")
    assert typical < opening / 2, (resets, opening)


def test_hundreds_of_episodes_leave_the_memory_where_it_was() -> None:
    from System import GC  # type: ignore[import-not-found]

    drawing = _plan()
    start = drawing.snapshot()
    generator = random.Random(9)
    at_forty = 0
    for episode in range(1, 201):
        _episode(drawing, generator, 20)
        drawing.reset(start)
        if episode == 40:
            gc.collect()
            at_forty = int(GC.GetTotalMemory(True))  # pyright: ignore[reportAny]
    gc.collect()
    at_end = int(GC.GetTotalMemory(True))  # pyright: ignore[reportAny]

    assert at_end - at_forty < 8 * 1024 * 1024, (at_forty, at_end)
