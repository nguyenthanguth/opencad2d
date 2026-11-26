"""K1: the styles, linetypes, dimensions, hatches and blocks of the Drawing API on a file, with no app (PW5): ``open_file`` and
``new_file`` give a ``Drawing`` over a ``CadDocument``; the scenarios of ``table_scenarios`` give the results they give on the
other backends, among them the three that failed at ``commit()`` before PW5 (a text, an MTEXT and a dimension in a style the
drawing has, a line in a linetype it has). Every file is written in ``tmp_path``.
"""

from pathlib import Path
from typing import Any

import pytest

import drawing_scenarios
import table_scenarios
from opencad2d.cad import new_file, open_file
from table_scenarios import BLANK, SCENARIOS, Scenario


def _drawing_for(scenario: Scenario, tmp_path: Path) -> Any:
    if scenario.drawing == BLANK:
        return new_file()
    path = tmp_path / "plan.dxf"
    table_scenarios.write_file(str(path))
    return open_file(str(path))


@pytest.mark.parametrize("scenario", SCENARIOS, ids=lambda scenario: scenario.name)
def test_a_scenario_on_a_file_gives_the_result_every_backend_gives(scenario: Scenario, tmp_path: Path) -> None:
    drawing = _drawing_for(scenario, tmp_path)

    drawing_scenarios.check(scenario, drawing, drawing.net.Undo)


def test_a_commit_of_records_adds_none_of_the_defaults_the_drawing_lacks() -> None:
    drawing = new_file()
    database = drawing.net.Database
    database.Dimstyle = "Standard"
    database.DimStyleTable["ISO-25"].Erase()

    drawing.text_styles.add("NOTE", font="romans.shx")
    drawing.dimension_styles.add("PLAN", text_style="NOTE")
    drawing.commit()

    assert sorted(str(style.Name) for style in drawing.dimension_styles) == ["PLAN", "Standard"]
    assert sorted(str(style.Name) for style in drawing.text_styles) == ["NOTE", "Standard"]


def test_a_refused_commit_of_records_leaves_the_drawing_and_its_queue_as_a_refused_commit_does() -> None:
    drawing = new_file()
    drawing.text_styles.add("Standard")
    drawing.add_line((0, 0), (10, 0))

    with pytest.raises(RuntimeError, match='The text style "Standard" already exists.'):
        drawing.commit()

    assert drawing.entities() == []
    assert drawing.commit() == []


def test_a_line_in_a_linetype_the_drawing_has_keeps_the_drawings_own_record_of_it(tmp_path: Path) -> None:
    path = tmp_path / "plan.dxf"
    table_scenarios.write_file(str(path))
    drawing = open_file(str(path))

    drawing.add_line((0, 5), (10, 5), linetype="DASHED")
    drawing.add_text("Plate", (0, 0), 3.0, text_style="NOTE")
    drawing.commit()

    dashed = drawing.net.Database.LinetypeTable["DASHED"]
    assert (dashed.NumDashes, dashed.DashLengthAt(0), dashed.DashLengthAt(1)) == (2, 12.0, -6.0)
    note = drawing.net.Database.TextStyleTable["NOTE"]
    assert (str(note.FileName), note.XScale) == ("romans.shx", 0.8)
    assert len(list(drawing.net.Database.TextStyleTable)) == 2
