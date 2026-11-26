"""The hook folder PyInstaller finds through the ``pyinstaller40`` entry point of the package: it freezes a script that imports ``opencad2d``."""

from __future__ import annotations

from pathlib import Path

__all__ = ["get_hook_dirs"]


def get_hook_dirs() -> list[str]:
    """Give the folders of the hooks the package brings, which PyInstaller reads before it freezes a script that imports ``opencad2d``."""
    return [str(Path(__file__).resolve().parent)]
