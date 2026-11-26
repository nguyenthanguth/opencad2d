"""C19: the export for AI pipelines from outside the app (PW6.3): ``flatten``, ``to_json``, ``to_dict`` and ``to_graph`` of a
drawing of a running app give what ``export_scenarios`` says they give on a file, as the one request ``export`` answers them, the
JSON text the app wrote; a layout the drawing lacks is refused in words.

``connect()`` finds the app in the folder of the real one, which a test never touches, so the tests make the ``App`` over the
``CadApplication`` the ``app`` fixture connected to the test server.
"""

from collections.abc import Iterator
from pathlib import Path
from typing import Any

import pytest

import export_scenarios


@pytest.fixture
def cad_app(app: Any) -> Iterator[Any]:
    """The ``opencad2d.cad`` App over this test's connection."""
    from opencad2d.cad import App

    with App(app) as made:
        yield made


def _drawing(cad_app: Any, tmp_path: Path) -> Any:
    path = tmp_path / "plan.dxf"
    export_scenarios.write_file(str(path))
    return cad_app.open(str(path))


def test_the_facts_through_the_app_are_the_facts_of_a_file(cad_app: Any, tmp_path: Path) -> None:
    assert export_scenarios.facts(_drawing(cad_app, tmp_path)) == export_scenarios.EXPECTED


def test_the_text_through_the_app_is_the_text_of_a_file(cad_app: Any, tmp_path: Path) -> None:
    from opencad2d.cad import open_file

    path = tmp_path / "plan.dxf"
    export_scenarios.write_file(str(path))
    shown = cad_app.open(str(path))

    # The name of the drawing is the one thing a file and the app do not both write alike (plan.dxf either way).
    assert shown.to_json() == open_file(str(path)).to_json()
    assert shown.to_graph()["edges"] == open_file(str(path)).to_graph()["edges"]


def test_a_layout_the_drawing_lacks_is_refused_in_words(cad_app: Any) -> None:
    from opencad2d.cad import new_file

    drawing = cad_app.new()
    drawing.add_line((0, 0), (100, 0))
    drawing.commit()

    assert len(drawing.flatten("Model")) == 1
    with pytest.raises(Exception, match="no layout named Nowhere"):
        drawing.flatten("Nowhere")
    with pytest.raises(Exception, match="above nought"):
        drawing.to_graph(tolerance=0.0)
    assert new_file().flatten() == []
