"""R2: comparing and sheets on a drawing of a running app (PW7.6, PW7.7): ``compare``, ``layouts`` (``add``, the page setup,
``add_viewport``), ``add_insert(..., layout=, attributes=)`` and ``plot_pdf`` give through the app what they give on a file.

A comparison reads the app's drawing as a copy of its database, as the renders do; the layout calls are the app's request ``sheets``, each
one undo step of the app; the PDF is plotted in this process from a copy of the app's drawing as it stands, changes not saved included.
``connect()`` finds the real app's folder, which a test never touches, so the tests make the ``App`` over the connection the ``app``
fixture made to the test server.
"""

import importlib
from collections.abc import Iterator
from pathlib import Path
from typing import Any

import pytest

from opencad2d.cad import Drawing, new_file, open_file

from pdf_words import words_of

_dxf: Any = importlib.import_module("GeometryHelper.Dxf")
_geo: Any = importlib.import_module("GeometryHelper.Geometry")


@pytest.fixture
def cad_app(app: Any) -> Iterator[Any]:
    """The ``opencad2d.cad`` App over this test's connection."""
    from opencad2d.cad import App

    with App(app) as made:
        yield made


def _file(tmp_path: Path) -> Path:
    """``plate.dxf``: a plate 100 by 60 with a text on a layer Notes, saved."""
    path = tmp_path / "plate.dxf"
    drawing = new_file()
    drawing.layers.add("Notes", color=3)
    drawing.commit()
    drawing.add_polyline([(0, 0), (100, 0), (100, 60), (0, 60)], closed=True)
    drawing.add_text("PLATE", (10, 20), 5.0, layer="Notes")
    drawing.commit()
    drawing.save_as(str(path))
    return path


def _title_block(drawing: Drawing) -> None:
    box = [_dxf.DxfLine(_geo.GeoPoint3(0, 0, 0), _geo.GeoPoint3(100, 0, 0))]
    attributes = [
        _dxf.DxfAttributeDefinition(_geo.GeoPoint3(2, 12, 0), "UNTITLED", "DRAWING", "Title", None),
        _dxf.DxfAttributeDefinition(_geo.GeoPoint3(2, 4, 0), "A-000", "NUMBER", "Number", None),
    ]
    for each in attributes:
        each.Height = 4.0
    drawing.blocks.add("TITLE", [*box, *attributes])


# ---- compare ---------------------------------------------------------------------------------------------------------------


def test_a_drawing_of_the_app_compares_with_its_file_and_shows_what_was_changed_and_not_saved(cad_app: Any, tmp_path: Path) -> None:
    path = _file(tmp_path)
    shown = cad_app.open(str(path))
    assert shown.compare(path).is_same

    shown.move(shown.entities("LWPOLYLINE"), (10, 0))
    shown.add_circle((50, 30), 5)
    shown.commit()

    diff = open_file(str(path)).compare(shown)
    assert [c.kind for c in diff.added] == ["CIRCLE"]
    (moved,) = diff.moved
    assert moved.kind == "LWPOLYLINE" and moved.moved_by == pytest.approx((10.0, 0.0))
    assert shown.compare(path).moved[0].moved_by == pytest.approx((-10.0, 0.0))  # this is the state before
    assert open_file(str(path)).entities("CIRCLE") == []  # the file was not written


def test_a_drawing_of_the_app_is_the_same_as_its_copy_after_a_reset(cad_app: Any, tmp_path: Path) -> None:
    path = _file(tmp_path)
    shown = cad_app.open(str(path))
    start = shown.snapshot()
    shown.add_circle((50, 30), 5)
    shown.commit()
    assert not shown.compare(path).is_same

    shown.reset(start)

    assert shown.compare(path).is_same
    start.release()


# ---- layouts ---------------------------------------------------------------------------------------------------------------


