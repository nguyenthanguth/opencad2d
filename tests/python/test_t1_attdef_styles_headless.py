"""T1: an INSERT of a block whose attribute definitions use a text style of the drawing's own, not Standard (N1).

The attributes an INSERT carries take the text style of their definitions. A commit gives the small drawing the INSERT is added to
the styles the INSERT's attributes name, as it gives the style of a TEXT, so that the style is the drawing's own record and not
refused at the commit as "not in the file's text style table".
"""

import importlib
from typing import Any

import pytest

from opencad2d.cad import Drawing, new_file

_dxf: Any = importlib.import_module("GeometryHelper.Dxf")
_geo: Any = importlib.import_module("GeometryHelper.Geometry")


def _plate_block(drawing: Drawing, style: str) -> None:
    """Queues the block TITLE: a line and two attribute definitions, 5 millimetres high, in the text style ``style`` (a record of
    the drawing, which the commit before this has made)."""
    record = drawing.text_styles[style]
    parts: list[Any] = [_dxf.DxfLine(_geo.GeoPoint3(0, 0, 0), _geo.GeoPoint3(100, 0, 0))]
    for tag, at in (("DRAWING", (2, 12)), ("NUMBER", (2, 4))):
        definition = _dxf.DxfAttributeDefinition(_geo.GeoPoint3(at[0], at[1], 0), "", tag, tag.title(), record)
        definition.Height = 5.0
        parts.append(definition)
    drawing.blocks.add("TITLE", parts)


def _styles_of(drawing: Drawing) -> list[tuple[str, str]]:
    """The tag and text style of each attribute of each INSERT of model space, in the order read."""
    return [
        (str(a.Tag), str(a.TextStyleName)) for insert in drawing.entities("INSERT") for a in insert.net.AttributeCollection
    ]


def test_an_insert_of_a_block_whose_attributes_use_a_user_style_commits() -> None:
    drawing = new_file()
    drawing.text_styles.add("SYN", font="txt")
    drawing.commit()
    _plate_block(drawing, "SYN")
    drawing.commit()

    drawing.add_insert("TITLE", (10, 20), attributes={"DRAWING": "PLATE PL12", "NUMBER": "A-101"})
    drawing.commit()

    assert _styles_of(drawing) == [("DRAWING", "SYN"), ("NUMBER", "SYN")]


def test_the_style_the_block_and_the_insert_are_made_with_in_one_commit_is_kept() -> None:
    drawing = new_file()
    drawing.text_styles.add("SYN", font="txt")
    drawing.commit()
    _plate_block(drawing, "SYN")

    drawing.add_insert("TITLE", (10, 20), attributes={"DRAWING": "PLATE PL12"})
    drawing.commit()

    assert _styles_of(drawing) == [("DRAWING", "SYN"), ("NUMBER", "SYN")]


def test_an_insert_made_with_its_own_attributes_in_a_style_the_drawing_has_commits() -> None:
    drawing = new_file()
    drawing.text_styles.add("SYN", font="txt")
    drawing.commit()
    drawing.blocks.add("BOX", [_dxf.DxfLine(_geo.GeoPoint3(0, 0, 0), _geo.GeoPoint3(1, 0, 0))])
    drawing.commit()
    insert = _dxf.DxfBlockReference(_geo.GeoPoint3(5, 5, 0), drawing.net.Database.BlockTable["BOX"])
    attribute = _dxf.DxfAttributeReference()
    attribute.Tag = "MARK"
    attribute.TextString = "P1"
    attribute.Height = 2.5
    attribute.Position = _geo.GeoPoint3(5, 5, 0)
    attribute.TextStyleName = "SYN"
    insert.AttributeCollection.AppendAttribute(attribute)

    drawing.add(insert)
    drawing.commit()

    assert _styles_of(drawing) == [("MARK", "SYN")]


def test_an_attribute_in_a_style_nobody_has_is_still_refused_in_words() -> None:
    drawing = new_file()
    drawing.blocks.add("BOX", [_dxf.DxfLine(_geo.GeoPoint3(0, 0, 0), _geo.GeoPoint3(1, 0, 0))])
    drawing.commit()
    insert = _dxf.DxfBlockReference(_geo.GeoPoint3(5, 5, 0), drawing.net.Database.BlockTable["BOX"])
    attribute = _dxf.DxfAttributeReference()
    attribute.Tag = "MARK"
    attribute.TextStyleName = "NOPE"
    insert.AttributeCollection.AppendAttribute(attribute)

    drawing.add(insert)

    with pytest.raises(ValueError, match='The text style "NOPE" is not in the drawing: add it with text_styles.add first'):
        drawing.commit()
