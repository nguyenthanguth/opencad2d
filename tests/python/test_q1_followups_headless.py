"""Q1: the three follow-ups of the export and the standards checker (PW6, PW7), on a file with no app.

A new drawing's layouts are in millimetres on a real sheet, so a length on Layout1 is a length in millimetres with nothing set up
(the export's ``toMillimetres``, the checker's text heights); a drawing read from a file keeps the units it was written in. The
box of an entity in ``to_json`` is the box it is drawn in (``entity.bounds``), a word by its letters. ``to_graph(max_edges=n)``
keeps the first ``n`` edges of the whole graph and says it was cut.
"""

import importlib
import json
from pathlib import Path
from typing import Any

import pytest

from opencad2d.cad import Drawing, new_file, open_file

_dxf: Any = importlib.import_module("GeometryHelper.Dxf")
_geo: Any = importlib.import_module("GeometryHelper.Geometry")


def _layout1(drawing: Drawing) -> Any:
    database: Any = drawing.net.Database
    return next(layout for layout in database.Layouts if layout.LayoutName == "Layout1")


def _words(height: float, at: tuple[float, float] = (0.0, 0.0)) -> Any:
    text = _dxf.DxfDBText()
    text.Position = _geo.GeoPoint3(at[0], at[1], 0.0)
    text.TextString = "PLATE"
    text.Height = height
    return text


# ---- 1. the layouts of a new drawing ---------------------------------------------------------------------------------------


def test_layout1_of_a_new_file_is_an_a3_sheet_in_millimetres() -> None:
    layout = _layout1(new_file())

    assert layout.PlotPaperUnits == _dxf.DxfPlotPaperUnit.Millimeters
    assert (layout.PlotPaperSize.X, layout.PlotPaperSize.Y) == (420.0, 297.0)
    assert str(layout.CanonicalMediaName) == "ISO_A3_(420.00_x_297.00_MM)"


def test_the_export_of_a_new_files_layout_is_one_to_one_in_millimetres() -> None:
    drawing = new_file()
    drawing.add(_dxf.DxfLine(_geo.GeoPoint3(0.0, 0.0, 0.0), _geo.GeoPoint3(100.0, 0.0, 0.0)), layout="Layout1")
    drawing.commit()

    document = drawing.to_dict("Layout1")

    (sheet,) = document["layouts"]
    assert (sheet["name"], sheet["toMillimetres"]) == ("Layout1", 1.0)
    assert document["entities"][0]["end"] == [100.0, 0.0]
    assert document["entities"][0]["box"] == [0.0, 0.0, 100.0, 0.0]


def test_the_text_height_rule_on_a_new_files_layout_measures_in_millimetres() -> None:
    drawing = new_file()
    drawing.add(_words(4.0), layout="Layout1")
    drawing.commit()

    result = drawing.check({"schema": "opencad2d.standard", "version": 1, "textHeights": {"paper": [2.5, 3.5]}})

    (found,) = result.violations
    assert found.value == pytest.approx(4.0)
    assert found.space == "Layout1"


def test_a_drawing_read_from_a_file_keeps_the_units_of_its_layouts(tmp_path: Path) -> None:
    path = tmp_path / "inches.dxf"
    _dxf.DxfDatabase("AC1027").Save(str(path))

    drawing = open_file(str(path))

    assert _layout1(drawing).PlotPaperUnits == _dxf.DxfPlotPaperUnit.Inches
    assert next(s for s in drawing.to_dict()["layouts"] if s["name"] == "Layout1")["toMillimetres"] == 25.4


def test_a_new_file_saved_and_read_again_keeps_its_sheet(tmp_path: Path) -> None:
    path = tmp_path / "sheet.dxf"
    new_file().save_as(str(path))

    layout = _layout1(open_file(str(path)))

    assert layout.PlotPaperUnits == _dxf.DxfPlotPaperUnit.Millimeters
    assert (layout.PlotPaperSize.X, layout.PlotPaperSize.Y) == (420.0, 297.0)


# ---- 2. the box of the JSON is the box as drawn ----------------------------------------------------------------------------


def test_the_json_box_of_a_text_is_the_box_it_is_drawn_in() -> None:
    drawing = new_file()
    drawing.text_styles.add("NOTE", font="romans.shx")
    text = drawing.add_text("Plate A-12", (10, 20), 5.0, text_style="NOTE")
    drawing.commit()
    drawn = text.bounds
    assert drawn is not None

    entity = next(e for e in drawing.to_dict()["entities"] if e["handle"] == text.handle)

    assert entity["box"] == pytest.approx([drawn.min_x, drawn.min_y, drawn.max_x, drawn.max_y], abs=1e-6)


