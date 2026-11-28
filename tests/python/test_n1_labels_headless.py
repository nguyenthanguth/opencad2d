"""N1 (labels): ``Drawing.render_transform``, ``render_masks`` and ``to_coco`` of a drawing of a file, with no window (PW7.4), on the
runtime the session asks for.

The matrix is the view the picture is drawn by, so a point of a line maps to a pixel the line coloured and a box drawn back from it
covers the pixels of its entity. The masks are the picture drawn in flat ids with no antialiasing: the pixels of a text are lit in
the picture of that text alone (the font's monochrome letters may sit a pixel from the antialiased ones, so a pixel of a mask is
within 2 pixels of a lit one), the classes are the five of ``LABEL_CLASSES``, the instances name the objects, and an object's
box in the COCO file holds every pixel of it. The drawings are built in code. The numbers of the timings test are printed and
written into the guide, not asserted but for a ceiling against a hang.
"""

import json
import statistics
import time
from pathlib import Path
from typing import Any

import numpy as np
import pytest
from numpy.typing import NDArray

from opencad2d.cad import LABEL_CLASSES, Drawing, RenderMasks, new_file


def _plan() -> Drawing:
    """A line, a circle, a text, an MTEXT, a hatch with a line under it, two dimensions and a block inserted twice."""
    drawing = new_file()
    drawing.add_line((0, 0), (400, 300))
    drawing.add_circle((300, 100), 40, color=1)
    drawing.add_text("Plate A-12", (20, 220), 20.0, color=3)
    drawing.add_mtext("Lines of text that wrap around the width of the box", (20, 160), 10.0, 150.0)
    square = [(250, 200), (350, 200), (350, 280), (250, 280)]
    drawing.add_polyline(square, closed=True, color=5)
    drawing.add_hatch(square, pattern="ANSI31", scale=2.0, color=2)
    drawing.add_linear_dim((0, 0), (200, 0), (100, -30))
    drawing.add_linear_dim((0, 0), (0, 100), (-30, 50))
    drawing.blocks.add("BOLT", [_line((0, 0), (20, 0)), _line((0, -10), (0, 10))])
    drawing.commit()
    drawing.add_insert("BOLT", (50, 50))
    drawing.add_insert("BOLT", (90, 50), scale=2.0, rotation=0.5)
    drawing.commit()
    return drawing


def _line(start: tuple[float, float], end: tuple[float, float]) -> Any:
    from GeometryHelper.Dxf import DxfLine
    from GeometryHelper.Geometry import GeoPoint3

    return DxfLine(GeoPoint3(start[0], start[1], 0), GeoPoint3(end[0], end[1], 0))


def _lit(picture: NDArray[np.uint8]) -> NDArray[np.bool_]:
    """The pixels of a picture that are not black."""
    return picture.any(axis=2)


def _grown(mask: NDArray[np.bool_], by: int) -> NDArray[np.bool_]:
    """A mask grown by ``by`` pixels each way (a square structuring element)."""
    grown = mask.copy()
    height, width = mask.shape
    for dy in range(-by, by + 1):
        for dx in range(-by, by + 1):
            shifted = np.zeros_like(mask)
            shifted[max(dy, 0) : height + min(dy, 0), max(dx, 0) : width + min(dx, 0)] = mask[
                max(-dy, 0) : height + min(-dy, 0), max(-dx, 0) : width + min(-dx, 0)
            ]
            grown |= shifted
    return grown


# ---- the matrix ------------------------------------------------------------------------------------------------------


def test_the_matrix_of_a_rectangle_is_the_view_zoom_extents_draws_by() -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (400, 300))

    m = drawing.render_transform()

    assert m.shape == (3, 3)
    assert m.dtype == np.float64
    assert np.allclose(m, [[3.6, 0, 80], [0, -3.6, 1140], [0, 0, 1]])
    assert np.allclose((m @ [400, 300, 1])[:2], [1520, 60])
    inverse = np.linalg.inv(m)
    assert np.allclose((inverse @ [80, 1140, 1])[:2], [0, 0])


