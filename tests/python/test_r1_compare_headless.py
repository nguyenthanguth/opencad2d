"""R1: comparing two drawings on a file with no app (PW7.6): ``drawing.compare(other)`` over the core's ``CadDrawingDiff``.

The objects only the other drawing has are ``added``, those only this drawing has ``removed``, and those both have that differ are
``changed`` with the names of what differs and, where the box kept its size and moved, ``moved_by``; boxes come as NumPy arrays
too. ``other`` is a ``Drawing`` or the path of a DXF or DWG file. A drawing is the same as the copy saved before an episode of
edits once it is reset to its snapshot. What is queued is in neither; a tolerance, a path and an ``other`` that are no good are
refused in words.
"""

import math
import random
import time
from pathlib import Path

import numpy as np
import pytest

from opencad2d.cad import Change, Diff, Drawing, new_file, open_file


def _plan() -> Drawing:
    """A plan with a layer, two lines, a circle, a text and a dimension, in millimetres."""
    drawing = new_file()
    drawing.layers.add("Steel", color=1)
    drawing.commit()
    drawing.add_line((0, 0), (100, 0))
    drawing.add_line((0, 10), (100, 10), layer="Steel")
    drawing.add_circle((50, 50), 10)
    drawing.add_text("NOTE", (0, 100), 5.0)
    drawing.add_linear_dim((0, 0), (100, 0), (50, -20))
    drawing.commit()
    return drawing


def _saved(drawing: Drawing, folder: Path, name: str = "before.dxf") -> Path:
    """A copy of the drawing as it stands, written beside it: the state before."""
    path = folder / name
    drawing.net.SaveCopy(str(path), None)
    return path


def _of_kind(changes: list[Change], kind: str) -> Change:
    (found,) = [change for change in changes if change.kind == kind]
    return found


# ---- 1. the same drawing ----------------------------------------------------------------------------------------------------


def test_a_drawing_compared_with_itself_is_the_same_and_every_object_is_unchanged() -> None:
    drawing = _plan()

    diff = drawing.compare(drawing)

    assert isinstance(diff, Diff)
    assert diff.is_same
    assert len(diff) == 0
    assert (diff.added, diff.removed, diff.changed, diff.moved) == ([], [], [], [])
    assert diff.unchanged >= 5
    assert diff.added_boxes is not None and diff.added_boxes.shape == (0, 4)
    assert diff.moved_by is not None and diff.moved_by.shape == (0, 2)


def test_a_drawing_compared_with_the_file_it_was_saved_to_is_the_same(tmp_path: Path) -> None:
    drawing = _plan()
    path = _saved(drawing, tmp_path)

    assert drawing.compare(path).is_same
    assert drawing.compare(str(path)).is_same
    assert drawing.compare(open_file(str(path))).is_same


def test_a_dwg_copy_is_the_same_drawing_too(tmp_path: Path) -> None:
    drawing = _plan()
    copy = tmp_path / "before.dwg"
    drawing.net.SaveCopy(str(copy), None)

    assert drawing.compare(copy).is_same


# ---- 2. what differs --------------------------------------------------------------------------------------------------------


def test_a_circle_moved_ten_millimetres_is_a_changed_and_moved_object_with_its_boxes(tmp_path: Path) -> None:
    drawing = _plan()
    before = open_file(str(_saved(drawing, tmp_path)))
    drawing.move(drawing.entities("CIRCLE"), (10, 0))
    drawing.commit()

    diff = before.compare(drawing)

    assert (len(diff.added), len(diff.removed), len(diff.changed)) == (0, 0, 1)
    (circle,) = diff.moved
    assert (circle.kind, circle.layout, circle.layer) == ("CIRCLE", "Model", "0")
    assert circle.moved_by == pytest.approx((10.0, 0.0))
    assert tuple(circle.box_before or ()) == pytest.approx((40.0, 40.0, 60.0, 60.0))
    assert tuple(circle.box_after or ()) == pytest.approx((50.0, 40.0, 70.0, 60.0))
    assert "box" in circle.properties and list(circle.properties) == sorted(circle.properties)
    assert diff.moved_by is not None
    np.testing.assert_allclose(diff.moved_by, [[10.0, 0.0]])
    assert diff.before_boxes is not None and diff.after_boxes is not None
    np.testing.assert_allclose(diff.after_boxes - diff.before_boxes, [[10.0, 0.0, 10.0, 0.0]])
    assert diff.unchanged >= 4
    assert not diff.is_same and len(diff) == 1


