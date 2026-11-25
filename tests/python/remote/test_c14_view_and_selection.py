"""C14: the view, the named views, the selection by area and the system variables through the app (A6): ``zoom_*``, ``pan``,
``regen``, ``view``, ``has_window``, ``named_views`` with ``save_view``, ``restore_view`` and ``delete_view``,
``select_all``, ``select_window``, ``select_crossing``, ``get_variable`` and ``set_variable``, over ``CadRemoteDocument``.

The drawing is a line from (0, 0) to (100, 0) and a circle about (50, 50) of radius 25: extents (0, 0) to (100, 75). The test
server's sessions have the console view (942 by 534 pixels, 10 units high about the origin) until the test gives them a window
with ``window 942 534``; the numbers below are those of ``CadView`` (margin 5 % for extents, none for a window).
"""

from collections.abc import Iterator
from typing import Any

import pytest

from opencad2d.cad import Box, Entity


@pytest.fixture
def cad_app(app: Any) -> Iterator[Any]:
    """The ``opencad2d.cad`` App over this test's connection."""
    from opencad2d.cad import App

    with App(app) as made:
        yield made


@pytest.fixture
def plan(cad_app: Any) -> Any:
    """The reference drawing, shown: a line (0, 0) to (100, 0) and a circle about (50, 50) of radius 25."""
    drawing = cad_app.new()
    drawing.add_line((0, 0), (100, 0))
    drawing.add_circle((50, 50), 25)
    drawing.commit()
    return drawing


def _equal(box: Box, expected: tuple[float, float, float, float], places: float = 1e-4) -> None:
    assert box == pytest.approx(expected, abs=places)


# ---- the view ----------------------------------------------------------------------------------------------------------


def test_zoom_extents_shows_the_box_the_typed_zoom_extents_shows(plan: Any) -> None:
    plan.zoom_extents()

    _equal(plan.view, (-23.50187, -4.16667, 123.50187, 79.16667))


def test_the_view_is_a_box_of_named_numbers(plan: Any) -> None:
    plan.zoom_extents()
    view = plan.view

    assert isinstance(view, Box)
    assert (view.min_x, view.min_y, view.max_x, view.max_y) == tuple(view)
    assert view.max_x - view.min_x == pytest.approx(147.00375, abs=1e-4)


def test_zoom_window_fits_the_window_with_no_margin(plan: Any) -> None:
    plan.zoom_window((0, 0), (10, 10))

    _equal(plan.view, (-3.82022, 0.0, 13.82022, 10.0))


def test_a_window_is_given_by_points_of_any_kind_and_in_either_order(plan: Any) -> None:
    from GeometryHelper.Geometry import GeoPoint2, GeoPoint3

    plan.zoom_window(GeoPoint3(10, 10, 5), GeoPoint2(0, 0))

    _equal(plan.view, (-3.82022, 0.0, 13.82022, 10.0))


def test_zoom_center_shows_the_height_given_about_the_centre(plan: Any) -> None:
    plan.zoom_center((50, 50), 20)

    _equal(plan.view, (32.35955, 40.0, 67.64045, 60.0))


def test_zoom_scaled_by_two_halves_the_width_about_the_same_centre(plan: Any) -> None:
    plan.zoom_window((0, 0), (10, 10))
    before = plan.view

    plan.zoom_scaled(2)
    after = plan.view

    assert after.max_x - after.min_x == pytest.approx((before.max_x - before.min_x) / 2, abs=1e-6)
    assert (after.min_x + after.max_x) / 2 == pytest.approx((before.min_x + before.max_x) / 2, abs=1e-6)


def test_zoom_objects_shows_the_box_of_the_objects_with_a_margin(plan: Any) -> None:
    circle = plan.entities("CIRCLE")[0]

    plan.zoom_objects([circle])

    # The circle's box is (25, 25) to (75, 75), 50 high: fitted with 5 % each side, the canvas shows 50 / 0.9 = 55.556 high.
    view = plan.view
    assert view.max_y - view.min_y == pytest.approx(55.5556, abs=1e-3)
    assert (view.min_x + view.max_x) / 2 == pytest.approx(50.0, abs=1e-6)
    assert (view.min_y + view.max_y) / 2 == pytest.approx(50.0, abs=1e-6)


def test_zoom_objects_takes_handles_as_well_as_entities(plan: Any) -> None:
    circle = plan.entities("CIRCLE")[0]
    plan.zoom_objects([circle])
    by_entity = plan.view

    plan.zoom_extents()
    plan.zoom_objects([circle.handle])

    assert plan.view == pytest.approx(tuple(by_entity), abs=1e-9)


