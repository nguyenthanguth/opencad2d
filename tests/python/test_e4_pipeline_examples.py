"""E4: the examples ``batch_export.py`` and ``layout_loop.py`` of ``src/python/examples/library`` run as a user runs them (PW6.4):
``python -I <file> <arguments>`` in the venv the wheel is installed in, ``batch_export.py`` on a folder of drawings built here
in code and ``layout_loop.py`` on the sheet it generates, never a file of the repository.

The folder has ``plate.dxf`` and ``frame.dwg`` (a plate 400 by 300 with a circle, a text, a linear dimension and a block BOLT
of two lines inserted twice: 6 objects, 8 once the blocks are opened, 9 edges), ``inches.dxf`` (a line and a text, INSUNITS
inches), ``unitless.dxf`` (a line, INSUNITS 0), ``empty.dxf``, ``Beam.dxf`` (the plate again, named to show the order) and ``broken.dxf`` (a file that is no
drawing); a folder ``sub`` the example does not enter. The sheet of the loop is the one ``generate(1)`` makes: twelve tags on a
grid of beams and two dimensions, 25 overlaps and 0.321 as a reward at first. The two timing tests hold loose bounds, not the numbers
of the guide, which are from a run it names.
"""

import importlib.util
import json
import re
import struct
import subprocess
import sys
import time
from pathlib import Path
from types import ModuleType
from typing import Any

import numpy as np
import pytest

import opencad2d  # noqa: F401  (boots .NET and loads the assemblies)
from opencad2d.cad import Drawing, new_file
from GeometryHelper.Dxf import DxfLine, DxfUnits
from GeometryHelper.Geometry import GeoPoint3

LIBRARY = Path(__file__).resolve().parents[2] / "src" / "python" / "examples" / "library"
NOT_A_DXF = "This is not a DXF that can be read"  # the start of the error of a file that is no DXF (as in E1)


def _plate() -> Drawing:
    """The plate: a closed polyline 400 by 300, a circle, a text, a linear dimension and two inserts of a block of two lines."""
    drawing = new_file()
    drawing.add_polyline([(0, 0), (400, 0), (400, 300), (0, 300)], closed=True)
    drawing.add_circle((200, 150), 40)
    drawing.add_text("Plate A-12", (50, 250), 10.0)
    drawing.add_linear_dim((0, 0), (400, 0), (200, -30))
    drawing.blocks.add("BOLT", [DxfLine(GeoPoint3(0, 0, 0), GeoPoint3(20, 0, 0)), DxfLine(GeoPoint3(0, 5, 0), GeoPoint3(0, -5, 0))])
    drawing.commit()
    drawing.add_insert("BOLT", (100, 100))
    drawing.add_insert("BOLT", (300, 100))
    drawing.commit()
    return drawing


def _folder(root: Path) -> Path:
    """The folder of the module docstring, with no file that is a drawing besides those it names."""
    folder = root / "drawings"
    (folder / "sub").mkdir(parents=True)
    _plate().save_as(str(folder / "plate.dxf"))
    _plate().save_as(str(folder / "frame.dwg"))
    _plate().save_as(str(folder / "Beam.dxf"))
    inches = new_file()
    inches.net.Database.Insunits = DxfUnits.Inches
    inches.add_line((0, 0), (10, 0))
    inches.add_text("10 in", (1, 1), 0.5)
    inches.commit()
    inches.save_as(str(folder / "inches.dxf"))
    new_file().save_as(str(folder / "empty.dxf"))
    unitless = new_file()
    unitless.net.Database.Insunits = DxfUnits.Undefined
    unitless.add_line((0, 0), (10, 0))
    unitless.commit()
    unitless.save_as(str(folder / "unitless.dxf"))
    (folder / "broken.dxf").write_text("not a drawing", encoding="utf-8")
    _plate().save_as(str(folder / "sub" / "never.dxf"))
    (folder / "notes.txt").write_text("not a drawing either", encoding="utf-8")
    return folder


def _run(example: str, *args: object, cwd: Path) -> "subprocess.CompletedProcess[str]":
    """Runs ``python -I library/<example> <args>`` of the venv under test, in ``cwd``, with a timeout of 120 s."""
    script = LIBRARY / example
    assert script.is_file(), f"{script} does not exist: the example is not written"
    return subprocess.run(
        [sys.executable, "-I", str(script), *map(str, args)], capture_output=True, text=True, timeout=120, cwd=cwd
    )


