"""K3: the render calls on a drawing of a running app (the road map's "render from the app"): ``render_png``, ``render_svg``,
``render_pdf``, ``render_image``, ``render_transform``, ``render_masks`` and ``to_coco`` of a drawing the app shows draw the
drawing as the app has it, the changes not saved included, in this process, from the ``database`` request, and give what the
same drawing saved and opened as a file gives, byte for byte. The drawing's file is never written.

``connect()`` finds the app in the folder of the real one, which a test never touches, so the tests make the ``App`` over the
``CadApplication`` the ``app`` fixture connected to the test server.
"""

import json
import math
from collections.abc import Iterator
from pathlib import Path
from typing import Any

import numpy as np
import pytest

from opencad2d.cad import Drawing, new_file, open_file


@pytest.fixture
def cad_app(app: Any) -> Iterator[Any]:
    """The ``opencad2d.cad`` App over this test's connection."""
    from opencad2d.cad import App

    with App(app) as made:
        yield made


def _plan(drawing: Drawing) -> None:
    """A line, a circle, two words in an SHX style (one turned), a linear dimension and a line through the first word."""
    drawing.text_styles.add("NOTE", font="romans.shx")
    drawing.add_line((0, 0), (400, 300))
    drawing.add_circle((200, 150), 60, color=1)
    drawing.add_text("Plate A-12", (50, 200), 30.0, text_style="NOTE")
    drawing.add_text("Plate A-12", (250, 20), 12.0, rotation=math.radians(30), text_style="NOTE")
    drawing.add_linear_dim((0, -40), (400, -40), (200, -70))
    drawing.add_line((140, 0), (140, 260))
    drawing.commit()


def _file(tmp_path: Path) -> Path:
    """``plan.dxf``: the plan above, saved."""
    path = tmp_path / "plan.dxf"
    drawing = new_file()
    _plan(drawing)
    drawing.save_as(str(path))
    return path


def _lit(picture: Any, point: Any) -> bool:
    """Whether a pixel within 2 of the matrix's image of the point is not the black ground."""
    column, row = (int(n) for n in np.floor(point))
    return bool(picture[max(row - 2, 0) : row + 3, max(column - 2, 0) : column + 3].any())


def test_an_unsaved_line_added_through_the_app_is_in_the_pixels(cad_app: Any, tmp_path: Path) -> None:
    shown = cad_app.open(str(_file(tmp_path)))
    m = shown.render_transform(width=800)
    point = (m @ [350, 37.5, 1])[:2]  # on the line (0, 300) to (400, 0) to come, 225 from the line the plan has
    before = shown.render_image(width=800)

    shown.add_line((0, 300), (400, 0))
    shown.commit()
    after = shown.render_image(width=800)

    assert shown.is_modified
    assert not _lit(before, point)
    assert _lit(after, point)
    assert after.shape == before.shape


def test_what_is_queued_is_committed_and_drawn_as_the_calls_on_a_file_do(cad_app: Any, tmp_path: Path) -> None:
    shown = cad_app.open(str(_file(tmp_path)))
    point = (shown.render_transform(width=800) @ [350, 37.5, 1])[:2]

    shown.add_line((0, 300), (400, 0))  # queued, not committed

    assert _lit(shown.render_image(width=800), point)


def test_the_png_of_the_app_is_the_png_of_the_file_byte_for_byte(cad_app: Any, tmp_path: Path) -> None:
    path = _file(tmp_path)
    shown = cad_app.open(str(path))
    opened = open_file(str(path))

    for options in ({}, {"width": 800}, {"ground": "white", "dpi": 150}, {"box": (0, 0, 200, 150), "width": 400}):
        shown.render_png(str(tmp_path / "app.png"), **options)
        opened.render_png(str(tmp_path / "file.png"), **options)
        assert (tmp_path / "app.png").read_bytes() == (tmp_path / "file.png").read_bytes(), options


def test_the_png_of_changes_not_saved_is_the_png_of_the_drawing_saved_and_opened(cad_app: Any, tmp_path: Path) -> None:
    shown = cad_app.open(str(_file(tmp_path)))
    shown.add_line((0, 300), (400, 0))
    shown.add_circle((300, 100), 25)
    shown.commit()
    shown.render_png(str(tmp_path / "app.png"))
    copy = tmp_path / "copy.dxf"
    shown.save_as(str(copy))
    open_file(str(copy)).render_png(str(tmp_path / "file.png"))

    assert (tmp_path / "app.png").read_bytes() == (tmp_path / "file.png").read_bytes()


def test_the_svg_the_pixels_and_the_labels_of_the_app_are_those_of_the_file(cad_app: Any, tmp_path: Path) -> None:
    path = _file(tmp_path)
    shown = cad_app.open(str(path))
    opened = open_file(str(path))

    shown.render_svg(str(tmp_path / "app.svg"), width=800)
    opened.render_svg(str(tmp_path / "file.svg"), width=800)
    assert (tmp_path / "app.svg").read_bytes() == (tmp_path / "file.svg").read_bytes()
    assert np.array_equal(shown.render_image(width=640), opened.render_image(width=640))
    assert np.array_equal(shown.render_transform(width=640), opened.render_transform(width=640))
    with_app = shown.render_masks(instances=True, width=640)
    with_file = opened.render_masks(instances=True, width=640)
    assert np.array_equal(with_app.classes, with_file.classes)
    assert np.array_equal(with_app.instances, with_file.instances)
    assert with_app.handles == with_file.handles
    assert int(with_app.classes.max()) > 0