def test_zoom_objects_of_an_entity_not_committed_is_refused_before_anything_is_asked(plan: Any) -> None:
    added = plan.add_line((0, 0), (1, 1))

    with pytest.raises(ValueError, match="commit"):
        plan.zoom_objects([added])


def test_zoom_previous_says_false_with_none_and_goes_back_one_view_otherwise(plan: Any) -> None:
    assert plan.zoom_previous() is False

    plan.zoom_extents()
    extents = plan.view
    plan.zoom_window((0, 0), (10, 10))

    assert plan.zoom_previous() is True
    assert plan.view == pytest.approx(tuple(extents), abs=1e-9)


def test_pan_moves_the_drawing_by_the_displacement(plan: Any) -> None:
    plan.zoom_window((0, 0), (10, 10))

    plan.pan((10, 0))

    _equal(plan.view, (-13.82022, 0.0, 3.82022, 10.0))


def test_a_displacement_is_a_vector_or_two_numbers_and_never_three(plan: Any) -> None:
    from GeometryHelper.Geometry import GeoVector2

    plan.zoom_window((0, 0), (10, 10))
    plan.pan(GeoVector2(10, 0))

    _equal(plan.view, (-13.82022, 0.0, 3.82022, 10.0))
    with pytest.raises(ValueError, match="displacement"):
        plan.pan((1, 2, 3))


def test_the_view_is_set_by_a_box_and_fits_it_with_no_margin(plan: Any) -> None:
    plan.view = Box(0.0, 0.0, 10.0, 10.0)

    _equal(plan.view, (-3.82022, 0.0, 13.82022, 10.0))


def test_a_flat_box_is_refused_by_the_app(plan: Any) -> None:
    from OpenCAD2D.Remote import CadRemoteException

    with pytest.raises(CadRemoteException):
        plan.view = Box(0.0, 0.0, 10.0, 0.0)


def test_a_height_that_is_not_above_nought_is_refused_by_the_app(plan: Any) -> None:
    from OpenCAD2D.Remote import CadRemoteException

    with pytest.raises(CadRemoteException):
        plan.zoom_center((0, 0), 0)
    with pytest.raises(CadRemoteException):
        plan.zoom_scaled(-1)


def test_a_view_call_is_undone_by_u_as_the_typed_zoom_is(plan: Any) -> None:
    plan.zoom_window((0, 0), (10, 10))
    window = plan.view
    plan.zoom_extents()

    plan.net.SendCommand("U\n")

    assert plan.view == pytest.approx(tuple(window), abs=1e-9)


def test_regen_changes_nothing_the_script_can_see(plan: Any) -> None:
    plan.zoom_window((0, 0), (10, 10))
    before = plan.view

    plan.regen()

    assert plan.view == pytest.approx(tuple(before), abs=1e-9)
    assert len(plan.entities()) == 2


def test_has_window_is_false_on_the_test_server_and_true_once_it_gives_the_sessions_a_view(
    plan: Any, server: Any
) -> None:
    assert plan.has_window is False

    assert server.send("window 942 534") == "DONE"

    assert plan.has_window is True


def test_a_view_request_on_a_drawing_not_shown_is_refused_in_the_apps_words(cad_app: Any) -> None:
    from OpenCAD2D.Remote import CadRemoteException

    first = cad_app.new()
    second = cad_app.new()
    words = f"The drawing {first.name} is not the drawing shown: activate it first."

    for call in (
        first.zoom_extents,
        lambda: first.zoom_window((0, 0), (1, 1)),
        lambda: first.pan((1, 1)),
        lambda: first.view,
        lambda: first.save_view("V"),
    ):
        with pytest.raises(CadRemoteException) as refused:
            call()
        assert str(refused.value.Message) == words
    # What does not depend on the canvas is not refused.
    first.regen()
    assert first.named_views == []
    assert first.get_variable("LUNITS") == 2
    assert first.select_all() == []
    assert second.is_active


# ---- the named views ---------------------------------------------------------------------------------------------------


