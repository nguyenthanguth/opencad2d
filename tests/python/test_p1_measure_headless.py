"""P1: measuring before placing, on a file with no app (PW7.1): ``measure_text``, ``measure_mtext``, ``measure_dimension`` and
``measure_leader`` give the box a thing would take, found by the code that draws it once it is added and measured by
the code that measures it there, so the box equals ``entity.bounds`` of the thing added to 1E-6; ``free_space`` gives the empty
rectangles of a frame and ``fits`` says whether a candidate place is free.

The things are made in code: SHX words level, turned and justified, an MTEXT wrapped in lines, dimensions of each kind in the
current style and in a style of their own, and a multileader with its landing run through the MLEADER command.
"""

import importlib
import math
from typing import Any

import pytest

from opencad2d.cad import Box, Drawing, Frame, Measurement, new_file

_dxf: Any = importlib.import_module("GeometryHelper.Dxf")
_geo: Any = importlib.import_module("GeometryHelper.Geometry")
_cad: Any = importlib.import_module("OpenCAD2D")


def _drawing() -> Drawing:
    drawing = new_file()
    drawing.text_styles.add("NOTE", font="romans.shx")
    drawing.commit()
    return drawing


def _equal(measured: Measurement, added: Any) -> None:
    box = added.bounds
    assert measured.box is not None and box is not None
    assert list(measured.box) == pytest.approx(list(box), abs=1e-6)


def test_a_text_measured_has_the_box_it_has_once_added_and_one_frame() -> None:
    drawing = _drawing()

    measured = drawing.measure_text("Plate A-12", (10, 20), 5.0, style="NOTE")
    text = drawing.add_text("Plate A-12", (10, 20), 5.0, text_style="NOTE")
    drawing.commit()

    _equal(measured, text)
    assert isinstance(measured, Measurement)
    assert len(measured.frames) == 1
    frame = measured.frames[0]
    assert isinstance(frame, Frame)
    assert frame.rotation == pytest.approx(0.0)
    assert frame.box.min_x == pytest.approx(10.0, abs=1e-6)
    assert frame.width > 20.0


def test_a_turned_text_has_a_turned_frame() -> None:
    drawing = _drawing()
    angle = math.radians(30)

    measured = drawing.measure_text("Plate A-12", (50, 50), 5.0, style="NOTE", rotation=angle)
    text = drawing.add_text("Plate A-12", (50, 50), 5.0, rotation=angle, text_style="NOTE")
    drawing.commit()

    _equal(measured, text)
    assert measured.frames[0].rotation == pytest.approx(angle)
    corners = measured.frames[0].corners
    assert corners[1][1] > corners[0][1]


@pytest.mark.parametrize("justify", ["left", "center", "right", "middle", "TL", "mc", "BR"])
def test_a_justified_text_stands_about_its_point_as_the_added_one_does(justify: str) -> None:
    drawing = _drawing()
    measured = drawing.measure_text("Plate A-12", (100, 100), 5.0, style="NOTE", rotation=0.2, justify=justify)

    net = _dxf.DxfDBText()
    net.TextString = "Plate A-12"
    net.Position = _geo.GeoPoint3(100.0, 100.0, 0.0)
    net.Height = 5.0
    net.Rotation = 0.2
    net.TextStyleName = "NOTE"
    codes = {"left": "BaseLeft", "center": "BaseCenter", "right": "BaseRight", "middle": "MiddleCenter"}
    codes.update({"TL": "TopLeft", "mc": "MiddleCenter", "BR": "BottomRight"})
    net.Justify = getattr(_dxf.DxfAttachmentPoint, codes[justify])
    net.AlignmentPoint = _geo.GeoPoint3(100.0, 100.0, 0.0)
    added = drawing.add(net)
    drawing.commit()

    _equal(measured, added)


def test_an_mtext_wrapped_in_lines_has_a_frame_for_each_line() -> None:
    drawing = _drawing()
    words = "Plate PL10 of grade S355 and the next plate PL12 beside it"

    measured = drawing.measure_mtext(words, (10, 80), 4.0, width=50.0, style="NOTE", rotation=0.3, justify="MC")
    net = _dxf.DxfMText()
    net.Contents = words
    net.Location = _geo.GeoPoint3(10.0, 80.0, 0.0)
    net.TextHeight = 4.0
    net.Width = 50.0
    net.TextStyleName = "NOTE"
    net.Rotation = 0.3
    net.Attachment = _dxf.DxfAttachmentPoint.MiddleCenter
    added = drawing.add(net)
    drawing.commit()

    _equal(measured, added)
    assert len(measured.frames) >= 2
    assert all(f.rotation == pytest.approx(0.3) for f in measured.frames)


