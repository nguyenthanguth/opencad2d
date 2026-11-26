"""N2: the code of the guide section "Labelled renders for training vision models" of ``docs/python.md`` runs (PW7.4): its ``python``
blocks are executed in order, in one namespace, in an empty folder; and each line that is an expression with a comment of the value
it gives (``masks.classes.shape  # (1200, 1600)``) gives that value. The classes and the categories the section tabulates are those of
the library.
"""

import ast
import re
from pathlib import Path
from typing import Any

import pytest

from opencad2d.cad import LABEL_CLASSES

GUIDE = Path(__file__).resolve().parents[2] / "docs" / "python.md"
FIRST = "## Labelled renders for training vision models"
LAST = "## Inside OpenCAD2D"


def _section() -> str:
    text = GUIDE.read_text(encoding="utf-8")
    return text[text.index(FIRST) : text.index(LAST)]


def _run(source: str, namespace: dict[str, Any]) -> list[tuple[str, Any, Any]]:
    """Runs a block; the lines that end in a comment that is a Python value give (the line, what it gave, the comment's value)."""
    lines = source.splitlines()
    checked: list[tuple[str, Any, Any]] = []
    for node in ast.parse(source).body:
        line = lines[node.end_lineno - 1]  # type: ignore[index]
        comment = line.rpartition("#")[2].strip() if "#" in line else ""
        if isinstance(node, ast.Expr) and comment:
            try:
                expected = ast.literal_eval(comment)
            except (ValueError, SyntaxError):
                expected = None
            else:
                got = eval(compile(ast.Expression(node.value), "<guide>", "eval"), namespace)  # noqa: S307
                checked.append((line.strip(), got, expected))
                continue
        exec(compile(ast.Module([node], []), "<guide>", "exec"), namespace)  # noqa: S102
    return checked


def test_the_code_of_the_section_runs_and_gives_the_values_its_comments_give(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    import opencad2d.cad  # noqa: F401  (boots .NET and loads the assemblies)

    blocks = re.findall(r"```python\n(.*?)```", _section(), flags=re.DOTALL)
    monkeypatch.chdir(tmp_path)
    namespace: dict[str, Any] = {}
    checked: list[tuple[str, Any, Any]] = []
    for block in blocks:
        checked.extend(_run(block, namespace))

    assert len(blocks) == 1
    assert len(checked) == 11
    for line, got, expected in checked:
        assert got == expected, line
    assert [p.name for p in tmp_path.iterdir()] == ["plan.png"]


def test_the_class_table_and_the_category_table_are_the_ones_of_the_library() -> None:
    section = _section()

    classes = re.findall(r"^\| (\d) \| (?:`(\w+)`|(background)) \|", section, flags=re.MULTILINE)
    categories = re.findall(r"^\| (\d) \| (\w+) \|$", section, flags=re.MULTILINE)

    assert [(int(number), name or ground) for number, name, ground in classes] == [
        (0, "background"),
        *[(i + 1, name) for i, name in enumerate(LABEL_CLASSES)],
    ]
    assert [(int(number), name) for number, name in categories] == [(i + 1, name) for i, name in enumerate(LABEL_CLASSES)]
