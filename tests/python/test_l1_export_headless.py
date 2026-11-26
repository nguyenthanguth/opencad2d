"""L1: the export for AI pipelines on a file, with no app (PW6.3): ``flatten``, ``to_json``, ``to_dict`` and ``to_graph`` of a
drawing opened from its file give what ``export_scenarios`` says, the document of the schema, in millimetres, the same text twice,
and a graph whose edge arrays are numpy arrays. Every file is written in ``tmp_path``.
"""

import json
from pathlib import Path
from typing import Any

import pytest

import export_scenarios
from GeometryHelper.Dxf import DxfUnits
from opencad2d.cad import new_file, open_file


def _drawing(tmp_path: Path) -> Any:
    path = tmp_path / "plan.dxf"
    export_scenarios.write_file(str(path))
    return open_file(str(path))


def test_the_facts_of_a_file_are_the_facts_every_backend_gives(tmp_path: Path) -> None:
    assert export_scenarios.facts(_drawing(tmp_path)) == export_scenarios.EXPECTED


def test_the_json_is_the_document_of_its_schema_in_millimetres(tmp_path: Path) -> None:
    drawing = _drawing(tmp_path)

    document = json.loads(drawing.to_json())

    assert [document["schema"], document["version"]] == ["opencad2d.drawing", 1]
    assert document["units"] == {"insunits": "Inches", "insunitsCode": 1, "toMillimetres": 25.4, "normalised": True}
    plate = next(e for e in document["entities"] if e["kind"] == "LWPOLYLINE")
    assert plate["vertices"][2] == [508.0, 254.0]
    assert plate["closed"] is True
    assert {layer["name"] for layer in document["layers"]} == {"0"}
    assert document["report"]["inserts"] == 2
    assert drawing.to_json() == drawing.to_json()
    assert drawing.to_dict() == document


def test_a_drawing_of_no_unit_is_left_as_it_is_and_says_so() -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (100, 0))
    drawing.commit()
    drawing.net.Database.Insunits = DxfUnits.Undefined

    document = drawing.to_dict()

    assert document["units"]["normalised"] is False
    assert document["units"]["toMillimetres"] == 1.0
    assert document["entities"][0]["end"] == [100.0, 0.0]


def test_a_layout_is_taken_by_its_name_and_a_name_the_drawing_lacks_is_refused() -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (100, 0))
    drawing.commit()

    assert len(drawing.flatten("Model")) == 1
    assert len(drawing.flatten("model")) == 1
    with pytest.raises(ValueError, match="no layout named Nowhere"):
        drawing.flatten("Nowhere")
    with pytest.raises(ValueError, match="no layout named Nowhere"):
        drawing.to_graph("Nowhere")


def test_what_is_queued_is_not_read_until_it_is_committed() -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (100, 0))

    assert drawing.flatten() == []

    drawing.commit()

    assert len(drawing.flatten()) == 1


def test_the_tolerance_of_a_graph_is_a_number_of_millimetres_above_nought() -> None:
    drawing = new_file()

    for tolerance in (0.0, -1.0, float("inf"), float("nan")):
        with pytest.raises(ValueError, match="above nought"):
            drawing.to_graph(tolerance=tolerance)


def test_two_lines_a_hair_apart_touch_by_the_tolerance_asked() -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (10, 0))
    drawing.add_line((10.4, 0), (20, 0))
    drawing.commit()

    assert drawing.to_graph(tolerance=0.1)["edges"] == []
    graph = drawing.to_graph(tolerance=0.5)
    assert [(e["source"], e["target"], e["kind"]) for e in graph["edges"]] == [(0, 1, "touches")]
    assert graph["edge_index"].tolist() == [[0], [1]]
    assert graph["edge_kind"].tolist() == [0]


def test_a_graph_without_edges_has_empty_arrays() -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (10, 0))
    drawing.commit()

    graph = drawing.to_graph()

    assert graph["edge_index"].shape == (2, 0)
    assert graph["edge_kind"].shape == (0,)
