"""K1 (render): ``Drawing.render_png``, ``render_svg``, ``render_pdf`` and ``render_image`` of a drawing of a file, with no
window (PW6.1 P3, spec section 7.6), on the runtime the session asks for.

The picture of ``render_image`` is the PNG of ``render_png`` decoded here (a small zlib and unfilter decoder), and on each
runtime it is the canvas of the Release app: the app is started by the test on the same file with ``--screenshot ...
--canvas`` (it writes no user settings), and every red, green and blue byte is the same, which ties .NET Framework and
.NET 8 to one picture. The SVG and the PDF are read back for what they state: the root of the one, the header, the page and
the MediaBox of the other. The numbers of the timings test are printed and written into the guide, not asserted but for a
ceiling against a hang.
"""

import ast
import json
import re
import shutil
import statistics
import struct
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
import zlib
from collections.abc import Callable
from pathlib import Path
from subprocess import CompletedProcess
from typing import Any, cast

import numpy as np
import pytest
from numpy.typing import NDArray

import opencad2d
from GeometryHelper.Dxf import DxfLine
from GeometryHelper.Geometry import GeoPoint3
from opencad2d import arrays
from opencad2d.cad import Box, new_file, open_file
from opencad2d.cad._drawing import Drawing

REPOSITORY = Path(__file__).resolve().parents[2]
APP = REPOSITORY / "src" / "OpenCAD2D.App" / "bin" / "Release" / "net8.0-windows" / "OpenCAD2D.exe"

Python = Callable[..., CompletedProcess[str]]


def _decode_png(data: bytes) -> NDArray[np.uint8]:
    """The pixels of an 8-bit PNG of colour type 2 or 6, not interlaced, as an array (height, width, channels)."""
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    position = 8
    idat = b""
    width = height = channels = 0
    while position < len(data):
        length, kind = struct.unpack(">I4s", data[position : position + 8])
        body = data[position + 8 : position + 8 + length]
        position += 12 + length
        if kind == b"IHDR":
            width, height, depth, colour, _, _, interlace = struct.unpack(">IIBBBBB", body)
            assert (depth, interlace) == (8, 0) and colour in (2, 6), (depth, colour, interlace)
            channels = 3 if colour == 2 else 4
        elif kind == b"IDAT":
            idat += body
    raw = zlib.decompress(idat)
    stride = width * channels
    rows = np.zeros((height, stride), dtype=np.uint8)
    for y in range(height):
        start = y * (stride + 1)
        mode = raw[start]
        line = np.frombuffer(raw, dtype=np.uint8, count=stride, offset=start + 1).copy()
        above = rows[y - 1] if y else np.zeros(stride, dtype=np.uint8)
        if mode == 1:
            line = np.cumsum(line.reshape(width, channels), axis=0, dtype=np.uint8).reshape(stride)
        elif mode == 2:
            line = line + above
        elif mode in (3, 4):
            out = [0] * stride
            bytes_in = line.tolist()
            above_bytes = above.tolist()
            for x in range(stride):
                a = out[x - channels] if x >= channels else 0
                b = above_bytes[x]
                if mode == 3:
                    out[x] = (bytes_in[x] + ((a + b) >> 1)) & 255
                else:
                    c = above_bytes[x - channels] if x >= channels else 0
                    p = a + b - c
                    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                    guess = a if pa <= pb and pa <= pc else (b if pb <= pc else c)
                    out[x] = (bytes_in[x] + guess) & 255
            line = np.array(out, dtype=np.uint8)
        else:
            assert mode == 0, mode
        rows[y] = line
    return rows.reshape(height, width, channels)