@pytest.fixture
def work(tmp_path: Path) -> Path:
    """An empty folder every example runs in, so that a file it wrote by accident shows."""
    folder = tmp_path / "work"
    folder.mkdir()
    return folder


def _load(name: str) -> ModuleType:
    """An example as a module, to call its functions: the file is run by path, as ``python <file>`` would, but not as main."""
    spec = importlib.util.spec_from_file_location(f"pipeline_example_{name}", LIBRARY / f"{name}.py")
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


# ---- batch_export.py ---------------------------------------------------------------------------------------------


def _manifest(out: Path) -> list[dict[str, Any]]:
    return [json.loads(line) for line in (out / "manifest.jsonl").read_text(encoding="utf-8").splitlines()]


def test_batch_export_writes_three_files_and_a_manifest_line_for_each_drawing_in_name_order(
    tmp_path: Path, work: Path
) -> None:
    folder, out = _folder(tmp_path), tmp_path / "out"

    process = _run("batch_export.py", folder, out, cwd=work)

    assert process.returncode == 1, process.stderr  # broken.dxf failed
    lines = process.stdout.splitlines()
    assert [line.split(":")[0] for line in lines[:-1]] == [
        "Beam.dxf",
        "broken.dxf",
        "empty.dxf",
        "frame.dwg",
        "inches.dxf",
        "plate.dxf",
        "unitless.dxf",
    ]  # case ignored: Beam before broken; sub, never.dxf and notes.txt are not drawings of the folder
    assert lines[:-1][0] == "Beam.dxf: 6 objects, 8 nodes, 9 edges"
    assert lines[-1] == "7 files, 1 failed"
    assert sorted(p.name for p in out.iterdir()) == sorted(
        ["manifest.jsonl"]
        + [
            f"{name}.{extension}"
            for name in ("Beam.dxf", "empty.dxf", "frame.dwg", "inches.dxf", "plate.dxf", "unitless.dxf")
            for extension in ("json", "npz", "png")
        ]
    )
    assert list(work.iterdir()) == []


def test_batch_export_manifest_has_the_counts_the_drawings_have_and_the_error_of_the_one_that_is_none(
    tmp_path: Path, work: Path
) -> None:
    folder, out = _folder(tmp_path), tmp_path / "out"
    _run("batch_export.py", folder, out, cwd=work)

    rows = {row["file"]: row for row in _manifest(out)}

    assert list(rows) == ["Beam.dxf", "broken.dxf", "empty.dxf", "frame.dwg", "inches.dxf", "plate.dxf", "unitless.dxf"]
    assert sorted(rows["broken.dxf"]) == ["error", "file"]
    assert rows["broken.dxf"]["error"].startswith(NOT_A_DXF)
    plate = rows["plate.dxf"]
    assert (plate["version"], plate["insunits"], plate["to_mm"], plate["normalised"]) == ("AC1027", "Millimeters", 1, True)
    # 6 objects of model space; 8 once the two inserts are opened (4 pieces, each with the block's name as its path).
    assert (plate["entities"], plate["flat"], plate["pieces"], plate["nodes"]) == (6, 8, 4, 8)
    assert plate["edges"] == {"touches": 2, "contains": 6, "dimensions": 1, "annotates": 0}
    assert (plate["polylines"], plate["vertices"], plate["png"][0]) == (1, 4, 800)
    assert sorted(plate["seconds"]) == ["flatten", "graph", "json", "open", "png"]
    assert all(0.0 <= value < 30.0 for value in plate["seconds"].values())
    assert rows["frame.dwg"]["version"] == "AC1032"  # a DWG: the writer's version
    assert {k: v for k, v in rows["frame.dwg"].items() if k not in ("file", "seconds", "version")} == {
        k: v for k, v in plate.items() if k not in ("file", "seconds", "version")
    }
    inches = rows["inches.dxf"]
    assert (inches["insunits"], inches["to_mm"], inches["normalised"], inches["entities"]) == ("Inches", 25.4, True, 2)
    unitless = rows["unitless.dxf"]  # left as it is, and the manifest says so
    assert (unitless["insunits"], unitless["to_mm"], unitless["normalised"]) == ("Undefined", 1, False)
    empty = rows["empty.dxf"]
    assert (empty["entities"], empty["nodes"], sum(empty["edges"].values()), empty["vertices"]) == (0, 0, 0, 0)


