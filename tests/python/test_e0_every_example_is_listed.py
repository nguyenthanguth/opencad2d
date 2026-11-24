"""E0: no example without a row, a page section and a test, and every example in the shape D1002 gives (P5 D1001,
D1002, D1007, D1008).

The set of ``.py`` files under ``src/python/examples`` is the thirteen of D1002, three folders; a Python tool of
``tools`` is a folder (a ``requirements.txt``, an ``__init__.py`` and a ``plugin.json``), the example being its
``__init__.py``. The set equals the examples the README of the folder names in its table, equals the files the guide page
``python-examples.md`` includes with ``[!code-python[...](...)]``, and each example's name is in at least one test file.
A new example without a test, a row or a page section fails here, and so does one left out.
"""

import ast
import re
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
EXAMPLES = ROOT / "src" / "python" / "examples"
GUIDE = ROOT / "docs" / "python-examples.md"
CSHARP_TESTS = ROOT / "tests" / "OpenCAD2D.Wpf.UnitTest" / "PythonToolExampleTests.cs"

EXPECTED = {
    "library": [
        "batch_folder.py",
        "convert.py",
        "coordinates_numpy.py",
        "list_layers.py",
        "make_drawing.py",
        "regions.py",
        "solids.py",
    ],
    "tools": ["drawing_tools", "number_points", "selection_lengths"],
    "remote": ["draw_and_read.py", "open_documents.py", "pick_and_mark.py"],
}
ON_DISK = sorted(EXAMPLES.rglob("*.py"))
GUARD = ast.unparse(ast.parse('if __name__ == "__main__":\n    sys.exit(main(sys.argv[1:]))'))


def _expected_paths() -> list[Path]:
    return sorted(
        EXAMPLES / place / name / "__init__.py" if place == "tools" else EXAMPLES / place / name
        for place, names in EXPECTED.items()
        for name in names
    )


def _name_of(path: Path) -> str:
    """The name an example goes by: its file, or for a tool the name of its folder."""
    return path.parent.name if path.name == "__init__.py" else path.name


def _tree(path: Path) -> ast.Module:
    assert path.is_file(), f"{path} does not exist: the example is not written"
    return ast.parse(path.read_text(encoding="utf-8"), filename=str(path))


def _function(tree: ast.Module, name: str) -> ast.FunctionDef | None:
    return next((n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == name), None)


def test_the_examples_are_the_thirteen_of_the_spec_each_in_its_place() -> None:
    assert [p.relative_to(EXAMPLES).as_posix() for p in ON_DISK] == [
        p.relative_to(EXAMPLES).as_posix() for p in _expected_paths()
    ]


def test_the_readme_table_names_every_example_and_no_other_file() -> None:
    readme = EXAMPLES / "README.md"
    assert readme.is_file(), f"{readme} does not exist"
    rows = [line for line in readme.read_text(encoding="utf-8").splitlines() if line.startswith("|")]
    named = {name for row in rows for name in re.findall(r"[A-Za-z0-9_]+\.py\b", row)}

    named |= {name for row in rows for name in re.findall(r"tools/([A-Za-z0-9_]+)/", row)}

    assert sorted(named) == sorted(_name_of(p) for p in ON_DISK)


def test_the_guide_page_includes_every_example_through_a_code_include_and_no_other_file() -> None:
    assert GUIDE.is_file(), f"{GUIDE} does not exist"
    included = re.findall(r"\[!code-python\[[^\]]*\]\(([^)\s]+)\)\]", GUIDE.read_text(encoding="utf-8"))

    assert sorted((GUIDE.parent / path).resolve() for path in included) == [p.resolve() for p in ON_DISK]


@pytest.mark.parametrize("path", _expected_paths(), ids=_name_of)
def test_every_example_file_name_is_in_a_test_file(path: Path) -> None:
    here = Path(__file__).resolve()
    tests = [p for p in (ROOT / "tests" / "python").rglob("*.py") if p.resolve() != here] + [CSHARP_TESTS]
    assert CSHARP_TESTS.is_file(), f"{CSHARP_TESTS} does not exist"

    assert any(_name_of(path) in test.read_text(encoding="utf-8") for test in tests)


