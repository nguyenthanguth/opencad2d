"""C15: the edit calls through the app (A8): ``move``, ``copy``, ``rotate``, ``scale``, ``mirror`` and ``offset`` of a ``Drawing``
over ``CadRemoteDocument``. Each edit is compared with the command it stands for typed in a twin drawing of the same app (the
objects of the two compared with their handles, their DXF groups and a number within 1E-9 of the other), and what is queued and
the edit are one commit.

The drawing is ``edit_scenarios.build_plan``; its numbers are in that module. The session's tolerance is the app's, an EqualPoint
of 0.001: a distance of 1E-6 is nought and one of 0.01 is not.
"""

import math
import time
from collections.abc import Iterator
from typing import Any

import pytest
from System import ArgumentException

import edit_scenarios
from edit_scenarios import EDITS, REFUSED_CALLS, Edit, Twins, make_twins
from GeometryHelper.Dxf import DxfArc, DxfCircle, DxfCurve, DxfDBText, DxfLine, DxfPolyline
from GeometryHelper.Geometry import GeoPoint2, GeoPoint3, GeoVector2
from OpenCAD2D.Remote import CadRemoteException

#: What a number that is no edit is refused with: by the client or by the app, in Python's words or the app's.
NO_EDIT = (ValueError, CadRemoteException, ArgumentException)

IN_PLACE = [edit for edit in EDITS if edit.in_place]
MADE = [edit for edit in EDITS if not edit.in_place]


@pytest.fixture
def cad_app(app: Any) -> Iterator[Any]:
    """The ``opencad2d.cad`` App over this test's connection."""
    from opencad2d.cad import App

    with App(app) as made:
        yield made


@pytest.fixture
def twins(cad_app: Any) -> Twins:
    """Two drawings of the app built alike: ``typed`` gets the command line, ``called`` (the one shown) the call."""
    typed = cad_app.new()
    return make_twins(typed, cad_app.new())


def _items(twins: Twins, edit: Edit) -> list[Any]:
    return [twins.plan[name] for name in edit.names]


def _undo(drawing: Any) -> None:
    edit_scenarios.type_command(drawing, "U")


def _ends(entity: Any) -> tuple[tuple[float, float], tuple[float, float]]:
    start, end = entity.net.StartPoint, entity.net.EndPoint
    return ((round(start.X, 9), round(start.Y, 9)), (round(end.X, 9), round(end.Y, 9)))


# ---- each edit against its command -------------------------------------------------------------------------------------


@pytest.mark.parametrize("edit", EDITS, ids=repr)
def test_an_edit_gives_the_drawing_the_typed_command_gives(twins: Twins, edit: Edit) -> None:
    before = edit_scenarios.snapshot(twins.called)

    edit_scenarios.type_edit(twins, edit)
    edit_scenarios.call_edit(twins, edit)

    assert edit_scenarios.same_drawing(twins.typed, twins.called) == []
    assert edit_scenarios.snapshot(twins.called) != before


@pytest.mark.parametrize("mirrtext", [0, 1])
@pytest.mark.parametrize("edit", [e for e in EDITS if e.name.startswith("mirror")], ids=repr)
def test_a_mirror_gives_the_drawing_the_typed_mirror_gives_under_mirrtext_nought_and_one(
    twins: Twins, edit: Edit, mirrtext: int
) -> None:
    # The text NOTE at (0, 200) is mirrored readable under MIRRTEXT 0 and written backwards under 1: the call reads the
    # drawing's MIRRTEXT as the command does.
    twins.typed.set_variable("MIRRTEXT", mirrtext)
    twins.called.set_variable("MIRRTEXT", mirrtext)

    edit_scenarios.type_edit(twins, edit)
    edit_scenarios.call_edit(twins, edit)

    assert edit_scenarios.same_drawing(twins.typed, twins.called) == []