def test_an_object_added_and_one_erased_are_told_with_their_kinds_and_boxes(tmp_path: Path) -> None:
    drawing = _plan()
    before = open_file(str(_saved(drawing, tmp_path)))
    drawing.erase(drawing.entities("TEXT")[0])
    drawing.add_arc((0, 0), 30, 0.0, math.pi / 2)
    drawing.commit()

    diff = before.compare(drawing)

    assert [c.kind for c in diff.added] == ["ARC"]
    assert [c.kind for c in diff.removed] == ["TEXT"]
    assert diff.changed == []
    assert diff.added[0].box_before is None and diff.added[0].box_after is not None
    assert diff.removed[0].box_after is None and diff.removed[0].box_before is not None
    assert diff.added_boxes is not None and diff.added_boxes.shape == (1, 4)
    assert diff.removed_boxes is not None and diff.removed_boxes.shape == (1, 4)
    assert diff.added[0].properties == () and diff.removed[0].properties == ()
    # What the other drawing has and this one has not is added; the reverse comparison is the mirror.
    back = drawing.compare(before)
    assert [c.kind for c in back.added] == ["TEXT"] and [c.kind for c in back.removed] == ["ARC"]


def test_a_layer_and_a_colour_changed_are_named_and_the_object_has_not_moved(tmp_path: Path) -> None:
    drawing = _plan()
    before = open_file(str(_saved(drawing, tmp_path)))
    circle = drawing.entities("CIRCLE")[0]
    circle.layer = "Steel"
    circle.color = 3
    drawing.commit()

    diff = before.compare(drawing)

    (changed,) = diff.changed
    assert changed.kind == "CIRCLE" and changed.layer == "Steel"
    assert set(changed.properties) == {"layer", "color"}
    assert changed.moved_by is None and diff.moved == []
    assert diff.moved_by is not None and diff.moved_by.shape == (0, 2)


def test_a_circle_grown_has_changed_its_box_and_has_not_moved(tmp_path: Path) -> None:
    drawing = _plan()
    before = open_file(str(_saved(drawing, tmp_path)))
    drawing.scale(drawing.entities("CIRCLE"), (50, 50), 2.0)
    drawing.commit()

    (changed,) = before.compare(drawing).changed

    assert changed.moved_by is None
    assert "box" in changed.properties and "radius" in changed.properties


def test_the_lists_come_in_the_order_of_the_handles(tmp_path: Path) -> None:
    drawing = _plan()
    before = open_file(str(_saved(drawing, tmp_path)))
    for index in range(12):
        drawing.add_line((index, 200), (index, 210 + index))
    drawing.commit()

    handles = [change.handle for change in before.compare(drawing).added]

    assert len(handles) == 12
    assert handles == sorted(handles, key=lambda handle: int(handle, 16))


def test_a_move_inside_the_tolerance_is_the_same_and_one_outside_is_not(tmp_path: Path) -> None:
    drawing = _plan()
    before = open_file(str(_saved(drawing, tmp_path)))
    drawing.move(drawing.entities("CIRCLE"), (0.0005, 0))
    drawing.commit()

    assert before.compare(drawing).is_same
    assert before.compare(drawing, tolerance=0.01).is_same
    tight = before.compare(drawing, tolerance=0.0001)
    assert len(tight.moved) == 1 and tight.moved[0].moved_by == pytest.approx((0.0005, 0.0))