# ---- the shape D1002 gives every example --------------------------------------------------------------------------


@pytest.mark.parametrize("path", _expected_paths(), ids=_name_of)
def test_an_example_is_utf_8_without_a_bom_with_line_feeds_and_lines_of_at_most_120_columns(path: Path) -> None:
    assert path.is_file(), f"{path} does not exist: the example is not written"
    data = path.read_bytes()

    assert not data.startswith(b"\xef\xbb\xbf")
    assert b"\r" not in data
    assert data.endswith(b"\n")
    assert [n for n, line in enumerate(data.decode("utf-8").splitlines(), 1) if len(line) > 120] == []


@pytest.mark.parametrize("path", _expected_paths(), ids=_name_of)
def test_an_example_opens_with_a_docstring_of_two_to_five_lines(path: Path) -> None:
    docstring = ast.get_docstring(_tree(path), clean=True)

    assert docstring is not None
    assert 2 <= len(docstring.strip().splitlines()) <= 5


@pytest.mark.parametrize("path", _expected_paths(), ids=_name_of)
def test_every_function_of_an_example_is_annotated(path: Path) -> None:
    unannotated = []
    for node in ast.walk(_tree(path)):
        if isinstance(node, ast.FunctionDef):
            arguments = node.args.posonlyargs + node.args.args + node.args.kwonlyargs
            missing = [a.arg for a in arguments if a.annotation is None]
            if node.args.vararg and node.args.vararg.annotation is None:
                missing.append("*" + node.args.vararg.arg)
            if node.returns is None:
                missing.append("return")
            if missing:
                unannotated.append(f"{node.name}: {missing}")

    assert unannotated == []


@pytest.mark.parametrize("name", EXPECTED["library"])
def test_a_library_example_has_main_of_argv_and_ends_with_the_exit_guard(name: str) -> None:
    tree = _tree(EXAMPLES / "library" / name)

    main = _function(tree, "main")
    assert main is not None
    assert [a.arg for a in main.args.args] == ["argv"]
    assert ast.unparse(main.args.args[0].annotation) == "list[str]"
    assert ast.unparse(main.returns) == "int"
    assert ast.unparse(tree.body[-1]) == GUARD


@pytest.mark.parametrize(
    ("name", "arguments", "returns"),
    [
        ("draw_and_read.py", ["app", "out"], "None"),
        ("pick_and_mark.py", ["app"], "int"),
        ("open_documents.py", ["app"], "None"),
    ],
)
def test_a_remote_example_has_a_main_that_takes_the_connection_and_a_guard_that_connects(
    name: str, arguments: list[str], returns: str
) -> None:
    tree = _tree(EXAMPLES / "remote" / name)

    main = _function(tree, "main")
    assert main is not None
    assert [a.arg for a in main.args.args] == arguments
    assert ast.unparse(main.args.args[0].annotation) == "CadApplication"
    assert ast.unparse(main.returns) == returns
    guards = [
        n
        for n in tree.body
        if isinstance(n, ast.If) and ast.unparse(n.test) in ("__name__ == '__main__'", '__name__ == "__main__"')
    ]
    assert len(guards) == 1
    assert "with CadApplication() as app:" in ast.unparse(guards[0])


def test_the_pick_example_has_its_time_limit_at_module_level() -> None:
    tree = _tree(EXAMPLES / "remote" / "pick_and_mark.py")

    assert any(
        isinstance(n, (ast.Assign, ast.AnnAssign))
        and "PICK_TIMEOUT" in ast.unparse(n)
        and "TimeSpan.FromSeconds(60)" in ast.unparse(n)
        for n in tree.body
    )


@pytest.mark.parametrize("name", EXPECTED["tools"])
def test_a_tool_example_has_no_main_and_is_a_folder_of_the_three_files_of_a_tool(name: str) -> None:
    folder = EXAMPLES / "tools" / name

    assert _function(_tree(folder / "__init__.py"), "main") is None
    assert sorted(p.name for p in folder.iterdir() if p.name != "__pycache__") == [
        "__init__.py",
        "plugin.json",
        "requirements.txt",
    ]
    assert (folder / "requirements.txt").read_text(encoding="utf-8").strip() == ""
