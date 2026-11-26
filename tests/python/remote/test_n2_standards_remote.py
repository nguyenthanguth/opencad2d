"""N2: a drawing checked against a company standard from outside the app (PW7.2): ``drawing.check()`` through an ``App`` on the test
server gives what it gives on a file, rule for rule and number for number, and a standard the app cannot read is refused in words.

``connect()`` finds the app in the folder of the real one, which a test never touches, so the tests make the ``App`` over the
``CadApplication`` the ``app`` fixture connected to the test server.
"""

import math
from collections.abc import Iterator
from typing import Any

import numpy as np
import pytest

from opencad2d.cad import CheckResult, new_file

STANDARD: dict[str, Any] = {
    "schema": "opencad2d.standard",
    "version": 1,
    "name": "remote",
    "layers": {"allowed": [{"name": "A-*", "color": 3}], "allowLayer0": False},
    "byLayer": {"color": True},
    "textHeights": {"paper": [2.5, 3.5, 5, 7]},
    "gaps": {"wordLine": 1.0, "wordWord": 1.0},
    "dimensionChains": {"tolerance": 0.5},
    "weights": {"text-height": 3},
}


@pytest.fixture
def cad_app(app: Any) -> Iterator[Any]:
    """The ``opencad2d.cad`` App over this test's connection."""
    from opencad2d.cad import App

    with App(app) as made:
        yield made


def _the_same_drawing(drawing: Any) -> None:
    """Words 3.1 high with a line through them and a second word close, a line on layer 0, a coloured line on a layer not listed,
    and three dimensions in a chain, the last one off the line."""
    drawing.layers.add("JUNK", color=1)
    drawing.commit()
    drawing.add_text("PLATE", (0, 0), 3.1)
    drawing.add_text("PLATE", (3, 0), 2.5)
    drawing.add_line((6, -10), (6, 10))
    drawing.add_line((0, 100), (50, 100), layer="JUNK", color=2)
    drawing.add_linear_dim((0, 200), (100, 200), (50, 180))
    drawing.add_linear_dim((100, 200), (200, 200), (150, 180))
    drawing.add_linear_dim((200, 200), (300, 200), (250, 179))
    drawing.commit()


def _facts(result: CheckResult) -> list[Any]:
    return [
        result.name,
        round(result.total, 12),
        [(r.rule, round(r.score, 12), r.subjects, r.offenders, r.weight) for r in result.rules],
        [
            (v.rule, v.other is None, v.space, v.measured, v.allowed, None if v.box is None else [round(b, 9) for b in v.box])
            for v in result.violations
        ],
        [None if math.isnan(v.value) else round(v.value, 9) for v in result.violations],
    ]


def test_the_check_through_the_app_is_the_check_of_a_file(cad_app: Any) -> None:
    remote = cad_app.new()
    local = new_file()
    _the_same_drawing(remote)
    _the_same_drawing(local)

    through, on_file = remote.check(STANDARD), local.check(STANDARD)

    assert len(on_file) >= 6
    assert {v.rule for v in on_file.violations} >= {
        "layer-name",
        "layer-zero",
        "by-layer-color",
        "text-height",
        "gap-word-line",
        "gap-word-word",
        "dim-chain",
    }
    # Handles of two drawings differ only by what each had before; compare everything else, and the handles against their own drawing.
    assert _facts(through) == _facts(on_file)
    assert through.boxes is not None and on_file.boxes is not None
    assert np.allclose(through.boxes, on_file.boxes, atol=1e-9, equal_nan=True)
    own = {entity.handle for entity in remote.entities()}
    assert {v.handle for v in through.violations if v.rule != "layer-properties"} <= own


def test_a_model_scale_and_a_tolerance_reach_the_app(cad_app: Any) -> None:
    drawing = cad_app.new()
    drawing.add_text("PLATE", (0, 0), 310.0)
    drawing.commit()
    standard = {"schema": "opencad2d.standard", "version": 1, "textHeights": {"paper": [2.5, 3.5]}}

    result = drawing.check(standard, model_scale="1:100")

    assert result.violations[0].value == pytest.approx(3.1)
    assert drawing.check(standard).violations[0].value == pytest.approx(310.0)


def test_a_standard_the_app_cannot_read_is_refused_in_words(cad_app: Any) -> None:
    drawing = cad_app.new()
    drawing.add_line((0, 0), (10, 0))
    drawing.commit()

    with pytest.raises(Exception, match="standard.gaps.wordLin"):
        drawing.check({"schema": "opencad2d.standard", "version": 1, "gaps": {"wordLin": 1}})
