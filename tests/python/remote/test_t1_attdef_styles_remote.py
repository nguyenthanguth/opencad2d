"""T1: an INSERT of a block whose attribute definitions use a text style of the drawing's own, on a drawing of a running app (N1).

The same cases as ``test_t1_attdef_styles_headless.py`` over the connection: the commit gives the small drawing the INSERT travels
in the text styles its attributes name, as it gives the style of a TEXT, so that the app keeps its own record of each.
"""

import importlib
from collections.abc import Iterator
from pathlib import Path
from typing import Any

import pytest

from opencad2d.cad import Drawing, new_file

_dxf: Any = importlib.import_module("GeometryHelper.Dxf")
_geo: Any = importlib.import_module("GeometryHelper.Geometry")


@pytest.fixture
def cad_app(app: Any) -> Iterator[Any]:
    """The ``opencad2d.cad`` App over this test's connection."""
    from opencad2d.cad import App

    with App(app) as made:
        yield made


def _shown(cad_app: Any, tmp_path: Path) -> Drawing:
    """A drawing open in the app with the text style SYN."""
    path = tmp_path / "plate.dxf"
    drawing = new_file()
    drawing.text_styles.add("SYN", font="txt")
    drawing.commit()
    drawing.save_as(str(path))
    return cad_app.open(str(path))


def _plate_block(drawing: Drawing, style: str) -> None:
    """Queues the block TITLE: a line and two attribute definitions, 5 millimetres high, in the text style ``style``."""
    record = drawing.text_styles[style]
    parts: list[Any] = [_dxf.DxfLine(_geo.GeoPoint3(0, 0, 0), _geo.GeoPoint3(100, 0, 0))]
    for tag, at in (("DRAWING", (2, 12)), ("NUMBER", (2, 4))):
        definition = _dxf.DxfAttributeDefinition(_geo.GeoPoint3(at[0], at[1], 0), "", tag, tag.title(), record)
        definition.Height = 5.0
        parts.append(definition)
    drawing.blocks.add("TITLE", parts)


def _styles_of(drawing: Drawing) -> list[tuple[str, str]]:
    return [
        (str(a.Tag), str(a.TextStyleName)) for insert in drawing.entities("INSERT") for a in insert.net.AttributeCollection
    ]


def test_an_insert_of_a_block_whose_attributes_use_a_user_style_commits_in_the_app(cad_app: Any, tmp_path: Path) -> None:
    shown = _shown(cad_app, tmp_path)
    _plate_block(shown, "SYN")
    shown.commit()

    shown.add_insert("TITLE", (10, 20), attributes={"DRAWING": "PLATE PL12", "NUMBER": "A-101"})
    shown.commit()

    assert _styles_of(shown) == [("DRAWING", "SYN"), ("NUMBER", "SYN")]


def test_the_block_and_its_insert_made_in_one_commit_keep_the_user_style_in_the_app(cad_app: Any, tmp_path: Path) -> None:
    shown = _shown(cad_app, tmp_path)
    _plate_block(shown, "SYN")

    shown.add_insert("TITLE", (10, 20), attributes={"DRAWING": "PLATE PL12"})
    shown.commit()

    assert _styles_of(shown) == [("DRAWING", "SYN"), ("NUMBER", "SYN")]