def _plan() -> Drawing:
    """A drawing 400 by 300 whose extents are its own: a white line corner to corner, a red circle, a yellow arc, a closed
    polyline, a text and an MTEXT."""
    drawing = new_file()
    drawing.add_line((0, 0), (400, 300))
    drawing.add_line((0, 300), (400, 0), color=2)
    drawing.add_circle((200, 150), 60, color=1)
    drawing.add_arc((100, 150), 50, 0.0, 3.0, color=3)
    drawing.add_polyline([(20, 20), (120, 20), (120, 80), (20, 80)], closed=True, color=5)
    drawing.add_text("Plan 123", (250, 40), 20.0, color=6)
    drawing.add_mtext("Lines of text that wrap around the width", (250, 240), 12.0, 120.0)
    drawing.commit()
    return drawing


def _file(tmp_path: Path, name: str = "plan.dxf") -> Path:
    """The plan saved as a DXF in ``tmp_path``."""
    path = tmp_path / name
    _plan().save_as(str(path))
    return path


# ---- one picture, three ways ----------------------------------------------------------------------------------------


def test_render_image_is_the_png_decoded_and_has_the_shape_the_size_rules_give(tmp_path: Path) -> None:
    drawing = _plan()
    png = tmp_path / "plan.png"

    drawing.render_png(str(png), width=400)
    image = drawing.render_image(width=400)

    assert image.shape == (300, 400, 3)
    assert image.dtype == np.uint8
    decoded = _decode_png(png.read_bytes())
    assert decoded.shape == (300, 400, 3)
    assert np.array_equal(decoded, image)
    assert image.any(), "the picture is black"


def test_the_size_of_a_picture_of_model_space_follows_the_rules_of_the_guide() -> None:
    drawing = _plan()

    assert drawing.render_image().shape == (1200, 1600, 3)
    assert drawing.render_image(width=800).shape == (600, 800, 3)
    assert drawing.render_image(height=300).shape == (300, 400, 3)
    assert drawing.render_image(width=500, height=100).shape == (100, 500, 3)
    # The dpi has no effect in model space.
    assert drawing.render_image(dpi=300).shape == (1200, 1600, 3)


def test_a_box_is_fitted_with_no_margin_and_the_corners_may_come_in_either_order() -> None:
    drawing = new_file()
    drawing.add_line((0, 50), (100, 50), color=1)
    drawing.add_line((-500, -500), (500, -500))

    image = drawing.render_image(box=(0, 0, 100, 100), width=200, height=200)
    other = drawing.render_image(box=Box(100, 100, 0, 0), width=200, height=200)

    assert np.array_equal(image, other)
    # The line y = 50 lies on the edge of rows 99 and 100 of 200: one pixel wide, half on each, red in both, edge to edge.
    lit = np.nonzero(image[:, :, 0])[0]
    assert sorted(set(lit.tolist())) == [99, 100]
    assert image[99:101, :, 0].all() and not image[:, :, 1:].any()


def test_the_ground_is_black_or_white_and_white_draws_colour_7_black() -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (100, 0))
    drawing.add_line((0, 0), (0, 100), color=1)

    black = drawing.render_image(width=200)
    white = drawing.render_image(width=200, ground="white")

    assert tuple(black[0, 0]) == (0, 0, 0)
    assert tuple(white[0, 0]) == (255, 255, 255)
    # Colour 7 is white on the black ground and black on the white one; a line a pixel wide is partly on two rows.
    assert black[:, :, 1].max() > 100 and white[:, :, 1].min() < 200
    assert (white == 255).all(axis=2).sum() > 100


def test_what_is_queued_is_committed_before_the_picture_as_save_does(tmp_path: Path) -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (100, 0), color=1)
    drawing.add_line((0, 0), (0, 100), color=1)

    image = drawing.render_image(width=100)

    assert image[:, :, 0].max() > 100 and not image[:, :, 1:].any()
    assert drawing.commit() == [], "nothing was left queued"


