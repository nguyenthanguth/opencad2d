#!/bin/bash
# Writes the Python stubs of the library to src/python/GeometryHelper-stubs (git-ignored): runs the stub generator, built
# by the solution build, on the Release DLLs and XML docs of GeometryHelper, OpenCAD2D and
# OpenCAD2D.Remote. Build the solution in Release first:
#   dotnet build OpenCAD2D.slnx -c Release -warnaserror --no-incremental
# The summary the generator prints (types, members, modules, what was left out) goes to standard output; the exit code
# is the generator's.
set -eu

root=$(cd "$(dirname "$0")/../.." && pwd)
tool="$root/tools/OpenCAD2D.PyStubs/bin/Release/net8.0/OpenCAD2D.PyStubs.dll"
lib="$root/src/OpenCAD2D/bin/Release/netstandard2.0"

if [ ! -f "$tool" ]; then
    echo "The stub generator is not built: $tool" >&2
    echo "Run: dotnet build OpenCAD2D.slnx -c Release -warnaserror --no-incremental" >&2
    exit 1
fi
if [ ! -f "$lib/GeometryHelper.dll" ] || [ ! -f "$lib/OpenCAD2D.dll" ] || [ ! -f "$lib/OpenCAD2D.Remote.dll" ]; then
    echo "The Release libraries are not built in $lib" >&2
    echo "Run: dotnet build OpenCAD2D.slnx -c Release -warnaserror --no-incremental" >&2
    exit 1
fi

timeout 300 dotnet "$tool" --lib "$lib" --out "$root/src/python"