def test_the_matrix_follows_the_size_and_the_box_of_the_call() -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (400, 300))

    wide = drawing.render_transform(width=800)
    boxed = drawing.render_transform(box=(0, 0, 200, 150), width=400, height=300)

    assert np.allclose(wide, [[1.8, 0, 40], [0, -1.8, 570], [0, 0, 1]])
    # A box is fitted with no margin: the part 200 by 150 fills 400 by 300 pixels, its lower left corner at the bottom left.
    assert np.allclose(boxed, [[2, 0, 0], [0, -2, 300], [0, 0, 1]])


def test_a_point_of_a_line_maps_to_a_pixel_the_line_coloured() -> None:
    drawing = _plan()
    picture = drawing.render_image(width=1000)
    m = drawing.render_transform(width=1000)
    lit = _lit(picture)

    for t in np.linspace(0.0, 1.0, 11):
        column, row = np.floor((m @ [400 * t, 300 * t, 1])[:2]).astype(int)
        assert lit[max(row - 1, 0) : row + 2, max(column - 1, 0) : column + 2].any(), (column, row)


def test_a_box_drawn_back_from_the_matrix_covers_the_pixels_of_its_entity() -> None:
    drawing = _plan()
    masks = drawing.render_masks(instances=True, classes=list(LABEL_CLASSES))
    table = drawing.bounds()

    for handle, box in zip(table.handles, table.boxes.tolist()):
        number = masks.handles.index(handle) + 1
        rows, columns = np.nonzero(masks.instances == number)
        if not rows.size:
            continue
        low = (masks.transform @ [box[0], box[3], 1])[:2]
        high = (masks.transform @ [box[2], box[1], 1])[:2]
        # The box of the drawing through the matrix, a pixel wider for the width of a line.
        assert columns.min() >= np.floor(low[0]) - 1 and columns.max() < np.ceil(high[0]) + 1, handle
        assert rows.min() >= np.floor(low[1]) - 1 and rows.max() < np.ceil(high[1]) + 1, handle


# ---- the masks -------------------------------------------------------------------------------------------------------


def test_the_masks_have_the_shape_and_types_of_the_picture() -> None:
    drawing = _plan()

    masks = drawing.render_masks(width=640)
    with_instances = drawing.render_masks(width=640, instances=True)
    picture = drawing.render_image(width=640)

    assert isinstance(masks, RenderMasks)
    assert masks.classes.shape == picture.shape[:2]
    assert masks.classes.dtype == np.uint8
    assert masks.instances is None
    assert with_instances.instances is not None
    assert with_instances.instances.dtype == np.int32
    assert with_instances.instances.shape == masks.classes.shape
    assert np.array_equal(with_instances.classes, masks.classes)
    assert np.allclose(masks.transform, drawing.render_transform(width=640))
    assert masks.names == ["background", "words", "dimensions", "lines", "hatches", "blocks"]


def test_the_pixels_of_a_text_are_the_lit_pixels_of_that_text_alone() -> None:
    drawing = new_file()
    drawing.add_text("Plate A-12", (20, 20), 20.0)
    drawing.add_mtext("Lines of text that wrap around the width of the box", (20, 120), 10.0, 150.0)

    masks = drawing.render_masks(width=800)
    picture = drawing.render_image(width=800)

    words = masks.classes == 1
    lit = _lit(picture)
    assert int(words.sum()) > 2000
    assert not (masks.classes > 1).any()
    # The picture is antialiased and the mask is not: every pixel of the mask is lit or beside a lit pixel, and nearly every
    # bright pixel of the picture is a pixel of the mask or beside one.
    assert not (words & ~_grown(lit, 2)).any()
    bright = (picture >= 128).any(axis=2)
    missed = bright & ~_grown(words, 2)
    assert int(missed.sum()) <= int(bright.sum()) // 50, (int(missed.sum()), int(bright.sum()))
    assert int(words.sum()) <= int(lit.sum())


def test_every_class_is_found_where_the_drawing_has_it() -> None:
    drawing = _plan()

    masks = drawing.render_masks(classes=list(LABEL_CLASSES[:4]), width=1200)

    counts = {name: int((masks.classes == number).sum()) for number, name in enumerate(masks.names)}
    assert counts["blocks"] == 0
    for name in ("words", "dimensions", "lines", "hatches"):
        assert counts[name] > 200, counts
    assert counts["background"] > counts["lines"]
    assert sum(counts.values()) == masks.classes.size