@pytest.mark.parametrize("edit", EDITS, ids=repr)
def test_an_edit_is_one_undo_step(twins: Twins, edit: Edit) -> None:
    before = edit_scenarios.snapshot(twins.called)

    edit_scenarios.call_edit(twins, edit)
    _undo(twins.called)

    assert edit_scenarios.differences(before, edit_scenarios.snapshot(twins.called)) == []


# ---- what comes back ---------------------------------------------------------------------------------------------------


@pytest.mark.parametrize("edit", IN_PLACE, ids=repr)
def test_an_edit_in_place_gives_back_the_objects_it_was_given_with_their_nets_read_again(
    twins: Twins, edit: Edit
) -> None:
    items = _items(twins, edit)
    handles = [item.handle for item in items]
    classes = [type(item.net) for item in items]
    before = [edit_scenarios.groups(item.net) for item in items]

    result = edit.call(twins.called, items)

    # The same Entity objects in the order given, still under their handles; each net is the drawing's object now (what a fresh
    # read of the handle gives), of its own class, and not what it was.
    assert len(result) == len(items)
    assert all(given is back for given, back in zip(items, result))
    assert [item.handle for item in items] == handles
    assert [type(item.net) for item in items] == classes
    for item, was in zip(items, before):
        assert edit_scenarios.groups(item.net) == edit_scenarios.groups(twins.called.entity(item.handle).net)
        assert edit_scenarios.groups(item.net) != was


@pytest.mark.parametrize("edit", MADE, ids=repr)
def test_an_edit_that_makes_objects_gives_new_entities_the_drawing_has_and_leaves_the_given_ones_alone(
    twins: Twins, edit: Edit
) -> None:
    items = _items(twins, edit)
    handles = {entity.handle for entity in twins.called.entities()}
    before = [edit_scenarios.groups(item.net) for item in items]

    result = edit.call(twins.called, items)

    assert result
    assert all(back is not given for back in result for given in items)
    for back in result:
        assert back.handle not in handles
        assert back.handle == twins.called.entity(back.handle).handle
        assert edit_scenarios.groups(back.net) == edit_scenarios.groups(twins.called.entity(back.handle).net)
    for item, was in zip(items, before):
        assert edit_scenarios.groups(item.net) == was
        assert edit_scenarios.groups(twins.called.entity(item.handle).net) == was
    assert len(twins.called.entities()) == len(handles) + len(result)


def test_copies_come_back_in_the_order_given_each_of_the_class_of_its_source(twins: Twins) -> None:
    given = [twins.plan[name] for name in ("text", "circle", "line", "arc", "polyline")]

    copies = twins.called.copy(given, (0, 50))

    assert [copy.dxf_type for copy in copies] == [item.dxf_type for item in given]
    assert [type(copy.net) for copy in copies] == [DxfDBText, DxfCircle, DxfLine, DxfArc, DxfPolyline]
    assert [copy.layer for copy in copies] == [item.layer for item in given]


def test_an_object_given_twice_to_a_copy_is_copied_once_and_the_list_given_is_left_alone(twins: Twins) -> None:
    side, line = twins.plan["side"], twins.plan["line"]
    given = [side, line, side]
    count = len(twins.called.entities())

    copies = twins.called.copy(given, (0, 50))

    # Three given, two copies made: the copy of the side first (on layer 0, ending at (0, 100)), that of the line second (on
    # Steel, ending at (100, 50)).
    assert given == [side, line, side] and given[0] is side
    assert [copy.layer for copy in copies] == ["0", "Steel"]
    assert [round(copy.net.EndPoint.Y, 9) for copy in copies] == [100.0, 50.0]
    assert len(twins.called.entities()) == count + 2


def test_a_tuple_of_entities_is_taken_as_a_list_is(twins: Twins) -> None:
    moved = twins.called.move((twins.plan["line"], twins.plan["circle"]), (10, 5))

    assert [m.dxf_type for m in moved] == ["LINE", "CIRCLE"]


