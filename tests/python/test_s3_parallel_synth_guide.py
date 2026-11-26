"""S3: the code of the guide sections "Parallel workers with the same answers" and "Synthetic drawings for training data" of
``docs/python.md`` runs (PW7.8, PW7.9): the ``python`` blocks of each section are executed in order, in one namespace; and each
line that is an expression with a comment of the value it gives (``len(drawing.entities())  # 2``) gives that value.
"""

import ast
import re
from pathlib import Path
from typing import Any

import pytest

ROOT = Path(__file__).resolve().parents[2]
GUIDE = ROOT / "docs" / "python.md"
SECTIONS = {
    "parallel": "### Parallel workers with the same answers",
    "synth": "### Synthetic drawings for training data",
}


def _blocks(first: str) -> list[str]:
    text = GUIDE.read_text(encoding="utf-8")
    start = text.index(first)
    following = re.search(r"^##", text[start + len(first) :], flags=re.MULTILINE)
    end = len(text) if following is None else start + len(first) + following.start()
    section = text[start:end]  # up to the next heading: the sections after it are not this one's
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


@pytest.mark.parametrize(("name", "minimum"), [("parallel", 5), ("synth", 7)])
def test_the_code_of_the_section_runs_and_gives_the_values_its_comments_give(
    name: str, minimum: int, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    import opencad2d.cad  # noqa: F401  (boots .NET and loads the assemblies)

    monkeypatch.chdir(tmp_path)  # the picture of the synth block is written here
    blocks = _blocks(SECTIONS[name])
    namespace: dict[str, Any] = {}
    checked: list[tuple[str, Any, Any]] = []
    for block in blocks:
        checked.extend(_run(block, namespace))

    assert len(blocks) == 1
    assert len(checked) >= minimum
    for line, got, expected in checked:
        assert got == pytest.approx(expected) if isinstance(expected, float) else got == expected, line