def test_a_block_is_class_five_only_where_blocks_are_asked_for() -> None:
    drawing = _plan()

    pieces = drawing.render_masks(width=1200, instances=True)
    blocks = drawing.render_masks(classes=list(LABEL_CLASSES), width=1200, instances=True)

    assert int((pieces.classes == 5).sum()) == 0
    assert blocks.instances is not None
    inserts = {entity.handle for entity in drawing.entities("INSERT")}
    on_blocks = blocks.classes == 5
    assert int(on_blocks.sum()) > 100
    assert {blocks.handles[k - 1] for k in np.unique(blocks.instances[on_blocks])} <= inserts
    # Where the block's pieces were lines, the pixel is now the block's: the other classes lose only what the blocks cover.
    assert int((blocks.classes == 3).sum()) < int((pieces.classes == 3).sum())


def test_the_instances_name_the_object_that_draws_each_pixel() -> None:
    drawing = new_file()
    line = drawing.add_line((0, 0), (100, 0))
    circle = drawing.add_circle((50, 50), 20)
    text = drawing.add_text("Plate", (0, 80), 15.0)
    drawing.commit()

    masks = drawing.render_masks(instances=True, width=800)

    assert masks.instances is not None
    assert masks.handles == [line.handle, circle.handle, text.handle]
    for number, (entity, cls) in enumerate(((line, 3), (circle, 3), (text, 1)), start=1):
        pixels = masks.instances == number
        assert int(pixels.sum()) > 100, entity.handle
        assert set(np.unique(masks.classes[pixels]).tolist()) == {cls}
    assert not (masks.instances[masks.classes == 0]).any()
    assert int(masks.instances.max()) == 3


def test_what_is_on_top_in_the_picture_is_on_top_in_the_masks() -> None:
    drawing = new_file()
    square = [(0, 0), (200, 0), (200, 100), (0, 100)]
    drawing.add_hatch(square, pattern="SOLID", color=1)
    drawing.add_text("OVER", (20, 30), 40.0, color=2)
    drawing.add_polyline(square, closed=True, color=3)
    drawing.commit()

    masks = drawing.render_masks(width=800)

    words = int((masks.classes == 1).sum())
    assert words > 300
    # The hatch is drawn under the letters and under the outline: the picture's red is where no letter is.
    picture = drawing.render_image(width=800)
    red = (picture[:, :, 0] > 200) & (picture[:, :, 1] < 50)
    assert int(((masks.classes == 4) & red).sum()) > 20000
    # The font's monochrome letters may sit a pixel from the antialiased ones: a few pixels of the letters' edge are red.
    assert int(((masks.classes == 1) & red).sum()) <= words // 50


def test_the_classes_asked_are_all_that_is_labelled() -> None:
    drawing = _plan()

    some = drawing.render_masks(classes=["words", "hatches"], width=1000)
    every = drawing.render_masks(classes=list(LABEL_CLASSES), width=1000)

    assert set(np.unique(some.classes).tolist()) <= {0, 1, 4}
    assert set(np.unique(every.classes).tolist()) >= {0, 1, 2, 3, 4, 5}
    same = some.classes != 0
    assert np.array_equal(some.classes[same], every.classes[same])


def test_two_calls_give_the_same_masks_and_the_same_picture_as_render_image() -> None:
    drawing = _plan()

    first = drawing.render_masks(instances=True, width=700)
    second = drawing.render_masks(instances=True, width=700)
    drawing.render_image(width=700)

    assert np.array_equal(first.classes, second.classes)
    assert first.instances is not None and second.instances is not None
    assert np.array_equal(first.instances, second.instances)
    assert first.handles == second.handles


def test_a_layout_labels_what_its_viewports_show(tmp_path: Path) -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (4000, 0))
    drawing.add_circle((2000, 1500), 600)
    drawing.add_text("Model words", (100, 100), 200.0)
    drawing.commit()

    masks = drawing.render_masks(width=800)

    assert (masks.classes == 1).any() and (masks.classes == 3).any()
    # A layout with no viewport has the paper and nothing to label: the ground everywhere.
    layout = drawing.render_masks(layout="Layout1", width=800)
    assert layout.classes.shape[1] == 800
    assert not layout.classes.any()


