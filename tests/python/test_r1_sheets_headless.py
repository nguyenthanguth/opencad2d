"""R1: sheets from code on a file with no app (PW7.7): ``drawing.layouts`` as a table, a layout's page setup, ``add_viewport`` at a
scale, ``add_insert(..., layout=, attributes=)`` for a title block, and ``layout.plot_pdf`` through the plot pipeline.

A layout is its name as a ``str`` too, so what took ``drawing.layouts[1]`` as a name still does. A write commits what is queued first
and is one undo step. The PDF of a sheet is read back for the words of its title block.
"""

import importlib
import math
from pathlib import Path
from typing import Any

import pytest

from opencad2d.cad import Drawing, Layout, Layouts, PlotInfo, new_file, open_file

from pdf_words import words_of

_dxf: Any = importlib.import_module("GeometryHelper.Dxf")
_geo: Any = importlib.import_module("GeometryHelper.Geometry")


def _plate() -> Drawing:
    """A plate 100 by 60 in model space, a layer Notes and a text on it."""
    drawing = new_file()
    drawing.layers.add("Notes", color=3)
    drawing.commit()
    drawing.add_polyline([(0, 0), (100, 0), (100, 60), (0, 60)], closed=True)
    drawing.add_text("PLATE", (10, 20), 5.0, layer="Notes")
    drawing.commit()
    return drawing


def _title_block(drawing: Drawing, constant: bool = False, preset: bool = False) -> None:
    """A block TITLE of a box and three attributes: DRAWING and NUMBER with defaults, and SCALE, constant where asked; NUMBER
    preset (its default taken without asking) where asked."""
    box = [
        _dxf.DxfLine(_geo.GeoPoint3(0, 0, 0), _geo.GeoPoint3(100, 0, 0)),
        _dxf.DxfLine(_geo.GeoPoint3(100, 0, 0), _geo.GeoPoint3(100, 20, 0)),
    ]
    attributes = [
        _dxf.DxfAttributeDefinition(_geo.GeoPoint3(2, 12, 0), "UNTITLED", "DRAWING", "Title", None),
        _dxf.DxfAttributeDefinition(_geo.GeoPoint3(2, 4, 0), "A-000", "NUMBER", "Number", None),
        _dxf.DxfAttributeDefinition(_geo.GeoPoint3(60, 4, 0), "1:1", "SCALE", "Scale", None),
    ]
    for each in attributes:
        each.Height = 4.0
    attributes[2].Constant = constant
    attributes[1].Preset = preset
    drawing.blocks.add("TITLE", [*box, *attributes])


def _attributes_of(drawing: Drawing, layout: str | None = None) -> list[dict[str, str]]:
    """The tag and words of the attributes of each INSERT in a layout, in the order of the inserts."""
    found: list[dict[str, str]] = []
    for insert in drawing.entities("INSERT", layout=layout):
        found.append({str(a.Tag): str(a.TextString) for a in insert.net.AttributeCollection})
    return found


def _viewports(drawing: Drawing, layout: str) -> list[Any]:
    """The viewports of a layout that show model space, as the library's objects."""
    database: Any = drawing.net.Database
    block = next(each.BlockTableRecord for each in database.Layouts if each.LayoutName == layout)
    return [v for v in block if isinstance(v, _dxf.DxfViewport) and not v.IsErased and v.Number > 1]


def _undo_steps(drawing: Drawing) -> int:
    """How many undo steps the drawing can take back."""
    document: Any = drawing.net
    steps = 0
    while document.CanUndo:
        assert document.Undo()
        steps += 1
    return steps


# ---- 1. the table of layouts ---------------------------------------------------------------------------------------------


def test_the_layouts_of_a_new_drawing_are_a_table_of_layouts_that_are_their_names() -> None:
    drawing = new_file()
    layouts = drawing.layouts

    assert isinstance(layouts, Layouts)
    assert [str(layout) for layout in layouts] == ["Model", "Layout1"]
    assert len(layouts) == 2
    assert "layout1" in layouts and "Nope" not in layouts and 5 not in layouts
    assert isinstance(layouts[1], Layout) and layouts[1] == "Layout1" and layouts["LAYOUT1"] == "Layout1"
    assert layouts[1].name == "Layout1" and repr(layouts[1]) == "<Layout Layout1>"
    assert repr(layouts) == "['Model', 'Layout1']"
    with pytest.raises(KeyError, match="has no layout 'Nope'"):
        layouts["Nope"]
    with pytest.raises(IndexError):
        layouts[5]