def test_the_copy_of_a_curve_is_a_curve_a_script_can_narrow_and_read(twins: Twins) -> None:
    (copy,) = twins.called.copy([twins.plan["circle"]], (0, 50))

    assert isinstance(copy.net, DxfCircle)
    assert round(copy.net.Radius, 9) == 10.0
    assert (round(copy.net.Center.X, 9), round(copy.net.Center.Y, 9)) == (200.0, 50.0)


# ---- the numbers the edits take ----------------------------------------------------------------------------------------


def test_a_move_by_ten_and_five_puts_the_line_from_ten_five_to_a_hundred_and_ten_five(twins: Twins) -> None:
    line = twins.plan["line"]

    twins.called.move([line], (10, 5))

    assert _ends(line) == ((10.0, 5.0), (110.0, 5.0))
    assert _ends(twins.called.entity(line.handle)) == ((10.0, 5.0), (110.0, 5.0))


def test_a_displacement_is_a_vector_or_two_numbers_and_never_three(twins: Twins) -> None:
    line = twins.plan["line"]

    twins.called.move([line], GeoVector2(10, 5))
    twins.called.move([line], [1, 2])

    assert _ends(line) == ((11.0, 7.0), (111.0, 7.0))
    for call in (
        lambda: twins.called.move([line], (1, 2, 3)),
        lambda: twins.called.copy([line], (1, 2, 3)),
    ):
        with pytest.raises(ValueError, match="displacement"):
            call()
    assert _ends(line) == ((11.0, 7.0), (111.0, 7.0))


def test_a_quarter_turn_in_radians_puts_the_line_end_at_nought_a_hundred(twins: Twins) -> None:
    line = twins.plan["line"]

    twins.called.rotate([line], (0, 0), math.pi / 2)

    assert _ends(line) == ((0.0, 0.0), (0.0, 100.0))


def test_a_point_is_a_point_of_two_or_three_numbers_and_a_z_is_dropped(twins: Twins) -> None:
    line, side = twins.plan["line"], twins.plan["side"]

    twins.called.rotate([line], GeoPoint3(0, 0, 7), math.pi / 2)
    twins.called.rotate([side], (0, 0, 7), math.pi / 2)

    assert _ends(line) == ((0.0, 0.0), (0.0, 100.0))
    assert _ends(side) == ((0.0, 0.0), (-50.0, 0.0))
    twins.called.rotate([line], GeoPoint2(0, 0), math.pi / 2)
    assert _ends(line) == ((0.0, 0.0), (-100.0, 0.0))


def test_the_angle_is_counter_clockwise_whatever_angdir_and_angbase_say(twins: Twins) -> None:
    line = twins.plan["line"]
    twins.called.set_variable("ANGDIR", 1)
    twins.called.set_variable("ANGBASE", 0.5)

    twins.called.rotate([line], (0, 0), math.pi / 2)

    # As AutoCAD's Matrix3d.Rotation: the call turns the object by the angle about the world Z, and the typed ROTATE's
    # reading of its angle (ANGDIR, ANGBASE) is for what is typed.
    assert _ends(line) == ((0.0, 0.0), (0.0, 100.0))


def test_a_scale_by_two_about_the_origin_doubles_the_circle_and_the_text(twins: Twins) -> None:
    circle, text = twins.plan["circle"], twins.plan["text"]

    twins.called.scale([circle, text], (0, 0), 2.0)

    assert round(circle.net.Radius, 9) == 20.0
    assert (round(circle.net.Center.X, 9), round(circle.net.Center.Y, 9)) == (400.0, 0.0)
    assert round(text.net.Height, 9) == 10.0


def test_a_mirror_across_the_x_axis_given_two_points_a_hundredth_apart_is_a_mirror(twins: Twins) -> None:
    side = twins.plan["side"]

    (copy,) = twins.called.mirror([side], (0, 0), (0.01, 0))

    assert _ends(copy) == ((0.0, 0.0), (0.0, -50.0))


def test_an_offset_of_a_hundredth_is_an_offset(twins: Twins) -> None:
    (made,) = twins.called.offset(twins.plan["line"], 0.01, (50, 10))

    assert _ends(made) == ((0.0, 0.01), (100.0, 0.01))


