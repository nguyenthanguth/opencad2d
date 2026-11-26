"""N3: the code of the guide section "Checking a drawing against a company standard" of ``docs/python.md`` runs (PW7.2): its ``python``
blocks are executed in order, in one namespace, from the root of the repository (the example standard of ``docs/schemas`` is read by
its path); and each line that is an expression with a comment of the value it gives (``result.scores["text-height"]  # 0.5``) gives
that value.
"""

import ast
import json
import re
from pathlib import Path
from typing import Any

import pytest

ROOT = Path(__file__).resolve().parents[2]
GUIDE = ROOT / "docs" / "python.md"
FIRST = "### Checking a drawing against a company standard"


def _blocks() -> list[str]:
    text = GUIDE.read_text(encoding="utf-8")
    start = text.index(FIRST)
    following = re.search(r"^##", text[start + len(FIRST) :], flags=re.MULTILINE)
    end = len(text) if following is None else start + len(FIRST) + following.start()
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


def test_the_code_of_the_section_runs_and_gives_the_values_its_comments_give(monkeypatch: pytest.MonkeyPatch) -> None:
    import opencad2d.cad  # noqa: F401  (boots .NET and loads the assemblies)

    monkeypatch.chdir(ROOT)
    blocks = _blocks()
    namespace: dict[str, Any] = {}
    checked: list[tuple[str, Any, Any]] = []
    for block in blocks:
        checked.extend(_run(block, namespace))

    assert len(blocks) == 2
    assert len(checked) >= 8
    for line, got, expected in checked:
        assert got == pytest.approx(expected) if isinstance(expected, float) else got == expected, line


def test_the_example_standard_is_the_one_the_schema_describes() -> None:
    standard = json.loads((ROOT / "docs" / "schemas" / "opencad2d-standard-example.json").read_text(encoding="utf-8"))
    schema = json.loads((ROOT / "docs" / "schemas" / "opencad2d-standard-v1.schema.json").read_text(encoding="utf-8"))

    assert standard["schema"] == schema["properties"]["schema"]["const"] == "opencad2d.standard"
    assert standard["version"] == schema["properties"]["version"]["const"] == 1
    assert set(standard) - {"$schema"} <= set(schema["properties"])
    assert set(standard["weights"]) <= set(schema["properties"]["weights"]["properties"])
