"""Q2: a snapshot and reset of a drawing of the app, from outside it (PW7.3): ``drawing.snapshot()`` and ``drawing.reset()`` through an ``App``
on the test server give what they give on a file: the JSON export to the byte and the same handles after an episode, a snapshot the app
has let go refused in words.

``connect()`` finds the app in the folder of the real one, which a test never touches, so the tests make the ``App`` over the
``CadApplication`` the ``app`` fixture connected to the test server.
"""

import random
from collections.abc import Iterator
from typing import Any

import pytest

from opencad2d.cad import Drawing


@pytest.fixture
def cad_app(app: Any) -> Iterator[Any]:
    """The ``opencad2d.cad`` App over this test's connection."""
    from opencad2d.cad import App

    with App(app) as made:
        yield made


def _plan(drawing: Drawing) -> None:
    drawing.layers.add("Steel", color=1)
    drawing.commit()
    drawing.add_line((0, 0), (100, 0), layer="Steel")
    drawing.add_circle((50, 50), 25)
    drawing.add_text("NOTE", (0, 100), 5.0)
    drawing.commit()


def _episode(drawing: Drawing, generator: random.Random, edits: int) -> None:
    for _ in range(edits):
        found = drawing.entities("LINE") + drawing.entities("CIRCLE")
        kind = generator.randrange(10)
        if kind < 3 or not found:
            drawing.add_line((generator.uniform(0, 500), generator.uniform(0, 500)), (generator.uniform(0, 500), 5))
        elif kind < 6:
            drawing.move([generator.choice(found)], (generator.uniform(-9, 9), generator.uniform(-9, 9)))
        elif kind < 8:
            drawing.erase(generator.choice(found))
        else:
            generator.choice(found).layer = "Steel"
        drawing.commit()


def test_an_episode_in_the_app_is_gone_after_a_reset_to_the_byte(cad_app: Any) -> None:
    drawing = cad_app.new()
    _plan(drawing)
    before = drawing.to_json()
    start = drawing.snapshot()
    generator = random.Random(4)

    for _ in range(3):
        _episode(drawing, generator, 20)
        assert drawing.to_json() != before
        drawing.reset(start)
        assert drawing.to_json() == before

    assert start.drawing is drawing


def test_the_handles_repeat_from_one_episode_to_the_next(cad_app: Any) -> None:
    drawing = cad_app.new()
    _plan(drawing)
    start = drawing.snapshot()
    first: list[str] = []

    for _ in range(3):
        drawing.add_circle((5, 5), 2)
        drawing.add_line((0, 0), (1, 1))
        made = drawing.commit()
        first = first or made
        assert made == first
        drawing.reset(start)


def test_a_released_snapshot_is_refused_in_words_and_a_second_release_is_no_error(cad_app: Any) -> None:
    drawing = cad_app.new()
    _plan(drawing)
    start = drawing.snapshot()
    start.release()
    start.release()

    with pytest.raises(ValueError, match="released"):
        drawing.reset(start)


def test_a_snapshot_the_app_has_forgotten_is_refused_by_the_app(cad_app: Any) -> None:
    drawing = cad_app.new()
    _plan(drawing)
    start = drawing.snapshot()
    drawing.net.Release(start._native)  # pyright: ignore[reportPrivateUsage]

    with pytest.raises(Exception, match="no snapshot"):
        drawing.reset(start)
