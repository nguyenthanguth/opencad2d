# Python

The library and the presenter from Python: geometry, DXF and DWG read and written, and a
[`CadDocument`](xref:OpenCAD2D.CadDocument) edited through its command session and saved, with no window. It is
the .NET library itself, run by [pythonnet](https://pythonnet.github.io/), in a package named `opencad2d`. The .NET
names stay as they are, so a script reads like the C# of the other pages; the package adds only the few Python helpers
that pythonnet makes slow or easy to get wrong, and type stubs, so that an editor completes and checks every name.

The package is not on PyPI yet. It is a wheel built from the repository ([Installing](#installing)), and the name it
will be published under is not chosen: `opencad2d` is taken on PyPI by an unrelated project, so a wheel called so
can never be uploaded. The name to `import` stays `opencad2d` whichever name is chosen.

Nothing is converted and no tolerance is chosen for you. Lengths are the library's own, in millimetres when the
drawing is, and a script passes a [`Tolerance`](xref:GeometryHelper.Tolerance) where the C# would.

## What it needs

| | |
|---|---|
| System | Windows, 64-bit. The two assemblies of the presenter are built for x64 and .NET Framework is Windows only, so the wheel is tagged `py3-none-win_amd64` and `pip` on any other system refuses it |
| Python | CPython 3.11 to 3.15, 64-bit (pythonnet 3.2.1's range) |
| .NET | the .NET 8 runtime or later, or .NET Framework 4.8; either runs the same script ([Choosing the runtime](#choosing-the-runtime)) |
| Installed with it | `pythonnet` 3.2.1 or later (below 4); numpy 1.26 or later when the `numpy` extra is asked for |

Linux, macOS and Windows on ARM are not supported: nothing has been run there.

## Installing

Build the wheel from a checkout of the repository, in Git Bash, from its root. The first two lines build the libraries
and the stub generator, the last two write the stubs and the wheel:

```bash
dotnet build src/OpenCAD2D.Core/OpenCAD2D.Core.csproj -c Release
dotnet build tools/OpenCAD2D.PyStubs/OpenCAD2D.PyStubs.csproj -c Release
bash tools/python/make-stubs.sh
bash tools/python/build-wheel.sh
```

The wheel is `artifacts/python/opencad2d-2.0.0-py3-none-win_amd64.whl` (the version is OpenCAD2D's, the one
`Directory.Build.props` carries, so the app and a wheel of one number hold the same code). Install it into a
virtual environment, with the `numpy` extra if you want [the array helpers](#numpy-in-blocks):

```
python -m venv .venv
.venv\Scripts\python -m pip install "artifacts\python\opencad2d-2.0.0-py3-none-win_amd64.whl[numpy]"
```

The wheel is 16 404 573 bytes. It holds the three assemblies (`GeometryHelper.dll`, `OpenCAD2D.Core.dll` and
`OpenCAD2D.Remote.dll`) and the seven files that draw with no window ([Rendering without a window](#rendering-without-a-window):
`OpenCAD2D.Rendering.dll`, `SkiaSharp.dll`, the native `libSkiaSharp.dll` and four `System.*` libraries for .NET Framework), with the
`Fonts`, `Hatches` and `Linetypes` folders the presenter finds beside them (16 files, 30 256 970 bytes unpacked), the Python module
(13 files, 226 602 bytes), and the stubs (14 files, 3 478 850 bytes for `GeometryHelper`, 5 files, 1 192 157 bytes for `OpenCAD2D`,
8 files, 9 162 bytes for `System`). It holds no XML documentation and no PDB: the XML is read once, when the stubs are written.

`bash tools/python/verify-python.sh` does all of it, then makes a fresh virtual environment, installs the wheel there and
runs `tests/python` against it, once on each runtime; the `python` job of `.github/workflows/build.yml` runs the same
script on Python 3.11 and 3.14, and nothing else of Python: the app no longer embeds it. On the test machine, 1 283 tests pass on .NET 8 (191 s) with 1 skipped (PyInstaller is not installed there), and 1 240 on .NET Framework 4.8 (161 s) with 44 skipped: the tests of a tool's process, which always runs on .NET 8, and that one.

## A first script

```python
import opencad2d
from OpenCAD2D import CadCommandSession, CadDocument
from GeometryHelper.Dxf import DxfDatabase, DxfWriter
from GeometryHelper.Geometry import GeoPoint3

print(opencad2d.__version__, opencad2d.runtime())      # 2.0.0 coreclr
print(GeoPoint3(0, 0, 0).DistanceTo(GeoPoint3(3, 4, 0)))         # 5.0

document = CadDocument.From(DxfDatabase.Parse(DxfWriter().ToString()))   # an empty drawing
session = CadCommandSession(document)                                     # its command line
with opencad2d.undo_group(document, "A circle"):                     # one undo step
    for answer in ("CIRCLE", "0,0", "25"):                                # what a user types
        session.Feed(answer)
print(document.EntityCount)                                               # 1

document.SaveAs("circle.dwg")                                    # DWG, by the extension; .dxf writes a DXF
print(CadDocument.Open("circle.dwg").EntityCount)                # 1
```

`import opencad2d` comes first: it starts .NET and loads the three assemblies, and only after it can
`from GeometryHelper.Geometry import ...` find anything (before it, the import fails with `ModuleNotFoundError: No module
named 'GeometryHelper'`). The drawing is read, edited and saved as the pages on [DXF](https://github.com/nguyenthanguth/GeometryHelper/blob/main/src/GeometryHelper/docs/dxf/index.md),
[DWG](https://github.com/nguyenthanguth/GeometryHelper/blob/main/src/GeometryHelper/docs/dxf/dwg.md) and [the 2D presenter](presenter/index.md) describe; this page is about what differs from C#.

`import opencad2d` takes 498 to 512 ms on .NET 8 and 562 to 589 ms on .NET Framework 4.8 (three runs of the whole
process, which includes starting Python: a bare `python -c pass` takes 56 ms). A script that reads a DXF and a DWG, draws,
edits, saves both and reads geometry into numpy takes 927 ms on .NET 8 and 1 257 ms on .NET Framework 4.8, whole process.

`opencad2d.__version__` is the version of the library, and `opencad2d.runtime()` is `"coreclr"` or `"netfx"`,
the runtime the script runs on: what a bug report needs.

## Choosing the runtime

The environment variable `OPENCAD2D_RUNTIME` says which .NET runs the script. It is read once, when
`opencad2d` is imported, and its case is ignored:

| Value | Runs on |
|---|---|
| unset, empty or `auto` | .NET 8 or later when one is installed, else .NET Framework 4.8 |
| `coreclr` | .NET 8 or later; an `ImportError` when none is installed |
| `netfx` | .NET Framework 4.8 |

With no .NET installed at all, `auto` is .NET Framework 4.8 and `coreclr` is the same `ImportError`.

Any other value is an `ImportError`: `OPENCAD2D_RUNTIME is 'bogus': use auto, coreclr or netfx.` With `coreclr` and
no .NET 8, it says `OPENCAD2D_RUNTIME is coreclr but no .NET 8 or later is installed: install the .NET 8 runtime from
https://dotnet.microsoft.com/download or set OPENCAD2D_RUNTIME=netfx.` The package does not read `PYTHONNET_RUNTIME`.

On .NET the package asks for 8.0 and lets the runtime roll forward to the lowest major installed from 8 up, at its latest
patch. On a machine with 8.0.11, 8.0.28, 9.0.17, 10.0.0, 10.0.9 and 10.0.13 that is 8.0.28. To run on the newest
installed instead, set the .NET variable `DOTNET_ROLL_FORWARD=LatestMajor`: the same machine then runs 10.0.13, because
that variable overrides the package's own setting.

When .NET is already running (the program called `pythonnet.load` itself, or it embeds Python and has `clr` imported),
the package starts nothing and uses it, so `OPENCAD2D_RUNTIME` is not read. Of its three assemblies, one whose name is
already loaded is used as it is, not loaded a second time, so a host's own copy stays the only one.

## The .NET names stay as they are

A script imports the .NET namespaces as they are written in C#, and uses members as C# does:

```python
import opencad2d
from GeometryHelper.Geometry import GeoPoint3, GeoPolygon2, GeoPoint2

p = GeoPoint3(x=1, y=2, z=3)           # arguments by their .NET names work
q = GeoPoint3(4, 6, 3)
print(q - p, (q - p).Length)           # [3, 4, 0] 5.0: operators work, a property takes no parentheses
print(p.DistanceTo(q))                 # 5.0
print(GeoPolygon2(GeoPoint2(0, 0), GeoPoint2(5, 0), GeoPoint2(5, 4)).Area)   # 10.0
```

The namespaces are `GeometryHelper`, `GeometryHelper.Geometry`, `.Dxf`, `.Dwg`, `.Cad`, `.Cad.Remote` and the others of
the [API reference](xref:GeometryHelper). A few things differ from C# at run time:

- A struct has no parameterless constructor: `GeoPoint3()` is a `TypeError`. Write `GeoPoint3(0, 0, 0)`.
- `str(p)` is the library's `ToString()`, `(1, 2, 3)`; `repr(p)` is the generic one. `GeoPoint3.DistanceTo.__doc__` lists
  the overload signatures and nothing else: pythonnet does not read the XML documentation, which is why the stubs carry it
  ([Stubs and an editor](#stubs-and-an-editor)).
- An enum value is a .NET object: `int(CadSupportKind.Fonts)` is 0 and `str(CadSupportKind.Fonts)` is `Fonts`, but
  `CadSupportKind.Hatches == 1` is False. Compare enum with enum, or take `int(...)` of both.
- Exceptions are the .NET ones, with the .NET message and stack trace in `str(error)`.

## What pythonnet does with .NET

Seven things differ from what a Python reader expects. Each was measured on .NET 8 and on .NET Framework 4.8, with the same
result, and the stubs are written to say them: a call that pythonnet would refuse is an error in the editor.

### Out parameters come back in a tuple

A .NET method with an `out` parameter returns a tuple of its result and the out values, and the out parameters are left
out of the call:

```python
from GeometryHelper.Geometry import GeoLine2, GeoPoint2

crossing = GeoLine2(GeoPoint2(0, 0), GeoPoint2(10, 0))
ok, point = crossing.TryIntersectWith(GeoLine2(GeoPoint2(5, -5), GeoPoint2(5, 5)))   # True, (5, 0)
ok, point = crossing.TryIntersectWith(GeoLine2(GeoPoint2(20, -5), GeoPoint2(20, 5))) # False, (0, 0)
```

When the call fails the out value is the default of its type, as in C# (the point above is `(0, 0)`), so test the first
item before using the others.

An `out` parameter that a parameter of the call follows, as in `GeoSolid3.TrySubtract(tool, out result, tolerance)`, is
left out only when the parameters after it are given by keyword: `plate.TrySubtract(cutter, tolerance=tolerance)` works,
and `plate.TrySubtract(cutter, tolerance)` is a `TypeError` (`No method matches given arguments`), where C# has no
choice. The stubs write those parameters after a `*`, so the editor says so. 876 members have such a parameter, 864 of
them followed by a `tolerance`, and none has a `ref` parameter or a params array after it. Passing a placeholder in
place of the out argument works as well (`None` for a class, `GeoPoint2()` for a struct, `0.0` for a `double`), but the
stubs do not promise it.

Some overloads differ from a shorter one only by out parameters, such as the `TrySubtract` that also says how the cut came
out. Both are the same call, `plate.TrySubtract(cutter)`, and with the out arguments left out pythonnet binds the one
with the fewest parameters (whichever is declared first), so the call gives the two-item tuple. The longer overload is
reached by naming one of its own out parameters, with a value nobody reads:

```python
from GeometryHelper.Enums import BooleanOutcome

made, left = plate.TrySubtract(cutter)                                           # two items
made, left, outcome = plate.TrySubtract(cutter, outcome=BooleanOutcome.Made)     # three; outcome == BooleanOutcome.Made
```

The value is one of the enum for an enum or a struct, since `outcome=None` and `outcome=0` quietly bind the shorter overload
again, and `None` for a class or an array (`polyline.TrySplitAtDistance(5.0, second=None)` gives `(bool, first, second)`
where `polyline.TrySplitAtDistance(5.0)` gives `(bool, pieces)`). The stubs write the longer overload with that out
parameter as a required keyword, so the editor gives the tuple that runs. 26 overloads are such: `TryIntersect`,
`TryUnion` and `TrySubtract` of `GeoSolid3` and of `Boolean3` (the keyword `outcome`), `TrySplitBy` by a plane of `GeoLine3`
and `GeoPolyline3` (`above` and `below`), `TrySplitAtDistance` of the four polylines (`first` and `second`), and
`SplitAtDistances` of `GeoPolylineArc3` (the array alone, or the flag and the array through `pieces`), each with and
without a `tolerance`.

### Lists: only for arrays

A Python `list` or `tuple` is accepted where the .NET parameter is an array (`T[]`), and where it is a `params T[]`, which
also takes separate arguments. Nowhere else: not for `IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`,
`IList<T>`, `ICollection<T>`, `List<T>` or `ISet<T>`, and a generator is accepted nowhere (make a `list` of it first).

```python
from System import Array
from System.Collections.Generic import List
from GeometryHelper.Geometry import GeoPoint2, GeoPolygon2, GeoRectangle2

a, b, c = GeoPoint2(0, 0), GeoPoint2(5, 0), GeoPoint2(5, 4)
GeoPolygon2(a, b, c).Area              # 10.0: a params array takes separate arguments
GeoPolygon2([a, b, c]).Area            # 10.0: or a list

GeoRectangle2.Fit([a, b, c])           # TypeError: No method matches given arguments for GeoRectangle2.Fit: (<class 'list'>)
GeoRectangle2.Fit(Array[GeoPoint2]([a, b, c])).Area    # 20.0: Fit takes an IEnumerable<GeoPoint2>

points = List[GeoPoint2]()             # List[GeoPoint2]([a, b, c]) is refused too
for point in (a, b, c):
    points.Add(point)
GeoRectangle2.Fit(points).Area         # 20.0
```

So for a collection interface, build an `Array[T]` (it takes a Python list) or a `List[T]` (add one by one). A .NET array
is accepted for `IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>` and `IList<T>`, and a `List[T]` for all of
them; an array is not accepted for a parameter of type `List<T>`. A nested `IEnumerable<IEnumerable<T>>` takes no Python
list of lists either.

Ten members take a `params` array, among them the constructors of `GeoPolygon2`, `GeoPolygon3`, `GeoPolyline2`,
`GeoPolyline3` and `GeoSolid3`. The stubs show each as an overload with a list and an overload with `*points`.

### `len` and `.Count`

What `len()` works on depends on the .NET type the object is wrapped as, and a list a method returns is wrapped as the
interface it declares:

| Object | `len(x)` | `x.Count` | `x[-1]` |
|---|---|---|---|
| a .NET array, `Array[GeoPoint2]` | works | no | the last item |
| a `List[T]`, built or returned | works | works | `ArgumentOutOfRangeException` |
| an `IReadOnlyList[T]`, such as `GeoPolygon2.Vertices` | `TypeError` | works | `ArgumentOutOfRangeException` |
| a class that only iterates, such as a drawing's model space | `TypeError` | works on the model space, not on every such class | not subscriptable |

An `IReadOnlyList` has an index from 0, iteration, `in` and `.Count`, and no slice. A negative index is not
`IndexError` but a .NET exception. Where you need a Python list, `list(polygon.Vertices)` is one.

```python
vertices = GeoPolygon2(GeoPoint2(0, 0), GeoPoint2(5, 0), GeoPoint2(5, 4)).Vertices
vertices.Count            # 3
len(vertices)             # TypeError: object of type 'IReadOnlyList[GeoPoint2]' has no len()
vertices[2]               # (5, 4)
```

### Delegates are built, events are not

A parameter or a property of a delegate type (`Action<...>`, `Func<...>` and the delegates of the plugins) does not take a
bare Python function. Build the delegate from it:

```python
import System
from System import Action
from GeometryHelper import GeometryHelperLog
from GeometryHelper.Enums import GeometryHelperLogLevel

messages: list[tuple[str, str]] = []

def write(level: GeometryHelperLogLevel, text: str, error: System.Exception) -> None:
    messages.append((str(level), text))

GeometryHelperLog.Writer = lambda level, text, error: None      # TypeError: 'function' value cannot be converted ...
GeometryHelperLog.Writer = Action[GeometryHelperLogLevel, str, System.Exception](write)   # works
GeometryHelperLog.Warn("hello")                                 # messages == [("Warn", "hello")]
```

Assigning `None` puts the default writer back, and the stubs say so. `None` is `null` to .NET, and any reference-type
property takes it, but the .NET assemblies carry no nullable annotations to say which ones the library means to take it.
The stubs go by two things: a property, field or static of a delegate type, and a property whose summary says null, as
`CadObjectFilter.Layer` does ("null for every layer"): `selection.Layer = None` is "every layer". A property whose comment
says it throws `ArgumentNullException`, such as `DxfDatabase.Cmaterial` ("null where the header names none"), does not
take `None` when it is set, whatever its summary says of what it gives. That makes 106 members, 10 of them delegates; the
other 331 settable reference-type properties accept `None` at run time or not as their code says, and the editor reports
an assignment of `None` to them. Only the value that is set takes `| None`: what a property gives back is typed as before.

An event is the exception: `+=` takes a plain function, with the .NET handler's two arguments, and `-=` takes it away:

```python
def on_changed(sender, args):
    print(args.Kind, args.StepName)

document.Changed += on_changed          # one CIRCLE command prints: Edit CIRCLE
document.Changed -= on_changed
```

Assigning a static property works as in C#: `GeometryHelperLog.Enable = False`. The library's settings are shared by the
whole process, here as there; `Tolerance.Global` is one, so a script that sets it sets it for everything it imports.

### Members named `None`, `True` or `False`

Python reads `DxfEndCap.None` as a syntax error, though the library has such a member. Read it with `getattr`:

```python
from GeometryHelper.Dxf import DxfEndCap

no_cap = getattr(DxfEndCap, "None")      # int(no_cap) is 0; int(DxfEndCap.Round) is 1
```

The library has 33 members so named: 29 `None`, 2 `True` and 2 `False`. The stubs leave them out and the generator lists
them, so an editor does not offer them and a `getattr` returns `Any`. A parameter named `from`, `with` or `except` (14
of them) is written `from_` in the stubs and made positional-only, because that is the way pythonnet takes it:
`line.ToLine(0.0, 10.0)` works, `line.ToLine(from_=0.0, to=10.0)` is a `TypeError`, and the editor says so first.

### Flags operators on `[Flags]` enums only

The 14 `[Flags]` enums combine with `|`, `&`, `^` and `~`, each between values of the same enum:

```python
from GeometryHelper.Dxf import DxfEraseFlags

both = DxfEraseFlags.ZeroLengthCurve | DxfEraseFlags.EmptyText      # str(both): ZeroLengthCurve, EmptyText
int(both)                                                           # 3
(both & DxfEraseFlags.ZeroLengthCurve) == DxfEraseFlags.ZeroLengthCurve   # True
int(~DxfEraseFlags.ZeroLengthCurve)                                 # -2
database.CountEmptyObjects(int(both))                               # a method that takes the number, not the enum
```

Two traps. `bool(a & b)` is True even when `a & b` is the zero value, so test `int(a & b) != 0`. And `|` on an enum that is
not `[Flags]`, such as `DxfEndCap.Round | DxfEndCap.Square`, does not raise a `TypeError`: pythonnet recurses until it
raises `RecursionError`. The stubs give the operators to the `[Flags]` enums only, so an editor reports the second case
before it runs. A flag and a plain number do not combine either: `DxfEraseFlags.ZeroLengthCurve | 1` is refused at run time
and in the editor alike, so combine the named values.

### An enum from a number

An enum value is made from a whole number as in C#, `DxfEraseFlags(3)` (the same as the two named values joined) and
`DxfEndCap(1)` (`DxfEndCap.Round`), and the stubs give every enum that one constructor. A `[Flags]` enum takes any number,
`DxfEraseFlags(1 << 20)` included; a plain enum refuses one it has no member for, `DxfEndCap(99)` is a `ValueError`, and
`DxfEndCap(99, True)` gives the value all the same, a second argument the stubs leave out. A fraction, a string, `None` and
no argument are a `TypeError`, and a number past 32 bits an `OverflowError`. All 181 enums of the library are `Int32` ones.

## numpy in blocks

Reading a point array from Python one property at a time, `p.X`, `p.Y`, `p.Z` of each element, crosses into .NET three
times a point. `opencad2d.arrays` copies a whole array in one block instead: it pins the .NET array and copies it
with one `memmove`. It needs numpy (`pip install "<wheel>[numpy]"`); without it `import opencad2d.arrays` is an
`ImportError`: `opencad2d.arrays needs numpy: pip install opencad2d[numpy]`.

| Function | Takes | Gives |
|---|---|---|
| `to_numpy(values)` | a .NET array, or an `IEnumerable<T>`, of `Double`, `GeoPoint2` or `GeoPoint3` | a new numpy array, float64: shape `(n,)`, `(n, 2)` or `(n, 3)` |
| `to_doubles(array)` | a 1-D array-like, any length, 0 included | an `Array[Double]` |
| `to_points2(array)` | an array-like of shape `(n, 2)` | an `Array[GeoPoint2]` |
| `to_points3(array)` | an array-like of shape `(n, 3)` | an `Array[GeoPoint3]` |

Every copy is bit for bit the source. Another shape is a `ValueError` naming the shape expected and the one given
(`to_points3 expects shape (n, 3), got (4, 2).`), and `to_numpy` of a Python list or of another element type is a
`TypeError` naming what it takes. NaN and infinities are copied as they are; the library decides what to do with them.
A collection that is not an array, such as `GeoPolygon2.Vertices`, goes through one `Enumerable.ToArray` call first.

```python
import numpy as np
from opencad2d.arrays import to_numpy, to_points2
from GeometryHelper.Geometry import GeoPolygon2

rectangle = np.array([[0.0, 0.0], [40.0, 0.0], [40.0, 30.0], [0.0, 30.0]])    # 40 by 30
polygon = GeoPolygon2(to_points2(rectangle))      # an Array[GeoPoint2], so no list of points is built by hand
polygon.Area                                      # 1200.0
corners = to_numpy(polygon.Vertices)              # shape (4, 2), equal to `rectangle`
```

For 100 000 points, on .NET 8 and on .NET Framework 4.8:

| Step | .NET 8 | .NET Framework 4.8 |
|---|---|---|
| numpy `(n, 3)` to `GeoPoint3[]` | 3.1 ms | 4.3 ms |
| `GeoPoint3[]` to numpy | 0.8 ms | 0.4 ms |
| `GeoPolygon2.Vertices` to numpy (`ToArray`, then the copy) | 3.2 ms | 4.4 ms |
| `GeoPoint3[]` to numpy, reading `X`, `Y` and `Z` of each point | 283 ms | 202 ms |

The block copy of `GeoPoint3[]` to numpy is 350 to 500 times faster than reading the properties. It rests on `GeoPoint2` and `GeoPoint3` being structs of exactly two and three
doubles (16 and 24 bytes on both runtimes); a test pins that, and there is no fallback if it ever changed. Vectors are not
offered: nobody has asked for them.

## Undo groups

An edit of a [`CadDocument`](xref:OpenCAD2D.CadDocument) made through its command session is one undo step per
command. `BeginGroup(name)` and `EndGroup()` make several of them one; `CancelGroup()` takes the group back. The pair has to
stay balanced: a group left open by an exception makes the next `SaveAs` throw `InvalidOperationException: A command is
running: end it before saving.` `opencad2d.undo_group` keeps it balanced:

```python
from opencad2d import undo_group

with undo_group(document, "Two circles"):
    for answer in ("CIRCLE", "0,0", "25", "CIRCLE", "100,0", "10"):
        session.Feed(answer)
document.EntityCount        # 2
document.Undo()             # True: both circles go in one step
document.EntityCount        # 0
```

`BeginGroup(name)` runs on entry. `EndGroup()` runs when the block ends normally. `CancelGroup()` runs when it raises, the
edits made in the block are taken back, and the exception goes on: in a drawing of one circle, a block that fed a
second one and then raised `ValueError("boom")` leaves one circle, and the `ValueError` reaches the caller. The block
receives the document itself (`with undo_group(document, "name") as target:`).

Groups nest, and the whole stack is one undo step. A cancel takes back every open group, so after an inner block that
raised, the outer block finds none left and neither ends nor cancels one: the first exception is the one that arrives.

`drawing.save()` and `drawing.save_as(path)` inside a group are the exception to the `SaveAs` that throws: the Drawing API
saves through `CadDocument.SavingInGroup(save)`, which closes the group for the save and opens it again, so that a block of
two circles, a `save()` and a third circle is still one undo step, and one `Undo()` takes back all three. The file holds the
two circles; the drawing is modified, for the third is not in it. A block that raises after a `save()` takes back only what
followed it, since the file cannot be taken back.

## The Drawing API

A drawing of a file is read and changed through a `Drawing`, the same one a command of a Python tool uses in the app and
`connect()` gives from outside ([Python commands and tools](presenter/python.md#the-drawing)). `open_file(path)` reads a DXF or
a DWG and `new_file()` makes a blank drawing of AutoCAD 2013 with layer 0 only, with no app and no window:

```python
from opencad2d.cad import new_file, open_file

drawing = new_file()                                   # drawing.name is 'Drawing'
drawing.layers.add("Steel", color=1)
drawing.add_line((0, 0), (100, 0), layer="Steel")
drawing.add_circle((50, 50), 25)
drawing.add_polyline([(0, 0), (100, 0), (100, 50)], closed=True)
drawing.commit()                                       # ['101', '102', '103']: three objects, one undo step
drawing.save_as("plan.dxf")                            # a DXF of the drawing's own version (AC1027)

again = open_file("plan.dxf")
len(again.entities())                                  # 3
again.entities("LWPOLYLINE")[0].net.Area               # 2500.0, the triangle
```

An `Entity` is a copy with a `handle`, a `dxf_type` (`"LINE"`, `"CIRCLE"`, `"LWPOLYLINE"`, the `DxfName` of its `net`) and the `DxfEntity` as `net`; after
changing `net`, `drawing.modify(entity)` and `drawing.commit()` write it back. The numbers above came from a run of the built
wheel on .NET 8 (`new_file`, the three adds, `commit`, `save_as`, `open_file`). `pick_point` and the other picks need somebody
to ask, so on a drawing of a file they raise `RuntimeError`. `make_drawing.py` and `list_layers.py` of
[Python examples](python-examples.md) are written this way.

A `Drawing` of the app or of a running app also moves the view and reads it as a `Box` (`zoom_extents`, `zoom_window`, `pan`,
`view`...), keeps named views, takes objects by an area (`select_window`, `select_crossing`, `select_all`) and gets and sets
system variables (`get_variable`, `set_variable`); on a drawing of a file the view calls raise `RuntimeError`, since a file
has no view, and the selections and the variables work ([the view, selection and variables](presenter/python.md#the-view-selection-and-variables)).

A `Drawing` also edits, with the commands' own code: `move`, `copy`, `rotate`, `scale`, `mirror` and `offset` take the `Entity`
objects a script read and give back `Entity` objects of the same class (`drawing.copy([circle], (0, 50))` is a
`list[Entity[DxfCircle]]`), one undo step with what the script had queued; a refused edit applies nothing
([edits](presenter/python.md#edits-move-copy-rotate-scale-mirror-offset)).

### Styles, linetypes, dimensions, hatches and blocks

A sheet needs more than lines and circles: text in a style of its own, hidden lines, dimensions drawn in a dimension style, hatches and
blocks. The `Drawing` makes all of them, queued like the layers and applied by `commit()` as one undo step, the same on a file, in the
app and from outside. There is no need to open the `GeometryHelper.Dxf` database of `drawing.net` for them, and an edit made there
is not in the undo step ([below](#going-below-the-calls)).

Records come first. `text_styles.add(name, font, x_scale, height)` and `dimension_styles.add(name, text_style, scale, text_height,
arrow_size, **variables)` queue a style (the dimension style starts from ISO-25's variables, as the Dimension Style Manager's New does,
and `Dimdec=1` or any other variable of the library sets one more), and `linetypes.load(name, file=None)` queues a linetype of the
built-in set (`OpenCAD2D.lin`: DASHED, HIDDEN, CENTER, PHANTOM, DOT, DASHDOT, DIVIDE, BORDER, each with its `2` and `X2`, GAS_LINE
and the ISO ones) or of a `.lin` file of yours, in the units of the drawing. They are written first in the commit, so a layer or an
object of the same commit can name them:

```python
import math

from opencad2d.cad import new_file
from GeometryHelper.Dxf import DxfCircle, DxfLine
from GeometryHelper.Geometry import GeoPoint3

drawing = new_file()
drawing.text_styles.add("NOTE", font="romans.shx", x_scale=0.8)               # 'NOTE'
drawing.linetypes.load("hidden")                                              # 'HIDDEN', as the set spells it
drawing.layers.add("Hidden", color=8, linetype="HIDDEN")
drawing.dimension_styles.add("PLAN", text_style="NOTE", scale=50, text_height=3.5, arrow_size=2.5, Dimdec=1)
drawing.commit()
[s.Name for s in drawing.text_styles]          # ['NOTE', 'Standard']
[s.Name for s in drawing.dimension_styles]     # ['ISO-25', 'PLAN', 'Standard']
list(drawing.linetypes)                        # ['ByBlock', 'ByLayer', 'Continuous', 'HIDDEN']
```

A style or linetype the drawing has already is refused at `commit()` for `add` (`RuntimeError: The text style "NOTE" already exists.`);
`load` leaves a linetype the drawing has as it is, as LINETYPE does. A name that an object or a dimension style names and nobody has
is a `ValueError` naming the call that adds it (`The text style "Nowhere" is not in the drawing: add it with text_styles.add
first.`). `current_text_style` and `current_dim_style` read and set the style new text and new dimensions take; they act at once, not
at `commit()`, so a style is current after the commit that made it.

Text and lines take the style and the linetype by name; `add_text` and `add_mtext` take `text_style`, and any object takes
`linetype=` in its props:

```python
drawing.add_text("Plate PL10", (0, -20), 5.0, text_style="NOTE")
drawing.add_mtext("Plate PL10, grade S355", (0, -40), 5.0, width=80, text_style="NOTE")
drawing.add_line((0, 0), (200, 0), layer="Hidden")
drawing.add_line((0, 10), (200, 10), linetype="HIDDEN")
len(drawing.commit())                          # 4
```

The dimension calls draw the picture as DIM does: a dimension is a `DxfDimension` with its `*D` block, which the app draws when
`commit()` adds it, in the style named with `dim_style` or else the current one. `add_linear_dim(first, second, location, angle,
text)` (level or upright from where `location` lies, as DIMLINEAR chooses, or `angle` in radians), `add_aligned_dim(first, second,
location, text)`, `add_radial_dim(center, radius, location, text)`, `add_diameter_dim(center, radius, location, text)` and
`add_angular_dim(vertex, first, second, location, text)`; `text` replaces the measurement, `"<>"` standing for it:

```python
drawing.current_dim_style = "PLAN"
drawing.add_linear_dim((0, 0), (200, 0), (100, 30))
drawing.add_aligned_dim((0, 0), (150, 100), (60, 120))
drawing.add_radial_dim((300, 50), 25, (330, 80))
drawing.add_diameter_dim((300, 50), 25, (270, 80))
drawing.add_angular_dim((0, 0), (200, 0), (150, 100), (100, 40))
len(drawing.commit())                          # 5
dim = drawing.entities("DIMENSION")[0]
dim.net.DimensionStyleName, dim.net.Measurement   # ('PLAN', 200.0)
```

`add_hatch(boundary, pattern, scale, angle)` hatches an area with a pattern of the built-in set (`OpenCAD2D.pat`: ANSI31 to ANSI38,
BRICK, EARTH, HONEY, STEEL, ISO02W100 to ISO15W100, AR-CONC and the rest, or `"SOLID"`) at a scale and an angle in radians. The
boundary is the corners of a closed outline, or a closed polyline or a circle (an `Entity` just added will do), or a list of them
whose first is the outer boundary and the rest islands. The hatch holds the loops as they are; it does not follow the objects after
that:

```python
drawing.add_hatch([(400, 0), (500, 0), (500, 60), (400, 60)], pattern="ANSI31", scale=2.0, angle=math.pi / 4)
outer = drawing.add_polyline([(600, 0), (700, 0), (700, 100), (600, 100)], closed=True)
hole = drawing.add_circle((650, 50), 20)
drawing.add_hatch([outer, hole], pattern="EARTH")
drawing.add_hatch([(800, 0), (840, 0), (820, 30)], pattern="SOLID", color=1)
len(drawing.commit())                          # 5
[(h.net.PatternName, round(h.net.Area, 1)) for h in drawing.entities("HATCH")]   # [('ANSI31', 6000.0), ('EARTH', 8743.4), ('SOLID', 600.0)]
```

`blocks.add(name, entities, base_point)` queues a block made of copies of the objects given (`GeometryHelper.Dxf` entities or
`Entity` objects; they are not added to the drawing by this), and `add_insert(name, insert, scale, rotation)` places a block the
drawing has or one made in the same commit. A commit that makes the styles, the linetype, the block and the objects that use them
is one undo step, and one `Undo()` takes all of it back:

```python
bolt = DxfCircle()
bolt.Center = GeoPoint3(0, 0, 0)
bolt.Radius = 5.0
drawing.blocks.add("BOLT", [bolt, DxfLine(GeoPoint3(-5, 0, 0), GeoPoint3(5, 0, 0))], base_point=(0, 0))
drawing.add_insert("BOLT", (50, 50))
drawing.add_insert("BOLT", (100, 50), scale=2.0, rotation=math.pi / 2)
len(drawing.commit()), [b.Name for b in drawing.blocks]    # (2, ['BOLT'])
drawing.net.Undo()                                          # True
[b.Name for b in drawing.blocks]                            # []
```

The numbers above came from a run of the built wheel on .NET 8 and on .NET Framework 4.8; the code blocks of this section are run as
a test, each line that ends in a comment with the value it gives checked against it (`test_k2_guide_examples.py`). Not in the calls: attributes on `add_insert`, an associative hatch, a custom `.pat` file, and a style of
any other kind (multileader, table, multiline); for those below.

#### Going below the calls

`drawing.net` is the `CadDocument` for a drawing of a file and of the app, and the `GeometryHelper.Dxf` records and entities are
there to use: `GeometryHelper.Dxf` for the objects, `OpenCAD2D` for the editing. A change made straight in
`drawing.net.Database` is not an edit of the document: it is not in the undo list, and the canvas does not know of it. One made through
`CadDocument.ApplyEdit` is, and so is every call above. `ApplyEdit(name, records, apply)` runs `apply(scope)` as one undo step called `name`,
`scope.Database` is the `DxfDatabase` to write in, and an exception inside takes the step back:

```python
from System import Action, Array
from OpenCAD2D import CadEditRecord, CadEditScope
from GeometryHelper.Dxf import DxfTextStyleTableRecord

def write(scope):
    style = DxfTextStyleTableRecord()
    style.Name = "TITLE"
    style.FileName = "arial.ttf"
    scope.Database.TextStyleTable.Add(style)       # in the undo step of this edit

result = drawing.net.ApplyEdit("TITLE STYLE", Array[CadEditRecord]([]), Action[CadEditScope](write))
result.Succeeded                                   # True
```

A drawing from `connect()` has no `Database`: `drawing.net` is a `CadRemoteDocument`, and the calls above are the way to write
there ([Driving a running app](#driving-a-running-app); `AddRecords` is the call of the client library that carries them).

### Typed entities

An `Entity` knows the class of its `net` for a type checker (`Entity[DxfCircle]`; a bare `Entity` is an `Entity[DxfEntity]`),
as C# knows it from `is` and `as`. `entities(DxfCircle)` gives the circles, typed, and a class asks for its subclasses too:
`DxfCurve` is the lines, arcs, circles, polylines and the rest of the curves. The `add_*` calls give the exact type
(`add_circle` an `Entity[DxfCircle]`, `add_polyline` an `Entity[DxfPolyline]`). An `Entity` read by name, by handle, from the
selection or from a pick has a `net` of the base class, which has no `Radius` until it is narrowed, the way C# asks for a cast:

```python
from GeometryHelper.Dxf import DxfCircle, DxfCurve, DxfLine

drawing.entities(DxfCircle)[0].net.Radius       # 25.0, typed float: no isinstance needed
[e.dxf_type for e in drawing.entities(DxfCurve)]  # ['LINE', 'CIRCLE', 'LWPOLYLINE']: the curves of the drawing above

for entity in drawing.entities():
    if isinstance(entity.net, DxfLine):         # as C#: if (entity is DxfLine line)
        print(entity.net.Length)                # 100.0
    match entity.net:                           # as C#: switch (entity) { case DxfCircle c: ... }
        case DxfCircle() as circle:
            print(circle.Radius, circle.Area)   # 25.0 1963.495...
        case _:
            pass
```

A class that is no `DxfEntity` (`entities(int)`) raises `TypeError`, and a type checker marks it too. At run time `net` has
always been a copy of its own class: only what a type checker knows has changed. A script that reads
`drawing.entities("CIRCLE")[0].net.Radius` still runs, and pyright now asks for `entities(DxfCircle)` or an `isinstance`
there. The numbers come from a run of the built wheel on .NET 8 (a line, a
circle of radius 25 and a closed polyline), and the two forms are the `ok_typed_entities.py` and `bad_typed_entities.py` of
the typecheck tests.

### Where things are drawn: bounds and overlaps

A program that lays a sheet out, or scores one, asks where things are on it, and `entity.net` cannot say: the insertion point of a
text is a corner of its word, a dimension is one object whose lines and words are somewhere else, and an INSERT is whatever its
block draws, however deep and however scaled. `entity.bounds`, `drawing.bounds()` and `drawing.overlaps()` read the display list
the canvas fills, so the answer is what is on the screen, the same on a file, in the app and from outside. The two table calls need
numpy (`pip install opencad2d[numpy]`).

```python
from opencad2d.cad import new_file

drawing = new_file()
drawing.text_styles.add("NOTE", font="romans.shx")
text = drawing.add_text("Plate A-12", (10, 60), 5.0, text_style="NOTE")
line = drawing.add_line((0, 0), (100, 40))
drawing.add_circle((50, 50), 10)
drawing.commit()
box = line.bounds
box                                                  # Box(min_x=0.0, min_y=0.0, max_x=100.0, max_y=40.0)
words = text.bounds
words.min_x                                          # 10.0
words.max_x - words.min_x > 20.0                     # True
table = drawing.bounds()
table.handles == [text.handle, line.handle, drawing.entities("CIRCLE")[0].handle]   # True
table.boxes.shape                                    # (3, 4)
table.boxes[2].tolist()                              # [40.0, 40.0, 60.0, 60.0]
```

`entity.bounds` is a `Box` of the box an object takes: the letters of an SHX or TrueType word and the frame they stand in, the lines,
arrows and words of a dimension, the contents of a block, the columns of an MTEXT, the width of a wide polyline, the lines of a
hatch. It is `None` for an object the drawing draws nothing of (one that is not visible) and a `ValueError` for one added and not
yet committed: the box is of the drawing as committed. `drawing.bounds(layout=None)` is the same for every object of model space or
of a layout, as a `BoundsTable`: `handles`, and `boxes`, a NumPy array of one row of (min_x, min_y, max_x, max_y) for each, in the
order the space draws them. A TrueType word is measured by the letters of its font, not by the display's guess of 0.6 of the height
a letter: ten i of Arial 10 high are 31 wide and ten W 132.

`drawing.overlaps()` gives the objects whose words overlap what is drawn near them. A word is the frame of a text, the rectangle
its height and the width of its letters make, turned as the text is; an MTEXT is framed one run of a line at a time, so that the
gap between its columns is no word, and a dimension's words are those of its picture. A word overlaps a line, an arc, a polyline or
the edge of a hatch that runs through its frame, a filled area its frame lies in, and the frame of another word it shares an area
with. What only touches the frame, or comes within `tolerance` of its edge (millimetres, 0.001), does not, and neither does a part
of the word's own object: a dimension's text is not over its own lines.

```python
drawing.add_line((0, 90), (100, 90))
box = words
across = (box.min_x + box.max_x) / 2.0
drawing.add_line((across, 50), (across, 70))                 # through the middle of the words
drawing.add_line((0, box.max_y), (100, box.max_y))           # along the top of the frame: touching, no overlap
drawing.commit()
hits = drawing.overlaps()
len(hits)                                                    # 1
hits.words == [text.handle]                                  # True: the object whose words they are
len(hits.others)                                             # 1
hits.boxes.shape                                             # (1, 4)
abs(hits.boxes[0][0] - across) < 1e-9                        # True: the part of the line inside the frame is at x = across
```

A pair is one row, however many of the parts of the two meet, with the box of all the places they do. Where both objects are words
the one drawn first is `words`. `kinds` names the DXF types whose words are looked at (`"TEXT"`, `"MTEXT"`, `"DIMENSION"`,
`"MULTILEADER"`, which are the default; the words of an INSERT, its attributes and the text of its block, are of the kind `"INSERT"`),
`over` the types they are looked at against (every kind where None, words included). `of` limits the answer to the pairs in which
one of those objects stands on either side, which is what a program that has moved one text wants and costs a few milliseconds:

```python
drawing.overlaps(over=["CIRCLE"])                            # no row: the circle is nowhere near the words
len(drawing.overlaps(kinds=["DIMENSION"]))                   # 0: there is no dimension
len(drawing.overlaps(of=[text]))                             # 1
len(drawing.overlaps(of=[line]))                             # 0: the diagonal stands clear of the words
len(drawing.overlaps(tolerance=0.001))                       # 1
```

The answer is the same on every run and does not follow the order of any table. A drawing of 100 000 objects (25 000 texts, 50 000
lines, 15 000 circles and 10 000 inserts at random over 3 000 by 3 000) has its overlaps, some 95 000 pairs, in 0.3 to 0.6 s after it
is drawn on .NET Framework 4.8 and 0.25 to 0.35 s on .NET 10; the index it asks is kept until an edit makes the space again, so a
reward function that asks after each change pays for what the change touched. From outside, the numbers travel as JSON text,
which a large drawing makes the slower part of the call ([Driving a running app](#driving-a-running-app)). The C# calls are
[`CadSpatial.Bounds` and `CadSpatial.Overlaps`](presenter/display.md#where-things-are-drawn).

### Preparing real drawings for AI: flatten, JSON and graph

A drawing from a real project is not what a model should read as it stands: its shapes are hidden inside blocks inside blocks,
turned, scaled and mirrored, in the unit its author chose. Three calls of a `Drawing` give it in the form a pipeline wants. The work
is C# in the core, so a drawing of 10 000 entities (13 800 once its blocks are opened) takes about 0.1 s for `flatten` and `to_json` and 0.25 s
for `to_graph` on .NET 8 (0.2 s and 0.4 s on .NET Framework 4.8), and a script gets the same text on a file, in the app and from outside it.

- `flatten(layout=None)` walks every block reference, nested ones and arrays of them (MINSERT) included, and gives the objects they
  draw as a list of `dict`, in world coordinates and millimetres. The drawing is not changed. A piece of a block has `path` (the
  names of the blocks it came out of, outermost first), `indexPath` and `matrix` (4 by 4, block coordinates to world millimetres), and
  the layer, colour and linetype it is drawn with: what is on layer 0 or ByBlock takes the look of the reference it is in, all the
  way down, as BURST gives it. The shapes are the ones EXPLODE gives, repeated down the nest, so a circle under a scale unlike along
  its axes comes out an ellipse. The attributes of a reference are objects of their own (`kind` `ATTRIB`, with `tag` and `text`), and
  so is a constant attribute definition of a block (`constant: true`); a variable definition is left out.
- `to_json(layout=None)` is the whole drawing as one JSON text, `to_dict()` the same read: `schema` `opencad2d.drawing`, `version` 1,
  the tables of layers, text styles, dimension styles, linetypes and blocks, a `report`, and the `entities` of `flatten`, in the order
  of their handles read as numbers, so the same drawing gives the same text every time, on both runtimes. Numbers are rounded to
  a nanometre. Every entity has `box` (its extents) whatever its `kind`; LINE, CIRCLE, ARC, ELLIPSE, LWPOLYLINE, POLYLINE, SPLINE,
  POINT, TEXT, ATTRIB, MTEXT, DIMENSION, LEADER, MULTILEADER, HATCH, SOLID, TRACE and 3DFACE carry their geometry and words as well.
  The JSON Schema is [`docs/schemas/opencad2d-drawing-v1.schema.json`](https://github.com/nguyenthanguth/OpenCAD2D/blob/main/docs/schemas/opencad2d-drawing-v1.schema.json),
  and a test validates the text against it.
- `to_graph(layout=None, tolerance=0.001)` is a graph for a GNN or a transformer: a node for each entity of `flatten`, in its order, and
  edges of four kinds. `touches`: two curves that come within `tolerance` millimetres of each other (their ends meet, an end lies on the other, they
  cross, or they run along each other; `at` is the place). `contains`: a closed curve (a closed polyline, a circle, a whole ellipse) that holds the other inside it, a line
  along its side not counted. `dimensions`: from a dimension to the objects its DIMASSOC ties it to, else to the curves its definition
  points lie on. `annotates`: from a leader to what its arrow tip lies on. With numpy installed the dict also has `edge_index` (2, E)
  and `edge_kind` (E,), int64, the shapes a graph library takes.

```python
import json

from GeometryHelper.Dxf import DxfCircle, DxfLine
from GeometryHelper.Geometry import GeoPoint3, GeoVector3
from opencad2d.cad import new_file

drawing = new_file()
drawing.blocks.add("BOLT", [DxfCircle(GeoPoint3(0, 0, 0), GeoVector3(0, 0, 1), 4.0), DxfLine(GeoPoint3(0, 0, 0), GeoPoint3(30, 0, 0))])
drawing.add_insert("BOLT", (100, 50))
drawing.add_insert("BOLT", (200, 50), scale=2.0)
drawing.add_circle((100, 50), 20)
drawing.add_line((0, 0), (100, 0))
drawing.add_line((100, 0), (100, 50))
len(drawing.commit())                                  # 5

flat = drawing.flatten()
len(flat)                                              # 7: two BOLTs of two shapes, a circle and two lines
[(e["kind"], e["path"]) for e in flat][:2]             # [('CIRCLE', ['BOLT']), ('LINE', ['BOLT'])]
flat[0]["matrix"][0]                                   # [1, 0, 0, 100]: the first BOLT at (100, 50), not turned
flat[0]["box"]                                         # [96, 46, 104, 54]: its circle of radius 4
json.loads(drawing.to_json())["schema"]                # 'opencad2d.drawing'
drawing.to_dict()["units"]["toMillimetres"]            # 1

graph = drawing.to_graph()
len(graph["nodes"])                                    # 7
[(e["source"], e["target"], e["kind"]) for e in graph["edges"] if e["kind"] == "contains"]  # [(4, 0, 'contains')]
graph["edge_index"].shape                              # (2, 8)
graph["edge_kind"].tolist()                            # [0, 0, 0, 0, 0, 0, 0, 1]
```

The numbers are from a run of the built wheel on .NET 8: the circle of radius 20 holds the first bolt's circle (node 4 holds node 0),
and the seven `touches` are the shapes that meet or cross: each bolt's line and circle, the first bolt's circle and line with the
line at x = 100, its line with the big circle, the big circle with the line at x = 100, and the two lines drawn.

Units are millimetres. The unit is the drawing's INSUNITS (a drawing of inches has `units.toMillimetres` 25.4, and every length,
radius, height and coordinate is written 25.4 times larger), and the plot paper units for a layout's paper space. A drawing that
says no unit (INSUNITS 0) is left as it is and says so, `units.normalised` false: decide what its numbers are before you train on it.
A block reference read from a file already has the unit ratio in its scale (AutoCAD writes it in when the block is inserted), so it
is not scaled again; a block drawn in a unit other than the drawing's is listed in `report.blockUnits` with the factor INSERT would
have used, for you to decide on.

What the walk leaves alone is in `report`, and nothing is lost silently. A reference with a clip (XCLIP) is walked unclipped, its
objects marked `clipped: true`; an external reference is one object of kind INSERT, its content not read; a reference that places
itself, is nested more than 64 deep or would give more than two million objects is one INSERT too, with the reason in
`report.refused`. A dynamic block is walked as the anonymous block it draws and named by the dynamic block in `path`
(`dynamic: true`). Where a block cannot hold its placing, a block turned within one scaled unlike along its axes, EXPLODE breaks it
in its turn and the pieces come out with the path of the block around it. `to_graph` finds `touches` and `contains` only between
objects of one layout.

From outside the app, and in a tool of the app, the same three calls ask the app for the text (`CadRemoteDocument.ExportJson` and
`ExportGraph`), and C# gets it from `CadDrawingJson.Write`, `CadFlatten.Walk` and `CadDrawingGraph.Build` of the core.

## Rendering without a window

A drawing of a file, from `open_file` or `new_file`, can be drawn to a picture with no app and no window: a PNG, an SVG, a PDF
or a numpy array. The picture is the canvas of the app's: the same renderer draws it, with the same fonts and colours, on
the app's default settings and never the settings file of the user, so a picture is the same on every machine that has the same
fonts. The grid, the UCS icon and the grips are the canvas's and not the drawing's, so they are not drawn.

```python
from opencad2d.cad import new_file

drawing = new_file()
drawing.add_line((0, 0), (400, 300))
drawing.add_circle((200, 150), 60, color=1)
drawing.commit()                                   # ['100', '101']; a render commits what is queued too, as save() does
drawing.render_png("plan.png")                     # 1 600 by 1 200 pixels, white and red on black
drawing.render_svg("plan.svg", width=800)          # the same picture in vectors, 800 by 600
drawing.render_pdf("plan.pdf")                     # one A3 page, the drawing fitted to the sheet
picture = drawing.render_image(width=400)          # the pixels as a numpy array, with no file
picture.shape                                      # (300, 400, 3)
str(picture.dtype)                                 # 'uint8'
```

`render_image` needs numpy (`pip install opencad2d[numpy]`) and gives red, green and blue, rows from the top: the bytes
`render_png` writes, which has no alpha channel (PNG colour type 2). The three file calls and the array take the same
arguments, `render_pdf` only the layout:

| Argument | Meaning |
|---|---|
| `box` | the part drawn, a `Box` or a tuple of four numbers in the units of the space (millimetres of paper on a layout), the corners in either order, fitted to the picture with no margin; None, the extents of the space with the 5 per cent margin ZOOM Extents leaves, a layout's sheet of paper among them |
| `width`, `height` | the size in pixels; one given, the other follows the aspect of the part drawn, rounded; both, the part is fitted and centred |
| `dpi` | pixels to the inch of paper, 96 by default, for a layout drawn at no size of its own |
| `ground` | `"black"`, the canvas's, or `"white"`, on which colour 7 is drawn black; a layout's paper is white whatever it is |
| `layout` | a layout's name (letters of either case) or `"Model"`; None, the space the drawing was left in |

With neither `width` nor `height` a layout is its part in millimetres times `dpi` over 25.4 (an A3 sheet, 420 by 297, is
1 587 by 1 123 pixels at 96 and 4 961 by 3 508 at 300; a layout whose paper units are inches is its part in inches times
`dpi`, so a sheet of 16.54 by 11.69 inches gives the same), and model space is 1 600 pixels along the longer side, whatever
`dpi` is. Lines keep the widths the canvas gives them, in pixels, so the picture is the app's whatever the size. A picture of
more than 16 384 pixels a side or 100 000 000 pixels in all (400 MB) raises `ValueError` naming the size and the cap, as do a
`ground` other than the two, a `dpi` that is not above nought, a size that is not a whole number or is negative, a
box with a number that is not finite and a layout the drawing does not have.

### The three forms

- **PNG and the array** are the same pixels: `render_image` of a drawing equals `render_png` decoded, byte for byte, and equals
  the canvas of the Release app (`--screenshot ... --canvas`) on the same file, on .NET 8 and on .NET Framework 4.8.
- **SVG** is the picture of the PNG in vectors: its `width`, `height` and `viewBox` are the pixels of that picture, and an
  image is in it as PNG data. The renderer of the canvas draws it, so the canvas's level of detail holds at the picture's
  size: an item under half a pixel is one dot, a text under 3 pixels high is left out and a hatch whose lines come closer than
  2 pixels is a tint. Draw at a larger `width` to keep more, or use the PDF.
- **PDF** is the plot of EXPORTPDF with every detail: one page of the layout's sheet, its scale and plot style, lines at the
  lineweights of the plot, text as text. An A3 sheet on its side is 1 190.55 by 841.89 points. A space that was never given a
  sheet, which is a drawing made in code, is fitted to ISO A3, as AutoCAD fits a page setup it has not been given; one that has
  a sheet is plotted at the scale it records. A space with nothing to plot raises the .NET `InvalidOperationException`.

### Where it works

Only a drawing of a file draws: a drawing of the app (`active_drawing()` inside a tool, `connect()` from outside) raises
`RuntimeError("render_png works on a drawing of a file ...")`, because the Python process holds no drawing to draw, only a
connection to the app that does. Save the drawing from the app (`drawing.save_as("copy.dxf")`) and render `open_file("copy.dxf")`.
Rendering a drawing through the app waits for the MCP server of the road map. One picture is drawn at a time in a process; the
renderer's caches are not locked against two threads drawing at once, so use processes to draw in parallel.

`import opencad2d` does not load the renderer. The first render call loads `OpenCAD2D.Rendering`, SkiaSharp and, by its full
path, the native `libSkiaSharp.dll` (13 MB) from the `lib` folder of the wheel, on .NET 8 and on .NET Framework 4.8 alike; on
.NET Framework it also loads the `System.Memory` family from there. They add 5 851 400 bytes zipped to the wheel (16 404 573 bytes in all) and 13 932 296 unpacked to its `lib` folder (16 324 162 to 30 256 970). A `lib` folder without `libSkiaSharp.dll` raises `ImportError` naming it. A script that never renders
does not pay for any of it.

### What it costs

Seconds for 1 600 by 960 pixels, as `first call / median of 5 after`, from the run of
`test_the_first_call_and_the_median_of_five_after_it_stay_far_below_a_hang` of `tests/python/test_k1_render_headless.py` with
`-s`, on the machine and commit named below the table. The first call of the first drawing carries the load of the native
library and the scan of the fonts; a display list is built by the first call of a drawing and kept.

| Drawing | Runtime | `render_png` | `render_svg` | `render_pdf` | `render_image` |
|---|---|---|---|---|---|
| curves: 40 lines, 40 circles, 40 texts | .NET 8 | 0.089 / 0.031 | 0.011 / 0.005 | 0.019 / 0.007 | 0.072 / 0.006 |
| | .NET Framework 4.8 | 0.184 / 0.032 | 0.012 / 0.005 | 0.038 / 0.008 | 0.090 / 0.006 |
| paragraphs: 30 MTEXT that wrap | .NET 8 | 0.057 / 0.030 | 0.024 / 0.024 | 0.039 / 0.022 | 0.007 / 0.006 |
| | .NET Framework 4.8 | 0.062 / 0.029 | 0.023 / 0.023 | 0.024 / 0.022 | 0.008 / 0.007 |
| hatches: 20 ANSI31 hatches in 20 squares | .NET 8 | 0.041 / 0.028 | 0.001 / 0.001 | 0.002 / 0.002 | 0.006 / 0.005 |
| | .NET Framework 4.8 | 0.071 / 0.032 | 0.002 / 0.002 | 0.001 / 0.001 | 0.006 / 0.006 |
| dimensions: 20 linear dimensions | .NET 8 | 0.032 / 0.026 | 0.002 / 0.002 | 0.003 / 0.003 | 0.007 / 0.005 |
| | .NET Framework 4.8 | 0.041 / 0.029 | 0.004 / 0.003 | 0.003 / 0.003 | 0.006 / 0.008 |

The numbers are from the second of two runs on 26 November 2026, an Intel Core i9-12900F with 64 GB, on commit `8caa88a6` and the
working tree of PW6.1 P3 (the first run was the first of the day, with the files of the wheel not yet in the system's cache: the
first call of `render_png` was 0.354 s on .NET 8 and 0.451 s on .NET Framework, the cold start of the native library and the scan
of the fonts). A PNG is dominated by its encoding, 25 to 30 ms for 1 600 by 960 pixels; a picture of a few dozen objects is
drawn in 5 to 10 ms; the SVG and the PDF of a drawing of lines and hatches take 1 to 25 ms. The test asserts only a ceiling of 10 seconds
a call, against a hang.

## Inside OpenCAD2D

The same library runs in the app's tools. OpenCAD2D loads a Python tool, a folder with a `requirements.txt`, an `__init__.py`
and a `plugin.json`, from a Pythons folder at start, from APPLOAD and PYLOAD, and runs a lone `.py` with PYRUN; a function
marked `@command("C1")` becomes a command that asks the user for points, distances and objects through `opencad2d.cad`, with
one undo step for the whole run. Python does not run inside the app: each tool runs in a `python.exe` of its own, started at its
first command, with a venv of its own made from a CPython 3.11 to 3.15 of your own, which Global settings can name; the wheel is
not installed there, as the app brings its own copy of the package. [Python commands and tools](presenter/python.md) is the guide.

## Stubs and an editor

The wheel carries the stubs of the library, so an editor completes the .NET names and checks a script that uses them. They
are a PEP 561 stub package per top-level namespace, `GeometryHelper-stubs` and `OpenCAD2D-stubs` (an `__init__.pyi` for each
of the 18 namespaces of the two libraries), and `System-stubs`, the few BCL classes the library's signatures name, as pythonnet spells them: `System.Array[T]`,
`System.Collections.Generic.List[T]`, `System.Action[...]`. They hold 827 types and 18 792 members (a member is one stub
entry, each overload once), and the summary of each type and member as its docstring, so hovering `GeoPoint3(` in Pylance
shows its overloads and what each does. A class written in several files shows all its summaries joined in the hover.
The same text is on the docs site as the [Python API](../docfx/python-api/index.md), a page for each type with
every member as Python sees it and a link to its page in the .NET reference.

What the stubs say, so that the editor and pythonnet agree:

- every .NET overload is an `@overload`, and an out parameter is not a parameter but part of the returned tuple;
- the parameters after an out parameter that is not the last are keyword-only;
- an overload that differs from a shorter one only by out parameters is reached by naming one of them: a required keyword
  (`outcome=BooleanOutcome.Made`), so the call without it is the shorter overload, as pythonnet binds it;
- a collection a method returns is typed as the .NET interface (`IReadOnlyList[GeoPoint2]`), without `len()` or a slice;
- a parameter that pythonnet takes a Python list for is `Sequence[T] | System.Array[T]`, and one it does not is the .NET
  class only;
- a delegate is `System.Action[...]` or `System.Func[...]`, and an event `+=` takes a plain function;
- a settable reference-type property takes `None` unless its comment says it throws `ArgumentNullException`, or
  `ArgumentException` with a summary that does not speak of null (306 setters take it, 214 do not). The assemblies carry no nullable
  annotations, so the comment is what there is to read; the library documents every setter that refuses null, which a probe of
  223 properties, each set to null on an object made by its default constructor, bore out: 91 refused it (80 with
  `ArgumentNullException`, 11 with `ArgumentException`) and none of those takes `None` in the stubs; of the 132 that did not, 129 take it
  and 3 do not;
- a getter, a field or a static property whose summary or returns says it can give null (`null where the file has none`, `or null`,
  `gives null`) is `T | None`, 359 of them, so a checker asks for the `None` to be seen to before the member is used:
  `CadApplication.ActiveDocument` is `CadRemoteDocument | None`, as the app may have no drawing open;
- an optional parameter whose default is null takes `None` (152 more parameters than before);
- an enum is made from a number, `DxfEraseFlags(3)`;
- a `[Flags]` enum has `|`, `&`, `^` and `~`, a plain enum none;
- the 33 members named as Python keywords are left out, and a parameter named so is `from_`, positional-only;
- two .NET types are written `Any`, `System.Double[,]` and `System.Xml.Linq.XDocument`.

pyright and Pylance find the stubs when the editor uses the virtual environment the wheel is installed in. The one setting
to make: the stubs have no source, because pythonnet makes the module at run time, so every import of one warns
`reportMissingModuleSource`. Turn that rule off, in `pyrightconfig.json`:

```json
{ "reportMissingModuleSource": "none" }
```

or, in VS Code, in `settings.json`: `"python.analysis.diagnosticSeverityOverrides": { "reportMissingModuleSource": "none" }`.
The rest is the checker's own. A script written against what this page says:

```python
text: str = p.DistanceTo(q)                          # Type "float" is not assignable to declared type "str"
p + q                                                # Operator "+" not supported for types "GeoPoint3" and "GeoPoint3"
len(GeoPolygon2(a, b, c).Vertices)                   # "__len__" is not present
DxfEraseFlags.ZeroLengthCurve | 1                    # Operator "|" not supported for types "DxfEraseFlags" and "Literal[1]"
```

each is reported before the script runs. On the test machine pyright 1.1.414 checks the acceptance script in 1.62
to 1.68 s from the command line (five runs), against 0.89 to 0.92 s for a file of one line, so the stubs add about 0.75 s
to a command-line check; an editor's language server reads them once. The package itself, `opencad2d`, type-checks
with no error in pyright's strict mode, and `py.typed` says its annotations are for use.

A script that imports `opencad2d` freezes with PyInstaller as it is, `pyinstaller script.py`: the package brings a hook
(the `pyinstaller40` entry point, `opencad2d._pyinstaller`) that names pythonnet and clr_loader, which the package imports by
name when it runs, and takes the .NET libraries and `opencad2d.runtimeconfig.json` into the frozen folder (40 MB for a script that
prints a point's X). The frozen program needs the .NET 8 runtime (or .NET Framework 4.8, with `OPENCAD2D_RUNTIME=netfx`) on the
machine, as the script does.

Do not install `dotnet-stubs` (from domdfcoding) beside it: it installs a `System-stubs` of its own, and the two clash.

The stubs are written by our own tool, `tools/OpenCAD2D.PyStubs` (`bash tools/python/make-stubs.sh`), which reads the
assemblies without running them and the XML documentation, and writes the same bytes on a rerun. It takes ideas from two
MIT-licensed projects and no code: a stub package per namespace, `@overload` for each .NET overload and out parameters as a
tuple from [mcneel/pythonstubs](https://github.com/mcneel/pythonstubs), and the table of .NET base class types from
[MHDante/pythonnet-stub-generator](https://github.com/MHDante/pythonnet-stub-generator).

## What is not supported

- Systems other than 64-bit Windows, and Python outside 3.11 to 3.15 or 32-bit. `pip` refuses the wheel there.
- Installing from PyPI, until the package is published under a name of its own.
- A frozen application (PyInstaller and the like): the package gives no hook, so the assemblies would have to be added
  by hand.
- A module for the presenter outside the app: `opencad2d.cad` prompts, `cmd`, `write` and `active_drawing` work in a
  tool of OpenCAD2D only (see [Inside OpenCAD2D](#inside-opencad2d)), and from outside `OpenCAD2D` is imported as any other
  namespace.

## Driving a running app

A script outside the app can connect to a running OpenCAD2D and draw, select, run commands and ask the user to
pick, with `connect()` of `opencad2d.cad` (`with connect() as app:`, the same `Drawing` as above), or with `CadApplication`
of `OpenCAD2D.Remote`, which `import opencad2d` has already loaded; either closes the connection at the end of
the block, also when it raises. A tool of the app uses `opencad2d.cad`, whose `connect()` reaches the channel of the tools.
[From Python](presenter/remote.md#from-python) has a full script, the points where Python differs from C#, and the timings.

## Where to go next

| Page | What it covers |
|---|---|
| [How do I](https://github.com/nguyenthanguth/GeometryHelper/blob/main/src/GeometryHelper/docs/howto.md) | a task, the call that does it, and the page that tells the rest: the calls are the ones a script makes |
| [Python examples](python-examples.md) | fifteen examples, on the library, as tools of the app and driving a running app, each shown with what it prints |
| [Python API](../docfx/python-api/index.md) | every type of the package as a script sees it, generated from the stubs, linked to the .NET reference |
| [DXF](https://github.com/nguyenthanguth/GeometryHelper/blob/main/src/GeometryHelper/docs/dxf/index.md), [Reading and writing DWG files](https://github.com/nguyenthanguth/GeometryHelper/blob/main/src/GeometryHelper/docs/dxf/dwg.md) | the drawing, its tables and entities, read and written |
| [The 2D presenter](presenter/index.md), [Editing as AutoCAD does](presenter/editing.md) | `CadDocument`, its command session and undo |
