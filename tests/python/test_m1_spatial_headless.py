"""M1: the boxes objects take as drawn and the overlaps of words, on a file with no app (PW6.2): ``entity.bounds``,
``drawing.bounds()`` and ``drawing.overlaps()`` over a ``CadDocument``.

The boxes are held to the display list's own, within 1E-6, on drawings made in code: a line, an SHX word turned, a dimension, a
block inside a block scaled and turned, an MTEXT in two columns, a wide polyline. The overlaps are tested on a text and what is
drawn through, beside and around it, at the tolerance, with the kinds and the objects asked.
"""

import importlib
import math
from typing import Any

import numpy as np
import pytest

from opencad2d.cad import Box, BoundsTable, Drawing, OverlapTable, new_file

_dxf: Any = importlib.import_module("GeometryHelper.Dxf")
_geo: Any = importlib.import_module("GeometryHelper.Geometry")


def _boxes_of_the_list(drawing: Drawing) -> dict[str, tuple[float, float, float, float]]:
    """The union of the boxes of the items of each handle in the display list: what ``bounds()`` must equal."""
    document: Any = drawing.net
    union: dict[str, list[float]] = {}
    for item in document.GetDisplay(document.ModelSpace).Items:
        handle = str(item.Handle)
        box = item.Bounds
        if handle not in union:
            union[handle] = [box.MinX, box.MinY, box.MaxX, box.MaxY]
        else:
            now = union[handle]
            union[handle] = [min(now[0], box.MinX), min(now[1], box.MinY), max(now[2], box.MaxX), max(now[3], box.MaxY)]
    return {handle: (b[0], b[1], b[2], b[3]) for handle, b in union.items()}


def _a_drawing_of_every_kind() -> Drawing:
    drawing = new_file()
    drawing.text_styles.add("NOTE", font="romans.shx")
    drawing.add_line((0, 0), (100, 40))
    drawing.add_text("Plate A-12", (10, 20), 2.5, text_style="NOTE")
    drawing.add_text("Plate A-12", (10, 60), 2.5, rotation=math.radians(30), text_style="NOTE")
    drawing.add_linear_dim((0, 100), (300, 100), (150, 130))
    columns = _dxf.DxfMText()
    columns.Contents = r"Alpha beta\PGamma delta\PEpsilon zeta\PEta theta"
    columns.Location = _geo.GeoPoint3(0.0, 200.0, 0.0)
    columns.TextHeight = 5.0
    columns.Width = 60.0
    columns.TextStyleName = "NOTE"
    columns.Height = 20.0
    columns.SetStaticColumns(60.0, 10.0, 2)
    drawing.add(columns)
    inner = _dxf.DxfBlockTableRecord()
    inner.Name = "INNER"
    nested = _dxf.DxfBlockReference(_geo.GeoPoint3(5.0, 5.0, 0.0), inner)
    nested.ScaleFactors = _geo.GeoVector3(2.0, 2.0, 1.0)
    nested.Rotation = math.pi / 2.0
    drawing.blocks.add("INNER", [_dxf.DxfLine(_geo.GeoPoint3(0.0, 0.0, 0.0), _geo.GeoPoint3(10.0, 0.0, 0.0))])
    drawing.blocks.add("OUTER", [nested])
    drawing.add_insert("OUTER", (100, 300), scale=3.0)
    drawing.add_hatch([(200, 10), (230, 10), (230, 40), (200, 40)], "SOLID")
    drawing.commit()
    return drawing


def test_a_lines_bounds_are_its_ends_and_an_object_added_has_none_until_commit() -> None:
    drawing = new_file()
    line = drawing.add_line((0, 0), (100, 40))

    with pytest.raises(ValueError, match="commit"):
        _ = line.bounds
    drawing.commit()

    assert line.bounds == Box(0.0, 0.0, 100.0, 40.0)
    assert drawing.entity(line.handle).bounds == Box(0.0, 0.0, 100.0, 40.0)


def test_an_object_that_draws_nothing_has_no_bounds_and_no_row() -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (10, 0))
    drawing.add_line((0, 5), (10, 5))
    first, second = drawing.commit()
    hidden, shown = drawing.entity(first), drawing.entity(second)
    hidden.net.Visible = False
    drawing.modify(hidden)
    drawing.commit()

    assert hidden.bounds is None
    assert shown.bounds == Box(0.0, 5.0, 10.0, 5.0)
    assert drawing.bounds().handles == [shown.handle]


