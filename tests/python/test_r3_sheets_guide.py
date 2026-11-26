"""R3: the code of the guide section "Sheets from code: layouts, viewports, a title block and a PDF" of ``docs/python.md`` runs (PW7.7): its ``python`` blocks are executed
in order, in one namespace and in a folder of the test's own; and each line that is an expression with a comment of the value it gives
(``len(diff.added)  # 1``) gives that value.
"""

import ast
import re
from pathlib import Path
from typing import Any

import pytest

ROOT = Path(__file__).resolve().parents[2]
GUIDE = ROOT / "docs" / "python.md"
FIRST = "### Sheets from code: layouts, viewports, a title block and a PDF"


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


def test_the_code_of_the_section_runs_and_gives_the_values_its_comments_give(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    import opencad2d.cad  # noqa: F401  (boots .NET and loads the assemblies)

    monkeypatch.chdir(tmp_path)
    blocks = _blocks()
    namespace: dict[str, Any] = {}
    checked: list[tuple[str, Any, Any]] = []
    for block in blocks:
        checked.extend(_run(block, namespace))

    assert len(blocks) == 4
    assert len(checked) >= 9
    for line, got, expected in checked:
        assert got == pytest.approx(expected) if isinstance(expected, float) else got == expected, line


def test_the_pdfs_the_section_plots_are_one_page_of_the_sheet_and_of_a4(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    import opencad2d.cad  # noqa: F401  (boots .NET and loads the assemblies)

    monkeypatch.chdir(tmp_path)
    namespace: dict[str, Any] = {}
    for block in _blocks():
        _run(block, namespace)

    assert sorted(p.name for p in tmp_path.iterdir()) == ["a4.pdf", "plate.pdf"]
    assert (tmp_path / "plate.pdf").read_bytes().startswith(b"%PDF-")
    from pdf_words import words_of

    assert "A-101" in words_of(tmp_path / "plate.pdf")