def test_a_layout_added_in_the_app_is_set_up_through_the_app_as_one_undo_step(cad_app: Any, tmp_path: Path) -> None:
    shown = cad_app.open(str(_file(tmp_path)))

    sheet = shown.layouts.add("Plate A3", paper="ISO_A3")

    assert [str(each) for each in shown.layouts] == ["Model", "Layout1", "Plate A3"]
    assert (sheet.paper, sheet.size, sheet.units, sheet.scale, sheet.plot_area) == ("ISO A3", (420.0, 297.0), "mm", "1:1", "layout")
    sheet.paper = "A2"
    sheet.landscape = False
    sheet.scale = 0.02
    sheet.plot_style = "acad.ctb"
    assert (sheet.paper, sheet.size, sheet.scale, sheet.plot_style) == ("ISO A2", (420.0, 594.0), "1:50", "acad.ctb")
    assert shown.layouts["Layout1"].paper == "ISO A3"
    assert shown.layouts.current == "Model"


def test_the_app_refuses_a_layout_in_words(cad_app: Any, tmp_path: Path) -> None:
    shown = cad_app.open(str(_file(tmp_path)))

    with pytest.raises(Exception, match="has a layout named Layout1 already"):
        shown.layouts.add("Layout1")
    with pytest.raises(Exception, match="The plotter has no sheet A7"):
        shown.layouts.add("Odd", paper="A7")
    with pytest.raises(ValueError, match="units is 'mm'"):
        shown.layouts.add("Inch", units="in")
    with pytest.raises(Exception, match="scale"):
        shown.layouts["Layout1"].scale = "1:0"
    assert [str(each) for each in shown.layouts] == ["Model", "Layout1"]


def test_a_viewport_added_in_the_app_is_at_its_scale_with_its_layers_frozen(cad_app: Any, tmp_path: Path) -> None:
    shown = cad_app.open(str(_file(tmp_path)))
    sheet = shown.layouts["Layout1"]

    handle = sheet.add_viewport((20, 30, 220, 130), "1:10", center=(50, 30), frozen_layers=["Notes"])

    assert sheet.viewports == 1
    with pytest.raises(Exception, match="no layer Missing"):
        sheet.add_viewport((20, 30, 220, 130), "1:10", frozen_layers=["Missing"])
    assert sheet.viewports == 1 and isinstance(handle, str)
    assert open_file(str(tmp_path / "plate.dxf")).layouts["Layout1"].viewports == 0  # the file was not written


# ---- a title block and a PDF -----------------------------------------------------------------------------------------------


def test_a_title_block_filled_in_the_app_plots_to_a_pdf_with_its_words(cad_app: Any, tmp_path: Path) -> None:
    shown = cad_app.open(str(_file(tmp_path)))
    sheet = shown.layouts.add("Plate A3")
    _title_block(shown)
    shown.add_insert("TITLE", (300, 10), layout=sheet, attributes={"DRAWING": "PLATE PL12", "NUMBER": "A-101"})
    sheet.add_viewport((20, 60, 280, 280), "1:1", center=(50, 30))
    shown.commit()

    info = sheet.plot_pdf(tmp_path / "plate.pdf")

    assert info.sheet == (420.0, 297.0) and info.scale == "1:1"
    words = words_of(tmp_path / "plate.pdf")
    for expected in ("PLATE PL12", "A-101", "PLATE"):
        assert expected in words, (expected, words)
    assert sheet.plot_pdf(tmp_path / "a4.pdf", paper="A4").sheet == (297.0, 210.0)


def test_attributes_for_a_tag_the_block_lacks_are_refused_before_anything_is_sent(cad_app: Any, tmp_path: Path) -> None:
    shown = cad_app.open(str(_file(tmp_path)))
    _title_block(shown)
    shown.commit()

    with pytest.raises(ValueError, match="has no attribute 'SCALE': its tags are DRAWING, NUMBER"):
        shown.add_insert("TITLE", (0, 0), attributes={"SCALE": "1:5"})
    assert shown.entities("INSERT", layout="Model") == []
