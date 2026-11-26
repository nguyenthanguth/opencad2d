"""S1: ``opencad2d.synth`` (PW7.9): seeded plate drawings with their ground truth: the same seed gives the same JSON and the same
picture, other seeds other drawings, ``check`` scores them, the COCO boxes lie inside the picture, and crowding makes words overlap
what they stand on."""

import time
from pathlib import Path

import pytest

import opencad2d  # noqa: F401  (boots .NET and loads the assemblies)
from opencad2d import synth

SEEDS = list(range(8))


def test_the_same_seed_twice_gives_the_same_json_and_the_same_picture_byte_for_byte(tmp_path: Path) -> None:
    one = synth.generate(7, 0.5, image=tmp_path / "one.png")
    two = synth.generate(7, 0.5, image=tmp_path / "two.png")

    assert one.json == two.json
    assert one.json.encode("utf-8") == two.json.encode("utf-8")
    assert (one.score, one.scores, one.overlaps, one.coco is not None) == (two.score, two.scores, two.overlaps, True)
    assert (tmp_path / "one.png").read_bytes() == (tmp_path / "two.png").read_bytes()
    assert one.coco is not None and two.coco is not None
    assert [a["bbox"] for a in one.coco["annotations"]] == [a["bbox"] for a in two.coco["annotations"]]


def test_different_seeds_give_different_drawings() -> None:
    texts = [synth.generate(seed).json for seed in SEEDS]

    assert len(set(texts)) == len(SEEDS)


def test_crowding_adds_notes_to_the_same_plate() -> None:
    clean, crowded = synth.make_drawing(3), synth.make_drawing(3, 1.0)

    assert len(crowded.entities("TEXT")) == len(clean.entities("TEXT")) + 8
    assert len(crowded.entities("CIRCLE")) == len(clean.entities("CIRCLE"))
    assert [e.handle for e in clean.entities("CIRCLE")] == [e.handle for e in crowded.entities("CIRCLE")]
    assert len(crowded.entities("LINE")) == len(clean.entities("LINE"))


@pytest.mark.parametrize("seed", SEEDS)
def test_a_plate_without_crowding_has_no_overlap_and_a_clean_score(seed: int) -> None:
    sample = synth.generate(seed)

    assert sample.overlaps == 0
    assert sample.score == pytest.approx(1.0)
    assert set(sample.scores.values()) == {1.0}


@pytest.mark.parametrize("seed", SEEDS)
def test_a_crowded_plate_has_words_across_its_lines_and_scores_lower(seed: int) -> None:
    sample = synth.generate(seed, 1.0)

    assert sample.overlaps > 0
    assert sample.score < 0.99
    assert sample.scores["gap-word-line"] < 1.0


def test_check_runs_every_rule_of_the_standard_and_scores_stay_between_nought_and_one() -> None:
    sample = synth.generate(5, 0.6)

    assert list(sample.scores) == [
        "layer-name",
        "layer-properties",
        "by-layer-color",
        "by-layer-linetype",
        "text-style",
        "text-height",
        "gap-word-line",
        "gap-word-word",
        "dim-chain",
    ]
    assert all(0.0 <= score <= 1.0 for score in sample.scores.values())
    assert 0.0 <= sample.score <= 1.0


@pytest.mark.parametrize("seed", [0, 4])
def test_every_box_of_the_coco_file_lies_inside_the_picture(seed: int, tmp_path: Path) -> None:
    sample = synth.generate(seed, 0.5, image=tmp_path / "plate.png", width=1000)

    coco = sample.coco
    assert coco is not None
    (image,) = coco["images"]
    assert image["width"] == 1000 and image["file_name"] == "plate.png"
    assert len(coco["annotations"]) > 20
    for annotation in coco["annotations"]:
        x, y, width, height = annotation["bbox"]
        assert x >= 0 and y >= 0 and width > 0 and height > 0, annotation
        assert x + width <= image["width"] and y + height <= image["height"], annotation
    assert {a["category_id"] for a in coco["annotations"]} >= {1, 2, 3}  # words, dimensions, lines


def test_no_picture_is_made_unless_it_is_asked_for() -> None:
    assert synth.generate(1).coco is None


def test_the_sheet_is_an_a3_layout_in_millimetres_with_one_viewport_and_a_filled_title_block() -> None:
    drawing = synth.make_drawing(2)

    sheet = drawing.layouts[synth.SHEET]
    assert (sheet.paper, sheet.size, sheet.units, sheet.viewports) == ("ISO A3", (420.0, 297.0), "mm", 1)
    (insert,) = drawing.entities("INSERT", layout=sheet)
    tags = {str(a.Tag): str(a.TextString) for a in insert.net.AttributeCollection}
    assert sorted(tags) == ["DRAWING", "NUMBER", "SCALE"]
    assert tags["DRAWING"].startswith("PLATE PL") and tags["NUMBER"].startswith("A-") and tags["SCALE"] in ("1:5", "1:10")


def test_the_drawings_use_the_layers_of_the_standard_and_the_plates_vary() -> None:
    layers: set[str] = set()
    shapes: set[tuple[int, int]] = set()
    for seed in range(12):
        drawing = synth.make_drawing(seed)
        layers |= {e.layer for e in drawing.entities()}
        shapes.add((len(drawing.entities("CIRCLE")), len(drawing.entities("DIMENSION"))))

    assert layers == set(synth.LAYERS)  # a weld on some, none on others, and nothing on layer 0
    assert len(shapes) >= 4


def test_a_crowding_outside_nought_to_one_is_refused() -> None:
    for crowding in (-0.1, 1.5):
        with pytest.raises(ValueError, match="crowding is from 0 to 1"):
            synth.make_drawing(1, crowding)


def test_a_drawing_takes_a_fraction_of_a_second(capsys: pytest.CaptureFixture[str]) -> None:
    synth.generate(0)
    started = time.perf_counter()
    for seed in range(1, 11):
        synth.generate(seed, 0.5)
    each = (time.perf_counter() - started) / 10.0
    with capsys.disabled():
        print(f"\nsynth.generate: {each * 1000.0:.0f} ms a drawing (10 drawings, crowding 0.5, no picture)")

    assert each < 2.0
