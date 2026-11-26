"""M3: the code of the guide section "Where things are drawn: bounds and overlaps" of ``docs/python.md`` runs (PW6.2): its ``python``
blocks are executed in order, in one namespace, on a new drawing; and each line that is an expression with a comment of the value
it gives (``table.boxes.shape  # (3, 4)``) gives that value.
"""

import ast
import re
from pathlib import Path
from typing import Any

import pytest

GUIDE = Path(__file__).resolve().parents[2] / "docs" / "python.md"
FIRST = "### Where things are drawn: bounds and overlaps"
LAST = "## Inside OpenCAD2D"


def _blocks() -> list[str]:
    text = GUIDE.read_text(encoding="utf-8")
    section = text[text.index(FIRST) : text.index(LAST)]
    return re.findall(r"```python\n(.*?)```", section, flags=re.DOTALL)


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


def test_the_code_of_the_section_runs_and_gives_the_values_its_comments_give() -> None:
    import opencad2d.cad  # noqa: F401  (boots .NET and loads the assemblies)

    blocks = _blocks()
    namespace: dict[str, Any] = {}
    checked: list[tuple[str, Any, Any]] = []
    for block in blocks:
        checked.extend(_run(block, namespace))

    assert len(blocks) == 3
    assert len(checked) >= 10
    for line, got, expected in checked:
        assert got == pytest.approx(expected) if isinstance(expected, float) else got == expected, line
