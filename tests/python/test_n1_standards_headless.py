"""N1: a drawing checked against a company standard, on a file with no app (PW7.2): ``drawing.check()`` over a ``CadDocument``.

Each rule is tried on a drawing made in code, once with the fault, where the violation has the numbers it should have, and once
mended, where the rule scores 1; and the result is held to its shape: the scores by rule, the total with the weights of the standard,
the violations sorted, the boxes as a NumPy array with NaN where a violation has no place.
"""

import json
import math
from pathlib import Path
from typing import Any

import numpy as np
import pytest

from opencad2d.cad import CheckResult, Drawing, RuleScore, Violation, new_file


def _standard(**members: Any) -> dict[str, Any]:
    return {"schema": "opencad2d.standard", "version": 1, **members}


def _same(a: CheckResult, b: CheckResult) -> bool:
    """Two results equal, NaN included."""
    return repr(a.rules) == repr(b.rules) and repr(a.violations) == repr(b.violations) and a.total == b.total


def test_a_result_is_scores_by_rule_a_total_violations_and_a_numpy_array_of_boxes() -> None:
    drawing = new_file()
    drawing.add_text("PLATE", (0, 0), 2.5)
    drawing.add_text("PLATE", (0, 10), 3.1)
    drawing.commit()

    result = drawing.check(_standard(name="Plates", textHeights={"paper": [2.5, 3.5]}))

    assert isinstance(result, CheckResult)
    assert result.name == "Plates"
    assert result.scores == {"text-height": 0.5}
    assert result.total == 0.5
    assert result.rules == [RuleScore("text-height", 0.5, 2, 1, 1.0)]
    assert len(result) == 1
    (found,) = result.violations
    assert isinstance(found, Violation)
    assert (found.rule, found.space, found.other, found.through) == ("text-height", "Model", None, None)
    assert (found.measured, found.allowed) == ("3.1 mm", "2.5, 3.5 mm")
    assert found.value == pytest.approx(3.1)
    assert found.limit == 3.5
    assert found.box is not None
    assert result.boxes is not None
    assert result.boxes.shape == (1, 4)
    assert result.boxes[0].tolist() == pytest.approx(list(found.box))


def test_text_heights_are_on_paper_at_the_scale_given_and_the_mended_drawing_scores_one() -> None:
    drawing = new_file()
    drawing.add_text("PLATE", (0, 0), 250.0)
    off = drawing.add_text("PLATE", (0, 500), 310.0)
    drawing.commit()
    standard = _standard(textHeights={"paper": [2.5, 3.5, 5, 7]})

    for scale in ("1:100", 0.01):
        result = drawing.check(standard, model_scale=scale)
        assert [v.handle for v in result.violations] == [off.handle]
        assert result.violations[0].value == pytest.approx(3.1)
    assert drawing.check(standard).violations[0].value == pytest.approx(250.0)

    drawing.erase(off)
    drawing.add_text("PLATE", (0, 500), 350.0)
    drawing.commit()

    assert drawing.check(standard, model_scale="1:100").scores == {"text-height": 1.0}


def test_layer_rules_find_the_object_on_a_layer_not_listed_and_the_one_on_layer_zero() -> None:
    drawing = new_file()
    drawing.layers.add("A-WALL", color=3)
    drawing.layers.add("JUNK", color=1)
    drawing.commit()
    ok = drawing.add_line((0, 0), (10, 0), layer="A-WALL")
    bad = drawing.add_line((0, 5), (10, 5), layer="JUNK")
    zero = drawing.add_line((0, 9), (10, 9))
    drawing.commit()
    standard = _standard(layers={"allowed": [{"name": "A-*", "color": 3}], "allowLayer0": False})

    result = drawing.check(standard)

    assert [(v.rule, v.handle, v.measured) for v in result.violations] == [
        ("layer-name", bad.handle, "JUNK"),
        ("layer-zero", zero.handle, "0"),
    ]
    assert result.scores["layer-name"] == pytest.approx(0.5)  # layer 0 is the layer-zero rule's, where it is given
    assert result.scores["layer-zero"] == pytest.approx(2.0 / 3.0)
    assert ok.handle not in [v.handle for v in result.violations]