@pytest.mark.parametrize(
    "arguments, message",
    [
        ({"classes": []}, "classes is a list of names"),
        ({"classes": "words"}, "classes is a list of names"),
        ({"classes": ["words", "walls"]}, "A class is one of"),
        ({"width": 0.5}, "whole number"),
        ({"dpi": 0}, "above nought"),
        ({"layout": "Elsewhere"}, "no layout named"),
    ],
)
def test_a_labelled_render_that_cannot_be_drawn_raises_value_error_naming_why(
    arguments: dict[str, Any], message: str
) -> None:
    with pytest.raises(ValueError, match=message):
        new_file().render_masks(**arguments)


# ---- COCO ------------------------------------------------------------------------------------------------------------


def test_the_coco_file_has_the_shape_of_a_detection_dataset(tmp_path: Path) -> None:
    drawing = _plan()
    image = tmp_path / "plan.png"

    coco = drawing.to_coco(str(image), classes=list(LABEL_CLASSES), width=1200)

    assert sorted(coco) == ["annotations", "categories", "images"]
    height = drawing.render_image(width=1200).shape[0]
    assert coco["images"] == [{"id": 1, "file_name": "plan.png", "width": 1200, "height": height}]
    assert coco["categories"] == [
        {"id": 1, "name": "words", "supercategory": "drawing"},
        {"id": 2, "name": "dimensions", "supercategory": "drawing"},
        {"id": 3, "name": "lines", "supercategory": "drawing"},
        {"id": 4, "name": "hatches", "supercategory": "drawing"},
        {"id": 5, "name": "blocks", "supercategory": "drawing"},
    ]
    assert image.is_file() and image.stat().st_size > 1000
    annotations = coco["annotations"]
    assert len(annotations) >= 8
    assert [a["id"] for a in annotations] == sorted(a["id"] for a in annotations)
    for annotation in annotations:
        assert sorted(annotation) == ["area", "bbox", "category_id", "id", "image_id", "iscrowd"]
        x, y, w, h = annotation["bbox"]
        assert 0 <= x and 0 <= y and w > 0 and h > 0 and x + w <= 1200 and y + h <= height
        assert annotation["iscrowd"] == 0 and annotation["image_id"] == 1
        assert annotation["category_id"] in (1, 2, 3, 4, 5)
        assert 0 < annotation["area"] <= w * h
    assert {a["category_id"] for a in annotations} == {1, 2, 3, 4, 5}
    # A COCO file is JSON.
    assert json.loads(json.dumps(coco)) == coco


def test_the_box_of_an_annotation_holds_every_pixel_of_its_object(tmp_path: Path) -> None:
    drawing = _plan()

    coco = drawing.to_coco(str(tmp_path / "plan.png"), classes=list(LABEL_CLASSES), width=1200)
    masks = drawing.render_masks(classes=list(LABEL_CLASSES), instances=True, width=1200)

    assert masks.instances is not None
    for annotation in coco["annotations"]:
        x, y, w, h = annotation["bbox"]
        rows, columns = np.nonzero(masks.instances == annotation["id"])
        assert rows.size == annotation["area"], annotation
        assert columns.min() >= x and columns.max() < x + w, annotation
        assert rows.min() >= y and rows.max() < y + h, annotation
        assert masks.handles[annotation["id"] - 1]


def test_an_object_is_annotated_when_its_kind_is_asked_for(tmp_path: Path) -> None:
    drawing = _plan()

    pieces = drawing.to_coco(str(tmp_path / "a.png"), width=1000)
    blocks = drawing.to_coco(str(tmp_path / "b.png"), classes=["blocks"], width=1000)

    # Without blocks an INSERT has no annotation and its pieces none of their own; with only blocks, the two INSERTs and no more.
    assert {a["category_id"] for a in pieces["annotations"]} == {1, 2, 3, 4}
    assert [a["category_id"] for a in blocks["annotations"]] == [5, 5]
    assert len(pieces["categories"]) == len(blocks["categories"]) == 5