def test_batch_export_arrays_are_the_graph_its_nodes_and_the_polyline_of_the_drawing(tmp_path: Path, work: Path) -> None:
    folder, out = _folder(tmp_path), tmp_path / "out"
    _run("batch_export.py", folder, out, cwd=work)

    with np.load(out / "plate.dxf.npz") as arrays:
        assert sorted(arrays.files) == [
            "edge_index",
            "edge_kind",
            "node_box",
            "node_kind",
            "node_layer",
            "offsets",
            "vertices",
        ]
        assert arrays["edge_index"].shape == (2, 9) and arrays["edge_index"].dtype == np.int64
        assert arrays["edge_kind"].shape == (9,) and arrays["edge_kind"].dtype == np.int64
        assert np.bincount(arrays["edge_kind"], minlength=4).tolist() == [2, 6, 1, 0]
        assert arrays["edge_index"].max() < 8  # the nodes are the 8 objects
        # One row for each node, in the order of the edges' numbers: the plate, the circle, the text, the dimension, 4 lines.
        assert arrays["node_kind"].tolist() == ["LWPOLYLINE", "CIRCLE", "TEXT", "DIMENSION", "LINE", "LINE", "LINE", "LINE"]
        assert arrays["node_layer"].tolist() == ["0"] * 8
        assert arrays["node_box"].shape == (8, 4)
        assert arrays["node_box"][0].tolist() == [0.0, 0.0, 400.0, 300.0]
        assert arrays["node_box"][1].tolist() == [160.0, 110.0, 240.0, 190.0]
        assert arrays["vertices"].tolist() == [[0.0, 0.0], [400.0, 0.0], [400.0, 300.0], [0.0, 300.0]]
        assert arrays["offsets"].tolist() == [0, 4]
    document = json.loads((out / "plate.dxf.json").read_text(encoding="utf-8"))
    assert document["schema"] == "opencad2d.drawing" and len(document["entities"]) == 8
    assert [e["kind"] for e in document["entities"]].count("LINE") == 4
    for name in ("plate.dxf", "frame.dwg", "inches.dxf", "empty.dxf"):
        data = (out / f"{name}.png").read_bytes()
        assert data[:8] == b"\x89PNG\r\n\x1a\n"
        assert struct.unpack(">I", data[16:20])[0] == 800
    with np.load(out / "empty.dxf.npz") as empty:
        assert empty["edge_index"].shape == (2, 0) and empty["vertices"].shape == (0, 2) and empty["offsets"].tolist() == [0]
        assert empty["node_box"].shape == (0, 4) and empty["node_kind"].shape == (0,)


def test_batch_export_of_the_same_folder_twice_gives_the_same_files_and_the_same_manifest_but_for_its_seconds(
    tmp_path: Path, work: Path
) -> None:
    folder = _folder(tmp_path)
    _run("batch_export.py", folder, tmp_path / "one", cwd=work)
    _run("batch_export.py", folder, tmp_path / "two", cwd=work)

    for path in sorted((tmp_path / "one").iterdir()):
        if path.suffix == ".npz":  # the zip holds a time stamp: its arrays are compared
            with np.load(path) as one, np.load(tmp_path / "two" / path.name) as two:
                assert all(np.array_equal(one[key], two[key]) for key in one.files), path.name
        elif path.name != "manifest.jsonl":
            assert path.read_bytes() == (tmp_path / "two" / path.name).read_bytes(), path.name
    without = [[{k: v for k, v in row.items() if k != "seconds"} for row in _manifest(tmp_path / n)] for n in ("one", "two")]
    assert without[0] == without[1] and len(without[0]) == 7


def test_batch_export_of_a_folder_of_good_drawings_exits_with_zero_and_of_none_writes_an_empty_manifest(
    tmp_path: Path, work: Path
) -> None:
    good = tmp_path / "good"
    good.mkdir()
    _plate().save_as(str(good / "a.dxf"))
    nothing = tmp_path / "nothing"
    nothing.mkdir()

    one = _run("batch_export.py", good, tmp_path / "one", cwd=work)
    none = _run("batch_export.py", nothing, tmp_path / "none", cwd=work)

    assert one.returncode == 0 and one.stdout.splitlines()[-1] == "1 files, 0 failed"
    assert none.returncode == 0 and none.stdout == "0 files, 0 failed\n"
    assert (tmp_path / "none" / "manifest.jsonl").read_text(encoding="utf-8") == ""