def test_two_drawings_drawn_apart_are_matched_by_handle_alone() -> None:
    one, other = new_file(), new_file()
    one.add_line((0, 0), (10, 0))
    one.add_line((0, 5), (10, 5))
    one.commit()
    other.add_circle((0, 0), 5)
    other.commit()

    diff = one.compare(other)

    # Both are new drawings, so the first object of each has the handle 100: it is one object that became a circle, and the
    # second line has no match. Drawings drawn apart are not what compare is for.
    assert [(c.handle, c.kind) for c in diff.removed] == [("101", "LINE")]
    assert [(c.handle, c.kind) for c in diff.changed] == [("100", "CIRCLE")]
    assert diff.added == []


def test_what_is_queued_is_in_neither_drawing(tmp_path: Path) -> None:
    drawing = _plan()
    path = _saved(drawing, tmp_path)
    drawing.add_line((0, 300), (10, 300))

    assert drawing.compare(path).is_same
    drawing.commit()
    assert [c.kind for c in open_file(str(path)).compare(drawing).added] == ["LINE"]


# ---- 3. after a reset ---------------------------------------------------------------------------------------------------------


def test_a_drawing_is_the_same_as_its_copy_after_an_episode_of_edits_and_a_reset(tmp_path: Path) -> None:
    drawing = _plan()
    path = _saved(drawing, tmp_path)
    start = drawing.snapshot()
    generator = random.Random(7)
    for _ in range(100):
        found = drawing.entities("LINE") + drawing.entities("CIRCLE")
        kind = generator.randrange(10)
        if kind < 3 or not found:
            drawing.add_line((generator.uniform(0, 500), generator.uniform(0, 500)), (generator.uniform(0, 500), 5))
        elif kind < 6:
            drawing.move([generator.choice(found)], (generator.uniform(-9, 9), generator.uniform(-9, 9)))
        elif kind < 8:
            drawing.erase(generator.choice(found))
        else:
            generator.choice(found).layer = "Steel"
        drawing.commit()

    episode = open_file(str(path)).compare(drawing)
    assert not episode.is_same and len(episode) > 10

    drawing.reset(start)

    assert drawing.compare(path).is_same
    assert open_file(str(path)).compare(drawing).is_same


# ---- 4. what is refused -------------------------------------------------------------------------------------------------------


@pytest.mark.parametrize("tolerance", [-0.1, math.nan, math.inf, True])
def test_a_tolerance_that_is_no_length_is_refused(tolerance: float) -> None:
    drawing = _plan()

    with pytest.raises(ValueError, match="tolerance is a finite number of nought or more"):
        drawing.compare(drawing, tolerance=tolerance)


def test_a_path_with_no_file_is_a_value_error_naming_it(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="There is no file .*missing.dxf"):
        _plan().compare(tmp_path / "missing.dxf")


def test_something_that_is_no_drawing_or_path_is_a_type_error() -> None:
    with pytest.raises(TypeError, match="Drawing or the path of a DXF or DWG file"):
        _plan().compare(5)  # type: ignore[arg-type]


# ---- 5. speed ----------------------------------------------------------------------------------------------------------------


@pytest.mark.timing
def test_a_drawing_of_ten_thousand_objects_is_compared_in_well_under_two_seconds(tmp_path: Path) -> None:
    drawing = new_file()
    for index in range(10_000):
        drawing.add_line((index % 100, index // 100), (index % 100 + 0.5, index // 100 + 0.5))
    drawing.commit()
    path = _saved(drawing, tmp_path)
    before = open_file(str(path))
    before.compare(drawing)  # the display of each is made once
    drawing.move(drawing.entities("LINE")[:100], (1, 1))
    drawing.commit()

    started = time.perf_counter()
    diff = before.compare(drawing)
    seconds = time.perf_counter() - started

    assert len(diff.moved) == 100 and diff.unchanged >= 9900
    assert seconds < 2.0, seconds
    assert np.isfinite(diff.moved_by).all() if diff.moved_by is not None else True
