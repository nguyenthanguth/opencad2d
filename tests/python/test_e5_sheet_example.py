"""E5: the example ``sheet_from_code.py`` of ``src/python/examples/library`` runs as a user runs it (PW7.7): ``python -I <file> OUT``
in the venv the wheel is installed in.

It makes a plate 1200 by 800 with two holes in model space, a layout ``Plate A3`` (ISO A3, 420 by 297 millimetres) with a border, a title
block with three attributes filled, and two viewports, one at 1:10 with the layer Notes frozen and a detail of the second hole at 1:5
with Dims frozen, saves ``plate.dxf`` and plots ``plate.pdf``. The PDF is read back for the words of the title block and of the
drawing, the DXF for the layout it holds.
"""

import subprocess
import sys
from pathlib import Path

import pytest

import opencad2d  # noqa: F401  (boots .NET and loads the assemblies)
from opencad2d.cad import open_file

from pdf_words import words_of

LIBRARY = Path(__file__).resolve().parents[2] / "src" / "python" / "examples" / "library"


def _run(*args: object, cwd: Path) -> "subprocess.CompletedProcess[str]":
    """Runs ``python -I library/sheet_from_code.py <args>`` of the venv under test, in ``cwd``, with a timeout of 120 s."""
    script = LIBRARY / "sheet_from_code.py"
    assert script.is_file(), f"{script} does not exist: the example is not written"
    return subprocess.run(
        [sys.executable, "-I", str(script), *map(str, args)], capture_output=True, text=True, timeout=120, cwd=cwd
    )


@pytest.fixture
def work(tmp_path: Path) -> Path:
    """An empty folder the example runs in, so that a file it wrote by accident shows."""
    folder = tmp_path / "work"
    folder.mkdir()
    return folder


def test_sheet_from_code_writes_the_drawing_and_its_pdf_and_says_what_the_plot_laid_on_the_sheet(
    tmp_path: Path, work: Path
) -> None:
    out = tmp_path / "sheet"

    process = _run(out, cwd=work)

    assert process.returncode == 0, process.stdout + process.stderr
    assert process.stdout.splitlines() == [
        "Plate A3: ISO A3, 2 viewports, title block A-101",
        "plate.pdf: sheet 420 by 297, scale 1:1, written to sheet",
    ]
    assert sorted(p.name for p in out.iterdir()) == ["plate.dxf", "plate.pdf"]
    assert (out / "plate.pdf").read_bytes().startswith(b"%PDF-")
    assert list(work.iterdir()) == []


def test_the_pdf_of_the_sheet_reads_back_with_the_words_of_its_title_block_and_of_the_plate(tmp_path: Path, work: Path) -> None:
    out = tmp_path / "sheet"
    assert _run(out, cwd=work).returncode == 0

    words = words_of(out / "plate.pdf")

    for expected in ("PLATE PL12", "A-101", "1:10, DETAIL 1:5", "2 HOLES D60", "1200", "800"):
        assert expected in words, (expected, words)
    assert words.count("1200") == 1  # the dimension's layer is frozen in the detail viewport
    assert "UNTITLED" not in words and "A-000" not in words  # the defaults of the attributes were filled over


def test_the_drawing_the_example_saves_holds_the_layout_with_its_viewports_at_their_scales(tmp_path: Path, work: Path) -> None:
    out = tmp_path / "sheet"
    assert _run(out, cwd=work).returncode == 0

    drawing = open_file(str(out / "plate.dxf"))

    assert [str(layout) for layout in drawing.layouts] == ["Model", "Layout1", "Plate A3"]
    sheet = drawing.layouts["Plate A3"]
    assert (sheet.paper, sheet.size, sheet.units, sheet.scale, sheet.viewports) == ("ISO A3", (420.0, 297.0), "mm", "1:1", 2)
    (insert,) = drawing.entities("INSERT", layout=sheet)
    assert {str(a.Tag): str(a.TextString) for a in insert.net.AttributeCollection} == {
        "DRAWING": "PLATE PL12",
        "NUMBER": "A-101",
        "SCALE": "1:10, DETAIL 1:5",
    }
    block = next(each.BlockTableRecord for each in drawing.net.Database.Layouts if each.LayoutName == "Plate A3")
    viewports = [v for v in block if str(v.DxfName) == "VIEWPORT" and v.Number > 1]
    frozen = {round(float(v.ViewHeight / v.Height), 6): [str(layer.Name) for layer in v.GetFrozenLayers()] for v in viewports}
    assert frozen == {10.0: ["Notes"], 5.0: ["Dims"]}  # model units to the millimetre of the sheet: 1:10 and 1:5


def test_the_same_run_twice_gives_the_same_words_and_the_same_drawing_text(tmp_path: Path, work: Path) -> None:
    assert _run(tmp_path / "one", cwd=work).returncode == 0
    assert _run(tmp_path / "two", cwd=work).returncode == 0

    assert words_of(tmp_path / "one" / "plate.pdf") == words_of(tmp_path / "two" / "plate.pdf")
    assert open_file(str(tmp_path / "one" / "plate.dxf")).to_json() == open_file(str(tmp_path / "two" / "plate.dxf")).to_json()


@pytest.mark.parametrize("arguments", [[], ["a", "b"]])
def test_sheet_from_code_given_the_wrong_arguments_says_how_to_use_it_and_exits_with_two(
    work: Path, arguments: list[str]
) -> None:
    process = _run(*arguments, cwd=work)

    assert process.returncode == 2, process.stdout + process.stderr
    assert process.stdout == ""
    assert process.stderr.lower().startswith("usage")
    assert "Traceback" not in process.stderr
    assert list(work.iterdir()) == []