def test_a_layout_is_drawn_on_white_paper_at_its_sheet_in_pixels() -> None:
    drawing = new_file()
    drawing.add(DxfLine(GeoPoint3(0, 0, 0), GeoPoint3(100, 50, 0)), layout="Layout1")
    drawing.commit()

    by_name = drawing.render_image(layout="layout1", box=(0, 0, 100, 50), ground="black")
    by_dpi = drawing.render_image(layout="Layout1", box=(0, 0, 100, 50), dpi=254)

    # 100 by 50 mm of paper at 96 pixels to the inch is 378 by 189; at 254 it is exactly 1 000 by 500.
    assert by_name.shape == (189, 378, 3)
    assert by_dpi.shape == (500, 1000, 3)
    assert (by_name == 255).all(axis=2).any(), "the paper of a layout is white whatever the ground"


def test_the_picture_of_a_drawing_opened_from_a_file_is_the_picture_of_the_drawing_made(tmp_path: Path) -> None:
    made = _plan().render_image(width=400)
    opened = open_file(str(_file(tmp_path))).render_image(width=400)

    assert np.array_equal(made, opened)


def test_a_render_of_a_drawing_with_no_entity_is_a_picture_of_the_default_size() -> None:
    image = new_file().render_image()

    assert image.shape == (960, 1600, 3)
    assert not image.any()


# ---- refusals --------------------------------------------------------------------------------------------------------


@pytest.mark.parametrize(
    ("arguments", "message"),
    [
        ({"ground": "grey"}, "ground is 'black' or 'white'"),
        ({"dpi": 0.0}, "dpi"),
        ({"dpi": float("nan")}, "dpi"),
        ({"box": (0.0, 0.0, float("inf"), 1.0)}, "box"),
        ({"width": -1}, "negative"),
        ({"width": 20000}, "over the most"),
        ({"width": 12000, "height": 12000}, "over the most"),
        ({"width": 1.5}, "whole number"),
        ({"width": True}, "whole number"),
        ({"layout": "Elsewhere"}, "no layout named"),
    ],
)
def test_a_render_that_cannot_be_drawn_raises_value_error_naming_why(arguments: dict[str, Any], message: str) -> None:
    drawing = _plan()

    with pytest.raises(ValueError, match=message):
        drawing.render_image(**arguments)
    with pytest.raises(ValueError, match=message):
        drawing.render_png("never.png", **arguments)


def test_a_failed_render_leaves_no_file(tmp_path: Path) -> None:
    drawing = _plan()

    with pytest.raises(ValueError):
        drawing.render_png(str(tmp_path / "x.png"), width=-5)
    with pytest.raises(ValueError):
        drawing.render_svg(str(tmp_path / "x.svg"), ground="red")  # pyright: ignore[reportArgumentType]

    assert list(tmp_path.iterdir()) == []


# ---- SVG and PDF read back -------------------------------------------------------------------------------------------


def test_an_svg_is_an_svg_root_of_the_pictures_size_with_the_drawing_in_it(tmp_path: Path) -> None:
    drawing = _plan()
    path = tmp_path / "plan.svg"

    drawing.render_svg(str(path), width=800)

    root = ET.parse(path).getroot()
    assert root.tag == "{http://www.w3.org/2000/svg}svg"
    assert (root.get("width"), root.get("height"), root.get("viewBox")) == ("800", "600", "0 0 800 600")
    text = path.read_text(encoding="utf-8")
    assert "<path" in text or "<line" in text, "the drawing is not in it"
    assert not path.read_bytes().startswith(b"\xef\xbb\xbf")


def test_an_svg_of_the_ground_white_has_a_white_background_rectangle(tmp_path: Path) -> None:
    path = tmp_path / "white.svg"

    _plan().render_svg(str(path), width=400, ground="white")

    assert re.search(r'<rect[^>]*fill="(#ffffff|white|rgb\(255,\s*255,\s*255\))"', path.read_text(encoding="utf-8"), re.I)


