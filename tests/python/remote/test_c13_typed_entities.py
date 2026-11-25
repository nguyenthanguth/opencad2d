"""C13: typed entities through the app (A7): ``entities(DxfCircle)`` over a connection asks the app for that class
(``CadObjectFilter.Kind``) and gives the circles only, a base class gives its subclasses, a copy is of its own class, and a
class that is no entity is refused with ``TypeError`` before anything is asked.
"""

from collections.abc import Iterator
from typing import Any

import pytest

from GeometryHelper.Dxf import DxfArc, DxfCircle, DxfCurve, DxfLine


@pytest.fixture
def cad_app(app: Any) -> Iterator[Any]:
    """The ``opencad2d.cad`` App over this test's connection."""
    from opencad2d.cad import App

    with App(app) as made:
        yield made


@pytest.fixture
def mixed(cad_app: Any) -> Any:
    """A drawing of a line, two circles on layers Steel and Wood, an arc and a text."""
    drawing = cad_app.new()
    drawing.layers.add("Steel", color=1)
    drawing.layers.add("Wood", color=3)
    drawing.commit()
    drawing.add_line((0, 0), (100, 0))
    drawing.add_circle((50, 50), 25, layer="Steel")
    drawing.add_circle((200, 50), 5, layer="Wood")
    drawing.add_arc((0, 0), 10, 0.0, 1.5)
    drawing.add_text("Plate", (0, 100), 5.0)
    drawing.commit()
    return drawing


def test_entities_of_a_class_are_the_objects_of_that_class_only(mixed: Any) -> None:
    circles = mixed.entities(DxfCircle)

    assert [e.dxf_type for e in circles] == ["CIRCLE", "CIRCLE"]
    assert [round(e.net.Radius, 6) for e in circles] == [25.0, 5.0]


def test_entities_of_a_base_class_include_its_subclasses(mixed: Any) -> None:
    assert [e.dxf_type for e in mixed.entities(DxfCurve)] == ["LINE", "CIRCLE", "CIRCLE", "ARC"]


def test_a_class_filter_takes_the_layer_too(mixed: Any) -> None:
    assert [e.layer for e in mixed.entities(DxfCircle, layer="wood")] == ["Wood"]


def test_a_copy_is_of_its_own_class(mixed: Any) -> None:
    assert type(mixed.entities(DxfCircle)[0].net) is DxfCircle
    assert type(mixed.entities(DxfLine)[0].net) is DxfLine
    assert type(mixed.entities(DxfArc)[0].net) is DxfArc
    assert type(mixed.entities()[0].net) is DxfLine


def test_a_class_that_is_no_dxf_entity_is_refused_with_a_type_error(mixed: Any) -> None:
    with pytest.raises(TypeError, match="DxfEntity"):
        mixed.entities(int)


def test_what_an_add_returns_is_of_its_own_class(cad_app: Any) -> None:
    drawing = cad_app.new()

    assert type(drawing.add_circle((0, 0), 5).net) is DxfCircle
    assert type(drawing.add_line((0, 0), (5, 0)).net) is DxfLine