def test_a_line_offset_by_five_toward_a_point_is_on_the_sources_layer_and_the_source_stays(twins: Twins) -> None:
    line = twins.plan["line"]

    made = twins.called.offset(line, 5.0, (50, 10))

    # The line from (0, 5) to (100, 5) on Steel (the source's layer, not the current 0); the source from (0, 0) to (100, 0).
    assert len(made) == 1 and isinstance(made[0].net, DxfCurve)
    assert made[0].layer == "Steel"
    assert _ends(made[0]) == ((0.0, 5.0), (100.0, 5.0))
    assert _ends(line) == ((0.0, 0.0), (100.0, 0.0))
    assert _ends(twins.called.entity(line.handle)) == ((0.0, 0.0), (100.0, 0.0))


def test_a_closed_polyline_offset_outward_is_a_closed_polyline_of_four_corners(twins: Twins) -> None:
    (made,) = twins.called.offset(twins.plan["plate"], 10.0, (50, 290))

    # The plate (0, 300)-(100, 350) offset toward (50, 290), below it: the rectangle (-10, 290)-(110, 360), kept closed.
    assert isinstance(made.net, DxfPolyline)
    assert made.net.Closed
    assert made.net.NumberOfVertices == 4
    first = made.net.GetPoint2dAt(0)
    assert (round(first.X, 9), round(first.Y, 9)) == (-10.0, 290.0)


# ---- the queue and the edit are one commit -----------------------------------------------------------------------------


def test_an_insert_queued_and_an_edit_called_are_one_undo_step_and_the_inserts_handle_is_real(twins: Twins) -> None:
    drawing, line = twins.called, twins.plan["line"]
    before = edit_scenarios.snapshot(drawing)

    added = drawing.add_line((0, 100), (50, 100))
    assert added.handle.startswith("~")
    drawing.move([line], (10, 5))

    # The new line is in the drawing, the line is moved, and one U takes both back.
    assert not added.handle.startswith("~")
    assert drawing.entity(added.handle).dxf_type == "LINE"
    assert _ends(drawing.entity(line.handle)) == ((10.0, 5.0), (110.0, 5.0))
    assert len(drawing.entities()) == len(before) + 1
    assert drawing.commit() == []
    _undo(drawing)
    assert edit_scenarios.differences(before, edit_scenarios.snapshot(drawing)) == []


def test_an_insert_queued_before_a_copy_is_added_before_it_so_the_handles_follow_that_order(twins: Twins) -> None:
    typed, called = twins.typed, twins.called

    typed.add_line((0, 100), (50, 100))
    typed.commit()
    typed.select([twins.typed_plan["line"]])
    edit_scenarios.type_command(typed, "COPY", "0,0", "0,50", "")
    called.add_line((0, 100), (50, 100))
    called.copy([twins.plan["line"]], (0, 50))

    # The inserted line has the handle after the plan's and the copy the one after it, as typing the two commands one after the
    # other gives them: the two drawings are the same, handles and all.
    assert edit_scenarios.same_drawing(typed, called) == []
    assert len(called.entities()) == 10


def test_a_modify_queued_before_an_edit_is_applied_before_it(twins: Twins) -> None:
    circle = twins.plan["circle"]

    circle.net.Radius = 30.0
    twins.called.modify(circle)
    twins.called.scale([circle], (0, 0), 2.0)

    # The radius set first (30) and then the scale by 2: 60. Done the other way round the radius would be 30.
    assert round(circle.net.Radius, 9) == 60.0
    assert round(twins.called.entity(circle.handle).net.Radius, 9) == 60.0


def test_a_layer_set_on_an_entity_before_an_edit_is_committed_with_it(twins: Twins) -> None:
    side = twins.plan["side"]

    side.layer = "Steel"
    twins.called.move([side], (10, 5))

    assert twins.called.entity(side.handle).layer == "Steel"
    assert _ends(twins.called.entity(side.handle)) == ((10.0, 5.0), (10.0, 55.0))
    _undo(twins.called)
    assert twins.called.entity(side.handle).layer == "0"
    assert _ends(twins.called.entity(side.handle)) == ((0.0, 0.0), (0.0, 50.0))


