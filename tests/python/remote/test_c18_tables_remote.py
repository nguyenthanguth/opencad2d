"""C18: the styles, linetypes, dimensions, hatches and blocks of the Drawing API from outside the app (PW5): the scenarios of
``table_scenarios`` through an ``App`` on the test server give the results they give on the other backends, among them the
three that failed at ``commit()`` before PW5 (a text, an MTEXT in a text style the drawing has, a line in a linetype it has);
and the records a commit adds reach the app in one undo step, in the order the objects need them.

``connect()`` finds the app in the folder of the real one, which a test never touches, so the tests make the ``App`` over the
``CadApplication`` the ``app`` fixture connected to the test server.
"""

from collections.abc import Iterator
from pathlib import Path
from typing import Any

import pytest

import drawing_scenarios
import table_scenarios
from table_scenarios import BLANK, SCENARIOS, Scenario


@pytest.fixture
def cad_app(app: Any) -> Iterator[Any]:
    """The ``opencad2d.cad`` App over this test's connection."""
    from opencad2d.cad import App

    with App(app) as made:
        yield made


def _drawing_for(scenario: Scenario, cad_app: Any, tmp_path: Path) -> Any:
    if scenario.drawing == BLANK:
        return cad_app.new()
    path = tmp_path / "plan.dxf"
    table_scenarios.write_file(str(path))
    return cad_app.open(str(path))


@pytest.mark.parametrize("scenario", SCENARIOS, ids=lambda scenario: scenario.name)
def test_a_scenario_through_the_app_gives_the_result_every_backend_gives(
    scenario: Scenario, cad_app: Any, tmp_path: Path
) -> None:
    drawing = _drawing_for(scenario, cad_app, tmp_path)

    # One U of the command line takes back the last step, as a user types it.
    drawing_scenarios.check(scenario, drawing, lambda: drawing.net.SendCommand("U\n"))
