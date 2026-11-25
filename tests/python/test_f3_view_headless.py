"""F3: the view, the selection by area and the system variables on a drawing of a file, with no app (A6, API-D26): a drawing of
``new_file`` or ``open_file`` has no view, so ``zoom_*``, ``pan``, ``view`` and the named views raise ``RuntimeError`` in the
words of API-D26 and ``has_window`` is False; the selection by area (everything, since no view holds it) and the variables
work, on a command session the file's drawing makes once; ``regen`` works too, as it moves no view.

The drawing is a line from (0, 0) to (100, 0) and a circle about (50, 50) of radius 25, and five lines for the selection.
"""

from collections.abc import Callable
from pathlib import Path
from typing import Any

import pytest

from opencad2d.cad import Box, new_file, open_file


def _plan() -> Any:
    drawing = new_file()
    drawing.add_line((0, 0), (100, 0))
    drawing.add_circle((50, 50), 25)
    drawing.commit()
    return drawing


def _rows() -> Any:
    drawing = new_file()
    for y in (0, 10, 20, 30, 40):
        drawing.add_line((0, y), (100, y))
    drawing.commit()
    return drawing


VIEW_CALLS: list[tuple[str, Callable[[Any], object]]] = [
    ("zoom_extents", lambda d: d.zoom_extents()),
    ("zoom_window", lambda d: d.zoom_window((0, 0), (10, 10))),
    ("zoom_objects", lambda d: d.zoom_objects(["101"])),
    ("zoom_center", lambda d: d.zoom_center((50, 50), 20)),
    ("zoom_scaled", lambda d: d.zoom_scaled(2)),
    ("zoom_previous", lambda d: d.zoom_previous()),
    ("pan", lambda d: d.pan((10, 0))),
    ("view", lambda d: d.view),
    ("named_views", lambda d: d.named_views),
    ("save_view", lambda d: d.save_view("V")),
    ("restore_view", lambda d: d.restore_view("V")),
    ("delete_view", lambda d: d.delete_view("V")),
]


@pytest.mark.parametrize(("name", "call"), VIEW_CALLS, ids=[name for name, _ in VIEW_CALLS])
def test_a_view_call_on_a_drawing_of_a_file_raises_in_the_words_of_api_d26(
    name: str, call: Callable[[Any], object]
) -> None:
    drawing = _plan()

    with pytest.raises(RuntimeError) as refused:
        call(drawing)

    assert str(refused.value) == f"{name} works on a drawing of the app: a drawing file has no view."


def test_setting_the_view_of_a_drawing_of_a_file_raises_in_the_same_words() -> None:
    drawing = _plan()

    with pytest.raises(RuntimeError, match="view works on a drawing of the app: a drawing file has no view."):
        drawing.view = Box(0.0, 0.0, 10.0, 10.0)


def test_has_window_is_false_for_a_new_file_and_an_opened_one(tmp_path: Path) -> None:
    path = tmp_path / "plan.dxf"
    _plan().save_as(str(path))

    assert new_file().has_window is False
    assert open_file(str(path)).has_window is False


def test_regen_works_on_a_drawing_of_a_file_and_changes_nothing_the_script_can_see() -> None:
    drawing = _plan()

    drawing.regen()

    assert len(drawing.entities()) == 2
    assert drawing.net.NextUndo.Name != "REGEN"


# ---- the selection by area ---------------------------------------------------------------------------------------------


def _ys(entities: list[Any]) -> list[float]:
    return [round(float(e.net.StartPoint.Y), 6) for e in entities]


def test_select_all_gives_every_object_as_copies_with_handles_in_the_drawings_order() -> None:
    drawing = _plan()

    found = drawing.select_all()

    assert [e.dxf_type for e in found] == ["LINE", "CIRCLE"]
    assert [e.handle for e in found] == [e.handle for e in drawing.entities()]
    assert type(found[1].net).__name__ == "DxfCircle"


def test_select_window_and_select_crossing_take_what_the_selection_options_take() -> None:
    drawing = _rows()

    assert _ys(drawing.select_window((-1, 5), (101, 35))) == [10.0, 20.0, 30.0]
    assert _ys(drawing.select_crossing((50, 15), (60, 25))) == [20.0]
    assert drawing.select_window((50, 15), (60, 25)) == []


def test_with_no_view_select_window_is_not_held_to_one() -> None:
    drawing = _rows()

    assert _ys(drawing.select_window((-1, -1), (101, 41))) == [0.0, 10.0, 20.0, 30.0, 40.0]


def test_a_selection_by_area_leaves_the_pickfirst_selection_as_it_was() -> None:
    drawing = _rows()
    first = drawing.entities()[0]
    drawing.select([first])

    drawing.select_all()
    drawing.select_window((-1, 5), (101, 35))
    drawing.select_crossing((50, 15), (60, 25))

    assert [e.handle for e in drawing.selection()] == [first.handle]


def test_a_corner_that_is_not_finite_is_refused_as_a_value_error() -> None:
    drawing = _rows()

    with pytest.raises(ValueError, match="corner"):
        drawing.select_window((float("nan"), 0), (1, 1))


# ---- the system variables ----------------------------------------------------------------------------------------------


def test_variables_round_trip_an_integer_a_real_a_text_and_a_point() -> None:
    drawing = _plan()

    assert drawing.get_variable("LUNITS") == 2
    assert isinstance(drawing.get_variable("LUNITS"), int)
    drawing.set_variable("LUNITS", 4)
    assert drawing.get_variable("LUNITS") == 4
    assert drawing.net.Database.Lunits == 4

    drawing.set_variable("LTSCALE", 2.5)
    assert drawing.get_variable("ltscale") == 2.5
    assert isinstance(drawing.get_variable("LTSCALE"), float)

    assert drawing.get_variable("CLAYER") == "0"
    assert isinstance(drawing.get_variable("CLAYER"), str)

    drawing.set_variable("INSBASE", (1.5, 2.5, 0.0))
    assert drawing.get_variable("INSBASE") == (1.5, 2.5, 0.0)


def test_a_variable_the_drawing_keeps_is_one_undo_step_named_setvar() -> None:
    drawing = _plan()

    drawing.set_variable("LUNITS", 4)

    assert drawing.net.NextUndo.Name == "SETVAR"
    drawing.net.Undo()
    assert drawing.get_variable("LUNITS") == 2


def test_a_variable_refused_raises_a_value_error_in_the_words_of_the_editor() -> None:
    drawing = _plan()

    with pytest.raises(ValueError, match="The system variable NOSUCH is not known."):
        drawing.get_variable("NOSUCH")
    with pytest.raises(ValueError, match="is read only."):
        drawing.set_variable("EXTMIN", (0.0, 0.0, 0.0))
    with pytest.raises(ValueError, match="The value 7 is refused for LUNITS."):
        drawing.set_variable("LUNITS", 7)
    assert drawing.get_variable("LUNITS") == 2


def test_a_variable_is_set_to_a_number_a_text_or_a_point_and_a_bool_is_none_of_them() -> None:
    drawing = _plan()

    with pytest.raises(TypeError, match="LUNITS"):
        drawing.set_variable("LUNITS", True)
    with pytest.raises(TypeError, match="LUNITS"):
        drawing.set_variable("LUNITS", None)  # pyright: ignore[reportArgumentType]


def test_the_variables_of_two_drawings_are_their_own() -> None:
    first = _plan()
    second = _plan()

    first.set_variable("LUNITS", 4)

    assert second.get_variable("LUNITS") == 2
