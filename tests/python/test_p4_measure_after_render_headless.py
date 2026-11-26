"""P4: what the core measures of words does not depend on whether a picture was drawn first in the process.

The first picture drawn in a process used to set the canvas's measure of TrueType letters for every drawing of it, and an MTEXT
in an SHX style of a drawing made with ``new_file()`` was measured with every letter 0.6 of its height wide before it and with
the canvas's letters after (a box 45.6 wide, then 31.2, in the drawing below). A drawing read for its boxes is set up as the
app sets one up on opening it, so that ``measure_mtext``, ``bounds`` and ``check`` give the same numbers before
and after a render, and the ones the app gives (45.8).
"""

import pytest

from opencad2d.cad import Drawing, new_file

_WORDS = "Plate PL10 of grade S355 beside it"


def _drawing() -> Drawing:
    drawing = new_file()
    drawing.text_styles.add("NOTE", font="romans.shx")
    drawing.add_mtext(_WORDS, (10, 80), 4.0, 50.0, "NOTE")
    drawing.add_text("Plate A-12", (10, 10), 5.0, text_style="NOTE")
    drawing.commit()
    return drawing


def _picture() -> None:
    """The first picture of a process: of a drawing that is not the one measured."""
    other = new_file()
    other.add_line((0, 0), (100, 60))
    other.commit()
    other.render_image(width=64)


def _numbers(drawing: Drawing) -> list[float]:
    """The box of an MTEXT measured, the boxes ``bounds`` gives, and the boxes of the violations ``check`` finds."""
    measured = drawing.measure_mtext(_WORDS, (10, 80), 4.0, width=50.0, style="NOTE").box
    assert measured is not None
    standard = {"schema": "opencad2d.standard", "version": 1, "textHeights": {"paper": [2.5, 3.5]}}
    boxes = [box for found in drawing.check(standard).violations if (box := found.box) is not None]
    return [*measured, *drawing.bounds().boxes.ravel().tolist(), *(n for box in boxes for n in box)]


def test_the_boxes_of_words_are_the_same_before_and_after_a_picture_is_drawn() -> None:
    drawing = _drawing()
    before = _numbers(drawing)

    _picture()

    assert _numbers(drawing) == pytest.approx(before, abs=1e-9)
    assert _numbers(_drawing()) == pytest.approx(before, abs=1e-9)


def test_the_box_of_an_mtext_is_the_one_the_app_gives() -> None:
    box = list(_drawing().measure_mtext(_WORDS, (10, 80), 4.0, width=50.0, style="NOTE").box or [])

    assert box[2] - box[0] == pytest.approx(45.8, abs=1e-6)