def test_bounds_is_a_table_of_handles_and_a_numpy_array_of_n_by_4_in_the_order_the_drawing_draws() -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (1, 0))
    drawing.add_circle((50, 50), 10)
    drawing.add_line((0, 9), (1, 9))
    handles = drawing.commit()

    table = drawing.bounds()

    assert isinstance(table, BoundsTable)
    assert table.handles == handles
    assert len(table) == 3
    assert table.boxes.dtype == np.float64
    assert table.boxes.shape == (3, 4)
    assert table.boxes.tolist() == [[0.0, 0.0, 1.0, 0.0], [40.0, 40.0, 60.0, 60.0], [0.0, 9.0, 1.0, 9.0]]


def test_the_bounds_of_every_kind_equal_the_display_lists_within_a_millionth() -> None:
    drawing = _a_drawing_of_every_kind()

    table = drawing.bounds()

    wanted = _boxes_of_the_list(drawing)
    assert len(table) >= 6
    for handle, row in zip(table.handles, table.boxes):
        assert row.tolist() == pytest.approx(list(wanted[handle]), abs=1e-6), handle
    # Every drawn handle has its row.
    assert sorted(table.handles) == sorted(h for h, b in wanted.items() if b[0] <= b[2])


def test_a_turned_word_is_taller_than_a_level_one_and_the_box_is_the_letters_not_the_point() -> None:
    drawing = new_file()
    drawing.text_styles.add("NOTE", font="romans.shx")
    level = drawing.add_text("Plate A-12", (10, 20), 2.5, text_style="NOTE")
    turned = drawing.add_text("Plate A-12", (10, 60), 2.5, rotation=math.radians(30), text_style="NOTE")
    drawing.commit()

    flat, tilt = level.bounds, turned.bounds

    assert flat is not None and tilt is not None
    assert flat.max_x - flat.min_x > 10.0
    assert tilt.max_y - tilt.min_y > 2.0 * (flat.max_y - flat.min_y)


def test_bounds_of_a_layout_and_of_a_layout_that_is_not_there() -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (10, 0))
    on_sheet = drawing.add(_dxf.DxfLine(_geo.GeoPoint3(0.0, 0.0, 0.0), _geo.GeoPoint3(100.0, 0.0, 0.0)), layout=drawing.layouts[1])
    drawing.commit()

    sheet = drawing.bounds(layout=drawing.layouts[1])

    assert sheet.handles == [on_sheet.handle]
    assert sheet.boxes.tolist() == [[0.0, 0.0, 100.0, 0.0]]
    assert on_sheet.bounds == Box(0.0, 0.0, 100.0, 0.0)
    with pytest.raises(ValueError, match="Nowhere"):
        drawing.bounds(layout="Nowhere")


def _text_with_a_line_through(drawing: Drawing) -> tuple[Any, Any]:
    drawing.text_styles.add("NOTE", font="romans.shx")
    text = drawing.add_text("Plate A-12", (10, 20), 5.0, text_style="NOTE")
    drawing.commit()
    box = text.bounds
    assert box is not None
    middle = (box.min_x + box.max_x) / 2.0
    line = drawing.add_line((middle, 0), (middle, 60))
    drawing.commit()
    return text, line


def test_a_line_through_a_text_is_one_row_with_the_piece_of_the_line_in_the_frame_as_its_box() -> None:
    drawing = new_file()
    text, line = _text_with_a_line_through(drawing)

    hits = drawing.overlaps()

    assert isinstance(hits, OverlapTable)
    assert hits.words == [text.handle]
    assert hits.others == [line.handle]
    assert len(hits) == 1
    assert hits.boxes.shape == (1, 4)
    x0, y0, x1, y1 = hits.boxes[0]
    assert x0 == pytest.approx(x1)
    assert 20.0 - 2.0 < y0 < y1 < 20.0 + 6.0


def test_a_line_that_only_touches_the_words_within_the_tolerance_is_no_overlap() -> None:
    drawing = new_file()
    drawing.text_styles.add("NOTE", font="romans.shx")
    text = drawing.add_text("Plate A-12", (10, 20), 5.0, text_style="NOTE")
    drawing.commit()
    hits = drawing.overlaps(tolerance=0.001)
    assert len(hits) == 0
    box = text.bounds
    assert box is not None

    # The frame of the words ends at the box's top: a line along it touches, a line a hundredth inside enters.
    drawing.add_line((0, box.max_y), (100, box.max_y))
    drawing.commit()
    assert len(drawing.overlaps(tolerance=0.001)) == 0
    drawing.add_line((0, box.max_y - 0.01), (100, box.max_y - 0.01))
    drawing.commit()
    assert len(drawing.overlaps(tolerance=0.001)) == 1
    assert len(drawing.overlaps(tolerance=0.05)) == 0


