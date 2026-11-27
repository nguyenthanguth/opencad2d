"""N3: a block made of an INSERT whose attributes use a text style of the drawing's own (a block in a block), by ``blocks.add``.

N1 gave an INSERT's attributes their text styles at commit; this holds the same for an INSERT that is a part of a block queued
with ``blocks.add``, in the commit that makes the block and in one after it.
"""

import importlib
from typing import Any

from opencad2d.cad import Drawing, new_file

_dxf: Any = importlib.import_module("GeometryHelper.Dxf")
_geo: Any = importlib.import_module("GeometryHelper.Geometry")


def _title_block(drawing: Drawing) -> None:
    """Queues the block TITLE: a line and an attribute definition 5 millimetres high in the text style SYN."""
    record = drawing.text_styles["SYN"]
    definition = _dxf.DxfAttributeDefinition(_geo.GeoPoint3(2, 4, 0), "", "NUMBER", "Number", record)
    definition.Height = 5.0
    drawing.blocks.add("TITLE", [_dxf.DxfLine(_geo.GeoPoint3(0, 0, 0), _geo.GeoPoint3(100, 0, 0)), definition])


def _title_insert(drawing: Drawing, value: str) -> Any:
    """An INSERT of TITLE at (10, 10) carrying the attribute NUMBER = ``value`` in the style SYN, as the block it is a part of takes it."""
    insert = _dxf.DxfBlockReference(_geo.GeoPoint3(10, 10, 0), drawing.net.Database.BlockTable["TITLE"])
    attribute = _dxf.DxfAttributeReference()
    attribute.Tag = "NUMBER"
    attribute.TextString = value
    attribute.Height = 5.0
    attribute.Position = _geo.GeoPoint3(12, 14, 0)
    attribute.TextStyleName = "SYN"
    insert.AttributeCollection.AppendAttribute(attribute)
    return insert


def _inner_attributes(drawing: Drawing, block: str) -> list[tuple[str, str, str]]:
    """The tag, value and text style of each attribute of each INSERT inside the block ``block``."""
    return [
        (str(a.Tag), str(a.TextString), str(a.TextStyleName))
        for entity in drawing.net.Database.BlockTable[block]
        if isinstance(entity, _dxf.DxfBlockReference)
        for a in entity.AttributeCollection
    ]


def test_a_block_that_holds_an_insert_with_attributes_in_a_user_style_is_made_and_placed() -> None:
    drawing = new_file()
    drawing.text_styles.add("SYN", font="txt")
    drawing.commit()
    _title_block(drawing)
    drawing.commit()

    drawing.blocks.add("SHEET", [_dxf.DxfLine(_geo.GeoPoint3(0, 0, 0), _geo.GeoPoint3(0, 50, 0)), _title_insert(drawing, "A-101")])
    drawing.commit()
    drawing.add_insert("SHEET", (200, 0))
    drawing.commit()

    assert _inner_attributes(drawing, "SHEET") == [("NUMBER", "A-101", "SYN")]
    assert [str(e.net.Name) for e in drawing.entities("INSERT")] == ["SHEET"]


def test_the_inner_block_and_the_outer_one_made_in_one_commit_keep_the_user_style() -> None:
    drawing = new_file()
    drawing.text_styles.add("SYN", font="txt")
    drawing.commit()
    _title_block(drawing)
    drawing.commit()
    inner = _title_insert(drawing, "A-102")

    drawing.blocks.add("SHEET", [inner])
    drawing.add_insert("SHEET", (200, 0))
    drawing.commit()

    assert _inner_attributes(drawing, "SHEET") == [("NUMBER", "A-102", "SYN")]
