"""G1: the blocks an attached drawing brings (PW1): ``drawing.blocks`` of a drawing with an external reference attached and
loaded lists ``grid|TAG`` for the block ``TAG`` of ``grid``, as AutoCAD 2021 lists them in its block table (``tblnext`` says
70 48), with its objects, flagged dependent and resolved; an unloaded reference or a file gone brings none, and the drawing's
own table and file hold none. Every file is written in ``tmp_path``.
"""

from pathlib import Path
from typing import Any

from GeometryHelper.Dxf import DxfBlockReference, DxfBlockTableRecord, DxfDatabase, DxfLine
from GeometryHelper.Geometry import GeoPoint3
from opencad2d.cad import open_file


def _line(x1: float, y1: float, x2: float, y2: float) -> Any:
    return DxfLine(GeoPoint3(x1, y1, 0), GeoPoint3(x2, y2, 0))


def _host_with_grid(folder: Path) -> Path:
    """host.dxf: the block ``Bolt`` and ``grid.dxf`` attached and placed; grid.dxf: the blocks ``TAG`` (one line) and ``PLAN`` (two)."""
    grid = DxfDatabase("AC1027")
    tag = DxfBlockTableRecord()
    tag.Name = "TAG"
    grid.BlockTable.Add(tag)
    tag.AppendEntity(_line(0, 0, 0, 10))
    plan = DxfBlockTableRecord()
    plan.Name = "PLAN"
    grid.BlockTable.Add(plan)
    plan.AppendEntity(_line(0, 0, 5, 0))
    plan.AppendEntity(_line(5, 0, 5, 5))
    grid.Save(str(folder / "grid.dxf"))

    host = DxfDatabase("AC1027")
    bolt = DxfBlockTableRecord()
    bolt.Name = "Bolt"
    host.BlockTable.Add(bolt)
    bolt.AppendEntity(_line(0, 0, 10, 0))
    reference = host.AttachXref("grid.dxf", "grid")
    host.ModelSpace.AppendEntity(DxfBlockReference(GeoPoint3(0, 0, 0), reference))
    path = folder / "host.dxf"
    host.Save(str(path))
    return path


def test_the_blocks_of_a_drawing_with_a_loaded_reference_hold_the_blocks_it_brings(tmp_path: Path) -> None:
    drawing = open_file(str(_host_with_grid(tmp_path)))

    blocks = drawing.blocks

    assert [str(b.Name) for b in blocks] == ["Bolt", "grid", "grid|PLAN", "grid|TAG"]
    tag = next(b for b in blocks if str(b.Name) == "grid|TAG")
    assert tag.IsDependent and tag.IsResolved and not tag.IsFromExternalReference
    assert sum(1 for _ in tag) == 1
    assert sum(1 for _ in next(b for b in blocks if str(b.Name) == "grid|PLAN")) == 2
    assert next(b for b in blocks if str(b.Name) == "grid").IsFromExternalReference
    assert not next(b for b in blocks if str(b.Name) == "Bolt").IsDependent


def test_a_reference_unloaded_or_whose_file_is_gone_brings_no_block(tmp_path: Path) -> None:
    drawing = open_file(str(_host_with_grid(tmp_path)))
    (tmp_path / "grid.dxf").unlink()

    assert [str(b.Name) for b in drawing.blocks] == ["Bolt", "grid"]


def test_the_drawings_own_table_holds_none_of_them(tmp_path: Path) -> None:
    drawing = open_file(str(_host_with_grid(tmp_path)))

    assert drawing.blocks
    assert not drawing.net.Database.BlockTable.Has("grid|TAG")