def test_a_layer_of_another_colour_than_the_standards_is_a_violation_of_its_record_with_no_place() -> None:
    drawing = new_file()
    drawing.layers.add("A-WALL", color=1)
    drawing.commit()

    result = drawing.check(_standard(layers={"allowed": [{"name": "A-WALL", "color": 3}], "allowOthers": True}))

    (found,) = result.violations
    assert found.rule == "layer-properties"
    assert (found.measured, found.allowed) == ("color 1", "color 3")
    assert found.box is None
    assert result.boxes is not None
    assert np.isnan(result.boxes[0]).all()
    assert math.isnan(found.value)


def test_a_look_of_an_objects_own_is_found_by_its_rule() -> None:
    drawing = new_file()
    coloured = drawing.add_line((0, 0), (10, 0), color=1)
    drawing.add_line((0, 5), (10, 5))
    drawing.commit()

    result = drawing.check(_standard(byLayer={"color": True, "linetype": True}))

    assert result.scores == {"by-layer-color": 0.5, "by-layer-linetype": 1.0}
    assert [(v.rule, v.handle, v.measured) for v in result.violations] == [("by-layer-color", coloured.handle, "1")]


def test_a_word_too_near_a_line_is_a_gap_in_millimetres_on_paper_with_the_line_named() -> None:
    drawing = new_file()
    word = drawing.add_text("PLATE", (0, 0), 2.5)
    through = drawing.add_line((6, -10), (6, 10))
    drawing.add_line((0, 100), (50, 100))
    drawing.commit()

    result = drawing.check(_standard(gaps={"wordLine": 0.5}))

    (found,) = result.violations
    assert (found.rule, found.handle, found.other) == ("gap-word-line", word.handle, through.handle)
    assert found.value == 0.0
    assert found.limit == 0.5
    assert found.allowed == "at least 0.5 mm"
    assert result.scores == {"gap-word-line": 0.0}


def test_two_words_too_near_are_named_first_drawn_first_and_apart_they_score_one() -> None:
    drawing = new_file()
    first = drawing.add_text("PLATE", (0, 0), 2.5)
    second = drawing.add_text("PLATE", (3, 0), 2.5)
    drawing.commit()

    result = drawing.check(_standard(gaps={"wordWord": 0.5}))

    (found,) = result.violations
    assert (found.rule, found.handle, found.other) == ("gap-word-word", first.handle, second.handle)
    assert result.scores == {"gap-word-word": 0.0}

    far = new_file()
    far.add_text("PLATE", (0, 0), 2.5)
    far.add_text("PLATE", (100, 0), 2.5)
    far.commit()
    assert far.check(_standard(gaps={"wordWord": 0.5})).scores == {"gap-word-word": 1.0}


def test_a_dimension_line_off_the_line_of_its_chain_is_found_and_in_line_it_scores_one() -> None:
    def chain(third: float) -> Drawing:
        drawing = new_file()
        drawing.add_linear_dim((0, 0), (100, 0), (50, -20))
        drawing.add_linear_dim((100, 0), (200, 0), (150, -20))
        drawing.add_linear_dim((200, 0), (300, 0), (250, third))
        drawing.commit()
        return drawing

    standard = _standard(dimensionChains={"tolerance": 0.5})

    off = chain(-21.0).check(standard)
    (found,) = off.violations
    assert found.rule == "dim-chain"
    assert found.value == pytest.approx(1.0)
    assert found.limit == 0.5
    assert off.scores == {"dim-chain": pytest.approx(2.0 / 3.0)}
    assert chain(-20.0).check(standard).scores == {"dim-chain": 1.0}


