"""P2: measuring before placing from outside the app (PW7.1): ``measure_text``, ``measure_mtext``, ``measure_dimension``,
``measure_leader``, ``free_space`` and ``fits`` through an ``App`` on the test server give what they give on a file, number for
number, and what the app refuses is refused.

``connect()`` finds the app in the folder of the real one, which a test never touches, so the tests make the ``App`` over the
``CadApplication`` the ``app`` fixture connected to the test server.
"""

import math
from collections.abc import Iterator
from typing import Any

import pytest

from opencad2d.cad import Box, Frame, new_file



@pytest.fixture
def cad_app(app: Any) -> Iterator[Any]:
    """The ``opencad2d.cad`` App over this test's connection."""
    from opencad2d.cad import App

    with App(app) as made:
        yield made


def _the_same_drawing(drawing: Any) -> None:
    drawing.text_styles.add("NOTE", font="romans.shx")
    drawing.add_line((70, 0), (70, 50))
    drawing.add_text("Plate A-12", (10, 10), 5.0, text_style="NOTE")
    drawing.commit()


def _same(through: Any, on_file: Any) -> None:
    assert (through.box is None) == (on_file.box is None)
    if through.box is not None:
        assert list(through.box) == pytest.approx(list(on_file.box), abs=1e-9)
    assert len(through.frames) == len(on_file.frames)
    for a, b in zip(through.frames, on_file.frames):
        assert (a.x, a.y, a.width, a.height, a.rotation) == pytest.approx((b.x, b.y, b.width, b.height, b.rotation), abs=1e-9)


def _texts(drawing: Any) -> list[Any]:
    return [
        drawing.measure_text("Plate A-12", (10, 40), 5.0, style="NOTE", rotation=0.4, justify="middle"),
        drawing.measure_mtext("Plate PL10 of grade S355 beside it", (10, 80), 4.0, width=50.0, style="NOTE"),
    ]


def test_a_text_and_an_mtext_measured_through_the_app_are_the_ones_a_file_gives(cad_app: Any) -> None:
    remote, local = cad_app.new(), new_file()
    _the_same_drawing(remote)
    _the_same_drawing(local)

    through, on_file = _texts(remote), _texts(local)

    for one, other in zip(through, on_file):
        _same(one, other)
    assert len(through[1].frames) >= 2


def test_a_dimension_measured_through_the_app_is_the_one_a_file_gives_in_each_style(cad_app: Any) -> None:
    remote, local = cad_app.new(), new_file()
    for drawing in (remote, local):
        drawing.dimension_styles.add("BIG", text_height=7.5, arrow_size=6.0, scale=2.0)
        drawing.commit()

    kinds = [
        ("linear", [(0, 0), (100, 0), (50, 20)]),
        ("aligned", [(0, 0), (30, 40), (40, 5)]),
        ("radial", [(50, 50), (75, 50), (80, 70)]),
        ("angular", [(0, 0), (100, 0), (0, 100), (50, 50)]),
    ]
    for kind, points in kinds:
        _same(remote.measure_dimension(kind, points, dim_style="BIG"), local.measure_dimension(kind, points, dim_style="BIG"))
    _same(remote.measure_dimension("linear", kinds[0][1]), local.measure_dimension("linear", kinds[0][1]))
    big = remote.measure_dimension("linear", kinds[0][1], dim_style="BIG").box
    plain = remote.measure_dimension("linear", kinds[0][1]).box
    assert big is not None and plain is not None
    assert big.max_y - big.min_y > plain.max_y - plain.min_y + 1.0


def test_a_leader_measured_through_the_app_is_the_one_a_file_gives_and_a_style_it_lacks_is_refused(cad_app: Any) -> None:
    remote, local = cad_app.new(), new_file()

    _same(remote.measure_leader((0, 0), (30, 20), "B-12"), local.measure_leader((0, 0), (30, 20), "B-12"))
    _same(remote.measure_leader((0, 0), (30, 20), "B-12", scale=2.0), local.measure_leader((0, 0), (30, 20), "B-12", scale=2.0))
    with pytest.raises(Exception, match="multileader style NOPE"):
        remote.measure_leader((0, 0), (30, 20), "B-12", style="NOPE")


def test_free_space_and_fits_through_the_app_are_the_ones_a_file_gives(cad_app: Any) -> None:
    remote, local = cad_app.new(), new_file()
    _the_same_drawing(remote)
    _the_same_drawing(local)

    assert remote.free_space((0, 0, 100, 50)) == local.free_space((0, 0, 100, 50))
    assert remote.free_space((0, 0, 100, 50), kinds=["LINE"], margin=2.0, min_width=40.0) == [Box(0.0, 0.0, 68.0, 50.0)]
    for candidate in ((40, 20, 60, 30), (60, 20, 80, 30), Frame(40, 20, 20, 10, 0.3), Frame(55, 20, 20, 10, 0.0)):
        assert remote.fits(candidate) == local.fits(candidate)
    assert remote.fits((40, 20, 60, 30)) is True
    assert remote.fits((60, 20, 80, 30)) is False
    assert remote.fits((40, 20, 60, 30), kinds=["LINE"], margin=11.0) is False
    word = remote.measure_text("Plate A-12", (10, 10), 5.0, style="NOTE")
    assert remote.fits(word) is False


def test_what_the_app_cannot_measure_is_refused_with_its_words(cad_app: Any) -> None:
    drawing = cad_app.new()
    _the_same_drawing(drawing)

    with pytest.raises(Exception, match="NOPE"):
        drawing.measure_dimension("linear", [(0, 0), (100, 0), (50, 20)], dim_style="NOPE")
    with pytest.raises(Exception, match="Nowhere"):
        drawing.fits((0, 0, 5, 5), layout="Nowhere")
    with pytest.raises(Exception, match="Nowhere"):
        drawing.free_space((0, 0, 100, 50), layout="Nowhere")
    with pytest.raises(ValueError, match="width and a height"):
        drawing.free_space((0, 0, 0, 50))
    assert not math.isnan(drawing.measure_text("Plate", (0, 0), 5.0, style="NOTE").box.max_x)