def test_named_views_round_trip_save_restore_and_delete(plan: Any) -> None:
    from OpenCAD2D.Remote import CadRemoteException

    plan.zoom_window((0, 0), (40, 30))
    saved = plan.view
    plan.save_view("Corner")
    plan.save_view("a")

    assert plan.named_views == ["a", "Corner"]

    plan.zoom_extents()
    plan.restore_view("corner")

    assert plan.view == pytest.approx(tuple(saved), abs=1e-9)

    plan.delete_view("Corner")

    assert plan.named_views == ["a"]
    with pytest.raises(CadRemoteException, match="no view named Corner"):
        plan.restore_view("Corner")
    with pytest.raises(CadRemoteException):
        plan.delete_view("Corner")
    with pytest.raises(CadRemoteException):
        plan.save_view("bad/name")


# ---- the selection by area ---------------------------------------------------------------------------------------------


@pytest.fixture
def rows(cad_app: Any) -> Any:
    """Five lines from (0, y) to (100, y) at y = 0, 10, 20, 30 and 40."""
    drawing = cad_app.new()
    for y in (0, 10, 20, 30, 40):
        drawing.add_line((0, y), (100, y))
    drawing.commit()
    return drawing


def _ys(entities: list[Entity]) -> list[float]:
    return [round(float(e.net.StartPoint.Y), 6) for e in entities]  # pyright: ignore[reportAttributeAccessIssue]


def test_select_all_gives_the_objects_in_the_drawings_order_as_copies_with_handles(plan: Any) -> None:
    found = plan.select_all()

    assert [e.dxf_type for e in found] == ["LINE", "CIRCLE"]
    assert [e.handle for e in found] == [e.handle for e in plan.entities()]
    assert all(isinstance(e, Entity) for e in found)


def test_select_window_takes_only_what_lies_wholly_inside_and_crossing_what_it_crosses(rows: Any) -> None:
    assert _ys(rows.select_window((-1, 5), (101, 35))) == [10.0, 20.0, 30.0]
    assert _ys(rows.select_crossing((50, 15), (60, 25))) == [20.0]
    assert rows.select_window((50, 15), (60, 25)) == []


def test_with_a_window_select_window_is_held_to_the_view(rows: Any, server: Any) -> None:
    server.send("window 942 534")
    rows.zoom_center((50, 20), 22.6)

    assert _ys(rows.select_window((-1, -1), (101, 41))) == [10.0, 20.0, 30.0]
    assert _ys(rows.select_crossing((-1, -1), (101, 41))) == [10.0, 20.0, 30.0]


def test_a_selection_by_area_leaves_the_users_selection_as_it_was(rows: Any) -> None:
    rows.select([rows.entities()[0]])

    rows.select_all()
    rows.select_window((-1, 5), (101, 35))
    rows.select_crossing((50, 15), (60, 25))

    assert [e.handle for e in rows.selection()] == [rows.entities()[0].handle]


# ---- the system variables ----------------------------------------------------------------------------------------------


def test_variables_round_trip_an_integer_a_real_a_text_and_a_point(plan: Any) -> None:
    assert plan.get_variable("LUNITS") == 2
    assert isinstance(plan.get_variable("LUNITS"), int)
    plan.set_variable("LUNITS", 4)
    assert plan.get_variable("LUNITS") == 4

    plan.set_variable("LTSCALE", 2.5)
    assert plan.get_variable("ltscale") == 2.5
    assert isinstance(plan.get_variable("LTSCALE"), float)

    assert plan.get_variable("CLAYER") == "0"
    assert isinstance(plan.get_variable("CLAYER"), str)

    plan.set_variable("INSBASE", (1.5, 2.5, 0.0))
    assert plan.get_variable("INSBASE") == (1.5, 2.5, 0.0)


def test_a_variable_refused_comes_in_the_apps_words(plan: Any) -> None:
    from OpenCAD2D.Remote import CadRemoteException

    with pytest.raises(CadRemoteException, match="The system variable NOSUCH is not known."):
        plan.get_variable("NOSUCH")
    with pytest.raises(CadRemoteException, match="is read only."):
        plan.set_variable("EXTMIN", (0.0, 0.0, 0.0))
    with pytest.raises(CadRemoteException, match="The value 7 is refused for LUNITS."):
        plan.set_variable("LUNITS", 7)
    assert plan.get_variable("LUNITS") == 2


def test_a_variable_is_set_to_a_number_a_text_or_a_point_and_a_bool_is_none_of_them(plan: Any) -> None:
    with pytest.raises(TypeError, match="LUNITS"):
        plan.set_variable("LUNITS", True)
    with pytest.raises(TypeError, match="LUNITS"):
        plan.set_variable("LUNITS", None)  # pyright: ignore[reportArgumentType]