def test_a_layout_goes_where_a_layout_name_goes(tmp_path: Path) -> None:
    drawing = _plate()
    sheet = drawing.layouts[1]
    drawing.add(_dxf.DxfLine(_geo.GeoPoint3(0, 0, 0), _geo.GeoPoint3(50, 0, 0)), layout=sheet)
    drawing.commit()

    assert len(drawing.entities("LINE", layout=sheet)) == 1
    assert drawing.bounds(layout=sheet).boxes.shape == (1, 4)
    drawing.render_png(str(tmp_path / "sheet.png"), layout=sheet, width=200)
    assert (tmp_path / "sheet.png").stat().st_size > 0
    assert drawing.current_layout == "Model"
    drawing.layouts.current = sheet
    assert drawing.current_layout == "Layout1" and drawing.layouts.current is not None
    assert drawing.layouts.current == "Layout1" and drawing.layouts["Layout1"].is_current
    assert not drawing.layouts["Model"].is_current


# ---- 2. adding a layout ------------------------------------------------------------------------------------------------


def test_a_layout_added_has_its_sheet_in_millimetres_at_one_to_one_and_is_not_shown() -> None:
    drawing = _plate()

    sheet = drawing.layouts.add("Plate A3", paper="ISO_A3")

    assert sheet == "Plate A3" and [str(each) for each in drawing.layouts] == ["Model", "Layout1", "Plate A3"]
    assert (sheet.paper, sheet.size, sheet.units, sheet.landscape) == ("ISO A3", (420.0, 297.0), "mm", True)
    assert (sheet.scale, sheet.plot_area, sheet.viewports, sheet.is_model) == ("1:1", "layout", 0, False)
    assert drawing.current_layout == "Model"
    assert drawing.layouts["Model"].is_model


def test_a_layout_added_is_one_undo_step_that_takes_it_away() -> None:
    drawing = new_file()
    drawing.layouts.add("Detail", paper="A4")
    drawing.layouts.add("Other")

    drawing.net.Undo()

    assert [str(each) for each in drawing.layouts] == ["Model", "Layout1", "Detail"]
    assert drawing.layouts["Detail"].paper == "ISO A4" and drawing.layouts["detail"].size == (297.0, 210.0)


def test_the_paper_is_named_as_the_plotter_does_in_any_of_three_forms() -> None:
    drawing = new_file()

    assert drawing.layouts.add("One", paper="ISO_A2").paper == "ISO A2"
    assert drawing.layouts.add("Two", paper="iso a1").size == (841.0, 594.0)
    assert drawing.layouts.add("Three", paper="ANSI_B").size == (431.8, 279.4)


def test_a_layout_is_refused_in_words_and_the_drawing_is_left_as_it_was() -> None:
    drawing = new_file()
    layouts = drawing.layouts

    for bad in ("", "  ", "Model", "model", "Layout1", "layout1", "a/b", "a:b", "x" * 256):
        with pytest.raises(ValueError):
            layouts.add(bad)
    with pytest.raises(ValueError, match="has a layout named Layout1 already"):
        layouts.add("Layout1")
    with pytest.raises(ValueError, match="The plotter has no sheet A7: its sheets are ISO A0, .*ANSI E"):
        layouts.add("Odd", paper="A7")
    with pytest.raises(ValueError, match="units is 'mm', not 'in'"):
        layouts.add("Inch", units="in")
    assert [str(each) for each in layouts] == ["Model", "Layout1"]


def test_a_layout_commits_what_is_queued_first() -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (10, 0))

    drawing.layouts.add("Detail")

    assert len(drawing.entities("LINE")) == 1
    assert drawing.net.CanUndo and _undo_steps(drawing) == 2


# ---- 3. the page setup -------------------------------------------------------------------------------------------------


def test_the_page_setup_is_set_a_property_at_a_time_and_read_back() -> None:
    drawing = _plate()
    sheet = drawing.layouts["Layout1"]

    sheet.paper = "A2"
    assert (sheet.paper, sheet.size) == ("ISO A2", (594.0, 420.0))
    sheet.landscape = False
    assert (sheet.landscape, sheet.size) == (False, (420.0, 594.0))
    sheet.landscape = True
    sheet.plot_area = "extents"
    assert sheet.plot_area == "extents"
    sheet.scale = "1:50"
    assert sheet.scale == "1:50"
    sheet.scale = 0.02
    assert sheet.scale == "1:50"
    sheet.scale = 2
    assert sheet.scale == "2:1"
    sheet.scale = "fit"
    assert sheet.scale == "fit"
    sheet.plot_style = "acad.ctb"
    assert sheet.plot_style == "acad.ctb"
    sheet.plot_style = ""
    assert sheet.plot_style == ""
    assert drawing.layouts["Model"].paper  # model space has a page setup too