def test_the_kinds_say_which_words_and_what_they_are_over() -> None:
    drawing = new_file()
    text, line = _text_with_a_line_through(drawing)
    box = text.bounds
    assert box is not None
    drawing.add_circle(((box.min_x + box.max_x) / 2.0, 22.0), 3.0)
    drawing.commit()

    assert len(drawing.overlaps()) == 2
    assert drawing.overlaps(over=["circle"]).others != [line.handle]
    assert len(drawing.overlaps(over=["circle"])) == 1
    assert len(drawing.overlaps(over=["LINE"])) == 1
    assert len(drawing.overlaps(kinds=["MTEXT"])) == 0
    assert len(drawing.overlaps(kinds=["LINE"])) == 0


def test_of_gives_the_pairs_an_object_stands_in_whichever_side() -> None:
    drawing = new_file()
    text, line = _text_with_a_line_through(drawing)
    far = drawing.add_line((500, 500), (600, 500))
    drawing.commit()

    assert drawing.overlaps(of=[line]).words == [text.handle]
    assert drawing.overlaps(of=[text.handle]).others == [line.handle]
    assert len(drawing.overlaps(of=[far])) == 0
    assert len(drawing.overlaps(of=["FFFF"])) == 0


def test_two_words_that_share_an_area_are_one_row_the_one_drawn_first_named_as_the_words() -> None:
    drawing = new_file()
    drawing.text_styles.add("NOTE", font="romans.shx")
    first = drawing.add_text("Plate A-12", (10, 20), 5.0, text_style="NOTE")
    second = drawing.add_text("Plate A-12", (12, 21), 5.0, text_style="NOTE")
    drawing.commit()

    hits = drawing.overlaps()

    assert hits.words == [first.handle]
    assert hits.others == [second.handle]


def test_a_dimension_is_not_over_its_own_lines() -> None:
    drawing = new_file()
    drawing.add_linear_dim((0, 0), (300, 0), (150, -40))
    drawing.commit()

    assert len(drawing.overlaps()) == 0


def test_a_bad_tolerance_is_a_value_error() -> None:
    drawing = new_file()

    with pytest.raises(ValueError, match="tolerance"):
        drawing.overlaps(tolerance=-1.0)
    with pytest.raises(ValueError, match="tolerance"):
        drawing.overlaps(tolerance=math.nan)


def test_a_thousand_objects_answer_at_once_and_twice_the_same() -> None:
    drawing = new_file()
    drawing.text_styles.add("NOTE", font="romans.shx")
    for i in range(300):
        drawing.add_text("Plate", (i * 7 % 200, i * 11 % 200), 4.0, text_style="NOTE")
        drawing.add_line((i * 13 % 200, 0), (i * 5 % 200, 200))
    drawing.commit()

    first = drawing.overlaps()
    second = drawing.overlaps()

    assert len(first) > 100
    assert first.words == second.words
    assert first.others == second.others
    assert first.boxes.tolist() == second.boxes.tolist()
    assert drawing.bounds().boxes.shape == (600, 4)


@pytest.mark.parametrize("change", ["off", "frozen"])
def test_a_layer_switched_off_or_frozen_on_a_file_takes_its_objects_out_of_the_bounds_and_the_overlaps(change: str) -> None:
    drawing = new_file()
    drawing.text_styles.add("NOTE", font="romans.shx")
    drawing.layers.add("Steel")
    text = drawing.add_text("Plate A-12", (10, 20), 5.0, text_style="NOTE")
    drawing.commit()
    box = text.bounds
    assert box is not None
    across = (box.min_x + box.max_x) / 2.0
    steel = drawing.add_line((across, 0), (across, 60), layer="Steel")
    drawing.commit()
    assert steel.bounds == Box(across, 0.0, across, 60.0)
    assert len(drawing.overlaps()) == 1

    if change == "off":
        drawing.layers["Steel"].is_on = False
    else:
        drawing.layers["Steel"].is_frozen = True
    drawing.commit()

    assert steel.bounds is None
    assert steel.handle not in drawing.bounds().handles
    assert len(drawing.overlaps()) == 0

    if change == "off":
        drawing.layers["Steel"].is_on = True
    else:
        drawing.layers["Steel"].is_frozen = False
    drawing.commit()

    assert steel.bounds == Box(across, 0.0, across, 60.0)
    assert len(drawing.overlaps()) == 1
