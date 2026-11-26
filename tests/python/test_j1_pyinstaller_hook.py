"""PW4: a script that imports ``opencad2d`` freezes with PyInstaller and runs with no option given, since the package brings its
own hook (``opencad2d._pyinstaller``, the ``pyinstaller40`` entry point): pythonnet and clr_loader, which the package imports by
name at run time, and the .NET libraries and ``opencad2d.runtimeconfig.json`` beside the package, go into the frozen folder.

Skipped where PyInstaller is not installed (it is not a dependency of the package): ``pip install pyinstaller`` in the venv."""

import importlib.util
import subprocess
import sys
from pathlib import Path

import pytest

pytestmark = pytest.mark.skipif(importlib.util.find_spec("PyInstaller") is None, reason="PyInstaller is not installed")

SCRIPT = """\
import opencad2d
from GeometryHelper.Geometry import GeoPoint3

print("version", opencad2d.__version__)
print("x", GeoPoint3(1, 2, 3).X)
"""


def test_a_frozen_script_finds_pythonnet_and_the_dotnet_libraries(tmp_path: Path) -> None:
    script = tmp_path / "hello.py"
    script.write_text(SCRIPT, encoding="utf-8")

    built = subprocess.run(
        [sys.executable, "-m", "PyInstaller", "--noconfirm", "--onedir", "--distpath", str(tmp_path / "dist"),
         "--workpath", str(tmp_path / "build"), "--specpath", str(tmp_path / "spec"), str(script)],
        capture_output=True, text=True, timeout=900,
    )
    assert built.returncode == 0, built.stderr[-2000:]

    ran = subprocess.run([str(tmp_path / "dist" / "hello" / "hello.exe")], capture_output=True, text=True, timeout=120)

    assert ran.returncode == 0, ran.stderr[-2000:]
    assert ran.stdout.splitlines()[-1] == "x 1.0"
    assert (tmp_path / "dist" / "hello" / "_internal" / "opencad2d" / "lib" / "GeometryHelper.dll").is_file()