def test_the_setup_of_one_layout_leaves_the_others_alone() -> None:
    drawing = new_file()
    other = drawing.layouts.add("Other")

    drawing.layouts["Layout1"].scale = "1:20"

    assert other.scale == "1:1" and drawing.layouts["Layout1"].scale == "1:20"


def test_a_page_setup_is_refused_in_words() -> None:
    sheet = new_file().layouts["Layout1"]

    for bad in (0, -1, math.nan, math.inf, True):
        with pytest.raises(ValueError, match="A scale is a number above nought"):
            sheet.scale = bad  # type: ignore[assignment]
    for bad_text in ("big", "1:0", "0:1", "1:2:3", ""):
        with pytest.raises(ValueError, match="A scale is two numbers above nought"):
            sheet.scale = bad_text
    with pytest.raises(ValueError, match="The area is one of layout, extents, display, limits: window is not"):
        sheet.plot_area = "window"
    with pytest.raises(ValueError, match="The plotter has no sheet"):
        sheet.paper = "A9"
    assert (sheet.paper, sheet.scale, sheet.plot_area) == ("ISO A3", "1:1", "layout")


def test_a_layout_that_is_gone_is_a_key_error() -> None:
    drawing = new_file()
    sheet = drawing.layouts.add("Gone")
    drawing.net.Undo()

    with pytest.raises(KeyError, match="has no layout 'Gone'"):
        sheet.paper


# ---- 4. viewports ----------------------------------------------------------------------------------------------------------


def test_a_viewport_at_one_to_ten_shows_ten_times_its_frame_centred_where_asked() -> None:
    drawing = _plate()
    sheet = drawing.layouts["Layout1"]

    handle = sheet.add_viewport((20, 30, 220, 130), "1:10", center=(50, 30))

    (viewport,) = _viewports(drawing, "Layout1")
    assert handle == str(viewport.Handle) and sheet.viewports == 1
    assert (viewport.Width, viewport.Height) == (200.0, 100.0)
    assert (viewport.CenterPoint.X, viewport.CenterPoint.Y) == (120.0, 80.0)
    assert viewport.ViewHeight == pytest.approx(1000.0)
    assert (viewport.ViewCenter.X, viewport.ViewCenter.Y) == (50.0, 30.0)


def test_a_scale_may_be_text_or_paper_millimetres_to_a_unit_and_a_frame_any_two_corners() -> None:
    drawing = _plate()
    sheet = drawing.layouts["Layout1"]

    sheet.add_viewport([220, 130, 20, 30], 0.2)
    sheet.add_viewport((20, 140, 220, 240), "1:5")

    first, second = _viewports(drawing, "Layout1")
    assert first.ViewHeight == pytest.approx(500.0) and second.ViewHeight == pytest.approx(500.0)
    assert (first.Width, first.Height) == (200.0, 100.0)


def test_a_viewport_with_no_centre_shows_the_middle_of_model_space() -> None:
    drawing = _plate()

    drawing.layouts["Layout1"].add_viewport((10, 10, 110, 70), 0.5)

    (viewport,) = _viewports(drawing, "Layout1")
    assert (viewport.ViewCenter.X, viewport.ViewCenter.Y) == pytest.approx((50.0, 30.0))
    assert viewport.ViewHeight == pytest.approx(120.0)


def test_a_viewport_freezes_the_layers_named_in_itself_alone() -> None:
    drawing = _plate()
    sheet = drawing.layouts["Layout1"]

    sheet.add_viewport((0, 0, 100, 50), "1:5", frozen_layers=["notes"])
    sheet.add_viewport((120, 0, 220, 50), "1:5")

    first, second = _viewports(drawing, "Layout1")
    assert [str(layer.Name) for layer in first.GetFrozenLayers()] == ["Notes"]
    assert list(second.GetFrozenLayers()) == []


def test_a_viewport_is_one_undo_step_and_commits_what_is_queued_first() -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (100, 0))
    drawing.add_line((0, 0), (0, 100))

    drawing.layouts["Layout1"].add_viewport((0, 0, 100, 100), "1:1")

    assert len(drawing.entities("LINE")) == 2
    assert drawing.net.Undo() and _viewports(drawing, "Layout1") == []
    assert len(drawing.entities("LINE")) == 2


