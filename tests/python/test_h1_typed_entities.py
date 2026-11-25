"""H1: typed entities on a file (A7): ``entities(DxfCircle)`` gives the circles and nothing else, a class asks for its
subclasses too (``DxfCurve`` is lines, arcs, circles and polylines), what comes back is of its own class, a class that is no
entity is refused with ``TypeError``, and the string form is as it was. The same on the other backends is
``remote/test_c13_typed_entities.py``.
"""

from typing import Any

import pytest

from GeometryHelper.Dxf import DxfArc, DxfCircle, DxfCurve, DxfDBText, DxfEntity, DxfLine, DxfMText, DxfPolyline
from opencad2d.cad import Entity, new_file


def _a_drawing_of_every_kind() -> Any:
    """A line, two circles on layers Steel and Wood, an arc, a polyline, a text and an mtext, in that order."""
    drawing = new_file()
    drawing.layers.add("Steel", color=1)
    drawing.layers.add("Wood", color=3)
    drawing.commit()
    drawing.add_line((0, 0), (100, 0))
    drawing.add_circle((50, 50), 25, layer="Steel")
    drawing.add_circle((200, 50), 5, layer="Wood")
    drawing.add_arc((0, 0), 10, 0.0, 1.5)
    drawing.add_polyline([(0, 0), (100, 0), (100, 50)], closed=True)
    drawing.add_text("Plate", (0, 100), 5.0)
    drawing.add_mtext("Plate PL10", (10, 20), 5.0)
    drawing.commit()
    return drawing


def test_entities_of_a_class_are_the_objects_of_that_class_only() -> None:
    drawing = _a_drawing_of_every_kind()

    circles = drawing.entities(DxfCircle)

    assert [e.dxf_type for e in circles] == ["CIRCLE", "CIRCLE"]
    assert [round(e.net.Radius, 6) for e in circles] == [25.0, 5.0]


def test_entities_of_a_base_class_include_its_subclasses() -> None:
    drawing = _a_drawing_of_every_kind()

    curves = drawing.entities(DxfCurve)

    assert [e.dxf_type for e in curves] == ["LINE", "CIRCLE", "CIRCLE", "ARC", "LWPOLYLINE"]


def test_entities_of_the_base_class_of_all_are_every_object() -> None:
    drawing = _a_drawing_of_every_kind()

    assert [e.handle for e in drawing.entities(DxfEntity)] == [e.handle for e in drawing.entities()]


def test_a_class_filter_takes_the_layer_and_the_layout_too() -> None:
    drawing = _a_drawing_of_every_kind()

    assert [e.layer for e in drawing.entities(DxfCircle, layer="steel")] == ["Steel"]
    assert drawing.entities(DxfCircle, layout="Layout1") == []
    assert len(drawing.entities(DxfCircle, layout="Model")) == 2


def test_a_class_with_no_object_in_the_drawing_gives_an_empty_list() -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (100, 0))
    drawing.commit()

    assert drawing.entities(DxfCircle) == []


@pytest.mark.parametrize(
    ("cls", "made"),
    [
        (DxfLine, lambda d: d.add_line((0, 0), (100, 0))),
        (DxfCircle, lambda d: d.add_circle((50, 50), 25)),
        (DxfArc, lambda d: d.add_arc((0, 0), 10, 0.0, 1.5)),
        (DxfPolyline, lambda d: d.add_polyline([(0, 0), (100, 0)])),
        (DxfDBText, lambda d: d.add_text("Plate", (0, 100), 5.0)),
        (DxfMText, lambda d: d.add_mtext("Plate PL10", (10, 20), 5.0)),
    ],
    ids=lambda value: getattr(value, "__name__", ""),
)
def test_what_is_read_is_of_its_own_class_not_of_the_base(cls: type, made: Any) -> None:
    drawing = new_file()
    made(drawing)
    drawing.commit()

    assert type(drawing.entities()[0].net) is cls
    assert type(drawing.entities(cls)[0].net) is cls
    assert type(drawing.entities(DxfEntity)[0].net) is cls


def test_what_an_add_returns_is_of_its_own_class() -> None:
    drawing = new_file()

    assert type(drawing.add_circle((0, 0), 5).net) is DxfCircle
    assert type(drawing.add(DxfLine()).net) is DxfLine


def test_the_entities_of_a_class_are_entities_with_their_handles() -> None:
    drawing = _a_drawing_of_every_kind()

    circles = drawing.entities(DxfCircle)

    assert all(isinstance(e, Entity) for e in circles)
    assert [e.handle for e in circles] == [e.handle for e in drawing.entities("CIRCLE")]


@pytest.mark.parametrize("bad", [int, str, object, 5, DxfCircle(), list])
def test_a_class_that_is_no_dxf_entity_is_refused_with_a_type_error(bad: Any) -> None:
    drawing = _a_drawing_of_every_kind()

    with pytest.raises(TypeError, match="DxfEntity"):
        drawing.entities(bad)


def test_the_string_form_is_as_it_was() -> None:
    drawing = _a_drawing_of_every_kind()

    assert [e.dxf_type for e in drawing.entities("circle")] == ["CIRCLE", "CIRCLE"]
    assert [e.dxf_type for e in drawing.entities(type="LINE")] == ["LINE"]
    assert len(drawing.entities()) == 7
    assert [e.dxf_type for e in drawing.entities(type=None, layer="Wood")] == ["CIRCLE"]


def test_entity_can_be_subscripted_at_run_time() -> None:
    # a script may write Entity[DxfCircle] outside an annotation, in a cast or an alias
    assert Entity[DxfCircle] is not None


def test_the_dxf_type_is_the_dxf_name_the_core_gives_not_a_word_of_the_string_form() -> None:
    from opencad2d.cad._drawing import dxf_type_of

    class Named:
        """An object whose string form says another word first, as ``ToString`` of an entity could be changed to say."""

        DxfName = "LWPOLYLINE"

        def __str__(self) -> str:
            return "POLYLINE 2F on steel"

    assert dxf_type_of(Named()) == "LWPOLYLINE"
    assert dxf_type_of(new_file().add_line((0, 0), (10, 0)).net) == "LINE"
