"""Loads the drawing of OpenCAD2D with no window, at the first render call and not at ``import opencad2d``.

``OpenCAD2D.Rendering`` draws with SkiaSharp, whose native library is 13 MB and whose managed part needs, on .NET Framework,
the ``System.Memory`` family beside it. A script that never renders does not pay for them, and an import that failed for want of
SkiaSharp would break everything else. The files are in the ``lib`` folder of the wheel (or the folder ``OPENCAD2D_LIB``
names: the app's own, where the native library is in ``runtimes/win-x64/native``).
"""

from __future__ import annotations

import ctypes
import importlib
import os
from pathlib import Path
from typing import Any

from opencad2d import _FOLDER, runtime  # pyright: ignore[reportPrivateUsage]

_NATIVE = "libSkiaSharp.dll"
# On .NET Framework python.exe has no binding redirects, so these load first, in the order each needs the one before.
_NETFX_FIRST = (
    "System.Runtime.CompilerServices.Unsafe",
    "System.Buffers",
    "System.Numerics.Vectors",
    "System.Memory",
)

_loaded: Any = None


def load() -> Any:
    """Return the ``OpenCAD2D`` module with ``CadRender`` and ``CadRenderOptions`` in it, loading what they need the first time.

    ``ImportError`` names the file that is missing from the library folder.
    """
    global _loaded
    if _loaded is not None:
        return _loaded
    lib = Path(os.environ.get("OPENCAD2D_LIB", "").strip() or _FOLDER / "lib")
    # The native library is loaded by its full path: once a module of that name is in the process, SkiaSharp's DllImport binds to
    # it on both runtimes, where .NET Framework would look only beside python.exe, in System32 and on the PATH.
    native = next((p for p in (lib / _NATIVE, lib / "runtimes" / "win-x64" / "native" / _NATIVE) if p.is_file()), None)
    if native is None:
        raise ImportError(f"Rendering needs {_NATIVE}, which is not in {lib}: reinstall opencad2d.")
    ctypes.WinDLL(str(native))
    clr = importlib.import_module("clr")
    app_domain = importlib.import_module("System").AppDomain
    present = {assembly.GetName().Name for assembly in app_domain.CurrentDomain.GetAssemblies()}
    # The shared framework of .NET 8 has the System.Memory family; the copies in lib are for .NET Framework only.
    ours = ("SkiaSharp", "OpenCAD2D.Rendering")
    wanted = (*_NETFX_FIRST, *ours) if runtime() == "netfx" else ours
    for name in wanted:
        if name not in present:
            if not (lib / (name + ".dll")).is_file():
                raise ImportError(f"Rendering needs {name}.dll, which is not in {lib}: reinstall opencad2d.")
            clr.AddReference(str(lib / (name + ".dll")))
    _loaded = importlib.import_module("OpenCAD2D")
    return _loaded