@pytest.mark.parametrize(
    ("kind", "points", "extra"),
    [
        ("linear", [(0, 0), (100, 0), (50, 20)], {}),
        ("linear", [(0, 0), (60, 40), (80, 0)], {"angle": math.pi / 2}),
        ("aligned", [(0, 0), (30, 40), (40, 5)], {"text": "<> TYP"}),
        ("radial", [(50, 50), (75, 50), (80, 70)], {}),
        ("diameter", [(50, 50), (75, 50), (80, 70)], {}),
        ("angular", [(0, 0), (100, 0), (0, 100), (50, 50)], {}),
    ],
)
def test_a_dimension_of_every_kind_has_the_box_of_its_picture(kind: str, points: list[Any], extra: dict[str, Any]) -> None:
    drawing = _drawing()
    added_with = {"text": extra.get("text")}

    measured = drawing.measure_dimension(kind, points, **extra)
    made = {
        "linear": lambda: drawing.add_linear_dim(*points, angle=extra.get("angle"), **added_with),
        "aligned": lambda: drawing.add_aligned_dim(*points, **added_with),
        "radial": lambda: drawing.add_radial_dim(points[0], math.dist(points[0], points[1]), points[2]),
        "diameter": lambda: drawing.add_diameter_dim(points[0], math.dist(points[0], points[1]), points[2]),
        "angular": lambda: drawing.add_angular_dim(*points),
    }[kind]()
    drawing.commit()

    _equal(measured, made)
    assert measured.frames


def test_a_dimension_in_a_style_of_its_own_is_drawn_in_that_style() -> None:
    drawing = _drawing()
    drawing.dimension_styles.add("BIG", text_height=7.5, arrow_size=6.0, scale=2.0)
    drawing.commit()

    plain = drawing.measure_dimension("linear", [(0, 0), (100, 0), (50, 20)])
    big = drawing.measure_dimension("linear", [(0, 0), (100, 0), (50, 20)], dim_style="BIG")
    added = drawing.add_linear_dim((0, 0), (100, 0), (50, 20), dim_style="BIG")
    drawing.commit()

    _equal(big, added)
    assert plain.box is not None and big.box is not None
    assert big.box.max_y - big.box.min_y > plain.box.max_y - plain.box.min_y + 1.0


def test_a_measure_queues_nothing_and_takes_no_handle() -> None:
    drawing = _drawing()
    line = drawing.add_line((0, 0), (10, 0))
    drawing.commit()
    before = drawing.net.Database.ToText()

    drawing.measure_text("Plate", (10, 20), 5.0, style="NOTE")
    drawing.measure_mtext("Plate", (10, 20), 5.0, style="NOTE")
    drawing.measure_dimension("linear", [(0, 0), (100, 0), (50, 20)])
    queued = drawing.add_line((0, 5), (10, 5))
    ticket = queued.handle
    handles = drawing.commit()

    assert ticket == "~2"  # the line was ~1: the measures took no ticket
    assert int(handles[0], 16) == int(line.handle, 16) + 1
    assert [e.handle for e in drawing.entities()] == [line.handle, handles[0]]


def test_a_measure_leaves_the_text_of_the_drawing_as_it_was() -> None:
    drawing = _drawing()
    drawing.add_line((0, 0), (10, 0))
    drawing.commit()
    before = drawing.net.Database.ToText()

    drawing.measure_text("Plate", (10, 20), 5.0, style="NOTE")
    drawing.measure_dimension("aligned", [(0, 0), (30, 40), (40, 5)])
    drawing.fits((0, 0, 5, 5))
    drawing.free_space((0, 0, 50, 50))

    assert drawing.net.Database.ToText() == before


