"""Q1: ``to_graph(max_edges=n)`` from outside the app: the app keeps the first ``n`` edges of the whole graph, and the text it wrote
says whether it was cut, as a file's does.

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


def _keys(graph: dict[str, Any]) -> list[tuple[str, int, int]]:
    return [(e["kind"], e["source"], e["target"]) for e in graph["edges"]]


def test_the_graph_through_the_app_is_cut_as_the_graph_of_a_file_is(cad_app: Any, tmp_path: Path) -> None:
    from opencad2d.cad import open_file

    path = tmp_path / "plan.dxf"
    export_scenarios.write_file(str(path))
    shown = cad_app.open(str(path))
    whole = open_file(str(path)).to_graph()
    assert len(whole["edges"]) >= 3

    for cap in range(1, len(whole["edges"]) + 2):
        graph = shown.to_graph(max_edges=cap)

        assert _keys(graph) == _keys(whole)[:cap]
        assert graph["truncated"] is (cap < len(whole["edges"]))
        assert graph["edgesFound"] >= len(graph["edges"])
        assert graph["edges"] == open_file(str(path)).to_graph(max_edges=cap)["edges"]

    assert shown.to_graph()["truncated"] is False


def test_a_cap_below_one_is_refused_before_the_app_is_asked(cad_app: Any) -> None:
    drawing = cad_app.new()
    drawing.add_line((0, 0), (100, 0))
    drawing.commit()

    with pytest.raises(ValueError, match="max_edges"):
        drawing.to_graph(max_edges=0)