def test_a_viewport_is_refused_in_words_and_leaves_the_layout_as_it_was() -> None:
    drawing = _plate()
    sheet = drawing.layouts["Layout1"]
    frame = (0, 0, 100, 50)

    with pytest.raises(ValueError, match="Model is not a sheet"):
        drawing.layouts["Model"].add_viewport(frame, "1:1")
    for bad_frame in ((0, 0, 100), (0, 0, 100, 0), (0, 0, 0, 50), (0, 0, math.nan, 5)):
        with pytest.raises(ValueError, match="frame"):
            sheet.add_viewport(bad_frame, "1:1")
    with pytest.raises(ValueError, match="A scale is a number above nought"):
        sheet.add_viewport(frame, 0)
    # A scale is written with 15 decimals: a smaller one would be written as 0.
    with pytest.raises(ValueError, match="0.000000000000001 or more"):
        sheet.add_viewport(frame, 1e-16)
    with pytest.raises(ValueError, match="A scale is two numbers"):
        sheet.add_viewport(frame, "1:0")
    with pytest.raises(ValueError, match="not fitted"):
        sheet.add_viewport(frame, "fit")
    with pytest.raises(ValueError, match="The centre is two finite numbers"):
        sheet.add_viewport(frame, "1:1", center=(1, 2, 3))
    with pytest.raises(ValueError, match="no layer Missing"):
        sheet.add_viewport(frame, "1:1", frozen_layers=["Notes", "Missing"])
    with pytest.raises(ValueError, match="list of layer names"):
        sheet.add_viewport(frame, "1:1", frozen_layers="Notes")
    assert sheet.viewports == 0


# ---- 5. a title block ----------------------------------------------------------------------------------------------------


def test_an_insert_on_a_layout_has_the_attributes_it_is_given_and_the_defaults_for_the_rest() -> None:
    drawing = new_file()
    sheet = drawing.layouts["Layout1"]
    _title_block(drawing)

    drawing.add_insert("TITLE", (300, 10), layout=sheet, attributes={"drawing": "PLATE PL12", "NUMBER": "A-101"})
    drawing.commit()

    assert _attributes_of(drawing, sheet) == [{"DRAWING": "PLATE PL12", "NUMBER": "A-101", "SCALE": "1:1"}]
    assert drawing.entities("INSERT", layout="Model") == []  # nothing in model space
    (insert,) = drawing.entities("INSERT", layout=sheet)
    assert [(a.Position.X, a.Position.Y) for a in insert.net.AttributeCollection] == [(302.0, 22.0), (302.0, 14.0), (360.0, 14.0)]


def test_an_empty_dictionary_gives_every_attribute_its_default_and_none_gives_no_attribute() -> None:
    drawing = new_file()
    _title_block(drawing)
    drawing.add_insert("TITLE", (0, 0), attributes={})
    drawing.add_insert("TITLE", (200, 0))
    drawing.commit()

    assert _attributes_of(drawing, "Model") == [{"DRAWING": "UNTITLED", "NUMBER": "A-000", "SCALE": "1:1"}, {}]


def test_a_constant_attribute_belongs_to_the_block_and_is_not_made() -> None:
    drawing = new_file()
    _title_block(drawing, constant=True)
    drawing.commit()

    drawing.add_insert("TITLE", (0, 0), attributes={"DRAWING": "X"})
    drawing.commit()

    assert _attributes_of(drawing, "Model") == [{"DRAWING": "X", "NUMBER": "A-000"}]
    with pytest.raises(ValueError, match="has no attribute 'SCALE': its tags are DRAWING, NUMBER"):
        drawing.add_insert("TITLE", (0, 0), attributes={"SCALE": "1:5"})


def test_a_preset_attribute_keeps_its_default_as_the_insert_command_does() -> None:
    drawing = new_file()
    _title_block(drawing, preset=True)
    drawing.commit()

    drawing.add_insert("TITLE", (0, 0), attributes={"DRAWING": "X", "NUMBER": "B-999"})
    drawing.commit()

    assert _attributes_of(drawing, "Model") == [{"DRAWING": "X", "NUMBER": "A-000", "SCALE": "1:1"}]