def test_a_pdf_is_one_page_of_a_sheet_in_points_with_the_drawing_fitted_to_it(tmp_path: Path) -> None:
    path = tmp_path / "plan.pdf"

    _plan().render_pdf(str(path))

    data = path.read_bytes()
    assert data.startswith(b"%PDF-")
    text = data.decode("latin-1")
    assert len(re.findall(r"/Type\s*/Page\b(?!s)", text)) == 1
    box = re.search(r"/MediaBox\s*\[\s*([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s*\]", text)
    assert box is not None
    left, bottom, right, top = (float(g) for g in box.groups())
    # ISO A3 on its side: 420 by 297 mm is 1 190.55 by 841.89 points.
    assert (left, bottom) == (0.0, 0.0)
    assert right == pytest.approx(1190.55, abs=0.01)
    assert top == pytest.approx(841.89, abs=0.01)


def test_a_pdf_of_a_layout_with_nothing_to_plot_raises_the_net_exception(tmp_path: Path) -> None:
    from System import InvalidOperationException

    with pytest.raises(InvalidOperationException):
        new_file().render_pdf(str(tmp_path / "none.pdf"))


def test_a_pdf_of_a_layout_that_is_not_there_is_a_value_error(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="no layout named"):
        _plan().render_pdf(str(tmp_path / "x.pdf"), layout="Elsewhere")


# ---- the same pixels as the app, on this runtime ---------------------------------------------------------------------


@pytest.mark.skipif(not APP.is_file(), reason="the Release build of the app is not there")
def test_the_picture_is_the_canvas_of_the_app_byte_for_byte_on_this_runtime(tmp_path: Path) -> None:
    drawing_file = _file(tmp_path)
    window = tmp_path / "window.png"
    canvas = tmp_path / "canvas.png"
    settings = Path.home() / "AppData" / "Roaming" / "OpenCAD2D" / "settings.json"
    before = settings.read_bytes() if settings.is_file() else None

    process = subprocess.run(
        [
            str(APP),
            "--screenshot",
            str(window),
            str(drawing_file),
            "--size",
            "640",
            "400",
            "--run",
            "UCSICON;OFF;GRIDMODE;0;ZOOM;E",
            "--canvas",
            str(canvas),
        ],
        capture_output=True,
        text=True,
        timeout=180,
    )

    assert process.returncode == 0, process.stdout + process.stderr
    assert (settings.read_bytes() if settings.is_file() else None) == before, "the app wrote the user's settings"
    pixels = _decode_png(canvas.read_bytes())
    height, width = pixels.shape[:2]
    mine = open_file(str(drawing_file)).render_image(width=width, height=height)
    assert np.array_equal(pixels[:, :, :3], mine), f"{int(np.count_nonzero(pixels[:, :, :3] != mine))} bytes differ"


# ---- the loader, the stubs, the arrays -------------------------------------------------------------------------------


def test_importing_the_package_and_making_a_drawing_load_neither_skiasharp_nor_the_rendering_assembly(
    python: Python,
) -> None:
    code = (
        "import opencad2d\n"
        "from opencad2d.cad import new_file\n"
        "import System\n"
        "names = lambda: {a.GetName().Name for a in System.AppDomain.CurrentDomain.GetAssemblies()}\n"
        "drawing = new_file(); drawing.add_line((0, 0), (1, 1)); drawing.commit()\n"
        "print('Skia' if 'SkiaSharp' in names() or 'OpenCAD2D.Rendering' in names() else 'none')\n"
        "drawing.render_image(width=10)\n"
        "print('Skia' if 'SkiaSharp' in names() and 'OpenCAD2D.Rendering' in names() else 'none')\n"
    )

    process = python(code, env={"OPENCAD2D_RUNTIME": opencad2d.runtime()})

    assert process.returncode == 0, process.stderr
    assert process.stdout.split() == ["none", "Skia"]


