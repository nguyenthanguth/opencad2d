"""PyInstaller hook of ``opencad2d``: what a frozen script needs that PyInstaller cannot see in the code.

The package starts .NET with ``importlib.import_module("pythonnet")`` and ``("clr")``, which the analysis of the imports does
not follow, so pythonnet and clr_loader (and the hooks they bring) are named here; and the .NET libraries it loads
(``lib``, the Fonts, Hatches and Linetypes folders) and ``opencad2d.runtimeconfig.json`` are data files that go beside the
package in the frozen folder, where ``opencad2d`` looks for them.
"""

from pathlib import Path

_PACKAGE = Path(__file__).resolve().parents[1]

hiddenimports: list[str] = ["pythonnet", "clr_loader", "clr"]

datas: list[tuple[str, str]] = [
    (str(path), str(Path("opencad2d") / path.relative_to(_PACKAGE).parent))
    for path in sorted(_PACKAGE.rglob("*"))
    if path.is_file()
    and path.suffix not in (".py", ".pyc")
    and "__pycache__" not in path.parts
    and "_pyinstaller" not in path.parts
]