def test_entities_of_a_class_takes_a_layout_as_the_layout_it_is() -> None:
    drawing = new_file()
    sheet = drawing.layouts["Layout1"]
    _title_block(drawing)
    drawing.add_insert("TITLE", (300, 10), layout=sheet, attributes={})
    drawing.commit()

    assert len(drawing.entities(_dxf.DxfBlockReference, layout=sheet)) == 1
    assert drawing.entities(_dxf.DxfBlockReference, layout="Model") == []


def test_an_insert_places_its_attributes_by_its_scale_and_rotation() -> None:
    drawing = new_file()
    _title_block(drawing)
    drawing.commit()

    drawing.add_insert("TITLE", (10, 20), scale=2.0, rotation=math.pi / 2, attributes={})
    drawing.commit()

    (insert,) = drawing.entities("INSERT")
    first = insert.net.AttributeCollection[0]
    assert (first.Position.X, first.Position.Y) == pytest.approx((10.0 - 24.0, 20.0 + 4.0))
    assert first.Height == pytest.approx(8.0)


def test_attributes_for_a_block_that_is_not_there_or_has_none_are_refused_with_the_tags_it_has() -> None:
    drawing = new_file()
    drawing.blocks.add("BOX", [_dxf.DxfLine(_geo.GeoPoint3(0, 0, 0), _geo.GeoPoint3(1, 0, 0))])
    drawing.commit()

    with pytest.raises(ValueError, match="The block NOPE is not in the drawing, nor made in this commit"):
        drawing.add_insert("NOPE", (0, 0), attributes={})
    with pytest.raises(ValueError, match="The block BOX has no attribute 'A': its tags are none"):
        drawing.add_insert("BOX", (0, 0), attributes={"A": "1"})
    # An empty dictionary is attributes too: a block with none to fill is refused as the docstring says.
    with pytest.raises(ValueError, match="The block BOX has no attribute to fill: its tags are none"):
        drawing.add_insert("BOX", (0, 0), attributes={})
    assert drawing.entities("INSERT", layout="Model") == []


def test_an_insert_with_no_attributes_is_what_it_was_before() -> None:
    drawing = new_file()
    drawing.blocks.add("BOX", [_dxf.DxfLine(_geo.GeoPoint3(0, 0, 0), _geo.GeoPoint3(1, 0, 0))])
    drawing.add_insert("BOX", (5, 5), scale=2.0, layer="0")
    drawing.commit()

    (insert,) = drawing.entities("INSERT")
    assert (insert.net.Position.X, insert.net.ScaleFactors.X, len(insert.net.AttributeCollection)) == (5.0, 2.0, 0)


# ---- 6. plotting --------------------------------------------------------------------------------------------------------------


def _sheet_with_title_block(drawing: Drawing) -> Layout:
    sheet = drawing.layouts.add("Plate A3")
    _title_block(drawing)
    drawing.add_insert("TITLE", (300, 10), layout=sheet, attributes={"DRAWING": "PLATE PL12", "NUMBER": "A-101"})
    sheet.add_viewport((20, 60, 280, 280), "1:1", center=(50, 30))
    return sheet


def test_a_sheet_plots_to_a_pdf_of_one_page_with_the_words_of_its_title_block(tmp_path: Path) -> None:
    drawing = _plate()
    sheet = _sheet_with_title_block(drawing)

    info = sheet.plot_pdf(tmp_path / "plate.pdf")

    assert isinstance(info, PlotInfo)
    assert info.path == str(tmp_path / "plate.pdf") and info.sheet == (420.0, 297.0) and info.scale == "1:1"
    assert info.area is not None and info.area.max_x > info.area.min_x
    data = (tmp_path / "plate.pdf").read_bytes()
    assert data.startswith(b"%PDF-") and data.count(b"/Type /Page\n") + data.count(b"/Type/Page") >= 1
    words = words_of(tmp_path / "plate.pdf")
    for expected in ("PLATE PL12", "A-101", "1:1", "PLATE"):
        assert expected in words, (expected, words)


def test_a_plot_of_a_layout_at_a_scale_is_that_scale_and_the_sheet_the_layout_has(tmp_path: Path) -> None:
    drawing = _plate()
    sheet = drawing.layouts["Layout1"]
    sheet.scale = "1:50"
    drawing.add(_dxf.DxfLine(_geo.GeoPoint3(0, 0, 0), _geo.GeoPoint3(100, 0, 0)), layout=sheet)

    info = sheet.plot_pdf(tmp_path / "scaled.pdf")

    assert (info.sheet, info.scale) == ((420.0, 297.0), "1:50")  # the line queued was committed before the plot