def test_a_library_folder_without_the_native_library_raises_import_error_naming_it(python: Python, tmp_path: Path) -> None:
    lib = Path(opencad2d.__file__).resolve().parent / "lib"
    thin = tmp_path / "lib"
    thin.mkdir()
    for name in ("GeometryHelper", "OpenCAD2D.Core", "OpenCAD2D.Remote", "OpenCAD2D.Rendering", "SkiaSharp"):
        shutil.copy2(lib / (name + ".dll"), thin / (name + ".dll"))
    for folder in ("Fonts", "Hatches", "Linetypes"):
        shutil.copytree(lib / folder, thin / folder)

    process = python(
        "from opencad2d.cad import new_file\n"
        "d = new_file(); d.add_line((0, 0), (1, 1))\n"
        "try:\n"
        "    d.render_image(width=10)\n"
        "except ImportError as error:\n"
        "    print(error)\n",
        env={"OPENCAD2D_RUNTIME": opencad2d.runtime(), "OPENCAD2D_LIB": str(thin)},
    )

    assert process.returncode == 0, process.stderr
    assert "libSkiaSharp.dll" in process.stdout


def test_the_native_library_is_found_in_the_runtimes_folder_of_the_apps_layout(python: Python, tmp_path: Path) -> None:
    lib = Path(opencad2d.__file__).resolve().parent / "lib"
    app = tmp_path / "lib"
    shutil.copytree(lib, app)
    (app / "runtimes" / "win-x64" / "native").mkdir(parents=True)
    shutil.move(str(app / "libSkiaSharp.dll"), str(app / "runtimes" / "win-x64" / "native" / "libSkiaSharp.dll"))

    process = python(
        "from opencad2d.cad import new_file\n"
        "d = new_file(); d.add_line((0, 0), (1, 1))\n"
        "print(d.render_image(width=10).shape)\n",
        env={"OPENCAD2D_RUNTIME": opencad2d.runtime(), "OPENCAD2D_LIB": str(app)},
    )

    assert process.returncode == 0, process.stderr
    assert process.stdout.strip() == "(10, 10, 3)"


def test_the_stubs_have_the_facade_and_none_of_the_presenters_namespace(pkg_dir: Path) -> None:
    stub = (pkg_dir.parent / "OpenCAD2D-stubs" / "__init__.pyi").read_text(encoding="utf-8")

    assert "class CadRender(" in stub and "class CadRenderOptions(" in stub and "class CadRenderImage(" in stub
    assert "SkiaCanvasRenderer" not in stub and "CadPdfWriter" not in stub


def test_to_numpy_copies_a_dotnet_byte_array_as_uint8() -> None:
    from System import Array, Byte

    bytes_in = Array[Byte]([0, 1, 127, 128, 255])

    copied = arrays.to_numpy(bytes_in)  # pyright: ignore[reportUnknownVariableType]

    assert copied.dtype == np.uint8 and copied.tolist() == [0, 1, 127, 128, 255]
    assert arrays.to_numpy(Array[Byte](0)).shape == (0,)


# ---- the guide -------------------------------------------------------------------------------------------------------

GUIDE = REPOSITORY / "docs" / "python.md"


