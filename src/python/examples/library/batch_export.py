"""Prepares every DXF and DWG file of a folder for a model that reads drawings: JSON, graph and a picture.
Run: python batch_export.py FOLDER OUT_FOLDER   (OUT_FOLDER is made if it is not there; needs numpy)
Writes NAME.json (flatten and units), NAME.npz (graph edges, nodes, polyline vertices) and NAME.png for each file, and
manifest.jsonl: a line each with the counts, the seconds of each step and the error of a file that failed (the next file
goes on). The exit code is 1 when any file failed.
"""

import json
import struct
import sys
import time
from collections.abc import Callable
from pathlib import Path
from typing import Any, TypeVar

import numpy as np
from numpy.typing import NDArray

import opencad2d  # noqa: F401  (starts .NET and loads the assemblies)
from opencad2d.arrays import to_numpy
from opencad2d.cad import Drawing, open_file
from GeometryHelper.Dxf import DxfPolyline

T = TypeVar("T")
EDGE_KINDS = ("touches", "contains", "dimensions", "annotates")  # the order of the numbers in edge_kind


def timed(seconds: dict[str, float], step: str, call: Callable[[], T]) -> T:
    """Runs ``call``, adds the seconds it took to ``seconds[step]`` and gives what it gave."""
    start = time.perf_counter()
    try:
        return call()
    finally:
        seconds[step] = round(time.perf_counter() - start, 4)


def polyline_vertices(drawing: Drawing) -> tuple[NDArray[np.float64], NDArray[np.int64]]:
    """The vertices of every polyline of model space, one after the other as an array of shape (n, 2), and the offsets
    that say where each polyline starts in it (one more than there are polylines), in handle order.

    Each is copied from .NET in one block by ``opencad2d.arrays``; a closed polyline gives its corners once.
    """
    polylines = drawing.entities(DxfPolyline, layout="Model")
    polylines.sort(key=lambda entity: int(entity.handle, 16))
    blocks: list[NDArray[np.float64]] = []
    for entity in polylines:
        shape = entity.net.ToGeoPolygon2() if entity.net.Closed else entity.net.ToGeoPolyline2()
        blocks.append(to_numpy(shape.Vertices))
    offsets = np.cumsum([0] + [len(block) for block in blocks]).astype(np.int64)
    return (np.vstack(blocks) if blocks else np.zeros((0, 2))), offsets


def export(path: Path, out: Path) -> dict[str, Any]:
    """Reads one drawing and writes its three files into ``out``; gives the manifest line, without its ``error``."""
    seconds: dict[str, float] = {}
    drawing = timed(seconds, "open", lambda: open_file(str(path)))
    # The drawing's own header, read through GeometryHelper.Dxf: the unit it was drawn in and what the reader said.
    database = drawing.net.Database
    flat = timed(seconds, "flatten", lambda: drawing.flatten())
    text = timed(seconds, "json", lambda: drawing.to_json())
    graph = timed(seconds, "graph", lambda: drawing.to_graph())
    nodes: list[dict[str, Any]] = graph["nodes"]
    vertices, offsets = polyline_vertices(drawing)
    units = json.loads(text)["units"]
    (out / f"{path.name}.json").write_text(text, encoding="utf-8")
    np.savez_compressed(
        out / f"{path.name}.npz",
        edge_index=graph["edge_index"],
        edge_kind=graph["edge_kind"],
        node_box=np.array([n["box"] or [np.nan] * 4 for n in nodes], dtype=np.float64).reshape(-1, 4),
        node_kind=np.array([n["kind"] for n in nodes], dtype="U"),
        node_layer=np.array([n["layer"] for n in nodes], dtype="U"),
        vertices=vertices,
        offsets=offsets,
    )
    picture = out / f"{path.name}.png"
    timed(seconds, "png", lambda: drawing.render_png(str(picture), width=800))
    width, height = struct.unpack(">II", picture.read_bytes()[16:24])  # the size is in the first chunk of a PNG
    edges = np.bincount(graph["edge_kind"], minlength=len(EDGE_KINDS))
    return {
        "file": path.name,
        "version": str(database.Version),
        "insunits": str(database.Insunits),
        "to_mm": units["toMillimetres"],
        "normalised": units["normalised"],
        "warnings": int(database.Warnings.Count),
        "entities": len(drawing.entities()),
        "flat": len(flat),
        "pieces": sum(1 for each in flat if each["path"]),
        "nodes": len(nodes),
        "edges": {kind: int(count) for kind, count in zip(EDGE_KINDS, edges)},
        "polylines": len(offsets) - 1,
        "vertices": int(len(vertices)),
        "png": [width, height],
        "seconds": seconds,
    }


def main(argv: list[str]) -> int:
    if len(argv) != 2 or not Path(argv[0]).is_dir():
        print("usage: python batch_export.py FOLDER OUT_FOLDER", file=sys.stderr)
        return 2
    files = [path for path in Path(argv[0]).iterdir() if path.is_file() and path.suffix.lower() in (".dxf", ".dwg")]
    files.sort(key=lambda path: (path.name.casefold(), path.name))
    out = Path(argv[1])
    out.mkdir(parents=True, exist_ok=True)
    failed = 0
    lines: list[str] = []
    for path in files:
        try:
            line = export(path, out)
        except Exception as error:  # a .NET exception arrives as a Python one; its first line says what is wrong
            failed += 1
            line = {"file": path.name, "error": str(error).splitlines()[0] if str(error) else type(error).__name__}
            print(f"{path.name}: {line['error']}")
        else:
            edges = sum(line["edges"].values())
            print(f"{path.name}: {line['entities']} objects, {line['nodes']} nodes, {edges} edges")
        lines.append(json.dumps(line))
    (out / "manifest.jsonl").write_text("".join(f"{line}\n" for line in lines), encoding="utf-8")
    print(f"{len(files)} files, {failed} failed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