def test_the_pdf_of_the_app_is_the_pdf_of_the_file(cad_app: Any, tmp_path: Path) -> None:
    path = _file(tmp_path)
    shown = cad_app.open(str(path))

    shown.render_pdf(str(tmp_path / "app.pdf"))
    open_file(str(path)).render_pdf(str(tmp_path / "file.pdf"))

    app_pdf = (tmp_path / "app.pdf").read_bytes()
    file_pdf = (tmp_path / "file.pdf").read_bytes()
    assert app_pdf.startswith(b"%PDF-")
    assert app_pdf == file_pdf


def test_to_coco_of_the_app_is_the_coco_of_the_file(cad_app: Any, tmp_path: Path) -> None:
    path = _file(tmp_path)
    shown = cad_app.open(str(path))
    shown.add_text("Added in the app", (10, 250), 12.0)
    shown.commit()
    copy = tmp_path / "copy.dxf"
    shown.save_as(str(copy))
    folder = tmp_path / "out"
    folder.mkdir()

    from_app = shown.to_coco(str(folder / "app.png"), classes=["words", "lines", "dimensions"], segmentation=True, width=800)
    from_file = open_file(str(copy)).to_coco(
        str(folder / "file.png"), classes=["words", "lines", "dimensions"], segmentation=True, width=800
    )

    assert len(from_app["annotations"]) >= 5
    assert json.dumps(from_app["annotations"]) == json.dumps(from_file["annotations"])
    assert from_app["categories"] == from_file["categories"]
    assert from_app["images"] == [{"id": 1, "file_name": "app.png", "width": 800, "height": 743}]
    assert (folder / "app.png").read_bytes() == (folder / "file.png").read_bytes()


def test_the_layout_the_drawing_was_left_in_is_the_one_drawn(cad_app: Any, tmp_path: Path) -> None:
    path = _file(tmp_path)
    shown = cad_app.open(str(path))
    shown.current_layout = "Layout1"
    copy = tmp_path / "copy.dxf"
    shown.save_as(str(copy))

    shown.render_png(str(tmp_path / "app.png"))
    open_file(str(copy)).render_png(str(tmp_path / "file.png"), layout="Layout1")
    shown.render_png(str(tmp_path / "app-model.png"), layout="Model")

    assert (tmp_path / "app.png").read_bytes() == (tmp_path / "file.png").read_bytes()
    assert (tmp_path / "app.png").read_bytes() != (tmp_path / "app-model.png").read_bytes()


def test_a_render_leaves_the_file_of_the_drawing_as_it_was(cad_app: Any, tmp_path: Path) -> None:
    path = _file(tmp_path)
    before = (path.read_bytes(), path.stat().st_mtime_ns)
    shown = cad_app.open(str(path))
    shown.add_line((0, 300), (400, 0))
    shown.commit()

    shown.render_png(str(tmp_path / "a.png"))
    shown.render_svg(str(tmp_path / "a.svg"))
    shown.render_pdf(str(tmp_path / "a.pdf"))
    shown.render_image()
    shown.render_masks()
    shown.to_coco(str(tmp_path / "b.png"))

    assert (path.read_bytes(), path.stat().st_mtime_ns) == before
    assert shown.is_modified
    assert shown.path == str(path)
    assert {p.name for p in tmp_path.iterdir()} >= {"a.pdf", "a.png", "a.svg", "b.png", "plan.dxf"}
    assert {p.suffix for p in tmp_path.iterdir() if p.is_file()} <= {".pdf", ".png", ".svg", ".dxf", ".txt"}


def test_a_refusal_of_the_options_is_the_value_error_a_file_gives(cad_app: Any, tmp_path: Path) -> None:
    shown = cad_app.open(str(_file(tmp_path)))

    with pytest.raises(ValueError, match="above nought"):
        shown.render_png(str(tmp_path / "x.png"), dpi=0)
    with pytest.raises(ValueError, match="no layout named"):
        shown.render_image(layout="Elsewhere")
    with pytest.raises(ValueError, match="A class is one of"):
        shown.render_masks(classes=["walls"])
    assert not (tmp_path / "x.png").exists()


def test_the_drawing_of_a_second_render_is_the_drawing_as_it_is_then(cad_app: Any, tmp_path: Path) -> None:
    shown = cad_app.open(str(_file(tmp_path)))
    shown.to_coco(str(tmp_path / "one.png"))  # keeps its document for the picture it writes: only that
    shown.add_line((0, 300), (400, 0))
    shown.commit()
    shown.render_png(str(tmp_path / "two.png"))
    copy = tmp_path / "copy.dxf"
    shown.save_as(str(copy))
    open_file(str(copy)).render_png(str(tmp_path / "file.png"))

    assert (tmp_path / "two.png").read_bytes() == (tmp_path / "file.png").read_bytes()
    assert (tmp_path / "two.png").read_bytes() != (tmp_path / "one.png").read_bytes()


class _Counting:
    """A ``CadRemoteDocument`` that counts the databases asked of it."""

    def __init__(self, inner: Any) -> None:
        self.inner = inner
        self.asked = 0

    def GetDatabase(self) -> Any:  # noqa: N802 - the name of the .NET member
        self.asked += 1
        return self.inner.GetDatabase()

    def __getattr__(self, name: str) -> Any:
        return getattr(self.inner, name)


def test_to_coco_draws_its_labels_and_its_picture_from_one_copy_and_every_other_call_from_its_own(
    cad_app: Any, tmp_path: Path
) -> None:
    shown = cad_app.open(str(_file(tmp_path)))
    counted = _Counting(shown._backend._document)  # pyright: ignore[reportPrivateUsage]
    shown._backend._document = counted  # pyright: ignore[reportPrivateUsage]

    shown.to_coco(str(tmp_path / "a.png"))
    one = counted.asked
    shown.render_png(str(tmp_path / "b.png"))
    shown.render_png(str(tmp_path / "c.png"))
    shown.render_masks()

    assert (one, counted.asked) == (1, 4)