def test_an_erase_queued_and_an_edit_called_are_one_commit(twins: Twins) -> None:
    drawing = twins.called
    count = len(drawing.entities())

    drawing.erase(twins.plan["side"])
    drawing.move([twins.plan["line"]], (10, 5))

    assert len(drawing.entities()) == count - 1
    _undo(drawing)
    assert len(drawing.entities()) == count


def test_a_change_made_to_a_net_and_not_queued_is_not_committed_and_the_edit_reads_the_drawings_object(
    twins: Twins,
) -> None:
    circle = twins.plan["circle"]

    circle.net.Radius = 99.0
    twins.called.move([circle], (10, 5))

    # The script never called modify: the circle that moved is the drawing's (radius 10), and the Entity is read from it.
    assert round(circle.net.Radius, 9) == 10.0
    assert round(twins.called.entity(circle.handle).net.Radius, 9) == 10.0


def test_the_same_move_called_twice_moves_twice_in_two_steps(twins: Twins) -> None:
    line = twins.plan["line"]

    twins.called.move([line], (10, 5))
    twins.called.move([line], (10, 5))

    assert _ends(line) == ((20.0, 10.0), (120.0, 10.0))
    _undo(twins.called)
    assert _ends(twins.called.entity(line.handle)) == ((10.0, 5.0), (110.0, 5.0))
    _undo(twins.called)
    assert _ends(twins.called.entity(line.handle)) == ((0.0, 0.0), (100.0, 0.0))


def test_the_same_copy_called_twice_makes_two_sets_of_copies(twins: Twins) -> None:
    count = len(twins.called.entities())

    first = twins.called.copy([twins.plan["line"]], (0, 50))
    second = twins.called.copy([twins.plan["line"]], (0, 50))

    assert first[0].handle != second[0].handle
    assert len(twins.called.entities()) == count + 2


def test_an_entity_an_edit_gave_back_can_be_modified_and_edited_again(twins: Twins) -> None:
    drawing, circle = twins.called, twins.plan["circle"]

    drawing.move([circle], (10, 5))
    (copy,) = drawing.copy([circle], (0, 50))
    circle.net.Radius = 12.0
    copy.net.Radius = 14.0
    drawing.modify(circle)
    drawing.modify(copy)
    drawing.commit()
    drawing.move([circle, copy], (1, 1))

    # The circle moved to (210, 5) and made 12 wide, the copy of it at (210, 55) made 14: each then moved by (1, 1), the radius
    # kept: the entity given back is one a script can go on with.
    read = {entity.handle: entity.net for entity in drawing.entities("CIRCLE")}
    assert (round(read[circle.handle].Radius, 9), round(read[circle.handle].Center.X, 9)) == (12.0, 211.0)
    assert round(read[circle.handle].Center.Y, 9) == 6.0
    assert (round(read[copy.handle].Radius, 9), round(read[copy.handle].Center.X, 9)) == (14.0, 211.0)
    assert round(read[copy.handle].Center.Y, 9) == 56.0
    assert round(circle.net.Center.X, 9) == 211.0 and round(copy.net.Center.Y, 9) == 56.0


def test_an_edit_leaves_the_users_selection_as_it_was(twins: Twins) -> None:
    drawing, line, side = twins.called, twins.plan["line"], twins.plan["side"]
    drawing.select([side])

    drawing.move([line], (10, 5))
    drawing.copy([side], (10, 5))

    assert [entity.handle for entity in drawing.selection()] == [side.handle]