def test_a_style_that_is_only_queued_is_refused_until_it_is_committed() -> None:
    drawing = new_file()
    drawing.text_styles.add("LATE", font="romans.shx")

    with pytest.raises(ValueError, match="LATE"):
        drawing.measure_text("Plate", (0, 0), 5.0, style="LATE")
    drawing.commit()
    assert drawing.measure_text("Plate", (0, 0), 5.0, style="LATE").box is not None
    with pytest.raises(ValueError, match="NOPE"):
        drawing.measure_dimension("linear", [(0, 0), (100, 0), (50, 20)], dim_style="NOPE")


def test_what_cannot_be_measured_is_a_value_error_in_words() -> None:
    drawing = _drawing()

    with pytest.raises(ValueError, match="height"):
        drawing.measure_text("x", (0, 0), 0.0)
    with pytest.raises(ValueError, match="finite"):
        drawing.measure_text("x", (float("nan"), 0), 1.0)
    with pytest.raises(ValueError, match="justification"):
        drawing.measure_text("x", (0, 0), 1.0, justify="sideways")
    with pytest.raises(ValueError, match="justification"):
        drawing.measure_mtext("x", (0, 0), 1.0, justify="left")
    with pytest.raises(ValueError, match="kind"):
        drawing.measure_dimension("curly", [(0, 0), (1, 1), (2, 2)])
    with pytest.raises(ValueError, match="3 points"):
        drawing.measure_dimension("linear", [(0, 0), (1, 1)])
    with pytest.raises(ValueError, match="landing"):
        drawing.measure_leader((0, 0), (0, 0), "x")
    with pytest.raises(ValueError, match="style"):
        drawing.measure_leader((0, 0), (10, 10), "x", style="NOPE")


def test_a_text_of_no_words_has_no_box_and_no_frame() -> None:
    drawing = _drawing()

    measured = drawing.measure_text("", (0, 0), 5.0, style="NOTE")

    assert measured.box is None
    assert measured.frames == []
    with pytest.raises(ValueError, match="draws nothing"):
        drawing.fits(measured)


def test_a_multileader_with_its_landing_has_the_box_the_command_makes() -> None:
    drawing = new_file()
    document: Any = drawing.net
    measured = drawing.measure_leader((0, 0), (30, 20), "B-12")

    session = _cad.CadCommandSession(document)
    session.Feed("MLD")
    session.Feed(_geo.GeoPoint2(0.0, 0.0))
    session.Feed(_geo.GeoPoint2(30.0, 20.0))
    session.Feed("B-12")
    leader = [e for e in drawing.entities() if e.dxf_type == "MULTILEADER"][0]

    _equal(measured, leader)
    assert measured.box is not None and measured.box.min_x == pytest.approx(0.0, abs=1e-6)
    assert measured.box.max_x > 30.0
    assert measured.frames
    scaled = drawing.measure_leader((0, 0), (30, 20), "B-12", scale=2.0)
    assert scaled.box is not None and scaled.box.max_x - scaled.box.min_x > measured.box.max_x - measured.box.min_x


# ---- free space and fits ------------------------------------------------------------------------------------------------


def test_the_free_space_of_a_frame_with_a_line_across_it_is_two_boxes_the_larger_first() -> None:
    drawing = new_file()
    drawing.add_line((40, -10), (40, 70))
    drawing.commit()

    places = drawing.free_space((0, 0, 100, 50))

    assert places == [Box(40.0, 0.0, 100.0, 50.0), Box(0.0, 0.0, 40.0, 50.0)]
    assert drawing.free_space(Box(0, 0, 100, 50), kinds=["TEXT"]) == [Box(0.0, 0.0, 100.0, 50.0)]
    assert drawing.free_space((0, 0, 100, 50), margin=5.0) == [Box(45.0, 0.0, 100.0, 50.0), Box(0.0, 0.0, 35.0, 50.0)]
    assert drawing.free_space((0, 0, 100, 50), min_width=50.0) == [Box(40.0, 0.0, 100.0, 50.0)]


