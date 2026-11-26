"""The drawing and the checks of the export for AI pipelines (PW6.3): ``flatten``, ``to_json``, ``to_dict`` and ``to_graph``.

``write_file`` writes one drawing, in inches, with a block inside a block, turned and scaled, a plate, a circle in it, a line from
its corner and a text; ``facts`` reads the drawing the way a pipeline would and gives what it found as plain data. The headless
runner (``test_l1_export_headless.py``), the remote runner (``remote/test_c19_export_remote.py``) and the tool runner
(``tool/test_tool_export.py``) all hold ``facts`` to ``EXPECTED``, so a script gives the same result on every backend.

The drawing: HEAD (a circle of radius 1 at the origin, ByBlock, and a line from (0, 0) to (2, 0)); BOLT (a line from (0, 0) to
(5, 0) and HEAD at (3, 0) turned 90 degrees); BOLT at (100, 100) scaled 2; the plate, a closed polyline from (0, 0) to (20, 10);
a circle of radius 1 at (5, 5); a line from (20, 10) to (30, 10); the text "A" at (2, 2). Every length is in inches
(INSUNITS 1), so the JSON has them 25.4 times larger.
"""

from __future__ import annotations

from typing import Any

from GeometryHelper.Dxf import (
    DxfBlockReference,
    DxfBlockTableRecord,
    DxfCircle,
    DxfColor,
    DxfDatabase,
    DxfDBText,
    DxfLine,
    DxfPolyline,
    DxfUnits,
    DxfWriter,
)
from GeometryHelper.Geometry import GeoPoint2, GeoPoint3, GeoVector3

MM = 25.4
"""Millimetres in an inch: the drawing is in inches."""


def write_file(path: str) -> None:
    """Write the drawing described above to ``path``."""
    database = DxfDatabase.Parse(DxfWriter().ToString())
    database.Insunits = DxfUnits.Inches
    head = DxfBlockTableRecord()
    head.Name = "HEAD"
    head.Origin = GeoPoint3(0, 0, 0)
    database.BlockTable.Add(head)
    circle = DxfCircle(GeoPoint3(0, 0, 0), GeoVector3(0, 0, 1), 1.0)
    circle.Color = DxfColor.ByBlock
    head.AppendEntity(circle)
    head.AppendEntity(DxfLine(GeoPoint3(0, 0, 0), GeoPoint3(2, 0, 0)))
    bolt = DxfBlockTableRecord()
    bolt.Name = "BOLT"
    bolt.Origin = GeoPoint3(0, 0, 0)
    database.BlockTable.Add(bolt)
    bolt.AppendEntity(DxfLine(GeoPoint3(0, 0, 0), GeoPoint3(5, 0, 0)))
    inner = DxfBlockReference(GeoPoint3(3, 0, 0), head)
    inner.Rotation = 1.5707963267948966
    bolt.AppendEntity(inner)
    outer = DxfBlockReference(GeoPoint3(100, 100, 0), bolt)
    outer.ScaleFactors = GeoVector3(2, 2, 2)
    database.ModelSpace.AppendEntity(outer)
    plate = DxfPolyline()
    for index, (x, y) in enumerate(((0, 0), (20, 0), (20, 10), (0, 10))):
        plate.AddVertexAt(index, GeoPoint2(x, y), 0.0, 0.0, 0.0)
    plate.Closed = True
    database.ModelSpace.AppendEntity(plate)
    database.ModelSpace.AppendEntity(DxfCircle(GeoPoint3(5, 5, 0), GeoVector3(0, 0, 1), 1.0))
    database.ModelSpace.AppendEntity(DxfLine(GeoPoint3(20, 10, 0), GeoPoint3(30, 10, 0)))
    text = DxfDBText()
    text.Position = GeoPoint3(2, 2, 0)
    text.TextString = "A"
    text.Height = 0.5
    database.ModelSpace.AppendEntity(text)
    database.Save(path)


def _edge(edge: dict[str, Any], kinds: list[str]) -> tuple[str, str, str]:
    """An edge as its kind and the kinds it joins; two curves that touch are no more one way than the other, so sorted."""
    ends = (kinds[edge["source"]], kinds[edge["target"]])
    return (edge["kind"], *(sorted(ends) if edge["kind"] == "touches" else ends))


def facts(drawing: Any) -> dict[str, Any]:
    """What a pipeline reads of the drawing: its units, the kinds and block paths of its flat entities, where the line of BOLT is,
    and the edges of its graph by the kinds they join."""
    document = drawing.to_dict()
    flat = drawing.flatten()
    graph = drawing.to_graph()
    kinds = [entity["kind"] for entity in graph["nodes"]]
    edges = sorted(_edge(edge, kinds) for edge in graph["edges"])
    bolt_line = next(e for e in flat if e["kind"] == "LINE" and e["path"] == ["BOLT"])
    return {
        "schema": [document["schema"], document["version"]],
        "units": [document["units"]["insunits"], document["units"]["toMillimetres"], document["units"]["normalised"]],
        "flat": sorted((e["kind"], tuple(e["path"])) for e in flat),
        "same_entities": [e["handle"] for e in document["entities"]] == [e["handle"] for e in flat],
        "bolt_line": [round(v, 3) for v in (*bolt_line["start"], *bolt_line["end"])],
        "bolt_matrix_row": [round(v, 3) for v in bolt_line["matrix"][0]],
        "head_circle_layer_color": next((e["layer"], e["color"]) for e in flat if e["kind"] == "CIRCLE" and e["path"]),
        "text": next(e["text"] for e in flat if e["kind"] == "TEXT"),
        "edges": edges,
        "edge_index": None if "edge_index" not in graph else graph["edge_index"].shape[0],
        "edge_count_match": "edge_index" not in graph or graph["edge_index"].shape[1] == len(graph["edges"]),
        "json_is_text": isinstance(drawing.to_json(), str) and drawing.to_json() == drawing.to_json(),
    }


EXPECTED: dict[str, Any] = {
    "schema": ["opencad2d.drawing", 1],
    "units": ["Inches", 25.4, True],
    "flat": [
        ("CIRCLE", ()),
        ("CIRCLE", ("BOLT", "HEAD")),
        ("LINE", ()),
        ("LINE", ("BOLT",)),
        ("LINE", ("BOLT", "HEAD")),
        ("LWPOLYLINE", ()),
        ("TEXT", ()),
    ],
    "same_entities": True,
    # BOLT at (100, 100) scaled 2: its line (0, 0)-(5, 0) is (100, 100)-(110, 100) inches, in millimetres; the matrix takes the
    # inches of the block to millimetres, 2 times 25.4 along x.
    "bolt_line": [2540.0, 2540.0, 2794.0, 2540.0],
    "bolt_matrix_row": [50.8, 0.0, 0.0, 2540.0],
    "head_circle_layer_color": ("0", "ByLayer"),
    "text": "A",
    "edges": [
        ("contains", "LWPOLYLINE", "CIRCLE"),
        ("contains", "LWPOLYLINE", "TEXT"),
        # The head's line starts on the bolt's line and runs through the head's circle, which the bolt's line crosses.
        ("touches", "CIRCLE", "LINE"),
        ("touches", "CIRCLE", "LINE"),
        ("touches", "LINE", "LINE"),
        ("touches", "LINE", "LWPOLYLINE"),
    ],
    "edge_index": 2,
    "edge_count_match": True,
    "json_is_text": True,
}
"""What ``facts`` gives for the drawing, with numpy installed (the tests install it with the wheel's ``numpy`` extra)."""
