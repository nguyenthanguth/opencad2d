"""H2: the sample tool ``examples/Pythons/DrawRandom`` (command PYRANDOM), its drawing function run on a file with no app: the
objects made, all inside the area, on the layers RANDOM-1 to RANDOM-5, in one undo step, and the same objects for the same
seed. The prompts of the command are asked in the app and are tested there (``ToolHostTests``)."""

import importlib.util
import random
import sys
from pathlib import Path
from types import ModuleType
from typing import Any

import pytest

from opencad2d.cad import new_file

SAMPLE = Path(__file__).resolve().parents[2] / "examples" / "Pythons" / "DrawRandom"
SLACK = 1e-6


@pytest.fixture(scope="module")
def sample() -> ModuleType:
    """The sample's ``__init__.py`` imported by its path, as the app's tool process imports the folder."""
    spec = importlib.util.spec_from_file_location("draw_random_sample", SAMPLE / "__init__.py")
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def _shape(drawing: Any) -> list[tuple[str, str, int, tuple[float, float, float, float]]]:
    """The type, layer, colour and box of every object, in the order of the space."""
    shapes: list[tuple[str, str, int, tuple[float, float, float, float]]] = []
    for entity in drawing.entities():
        box = entity.net.GeometricExtents
        shapes.append(
            (entity.dxf_type, entity.layer, entity.color, (box.Min.X, box.Min.Y, box.Max.X, box.Max.Y))
        )
    return shapes


def test_300_objects_land_inside_the_area_on_the_five_layers_every_kind_and_colouring_among_them(sample: ModuleType) -> None:
    drawing = new_file()

    sample.draw_random(drawing, 300, (100, 200), (600, 500), random.Random(7))

    shapes = _shape(drawing)
    assert len(shapes) == 300
    for _, _, _, (min_x, min_y, max_x, max_y) in shapes:
        assert 100 - SLACK <= min_x and max_x <= 600 + SLACK
        assert 200 - SLACK <= min_y and max_y <= 500 + SLACK
    assert {layer for _, layer, _, _ in shapes} == {f"RANDOM-{n}" for n in range(1, 6)}
    assert {kind for kind, _, _, _ in shapes} == {"ARC", "CIRCLE", "LINE", "LWPOLYLINE", "TEXT"}
    colours = {colour for _, _, colour, _ in shapes}
    assert 256 in colours and any(1 <= colour <= 255 for colour in colours)
    for number in range(1, 6):
        assert 1 <= drawing.layers[f"RANDOM-{number}"].color <= 255


def test_the_corners_may_be_given_in_either_order(sample: ModuleType) -> None:
    drawing = new_file()

    sample.draw_random(drawing, 40, (600, 500), (100, 200), random.Random(3))

    for _, _, _, (min_x, min_y, max_x, max_y) in _shape(drawing):
        assert 100 - SLACK <= min_x and max_x <= 600 + SLACK
        assert 200 - SLACK <= min_y and max_y <= 500 + SLACK


def test_one_undo_takes_back_the_objects_and_the_layers(sample: ModuleType) -> None:
    drawing = new_file()
    layers_before = len(drawing.layers)

    sample.draw_random(drawing, 50, (0, 0), (1000, 500), random.Random(1))
    assert len(drawing.entities()) == 50
    assert len(drawing.layers) == layers_before + 5

    drawing.net.Undo()

    assert drawing.entities() == []
    assert len(drawing.layers) == layers_before
    assert not drawing.net.CanUndo


def test_a_layer_that_is_there_keeps_its_colour(sample: ModuleType) -> None:
    drawing = new_file()
    drawing.layers.add("RANDOM-3", color=1)
    drawing.commit()

    sample.draw_random(drawing, 20, (0, 0), (100, 100), random.Random(5))

    assert drawing.layers["RANDOM-3"].color == 1
    assert len(drawing.layers) == 1 + 5


def test_the_same_seed_gives_the_same_objects_and_another_seed_others(sample: ModuleType) -> None:
    def run(seed: int) -> list[tuple[str, str, int, tuple[float, float, float, float]]]:
        drawing = new_file()
        sample.draw_random(drawing, 30, (0, 0), (1000, 1000), random.Random(seed))
        return _shape(drawing)

    assert run(42) == run(42)
    assert run(42) != run(43)


def test_the_seed_variable_makes_the_generator_repeatable(sample: ModuleType, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("OPENCAD2D_RANDOM_SEED", "9")
    first = sample.new_random().random()
    second = sample.new_random().random()
    monkeypatch.setenv("OPENCAD2D_RANDOM_SEED", "")

    assert first == second
    assert sample.new_random().random() != sample.new_random().random()