def test_the_code_of_the_guide_section_runs_and_gives_the_values_its_comments_give(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    text = GUIDE.read_text(encoding="utf-8")
    start = text.index("## Rendering without a window")
    following = re.search(r"^## ", text[start + 3 :], flags=re.MULTILINE)
    section = text[start:] if following is None else text[start : start + 3 + following.start()]  # up to the next section
    blocks = re.findall(r"```python\n(.*?)```", section, flags=re.DOTALL)
    monkeypatch.chdir(tmp_path)
    namespace: dict[str, Any] = {}
    checked = 0

    for source in blocks:
        lines = source.splitlines()
        for node in ast.parse(source).body:
            line = lines[node.end_lineno - 1]  # type: ignore[index]
            comment = line.rpartition("#")[2].strip() if "#" in line else ""
            expected: Any = None
            if isinstance(node, ast.Expr) and comment:
                try:
                    expected = ast.literal_eval(comment)
                except (ValueError, SyntaxError):
                    expected = None
            if expected is not None:
                got = eval(compile(ast.Expression(cast(ast.Expr, node).value), "<guide>", "eval"), namespace)  # noqa: S307
                assert got == expected, line
                checked += 1
            else:
                exec(compile(ast.Module([node], []), "<guide>", "exec"), namespace)  # noqa: S102

    assert len(blocks) == 1 and checked == 2
    assert sorted(p.name for p in tmp_path.iterdir()) == ["plan.pdf", "plan.png", "plan.svg"]
    assert _decode_png((tmp_path / "plan.png").read_bytes()).shape == (1200, 1600, 3)


# ---- timings, printed and not asserted -------------------------------------------------------------------------------

# Five drawings of 40 objects or so each, every call on each timed once for the first call (the native library and the font
# scan are in it in the first drawing's first call) and five times more for the median. The picture is 1 600 by 960 pixels.
_TIMINGS = r"""
import json, statistics, sys, time
from opencad2d.cad import new_file

def curves(drawing):
    for i in range(40):
        drawing.add_line((0, 10 * i), (400, 10 * i + 5), color=1 + i % 7)
        drawing.add_circle((10 * i, 150), 5 + i, color=1 + i % 7)
        drawing.add_text("Label %d" % i, (10 * i, 10 * i), 6.0)

def paragraphs(drawing):
    for i in range(30):
        drawing.add_mtext("Paragraph %d of words that wrap around the width of the box and go on" % i, (0, 40 * i), 5.0, 150.0)

def hatches(drawing):
    for i in range(20):
        corners = [(30 * i, 0), (30 * i + 25, 0), (30 * i + 25, 25), (30 * i, 25)]
        drawing.add_hatch(corners, pattern="ANSI31", scale=0.5, color=1 + i % 7)
        drawing.add_polyline(corners, closed=True)

def dimensions(drawing):
    for i in range(20):
        drawing.add_linear_dim((0, 30 * i), (100 + 5 * i, 30 * i), (50, 30 * i + 10))

built = {"curves": curves, "paragraphs": paragraphs, "hatches": hatches, "dimensions": dimensions}
folder = sys.argv[1]
result = {}
for name, make in built.items():
    drawing = new_file()
    make(drawing)
    drawing.commit()
    calls = (
        ("png", lambda: drawing.render_png(folder + "/t.png", width=1600, height=960)),
        ("svg", lambda: drawing.render_svg(folder + "/t.svg", width=1600, height=960)),
        ("pdf", lambda: drawing.render_pdf(folder + "/t.pdf")),
        ("image", lambda: drawing.render_image(width=1600, height=960)),
    )
    result[name] = {}
    for kind, call in calls:
        started = time.perf_counter()
        call()
        first = time.perf_counter() - started
        later = []
        for _ in range(5):
            started = time.perf_counter()
            call()
            later.append(time.perf_counter() - started)
        result[name][kind] = [round(first, 3), round(statistics.median(later), 3)]
print(json.dumps(result))
"""


def test_the_first_call_and_the_median_of_five_after_it_stay_far_below_a_hang(python: Python, tmp_path: Path) -> None:
    process = python(_TIMINGS, str(tmp_path), env={"OPENCAD2D_RUNTIME": opencad2d.runtime()})

    assert process.returncode == 0, process.stderr
    timings: dict[str, dict[str, list[float]]] = json.loads(process.stdout.strip().splitlines()[-1])
    print(f"\nrender timings on {opencad2d.runtime()}, seconds as [first call, median of 5 after]:")
    for name, kinds in timings.items():
        print(f"  {name:11} " + "  ".join(f"{kind} {pair}" for kind, pair in kinds.items()))
    assert max(max(pair) for kinds in timings.values() for pair in kinds.values()) < 10.0