def test_the_segmentation_of_words_is_the_turned_frame_of_each_word(tmp_path: Path) -> None:
    drawing = new_file()
    drawing.add_text("Turned words", (50, 50), 30.0, rotation=0.5)
    drawing.add_line((0, 0), (300, 100))
    drawing.commit()

    coco = drawing.to_coco(str(tmp_path / "t.png"), segmentation=True, width=800)

    words = next(a for a in coco["annotations"] if a["category_id"] == 1)
    line = next(a for a in coco["annotations"] if a["category_id"] == 3)
    assert len(words["segmentation"]) == 1 and len(words["segmentation"][0]) == 8
    corners = np.array(words["segmentation"][0]).reshape(4, 2)
    side = corners[1] - corners[0]
    other = corners[3] - corners[0]
    assert abs(float(side @ other)) < 1e-6 * float(np.linalg.norm(side) * np.linalg.norm(other))
    # The text is turned half a radian, y down in pixels: the first side climbs at -0.5 radian.
    assert np.arctan2(side[1], side[0]) == pytest.approx(-0.5, abs=1e-6)
    x, y, w, h = line["bbox"]
    assert line["segmentation"] == [[float(v) for v in (x, y, x + w, y, x + w, y + h, x, y + h)]]


def test_the_picture_of_a_coco_call_is_the_picture_of_render_png(tmp_path: Path) -> None:
    drawing = _plan()
    image = tmp_path / "plan.png"

    drawing.to_coco(str(image), width=640, ground="white")
    drawing.render_png(str(tmp_path / "again.png"), width=640, ground="white")

    assert image.read_bytes() == (tmp_path / "again.png").read_bytes()


def test_a_coco_call_on_a_drawing_with_nothing_labelled_has_no_annotations(tmp_path: Path) -> None:
    coco = new_file().to_coco(str(tmp_path / "empty.png"))

    assert coco["annotations"] == []
    assert len(coco["categories"]) == 5
    assert coco["images"][0]["file_name"] == "empty.png"


# ---- timings, printed and not asserted -------------------------------------------------------------------------------


def _many(count: int) -> Drawing:
    """``count`` objects over a square of 100 000 mm: a mix of lines, circles, texts, hatches and dimensions in a grid."""
    drawing = new_file()
    side = int(count**0.5) + 1
    for i in range(count):
        x, y = 1000.0 * (i % side), 1000.0 * (i // side)
        kind = i % 5
        if kind == 0:
            drawing.add_line((x, y), (x + 800, y + 600), color=1 + i % 7)
        elif kind == 1:
            drawing.add_circle((x + 400, y + 400), 300, color=1 + i % 7)
        elif kind == 2:
            drawing.add_text(f"T{i}", (x, y + 200), 400.0)
        elif kind == 3:
            drawing.add_hatch([(x, y), (x + 500, y), (x + 500, y + 500), (x, y + 500)], pattern="ANSI31", scale=20.0)
        else:
            drawing.add_linear_dim((x, y), (x + 600, y), (x + 300, y - 150))
    drawing.commit()
    return drawing


def test_the_masks_and_the_coco_file_of_ten_thousand_objects_stay_far_below_a_hang(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    drawing = _many(10000)
    drawing.render_image(width=1600, height=1200)  # the first call: the native library, the fonts, the display list

    def best(call: Any) -> float:
        times: list[float] = []
        for _ in range(3):
            start = time.perf_counter()
            call()
            times.append(time.perf_counter() - start)
        return statistics.median(times)

    timings = {
        "render_image": best(lambda: drawing.render_image(width=1600, height=1200)),
        "render_transform": best(lambda: drawing.render_transform(width=1600, height=1200)),
        "render_masks": best(lambda: drawing.render_masks(width=1600, height=1200)),
        "render_masks instances": best(lambda: drawing.render_masks(instances=True, width=1600, height=1200)),
        "to_coco": best(lambda: drawing.to_coco(str(tmp_path / "many.png"), width=1600, height=1200)),
        "to_coco segmentation": best(
            lambda: drawing.to_coco(str(tmp_path / "many.png"), segmentation=True, width=1600, height=1200)
        ),
    }
    with capsys.disabled():
        print("\nTIMINGS " + json.dumps({name: round(seconds, 3) for name, seconds in timings.items()}))

    assert all(seconds < 60.0 for seconds in timings.values()), timings
    coco = drawing.to_coco(str(tmp_path / "many.png"), width=1600, height=1200)
    assert len(coco["annotations"]) > 1000
