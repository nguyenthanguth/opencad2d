"""M2: the boxes objects take as drawn and the overlaps of words from outside the app (PW6.2): ``entity.bounds``,
``drawing.bounds()`` and ``drawing.overlaps()`` through an ``App`` on the test server give what they give on a file, number for
number, and the layout and the errors are the same.

``connect()`` finds the app in the folder of the real one, which a test never touches, so the tests make the ``App`` over the
``CadApplication`` the ``app`` fixture connected to the test server.
"""

import importlib
import math
from collections.abc import Iterator
from typing import Any

import numpy as np
import pytest

from opencad2d.cad import Box, new_file

_dxf: Any = importlib.import_module("GeometryHelper.Dxf")
_geo: Any = importlib.import_module("GeometryHelper.Geometry")


@pytest.fixture
def cad_app(app: Any) -> Iterator[Any]:
    """The ``opencad2d.cad`` App over this test's connection."""
    from opencad2d.cad import App

    with App(app) as made:
        yield made


def _the_same_drawing(drawing: Any) -> None:
    """A line, two words in an SHX style (one turned), a dimension and a line through the first word."""
    drawing.text_styles.add("NOTE", font="romans.shx")
    drawing.add_line((0, 0), (100, 40))
    drawing.add_text("Plate A-12", (10, 20), 5.0, text_style="NOTE")
    drawing.add_text("Plate A-12", (10, 80), 2.5, rotation=math.radians(30), text_style="NOTE")
    drawing.add_linear_dim((0, 100), (300, 100), (150, 130))
    drawing.add_line((28, 0), (28, 60))
    drawing.commit()


def test_the_bounds_through_the_app_are_the_ones_a_file_gives(cad_app: Any) -> None:
    remote = cad_app.new()
    local = new_file()
    _the_same_drawing(remote)
    _the_same_drawing(local)

    through, on_file = remote.bounds(), local.bounds()

    assert len(through) == len(on_file) == 5
    assert through.boxes.shape == (5, 4)
    assert np.allclose(through.boxes, on_file.boxes, atol=1e-9)
    assert through.boxes.dtype == np.float64


def test_an_entitys_bounds_through_the_app_and_none_for_one_that_draws_nothing(cad_app: Any) -> None:
    drawing = cad_app.new()
    drawing.add_line((0, 0), (100, 40))
    drawing.add_line((0, 5), (10, 5))
    pending = drawing.add_line((1, 1), (2, 2))
    with pytest.raises(ValueError, match="commit"):
        _ = pending.bounds
    first, second, third = drawing.commit()
    line, hidden = drawing.entity(first), drawing.entity(second)
    hidden.net.Visible = False
    drawing.modify(hidden)
    drawing.commit()

    assert line.bounds == Box(0.0, 0.0, 100.0, 40.0)
    assert hidden.bounds is None
    assert drawing.bounds().handles == [first, third]


def test_the_overlaps_through_the_app_are_the_ones_a_file_gives_with_the_kinds_and_the_objects_asked(cad_app: Any) -> None:
    remote = cad_app.new()
    local = new_file()
    _the_same_drawing(remote)
    _the_same_drawing(local)

    through, on_file = remote.overlaps(), local.overlaps()

    assert len(through) == 1
    assert through.words == on_file.words
    assert through.others == on_file.others
    assert np.allclose(through.boxes, on_file.boxes, atol=1e-9)
    assert len(remote.overlaps(over=["CIRCLE"])) == 0
    assert len(remote.overlaps(kinds=["LINE"])) == 0
    assert remote.overlaps(of=[through.others[0]]).words == through.words
    assert len(remote.overlaps(of=["FFFF"])) == 0
    with pytest.raises(ValueError, match="tolerance"):
        remote.overlaps(tolerance=-1.0)


def test_a_layout_of_the_app_is_asked_by_its_name_and_one_it_lacks_is_refused(cad_app: Any) -> None:
    drawing = cad_app.new()
    layout = drawing.layouts[1]
    drawing.add_line((0, 0), (10, 0))
    on_sheet = drawing.add(_dxf.DxfLine(_geo.GeoPoint3(0.0, 0.0, 0.0), _geo.GeoPoint3(100.0, 0.0, 0.0)), layout=layout)
    drawing.commit()

    assert drawing.bounds(layout=layout).handles == [on_sheet.handle]
    assert drawing.overlaps(layout=layout).words == []
    with pytest.raises(Exception, match="Nowhere"):
        drawing.bounds(layout="Nowhere")
