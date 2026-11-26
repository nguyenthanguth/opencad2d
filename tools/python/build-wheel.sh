#!/usr/bin/env bash
# Builds the opencad2d wheel into artifacts/python. Needs the Release build of src/OpenCAD2D.Core and
# src/OpenCAD2D.Rendering (the solution build) and the stubs (make-stubs.sh). Python: see find-python.sh.
set -eu

root=$(cd "$(dirname "$0")/../.." && { pwd -W 2>/dev/null || pwd; })
. "$root/tools/python/find-python.sh"

built=$root/src/OpenCAD2D.Core/bin/Release/netstandard2.0
rendered=$root/src/OpenCAD2D.Rendering/bin/Release/netstandard2.0
project=$root/src/python
lib=$project/opencad2d/lib
out=$root/artifacts/python

for name in GeometryHelper OpenCAD2D.Core OpenCAD2D.Remote; do
    if [ ! -f "$built/$name.dll" ]; then
        echo "$built/$name.dll is missing: build OpenCAD2D.slnx in Release first." >&2
        exit 1
    fi
done

# What the drawing with no window needs: OpenCAD2D.Rendering and SkiaSharp, and on .NET Framework the System.Memory family
# SkiaSharp is built against. The native SkiaSharp library is not in the bin folder of a netstandard2.0 project: it is the
# win-x64 file of the NuGet package the assets file of the project resolved, found there and not by a path of this machine.
rendering="OpenCAD2D.Rendering SkiaSharp System.Memory System.Buffers System.Numerics.Vectors System.Runtime.CompilerServices.Unsafe"
for name in $rendering; do
    if [ ! -f "$rendered/$name.dll" ]; then
        echo "$rendered/$name.dll is missing: build OpenCAD2D.slnx in Release first." >&2
        exit 1
    fi
done
native=$(timeout 60 "$GH_PYTHON" - "$root/src/OpenCAD2D.Rendering/obj/project.assets.json" <<'PY'
import json, os, sys

assets = json.load(open(sys.argv[1], encoding="utf-8"))
(target,) = assets["targets"].values()
package = next((key for key in target if key.startswith("SkiaSharp.NativeAssets.Win32/")), None)
if package is not None:
    name, version = package.split("/")
    for folder in assets["packageFolders"]:
        found = os.path.join(folder, name.lower(), version, "runtimes", "win-x64", "native", "libSkiaSharp.dll")
        if os.path.isfile(found):
            print(found.replace("\\", "/"))
            break
PY
)
if [ -z "$native" ]; then
    echo "libSkiaSharp.dll of SkiaSharp.NativeAssets.Win32 is not in the NuGet folders the assets file of OpenCAD2D.Rendering names: restore the solution." >&2
    exit 1
fi

# Only the DLLs and the three support folders: no XML docs and no PDBs.
rm -rf "$lib"
mkdir -p "$lib"
for name in GeometryHelper OpenCAD2D.Core OpenCAD2D.Remote; do
    cp "$built/$name.dll" "$lib/"
done
for name in $rendering; do
    cp "$rendered/$name.dll" "$lib/"
done
cp "$native" "$lib/libSkiaSharp.dll"
for folder in Fonts Hatches Linetypes; do
    cp -r "$built/$folder" "$lib/$folder"
done
cp "$root/LICENSE" "$project/LICENSE"

# `build` lives in its own venv, made once and kept; hatchling is fetched by the isolated build.
mkdir -p "$out"
rm -f "$out"/opencad2d-*.whl
if [ ! -x "$out/venv-build/Scripts/python.exe" ]; then
    rm -rf "$out/venv-build"
    timeout 300 "$GH_PYTHON" -m venv "$out/venv-build"
    timeout 600 "$out/venv-build/Scripts/python.exe" -m pip install --quiet --disable-pip-version-check "build==1.6.1"
fi
(cd "$project" && timeout 600 "$out/venv-build/Scripts/python.exe" -m build --wheel --outdir "$out")

wheel=$(ls "$out"/opencad2d-*.whl)
echo "Built $wheel ($(stat -c %s "$wheel") bytes)"