def test_a_plot_asked_for_another_sheet_scale_or_fit_leaves_the_layout_as_it_is(tmp_path: Path) -> None:
    drawing = _plate()
    sheet = _sheet_with_title_block(drawing)
    before = (sheet.paper, sheet.scale, sheet.plot_area, sheet.size)

    small = sheet.plot_pdf(tmp_path / "small.pdf", paper="A4")
    tenth = sheet.plot_pdf(tmp_path / "tenth.pdf", scale="1:10")
    half = sheet.plot_pdf(tmp_path / "half.pdf", scale=0.5)
    fitted = sheet.plot_pdf(tmp_path / "fit.pdf", fit=True)

    assert small.sheet == (297.0, 210.0) and small.scale is not None and small.scale != "1:1"
    assert (tenth.sheet, tenth.scale) == ((420.0, 297.0), "1:10")
    assert half.scale == "1:2"
    assert fitted.scale is not None
    assert (sheet.paper, sheet.scale, sheet.plot_area, sheet.size) == before


def test_a_plot_is_refused_in_words(tmp_path: Path) -> None:
    sheet = _plate().layouts["Layout1"]

    with pytest.raises(ValueError, match="Give a scale or fit, not both"):
        sheet.plot_pdf(tmp_path / "x.pdf", scale="1:1", fit=True)
    with pytest.raises(ValueError, match="The plotter has no sheet A9"):
        sheet.plot_pdf(tmp_path / "x.pdf", paper="A9")
    with pytest.raises(ValueError, match="A scale is two numbers above nought"):
        sheet.plot_pdf(tmp_path / "x.pdf", scale="1:0")
    with pytest.raises(ValueError, match="A scale is a number above nought"):
        sheet.plot_pdf(tmp_path / "x.pdf", scale=-1)
    assert not (tmp_path / "x.pdf").exists()


def test_a_space_with_nothing_to_plot_is_a_runtime_error_and_an_empty_sheet_is_a_blank_page(tmp_path: Path) -> None:
    drawing = new_file()

    with pytest.raises(RuntimeError, match="Nothing to plot: the plot area of Model is empty"):
        drawing.layouts["Model"].plot_pdf(tmp_path / "none.pdf")
    blank = drawing.layouts["Layout1"].plot_pdf(tmp_path / "blank.pdf")
    assert blank.sheet == (420.0, 297.0) and (tmp_path / "blank.pdf").read_bytes().startswith(b"%PDF-")


def test_model_space_plots_to_a_pdf_too(tmp_path: Path) -> None:
    drawing = _plate()

    info = drawing.layouts["Model"].plot_pdf(tmp_path / "model.pdf")

    assert info.sheet == (420.0, 297.0) and "PLATE" in words_of(tmp_path / "model.pdf")


# ---- 7. the example ---------------------------------------------------------------------------------------------------------


def test_a_sheet_saved_and_read_again_is_the_same_sheet(tmp_path: Path) -> None:
    drawing = _plate()
    sheet = _sheet_with_title_block(drawing)
    sheet.scale = "1:2"
    drawing.save_as(str(tmp_path / "sheet.dxf"))

    again = open_file(str(tmp_path / "sheet.dxf")).layouts["Plate A3"]

    assert (again.paper, again.size, again.scale, again.viewports) == ("ISO A3", (420.0, 297.0), "1:2", 1)
    assert again.plot_pdf(tmp_path / "again.pdf").scale == "1:2"
    assert "A-101" in words_of(tmp_path / "again.pdf")


# ---- 8. words that look like commands --------------------------------------------------------------------------------------


def test_a_name_or_a_word_that_starts_like_lisp_or_a_transparent_command_is_only_text() -> None:
    drawing = new_file()
    _title_block(drawing)

    sheet = drawing.layouts.add("(alert 1)")
    other = drawing.layouts.add("!x'y")
    drawing.add_insert("TITLE", (0, 0), layout=sheet, attributes={"DRAWING": "(princ 1)", "NUMBER": "'ZOOM", "SCALE": "!a"})
    drawing.commit()

    assert [str(each) for each in drawing.layouts] == ["Model", "Layout1", "(alert 1)", "!x'y"] and other == "!x'y"
    assert _attributes_of(drawing, sheet) == [{"DRAWING": "(princ 1)", "NUMBER": "'ZOOM", "SCALE": "!a"}]
    assert sheet.paper == "ISO A3"  # nothing was run: the layouts are made by the core's edits, not typed at a prompt