def test_the_total_follows_the_weights_and_a_rule_of_weight_nought_is_left_out_of_it() -> None:
    drawing = new_file()
    drawing.add_line((0, 0), (10, 0), color=1)
    drawing.add_line((0, 5), (10, 5))
    drawing.commit()
    members: dict[str, Any] = {"layers": {"allowLayer0": False}, "byLayer": {"color": True}}

    plain = drawing.check(_standard(**members))
    weighted = drawing.check(_standard(**members, weights={"layer-zero": 3}))
    left = drawing.check(_standard(**members, weights={"layer-zero": 0}))

    assert plain.scores == {"layer-zero": 0.0, "by-layer-color": 0.5}
    assert plain.total == pytest.approx(0.25)
    assert weighted.total == pytest.approx((3 * 0.0 + 0.5) / 4.0)
    assert left.total == pytest.approx(0.5)
    assert drawing.check(_standard()).total == 1.0


def test_a_drawing_scored_before_and_after_a_fix_goes_from_its_faults_to_one() -> None:
    standard = _standard(
        layers={"allowed": [{"name": "A-*"}], "allowLayer0": False},
        textHeights={"paper": [2.5]},
        gaps={"wordLine": 1.0},
    )
    drawing = new_file()
    drawing.layers.add("A-TEXT")
    drawing.commit()
    word = drawing.add_text("PLATE", (0, 0), 4.0)
    drawing.add_line((6, -10), (6, 10), layer="A-TEXT")
    drawing.commit()

    before = drawing.check(standard)
    drawing.erase(word)
    drawing.add_text("PLATE", (0, 50), 2.5, layer="A-TEXT")
    drawing.commit()
    after = drawing.check(standard)

    assert before.total < 0.5
    assert {v.rule for v in before.violations} == {"layer-zero", "text-height", "gap-word-line"}
    assert after.total == 1.0
    assert after.violations == []


def test_the_result_is_the_same_each_time_sorted_by_rule_and_the_standard_may_be_a_file() -> None:
    drawing = new_file()
    for index in range(30):
        drawing.add_text("PLATE", (index * 40, 0), 1.0 + index % 4, color=1 + index % 3)
    drawing.commit()
    members: dict[str, Any] = {
        "layers": {"allowLayer0": False},
        "byLayer": {"color": True},
        "textHeights": {"paper": [2.5]},
    }

    a = drawing.check(_standard(**members))
    b = drawing.check(_standard(**members))

    assert _same(a, b)
    order = ["layer-zero", "by-layer-color", "text-height"]
    keys = [(order.index(v.rule), int(v.handle, 16)) for v in a.violations]
    assert keys == sorted(keys)
    assert len(a) > 60


def test_a_standard_file_gives_the_result_a_dict_gives(tmp_path: Path) -> None:
    drawing = new_file()
    drawing.add_text("PLATE", (0, 0), 3.1)
    drawing.commit()
    members = _standard(name="from a file", textHeights={"paper": [2.5, 3.5]})
    path = tmp_path / "company.json"
    path.write_text(json.dumps(members), encoding="utf-8")

    from_file = drawing.check(path)
    from_text = drawing.check(str(path))

    assert _same(from_file, drawing.check(members))
    assert _same(from_text, from_file)
    assert from_file.name == "from a file"


def test_a_standard_that_is_not_one_and_a_scale_that_is_not_a_scale_are_refused_in_words(tmp_path: Path) -> None:
    drawing = new_file()
    drawing.add_text("PLATE", (0, 0), 3.1)
    drawing.commit()

    with pytest.raises(ValueError, match="standard.gaps.wordLin"):
        drawing.check(_standard(gaps={"wordLin": 1}))
    with pytest.raises(ValueError, match="version"):
        drawing.check({"schema": "opencad2d.standard", "version": 2})
    with pytest.raises(ValueError, match="model scale"):
        drawing.check(_standard(), model_scale="big")
    with pytest.raises(ValueError, match="model scale"):
        drawing.check(_standard(), model_scale=0.0)
    with pytest.raises(ValueError, match="tolerance"):
        drawing.check(_standard(), tolerance=-1.0)
    with pytest.raises(FileNotFoundError):
        drawing.check(tmp_path / "missing.json")