def test_free_space_around_words_leaves_the_frame_of_the_words_out() -> None:
    drawing = _drawing()
    drawing.add_line((70, 0), (70, 50))
    drawing.commit()
    measured = drawing.measure_text("Plate A-12", (10, 10), 5.0, style="NOTE")
    drawing.add_text("Plate A-12", (10, 10), 5.0, text_style="NOTE")
    drawing.commit()
    frame = measured.frames[0].box

    places = drawing.free_space((0, 0, 100, 50))

    assert places
    for place in places:
        entered = (
            place.min_x < frame.max_x - 1e-6
            and place.max_x > frame.min_x + 1e-6
            and place.min_y < frame.max_y - 1e-6
            and place.max_y > frame.min_y + 1e-6
        )
        assert not entered, place


def test_fits_says_whether_a_place_is_free_for_a_box_a_frame_and_a_measurement() -> None:
    drawing = _drawing()
    drawing.add_line((70, 0), (70, 50))
    drawing.add_text("Plate A-12", (10, 10), 5.0, text_style="NOTE")
    drawing.commit()

    assert drawing.fits((40, 20, 60, 30)) is True
    assert drawing.fits((60, 20, 80, 30)) is False
    assert drawing.fits(Box(40, 20, 60, 30), kinds=["LINE"], margin=9.0) is True
    assert drawing.fits(Box(40, 20, 60, 30), kinds=["LINE"], margin=11.0) is False
    assert drawing.fits(Frame(40, 20, 20, 10, 0.3)) is True
    assert drawing.fits(Frame(55, 20, 20, 10, 0.0)) is False
    assert drawing.fits((60, 20, 80, 30), kinds=["TEXT"]) is True
    word = drawing.measure_text("Plate A-12", (10, 10), 5.0, style="NOTE")
    assert drawing.fits(word) is False
    assert drawing.fits(drawing.measure_text("Plate A-12", (10, 30), 5.0, style="NOTE"), margin=2.0) is True


def test_a_measured_frame_is_a_candidate_and_a_place_found_free_is_taken() -> None:
    drawing = _drawing()
    drawing.add_line((0, 25), (100, 25))
    drawing.commit()
    wide = drawing.measure_text("Plate A-12", (0, 0), 5.0, style="NOTE").box
    assert wide is not None
    width, height = wide.max_x - wide.min_x, wide.max_y - wide.min_y

    places = drawing.free_space((0, 0, 100, 50), min_width=width, min_height=height)
    corner = places[0]
    where = (corner.min_x, corner.min_y)
    placed = drawing.measure_text("Plate A-12", where, 5.0, style="NOTE")

    assert placed.box is not None
    assert drawing.fits(placed) is True
    drawing.add_text("Plate A-12", where, 5.0, text_style="NOTE")
    drawing.commit()
    assert len(drawing.overlaps()) == 0


def test_free_space_and_fits_refuse_what_is_no_frame_or_candidate() -> None:
    drawing = new_file()

    with pytest.raises(ValueError, match="frame"):
        drawing.free_space((0, 0, 100))
    with pytest.raises(ValueError, match="width and a height"):
        drawing.free_space((0, 0, 0, 50))
    with pytest.raises(ValueError, match="margin"):
        drawing.free_space((0, 0, 100, 50), margin=-1.0)
    with pytest.raises(ValueError, match="tolerance"):
        drawing.free_space((0, 0, 100, 50), tolerance=-1.0)
    with pytest.raises(ValueError, match="finite"):
        drawing.free_space((0, 0, float("inf"), 50))
    with pytest.raises(ValueError, match="width and a height"):
        drawing.fits((0, 0, 0, 5))
    with pytest.raises(ValueError, match="margin"):
        drawing.fits((0, 0, 5, 5), margin=-1.0)
    with pytest.raises(ValueError, match="Nowhere"):
        drawing.fits((0, 0, 5, 5), layout="Nowhere")
    with pytest.raises(ValueError, match="Nowhere"):
        drawing.free_space((0, 0, 100, 50), layout="Nowhere")


def test_the_frame_of_a_word_gives_its_corners_and_its_box() -> None:
    frame = Frame(10, 30, 20, 5, math.pi / 2)

    assert [tuple(round(n, 9) for n in corner) for corner in frame.corners] == [
        (10.0, 30.0),
        (10.0, 50.0),
        (5.0, 50.0),
        (5.0, 30.0),
    ]
    assert tuple(round(n, 9) for n in frame.box) == (5.0, 30.0, 10.0, 50.0)
    assert Frame(0, 0, 4, 2).box == Box(0.0, 0.0, 4.0, 2.0)
