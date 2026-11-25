"""OpenCAD2D for Python: the app's .NET libraries (OpenCAD2D and its core library GeometryHelper), on pythonnet.

Importing this package starts a .NET runtime (unless one is already running) and loads GeometryHelper,
OpenCAD2D.Remote and OpenCAD2D from the ``lib`` folder beside this file, or from the folder the environment variable
``OPENCAD2D_LIB`` names (the app's own folder, for the Python tools it starts). After that the .NET names
import as they are written in C#::

    from GeometryHelper.Geometry import GeoPoint3
    from OpenCAD2D import CadDocument

The runtime is chosen by the environment variable ``OPENCAD2D_RUNTIME``: ``auto`` (also when unset), ``coreclr``
(.NET 8 or later) or ``netfx`` (.NET Framework 4.8).
"""

from __future__ import annotations

import contextlib
import importlib
import importlib.metadata
import os
import sys
from collections.abc import Generator
from pathlib import Path
from typing import TYPE_CHECKING, Literal

if TYPE_CHECKING:
    from OpenCAD2D import CadDocument

_DISTRIBUTION = "opencad2d"

_FOLDER = Path(__file__).resolve().parent
_ASSEMBLIES = ("GeometryHelper", "OpenCAD2D.Remote", "OpenCAD2D.Core")


def _has_dotnet8() -> bool:
    """Say whether a Microsoft.NETCore.App of major version 8 or more is installed; none at all, as on a clean Windows, is False."""
    import clr_loader

    try:
        runtimes = list(clr_loader.find_runtimes())
    except RuntimeError:
        # With no .NET installed, clr_loader raises "Can not determine dotnet root" instead of listing nothing.
        return False
    return any(info.name == "Microsoft.NETCore.App" and int(info.version.split(".")[0]) >= 8 for info in runtimes)


def _start_runtime() -> None:
    """Starts coreclr or netfx through pythonnet; does nothing when pythonnet's ``clr`` is already imported."""
    if "clr" in sys.modules:
        return
    given = os.environ.get("OPENCAD2D_RUNTIME", "")
    chosen = given.strip().lower()
    if chosen not in ("", "auto", "coreclr", "netfx"):
        raise ImportError(f"OPENCAD2D_RUNTIME is '{given}': use auto, coreclr or netfx.")
    if chosen in ("", "auto"):
        chosen = "coreclr" if _has_dotnet8() else "netfx"
    elif chosen == "coreclr" and not _has_dotnet8():
        raise ImportError(
            "OPENCAD2D_RUNTIME is coreclr but no .NET 8 or later is installed: install the .NET 8 runtime from "
            "https://dotnet.microsoft.com/download or set OPENCAD2D_RUNTIME=netfx."
        )
    pythonnet = importlib.import_module("pythonnet")

    if chosen == "netfx":
        pythonnet.load("netfx")
    else:
        pythonnet.load("coreclr", runtime_config=str(_FOLDER / "opencad2d.runtimeconfig.json"))


def _load_assemblies() -> None:
    """Adds each of our assemblies from ``OPENCAD2D_LIB`` where it is set, else from ``lib``, unless one of that name is
    already loaded in the AppDomain."""
    clr = importlib.import_module("clr")
    lib = Path(os.environ.get("OPENCAD2D_LIB", "").strip() or _FOLDER / "lib")
    app_domain = importlib.import_module("System").AppDomain
    loaded = {assembly.GetName().Name for assembly in app_domain.CurrentDomain.GetAssemblies()}
    for name in _ASSEMBLIES:
        if name not in loaded:
            clr.AddReference(str(lib / (name + ".dll")))


def _version() -> str:
    """Return the installed distribution's version, or inside the app, where there is none, that of the OpenCAD2D.Core assembly."""
    try:
        return importlib.metadata.version(_DISTRIBUTION)
    except importlib.metadata.PackageNotFoundError:
        for assembly in importlib.import_module("System").AppDomain.CurrentDomain.GetAssemblies():
            if assembly.GetName().Name == "OpenCAD2D.Core":
                version = assembly.GetName().Version
                return f"{version.Major}.{version.Minor}.{version.Build}"
        raise


_start_runtime()
_load_assemblies()
__version__: str = _version()


def runtime() -> Literal["coreclr", "netfx"]:
    """Say which .NET runtime the package runs on: ``netfx`` for .NET Framework, else ``coreclr``.

    It is true also when the runtime was started by someone else, such as the host application that embeds Python.
    """
    description = importlib.import_module("System.Runtime.InteropServices").RuntimeInformation.FrameworkDescription
    return "netfx" if str(description).startswith(".NET Framework") else "coreclr"


@contextlib.contextmanager
def undo_group(document: CadDocument, name: str) -> Generator[CadDocument, None, None]:
    """Make the edits of a ``with`` block one undo step of the document, or none.

    ``BeginGroup(name)`` runs on entry. ``EndGroup()`` runs when the block ends normally; ``CancelGroup()`` runs when
    it raises, and the exception goes on. A group left open by a forgotten ``EndGroup`` makes ``SaveAs`` throw.
    Groups nest, and a cancel takes back every open group: after an inner block that raised, the outer block has no
    group left to end or cancel, and does neither.
    """
    document.BeginGroup(name)
    try:
        yield document
    except BaseException:
        if document.IsInGroup:
            document.CancelGroup()
        raise
    else:
        if document.IsInGroup:
            document.EndGroup()


def __getattr__(name: str) -> object:
    """Give the names of ``opencad2d.cad`` from the package too: ``from opencad2d import command`` as ``from opencad2d.cad import command``.

    ``opencad2d.cad`` is imported at the first such name, not with the package. Type checkers know the names only in
    ``opencad2d.cad``, where they are written.
    """
    if not name.startswith("_"):
        cad = importlib.import_module(__name__ + ".cad")
        if hasattr(cad, name):
            return getattr(cad, name)
    raise AttributeError("module " + repr(__name__) + " has no attribute " + repr(name))
