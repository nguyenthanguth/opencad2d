"""F5: an edit on a drawing of a file is checked before what is queued with it is applied (A8 follow-up): everything the app's
``CadEditor`` refuses, the checks of its plan too (an offset that makes no curve, a side point on the curve, a distance under
the tolerance), is found by its dry run, ``CadEditor.Check``, so that an insert queued with a refused edit is never applied,
inside an undo group of the script or not.

The drawing is ``edit_scenarios.build_plan``; its numbers are in that module. The session's tolerance is an EqualPoint of 0.001.
"""

from typing import Any

import pytest

import edit_scenarios
import opencad2d
from edit_scenarios import Twins, make_twins
from opencad2d.cad import new_file


@pytest.fixture
def twins() -> Twins:
    """Two drawings of files built alike; the tests use ``called``."""
    return make_twins(new_file(), new_file())


def _refused_offset(twins: Twins, kind: str) -> tuple[type[Exception], str, Any]:
    """An offset the app refuses only when it plans it: the exception it raises, a word it says and the call."""
    plan = twins.plan
    if kind == "no curve":
        # A circle of radius 10 offset inward by 15 makes nothing.
        return RuntimeError, "Cannot offset that object.", lambda: twins.called.offset(plan["circle"], 15.0, (205, 0))
    if kind == "on the curve":
        return ValueError, "lies on the curve", lambda: twins.called.offset(plan["line"], 5.0, (50, 0))
    return ValueError, "tolerance", lambda: twins.called.offset(plan["line"], 1e-6, (50, 10))


KINDS = ["no curve", "on the curve", "under the tolerance"]


@pytest.mark.parametrize("kind", KINDS)
def test_an_insert_queued_with_an_offset_refused_in_its_planning_inside_an_undo_group_is_not_applied(
    twins: Twins, kind: str
) -> None:
    document = twins.called.net
    error, words, call = _refused_offset(twins, kind)

    with opencad2d.undo_group(document, "mine"):
        twins.called.copy([twins.plan["line"]], (0, 50))
        copied = edit_scenarios.snapshot(twins.called)
        twins.called.add_line((0, 100), (50, 100))
        with pytest.raises(error, match=words):
            call()
        # The queued line was refused with the offset and is not in the drawing; the copy made before it stays in the group.
        assert document.IsInGroup
        assert edit_scenarios.snapshot(twins.called) == copied
        assert twins.called.commit() == []

    assert edit_scenarios.snapshot(twins.called) == copied


@pytest.mark.parametrize("kind", KINDS)
def test_an_insert_queued_with_an_offset_refused_in_its_planning_is_not_applied_outside_a_group_either(
    twins: Twins, kind: str
) -> None:
    document = twins.called.net
    before = edit_scenarios.snapshot(twins.called)
    last = str(document.NextUndo.Name)
    error, words, call = _refused_offset(twins, kind)

    twins.called.add_line((0, 100), (50, 100))
    with pytest.raises(error, match=words):
        call()

    # Nothing was applied and no step of the script's was made: the last step is the one the plan's own commit left.
    assert edit_scenarios.snapshot(twins.called) == before
    assert not document.IsInGroup
    assert str(document.NextUndo.Name) == last


def test_a_modify_queued_with_a_refused_offset_is_not_applied(twins: Twins) -> None:
    before = edit_scenarios.snapshot(twins.called)
    twins.plan["line"].net.StartPoint = twins.plan["line"].net.EndPoint
    twins.called.modify(twins.plan["line"])

    with opencad2d.undo_group(twins.called.net, "mine"):
        with pytest.raises(RuntimeError, match="Cannot offset that object."):
            twins.called.offset(twins.plan["circle"], 15.0, (205, 0))
        assert edit_scenarios.snapshot(twins.called) == before


def test_an_edit_that_is_accepted_is_still_one_undo_step_named_python_after_the_check(twins: Twins) -> None:
    document = twins.called.net

    twins.called.add_line((0, 100), (50, 100))
    twins.called.offset(twins.plan["line"], 5.0, (50, 10))

    assert not document.IsInGroup
    assert str(document.NextUndo.Name) == "PYTHON"
    assert len(twins.called.entities()) == 10