def test_an_empty_list_gives_an_empty_list_and_records_nothing(twins: Twins) -> None:
    drawing = twins.called
    before = edit_scenarios.snapshot(drawing)

    assert drawing.move([], (10, 5)) == []
    assert drawing.copy([], (10, 5)) == []
    assert drawing.rotate([], (0, 0), 1.0) == []
    assert drawing.scale([], (0, 0), 2.0) == []
    assert drawing.mirror([], (0, 0), (0, 10)) == []

    # No step of its own: U takes back the step before them, the locking of the layer Locked.
    assert edit_scenarios.snapshot(drawing) == before
    assert edit_scenarios.locked(drawing)
    _undo(drawing)
    assert not edit_scenarios.locked(drawing)


# ---- refusals: the whole call refused, nothing changed -----------------------------------------------------------------


@pytest.mark.parametrize(("name", "call"), REFUSED_CALLS, ids=[name for name, _ in REFUSED_CALLS])
def test_an_object_on_a_locked_layer_refuses_the_whole_call_and_the_insert_queued_with_it(
    twins: Twins, name: str, call: Any
) -> None:
    drawing, locked = twins.called, twins.plan["locked"]
    before = edit_scenarios.snapshot(drawing)

    drawing.add_line((0, 100), (50, 100))
    with pytest.raises(CadRemoteException, match=f"The object {locked.handle} is on the locked layer Locked."):
        call(drawing, [locked])

    # Nothing is applied, the queue is not kept for the next commit, and no step was taken.
    assert edit_scenarios.snapshot(drawing) == before
    assert drawing.commit() == []
    assert edit_scenarios.snapshot(drawing) == before
    _undo(drawing)
    assert not edit_scenarios.locked(drawing)


def test_a_locked_object_among_free_ones_refuses_the_whole_call_and_the_free_ones_stay(twins: Twins) -> None:
    drawing = twins.called
    before = edit_scenarios.snapshot(drawing)
    given = [twins.plan["line"], twins.plan["locked"], twins.plan["side"]]

    with pytest.raises(CadRemoteException, match="is on the locked layer Locked."):
        drawing.move(given, (10, 5))

    assert edit_scenarios.snapshot(drawing) == before
    assert _ends(given[0]) == ((0.0, 0.0), (100.0, 0.0))


@pytest.mark.parametrize(("name", "call"), REFUSED_CALLS, ids=[name for name, _ in REFUSED_CALLS])
def test_an_object_erased_after_it_was_read_refuses_the_whole_call(twins: Twins, name: str, call: Any) -> None:
    drawing, side = twins.called, twins.plan["side"]
    drawing.erase(side)
    drawing.commit()
    before = edit_scenarios.snapshot(drawing)

    with pytest.raises(CadRemoteException, match=side.handle):
        call(drawing, [side])

    assert edit_scenarios.snapshot(drawing) == before


@pytest.mark.parametrize(("name", "call"), REFUSED_CALLS, ids=[name for name, _ in REFUSED_CALLS])
def test_an_object_read_from_another_drawing_is_refused_before_anything_is_asked(
    twins: Twins, name: str, call: Any
) -> None:
    before = edit_scenarios.snapshot(twins.called)
    before_other = edit_scenarios.snapshot(twins.typed)

    # The line of the twin has the handle of the line of this drawing: it is not this drawing's object.
    with pytest.raises(ValueError, match="read from the drawing"):
        call(twins.called, [twins.typed_plan["line"]])

    assert edit_scenarios.snapshot(twins.called) == before
    assert edit_scenarios.snapshot(twins.typed) == before_other


@pytest.mark.parametrize(("name", "call"), REFUSED_CALLS, ids=[name for name, _ in REFUSED_CALLS])
def test_an_entity_not_committed_yet_is_refused_and_nothing_is_changed(twins: Twins, name: str, call: Any) -> None:
    before = edit_scenarios.snapshot(twins.called)
    added = twins.called.add_line((0, 100), (50, 100))

    with pytest.raises(ValueError, match="commit"):
        call(twins.called, [added])

    assert edit_scenarios.snapshot(twins.called) == before


def test_a_handle_is_no_entity_for_an_edit(twins: Twins) -> None:
    before = edit_scenarios.snapshot(twins.called)

    with pytest.raises(ValueError, match="takes an Entity"):
        twins.called.move([twins.plan["line"].handle], (10, 5))  # pyright: ignore[reportArgumentType, reportCallIssue]

    assert edit_scenarios.snapshot(twins.called) == before