@pytest.mark.timing
def test_batch_export_of_twenty_drawings_takes_under_a_second_each_after_the_first(tmp_path: Path, work: Path) -> None:
    folder = tmp_path / "many"
    folder.mkdir()
    for number in range(20):
        _plate().save_as(str(folder / f"d{number:02d}.dxf"))

    start = time.perf_counter()
    process = _run("batch_export.py", folder, tmp_path / "out", cwd=work)
    seconds = time.perf_counter() - start

    assert process.returncode == 0, process.stderr
    assert seconds < 60.0  # a loose bound: the whole run took about 1.7 s on .NET 8 and 2.2 s on .NET Framework 4.8
    rows = _manifest(tmp_path / "out")
    assert len(rows) == 20 and all(sum(row["seconds"].values()) < 1.0 for row in rows[1:])


@pytest.mark.parametrize("arguments", [[], ["folder"], ["folder", "out", "extra"], ["not-a-folder", "out"]])
def test_batch_export_given_the_wrong_arguments_says_how_to_use_it_and_exits_with_two(
    work: Path, arguments: list[str]
) -> None:
    process = _run("batch_export.py", *arguments, cwd=work)

    assert process.returncode == 2, process.stdout + process.stderr
    assert process.stdout == ""
    assert process.stderr.lower().startswith("usage")
    assert "Traceback" not in process.stderr
    assert list(work.iterdir()) == []


# ---- layout_loop.py ----------------------------------------------------------------------------------------------

ROUND = re.compile(r"round (\d+): (?:moved (\d+), )?reward (\d\.\d{3}) \(standard (\d\.\d{3}), clear (\d\.\d{3}), (\d+) overlaps\)")


def test_layout_loop_raises_the_reward_of_the_sheet_until_it_stops_rising_and_ends_with_no_overlaps(
    tmp_path: Path, work: Path
) -> None:
    out = tmp_path / "loop"

    process = _run("layout_loop.py", out, cwd=work)

    assert process.returncode == 0, process.stdout + process.stderr
    lines = process.stdout.splitlines()
    rounds = [ROUND.fullmatch(line) for line in lines[:-1]]
    assert all(rounds), lines
    values = [float(match.group(3)) for match in rounds if match]
    overlaps = [int(match.group(6)) for match in rounds if match]
    assert values[0] == 0.321 and overlaps[0] == 25  # the sheet as the AI first puts it
    assert values[1] == 1.0 and overlaps[1] == 0  # one round of moving cures it
    assert values[-1] == values[-2]  # the loop stops on the round that does not rise
    assert overlaps[-1] == 0
    assert lines[-1] == "reward 0.321 -> 1.000; before.png and after.png written to loop"
    assert sorted(p.name for p in out.iterdir()) == ["after.png", "before.png"]
    for name in ("before.png", "after.png"):
        data = (out / name).read_bytes()
        assert struct.unpack(">II", data[16:24]) == (1200, 849)  # the sheet 420 by 297 fitted to 1 200 wide (848.57)
    assert (out / "before.png").read_bytes() != (out / "after.png").read_bytes()
    assert list(work.iterdir()) == []


def test_layout_loop_with_the_same_seed_gives_the_same_rounds_and_the_same_pictures_and_another_seed_another_sheet(
    tmp_path: Path, work: Path
) -> None:
    one = _run("layout_loop.py", tmp_path / "one", 7, cwd=work)
    two = _run("layout_loop.py", tmp_path / "two", 7, cwd=work)
    other = _run("layout_loop.py", tmp_path / "other", 8, cwd=work)

    assert one.returncode == two.returncode == other.returncode == 0, one.stderr + other.stderr
    assert one.stdout.replace("one", "x") == two.stdout.replace("two", "x")  # but for the name of the folder
    for name in ("before.png", "after.png"):
        assert (tmp_path / "one" / name).read_bytes() == (tmp_path / "two" / name).read_bytes()
    assert (tmp_path / "one" / "before.png").read_bytes() != (tmp_path / "other" / "before.png").read_bytes()
    assert other.stdout.splitlines()[-1].endswith("written to other")


