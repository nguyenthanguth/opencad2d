"""Q1: the code of the guide section "Batch and AI pipelines" of ``docs/python.md`` runs (PW6.4): its ``python`` blocks are executed
in order, in one namespace, in an empty folder; and each line that is an expression with a comment of the value it gives
(``hits.words  # ['102']``) gives that value. The links of the section go to files and headings that are there, it names every
example it is about, and it says nothing of the types of the app's window.
"""

import ast
import re
from pathlib import Path
from typing import Any

import pytest

DOCS = Path(__file__).resolve().parents[2] / "docs"
GUIDE = DOCS / "python.md"
FIRST = "## Batch and AI pipelines"
EXAMPLES = Path(__file__).resolve().parents[2] / "src" / "python" / "examples" / "library"


def _section() -> str:
    text = GUIDE.read_text(encoding="utf-8")
    start = text.index(FIRST)
    following = re.search(r"^## ", text[start + len(FIRST) :], flags=re.MULTILINE)
    end = len(text) if following is None else start + len(FIRST) + following.start()
    return text[start:end]  # up to the next section: the sections after it are not this one's


def _slugs(page: Path) -> set[str]:
    """The anchors of the headings of a page, as DocFX makes them: lower case, the spaces dashes, no other punctuation."""
    headings = re.findall(r"^#{1,6} (.+)$", page.read_text(encoding="utf-8"), flags=re.MULTILINE)
    return {re.sub(r"[^\w\- ]", "", heading.replace("`", "").lower()).replace(" ", "-") for heading in headings}


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
    assert [p.name for p in tmp_path.iterdir()] == ["after.png"]


def test_the_links_of_the_section_go_to_files_and_headings_that_are_there() -> None:
    section = _section()

    links = re.findall(r"\]\(([^)\s]+)\)", section)

    assert len(links) >= 10
    for link in links:
        if link.startswith("http"):
            continue
        target, _, anchor = link.partition("#")
        page = (GUIDE.parent / target) if target else GUIDE
        assert page.is_file(), f"{link}: {page} is not there"
        if anchor and page.suffix == ".md":
            assert anchor in _slugs(page), f"{link}: {page.name} has no heading {anchor}"


def test_the_section_names_and_links_every_example_it_is_about_and_the_mcp_guide() -> None:
    section = _section()

    for example in ("batch_export.py", "layout_loop.py", "render_views.py", "coco_dataset.py", "coordinates_numpy.py"):
        assert f"[`{example}`](" in section or f"`{example}`](" in section, example
        assert (EXAMPLES / example).is_file()
    assert "presenter/mcp.md" in section
    assert "xref:OpenCAD2D.Wpf" not in section


def test_the_table_of_timings_has_a_row_for_each_step_and_numbers_for_both_runtimes() -> None:
    section = _section()

    rows = re.findall(r"^\| (`[^`]+`[^|]*|[A-Za-z][^|]*) \| (\d+\.\d+) / (\d+\.\d+) \| (\d+\.\d+) / (\d+\.\d+) \|$", section, flags=re.MULTILINE)

    names = [row[0].strip() for row in rows]
    assert names == [
        "`open_file`",
        "`flatten`",
        "`to_json`",
        "`to_graph`",
        "`render_png`",
        "`reward` (`check` and `overlaps`) of the sheet of `layout_loop.py`",
        "`reward` of 10 000 objects",
    ]
    assert all(float(value) > 0.0 for row in rows for value in row[1:])