def test_the_json_box_of_every_entity_the_space_draws_is_its_bounds() -> None:
    drawing = new_file()
    drawing.text_styles.add("NOTE", font="romans.shx")
    drawing.add_text("Plate A-12", (10, 20), 5.0, text_style="NOTE")
    drawing.add_mtext("One\\PTwo", (50, 50), 3.0)
    drawing.add_line((0, 0), (100, 40))
    wide = _dxf.DxfPolyline()
    wide.AddVertexAt(0, _geo.GeoPoint2(0.0, 100.0), 0.0, 4.0, 4.0)
    wide.AddVertexAt(1, _geo.GeoPoint2(100.0, 100.0), 0.0, 4.0, 4.0)
    drawing.add(wide)
    drawing.commit()
    table = drawing.bounds()
    boxes = dict(zip(table.handles, table.boxes.tolist()))

    entities = drawing.to_dict()["entities"]

    assert len(entities) == len(boxes) == 4
    for entity in entities:
        assert entity["box"] == pytest.approx(boxes[entity["handle"]], abs=1e-6)
    assert max(e["box"][3] - e["box"][1] for e in entities if e["kind"] == "LWPOLYLINE") == pytest.approx(4.0, abs=1e-6)


# ---- 3. to_graph(max_edges=n) ----------------------------------------------------------------------------------------------


def _ladder() -> Drawing:
    """A rail with eight rungs across it, and a plate round a circle: touches and a contains edge."""
    drawing = new_file()
    drawing.add_line((0, 0), (100, 0))
    for i in range(8):
        drawing.add_line((10 * i + 5, -5), (10 * i + 5, 5))
    drawing.add_polyline([(200, 0), (300, 0), (300, 50), (200, 50)], closed=True)
    drawing.add_circle((250, 25), 10)
    drawing.commit()
    return drawing


def _keys(graph: dict[str, Any]) -> list[tuple[str, int, int]]:
    return [(e["kind"], e["source"], e["target"]) for e in graph["edges"]]


def test_a_graph_without_a_cap_says_it_was_not_cut() -> None:
    graph = _ladder().to_graph()

    assert graph["truncated"] is False
    assert graph["edgesFound"] == len(graph["edges"]) == 9


def test_a_capped_graph_is_the_first_edges_of_the_whole_graph_for_every_cap() -> None:
    drawing = _ladder()
    whole = drawing.to_graph()

    for cap in range(1, len(whole["edges"]) + 2):
        graph = drawing.to_graph(max_edges=cap)

        assert _keys(graph) == _keys(whole)[:cap]
        assert graph["truncated"] is (cap < len(whole["edges"]))
        assert graph["edgesFound"] >= len(graph["edges"])
        assert len(graph["nodes"]) == len(whole["nodes"])
        assert graph["edge_index"].shape == (2, len(graph["edges"]))
        assert graph["edge_kind"].shape == (len(graph["edges"]),)


def test_a_cut_graph_says_how_many_edges_it_had_found() -> None:
    graph = _ladder().to_graph(max_edges=3)

    assert graph["truncated"] is True
    assert len(graph["edges"]) == 3
    assert 4 <= graph["edgesFound"] <= 9


def test_the_json_of_a_capped_graph_is_the_same_on_every_call() -> None:
    drawing = _ladder()

    assert json.dumps(drawing.to_graph(max_edges=4)["edges"]) == json.dumps(drawing.to_graph(max_edges=4)["edges"])


@pytest.mark.parametrize("cap", [0, -1, 1.5, True, "3"])
def test_a_cap_that_is_not_a_whole_number_of_edges_above_nought_is_refused(cap: Any) -> None:
    with pytest.raises(ValueError, match="max_edges"):
        _ladder().to_graph(max_edges=cap)


def test_a_dense_drawing_stops_at_the_cap_without_finding_every_crossing() -> None:
    drawing = new_file()
    for i in range(300):
        drawing.add_line((0, i), (300, i))
    for i in range(300):
        drawing.add_line((i + 0.5, 0), (i + 0.5, 300))
    drawing.commit()

    graph = drawing.to_graph(max_edges=20)

    assert graph["truncated"] is True
    assert len(graph["edges"]) == 20
    assert graph["edgesFound"] < 90000