def test_the_rounds_of_the_loop_never_lower_the_reward_and_leave_no_word_over_a_line_or_another_and_no_height_off_the_standard() -> None:
    loop = _load("layout_loop")
    drawing, notes = loop.generate(1)

    before = loop.reward(drawing, loop.STANDARD, len(notes))
    first_wrong = {v.rule for v in drawing.check(loop.STANDARD, model_scale=1.0).violations}
    rewards = [before.value]
    for _ in range(3):
        loop.refine(drawing, notes, loop.STANDARD)
        rewards.append(loop.reward(drawing, loop.STANDARD, len(notes)).value)
    result = drawing.check(loop.STANDARD, model_scale=1.0)

    assert first_wrong == {"text-height", "gap-word-line"}
    assert before.value < 0.5 and len(drawing.overlaps()) == 0
    assert rewards == sorted(rewards) and rewards[1] == 1.0
    assert result.total == 1.0 and result.violations == []
    assert [e.dxf_type for e in drawing.entities()].count("TEXT") == 12
    assert [e.dxf_type for e in drawing.entities()].count("DIMENSION") == 2
    for note in notes:  # every note is still where its anchor is: within the reach asked of a place, and inside the border
        assert note.entity is not None
        box = drawing.entity(note.entity.handle).bounds
        assert box is not None and 20.0 <= box.min_x and box.max_x <= 400.0 and 20.0 <= box.min_y and box.max_y <= 277.0


def test_a_moved_note_has_a_place_nothing_runs_through_and_a_height_the_standard_has() -> None:
    loop = _load("layout_loop")
    drawing, notes = loop.generate(1)
    loop.refine(drawing, notes, loop.STANDARD)

    for note in notes:
        assert note.entity is not None
        entity = drawing.entity(note.entity.handle)
        if note.kind == "text":
            assert note.height in loop.STANDARD["textHeights"]["paper"]
            assert drawing.overlaps(of=[entity]).words == []
            assert entity.net.Height == pytest.approx(note.height)
    assert len(drawing.overlaps()) == 0


def test_reward_reads_one_for_a_clean_sheet_and_less_for_a_word_with_a_line_through_it() -> None:
    loop = _load("layout_loop")
    clean = new_file()
    clean.add_line((0, 0), (100, 0))
    clean.add_text("A1", (10, 10), 3.5)
    crossed = new_file()
    crossed.add_line((0, 0), (100, 0))
    crossed.add_text("A1", (10, -1.0), 3.5)
    empty = new_file()

    reward_clean = loop.reward(clean, loop.STANDARD, 1)  # also commits what was queued
    reward_crossed = loop.reward(crossed, loop.STANDARD, 1)

    assert reward_clean.value == 1.0 and reward_clean.overlaps == 0
    assert reward_crossed.overlaps == 1 and reward_crossed.clear == 0.0
    assert reward_crossed.value == pytest.approx((reward_crossed.standard + 0.0) / 2.0)
    assert reward_crossed.value < reward_clean.value
    assert loop.reward(empty, loop.STANDARD, 0).value == 1.0  # nothing to place is nothing wrong


@pytest.mark.timing
def test_reward_of_a_sheet_of_two_hundred_tags_takes_well_under_a_second() -> None:
    loop = _load("layout_loop")
    drawing = new_file()
    for row in range(10):
        for column in range(20):
            drawing.add_line((column * 20.0, row * 20.0), (column * 20.0 + 15.0, row * 20.0))
            drawing.add_text(f"T{row}{column}", (column * 20.0 + 1.0, row * 20.0 - 1.0), 3.5)
    drawing.commit()
    loop.reward(drawing, loop.STANDARD, 200)  # the first call makes the display list and the index

    start = time.perf_counter()
    value = loop.reward(drawing, loop.STANDARD, 200)
    seconds = time.perf_counter() - start

    assert seconds < 1.0  # a loose bound: about 3 ms on .NET 8 and 1 ms on .NET Framework 4.8
    assert 0.0 <= value.value < 1.0 and value.overlaps == 200  # every word has the line under it through it


@pytest.mark.parametrize("arguments", [[], ["out", "1", "extra"], ["out", "x"], ["out", "-1"]])
def test_layout_loop_given_the_wrong_arguments_says_how_to_use_it_and_exits_with_two(
    work: Path, arguments: list[str]
) -> None:
    process = _run("layout_loop.py", *arguments, cwd=work)

    assert process.returncode == 2, process.stdout + process.stderr
    assert process.stdout == ""
    assert process.stderr.lower().startswith("usage")
    assert "Traceback" not in process.stderr
    assert list(work.iterdir()) == []