@pytest.mark.parametrize("factor", [0.0, -1.0, float("nan"), float("inf")])
def test_a_scale_factor_of_nought_below_it_or_not_finite_is_refused_and_nothing_changes(
    twins: Twins, factor: float
) -> None:
    before = edit_scenarios.snapshot(twins.called)

    with pytest.raises(NO_EDIT):
        twins.called.scale([twins.plan["line"]], (0, 0), factor)

    assert edit_scenarios.snapshot(twins.called) == before


@pytest.mark.parametrize("second", [(0.0, 0.0), (1e-6, 0.0), (0.0, 5e-4)])
def test_a_mirror_line_of_two_points_under_the_tolerance_apart_is_refused_and_nothing_changes(
    twins: Twins, second: tuple[float, float]
) -> None:
    before = edit_scenarios.snapshot(twins.called)

    with pytest.raises(NO_EDIT):
        twins.called.mirror([twins.plan["side"]], (0, 0), second)

    assert edit_scenarios.snapshot(twins.called) == before


@pytest.mark.parametrize("distance", [0.0, -5.0, 1e-6, float("nan"), float("inf")])
def test_an_offset_distance_of_nought_below_it_or_under_the_tolerance_is_refused_and_nothing_changes(
    twins: Twins, distance: float
) -> None:
    before = edit_scenarios.snapshot(twins.called)

    with pytest.raises(NO_EDIT):
        twins.called.offset(twins.plan["line"], distance, (50, 10))

    assert edit_scenarios.snapshot(twins.called) == before


def test_an_offset_that_makes_no_curve_is_refused_in_the_commands_words(twins: Twins) -> None:
    before = edit_scenarios.snapshot(twins.called)

    # A circle of radius 10 offset inward by 15 makes nothing: OFFSET says "Cannot offset that object."
    with pytest.raises(CadRemoteException, match="Cannot offset that object."):
        twins.called.offset(twins.plan["circle"], 15.0, (205, 0))

    assert edit_scenarios.snapshot(twins.called) == before


def test_an_offset_whose_side_point_lies_on_the_curve_is_refused_and_nothing_changes(twins: Twins) -> None:
    before = edit_scenarios.snapshot(twins.called)

    with pytest.raises(NO_EDIT):
        twins.called.offset(twins.plan["line"], 5.0, (50, 0))

    assert edit_scenarios.snapshot(twins.called) == before


def test_an_object_offset_does_not_take_is_refused_in_the_commands_words(twins: Twins) -> None:
    before = edit_scenarios.snapshot(twins.called)

    with pytest.raises(ValueError, match="Cannot offset that object."):
        twins.called.offset(twins.plan["text"], 5.0, (50, 10))  # pyright: ignore[reportArgumentType]

    assert edit_scenarios.snapshot(twins.called) == before


# ---- a drawing that is not the one shown, and a big one ----------------------------------------------------------------


def test_an_edit_works_on_a_drawing_that_is_not_the_one_shown(twins: Twins) -> None:
    assert twins.typed.is_active is False
    line = twins.typed_plan["line"]

    twins.typed.move([line], (10, 5))

    assert _ends(line) == ((10.0, 5.0), (110.0, 5.0))


def test_every_object_of_a_big_drawing_moved_and_copied_by_one_call_takes_seconds_not_minutes(cad_app: Any) -> None:
    drawing = cad_app.new()
    for index in range(2000):
        drawing.add_line((0, index), (100, index))
    drawing.commit()
    everything = drawing.entities()

    # A blow-up guard, not a measure: a request for each object, or a read of the whole drawing for each, would not pass.
    start = time.perf_counter()
    drawing.move(everything, (10, 5))
    copies = drawing.copy(everything, (0, 5000))
    taken = time.perf_counter() - start

    assert len(copies) == 2000
    assert len(drawing.entities()) == 4000
    assert taken < 30.0
