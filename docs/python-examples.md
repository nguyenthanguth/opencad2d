# Python examples

Sixteen examples, each doing one thing people do with OpenCAD2D from Python: list the layers of a drawing, make a
drawing in code, draw one to pictures with no window, cut one solid from another, number points in the app, draw in an app that is running. They are in
`src/python/examples` of the repository, in three folders for the three places Python works with the library (a tool of the
app is a folder of its own), and this page shows each as it is, with what it prints. A test of the repository runs every file, on .NET 8 and on .NET Framework
4.8, against a drawing the test builds, and a type check (pyright) finds no error in any of them, so what the page shows
is what the files do.

| Folder | Where it runs | How a file is run |
|---|---|---|
| `library` | outside the app, on the libraries alone | `python list_layers.py plan.dxf`, in the virtual environment the wheel is installed in ([Installing](python.md#installing)) |
| `tools` | tools of OpenCAD2D | each a folder with a `requirements.txt`, an `__init__.py` and a `plugin.json`: PYLOAD its `requirements.txt`, or put the folder in the Pythons folder ([Python commands and tools](presenter/python.md)) |
| `remote` | outside the app, driving one that is running | `python draw_and_read.py` while the app runs with "Allow other programs to connect" on ([the setting](presenter/remote.md#the-setting)) |

| File | What it does |
|---|---|
| [`list_layers.py`](#list-the-layers-of-a-drawing) | the objects and the closed area on each layer of a DXF or DWG |
| [`coordinates_numpy.py`](#the-vertices-of-the-polylines-in-numpy) | every polyline's vertices as a numpy array, and the extents of all of them |
| [`make_drawing.py`](#make-a-drawing-in-code) | a layer, a rectangle and a circle added to a new drawing, saved as DXF or DWG |
| [`regions.py`](#union-difference-and-offset-of-regions) | union, difference, intersection and offset of two squares |
| [`solids.py`](#a-box-with-a-hole-measured) | a box with a hole cut through it: volume, surface area and centroid |
| [`batch_folder.py`](#a-csv-report-of-a-folder) | one CSV row for each DXF and DWG file of a folder |
| [`convert.py`](#convert-between-dxf-and-dwg) | a drawing read and written again as DXF or DWG, in a version |
| [`render_views.py`](#pictures-of-a-drawing-with-no-window) | a drawing drawn to two PNGs, an SVG and a PDF with no window |
| [`coco_dataset.py`](#a-dataset-for-a-vision-model) | three drawings written as pictures, class masks and one COCO file |
| [`number_points`](#number-the-points-you-pick) | a number written at each point you pick |
| [`drawing_tools`](#commands-of-your-own) | the commands RECTC and LAYERCOUNT |
| [`selection_lengths`](#the-length-of-a-selection) | the length and closed area of the selected objects |
| [`circle_marks`](#a-snap-a-setting-and-a-button-for-circles) | the snap CTOP, the setting textHeight, the command MARKTOPS and a ribbon button |
| [`draw_and_read.py`](#draw-a-grid-and-read-it-back) | a grid drawn in a new drawing of the running app, read back |
| [`pick_and_mark.py`](#pick-a-point-and-mark-it) | a circle drawn where the user picks |
| [`open_documents.py`](#the-open-drawings) | the drawings the app has open and what is in each |

Every file follows the same plan. The ones that take arguments are a `main(argv)` that prints a usage line to the standard
error and returns 2 when the arguments are wrong, and ends in `sys.exit(main(sys.argv[1:]))`. Numbers are printed with three
decimals, in the drawing's own units. Where a script compares or offsets geometry it passes a
[`Tolerance`](xref:GeometryHelper.Tolerance) (`Tolerance.Default`, a thousandth of a millimetre for points). The numbers
below were printed by the files on a drawing of four layers and six objects that the tests build, called the test drawing
here: layers `0`, `Empty`, `Slabs` and `Walls`; on `0` a line of 100; on `Slabs` a closed polyline 4 000 by 3 000 and a
circle of radius 300 at its middle; on `Walls` an open polyline of two segments of 1 000 and two lines of 500; `Empty`
holds nothing. The same drawing saved as DXF and as DWG gives the same lines on both runtimes.

## Outside the app

### List the layers of a drawing

`list_layers.py DRAWING` reads a DXF or DWG file with `open_file` and prints one line for each layer of its layer table, sorted by name, with
the number of objects of model space on it and the area enclosed by the closed polylines and circles on it.

```text
layer	objects	area
0	1	0.000
Empty	0	0.000
Slabs	2	12282743.339
Walls	3	0.000
total	6	12282743.339
```

The area of `Slabs` is 12 000 000 for the polyline and 282 743.339 for the circle (pi times 300 squared). The open polyline
of `Walls` encloses nothing, and `list_layers.py` counts it as an object only. A DWG gives the same lines as the DXF. A
layer name is one name in whatever case, while an object keeps the spelling it was written with, so a line on `Walls` and a
circle of radius 10 on `walls` are counted together under the layer table's spelling: `Walls` with 2 objects and an area
of 314.159.

[!code-python[](../src/python/examples/library/list_layers.py)]

### The vertices of the polylines in numpy

`coordinates_numpy.py DRAWING` reads the file with `open_file`, takes every polyline of model space in the order of its handles, and copies its vertices
into a numpy array of shape (n, 2) through [`opencad2d.arrays`](python.md#numpy-in-blocks), which takes a whole .NET
array in one copy where a loop over the points would call into .NET three times a point. It needs numpy
(`pip install opencad2d[numpy]`).

```text
104	4	14000.000
106	3	2000.000
extents	0.000,0.000	4000.000,5000.000
```

Each line is a polyline's handle, its vertex count and its length: the closed polyline is four corners with a perimeter of
14 000, the open one three vertices with a length of 2 000. The last line is the extents of all the vertices, taken with
numpy. A closed polyline's corners come from `ToGeoPolygon2`, since `ToGeoPolyline2` repeats the first corner at the end
and would give five.

[!code-python[](../src/python/examples/library/coordinates_numpy.py)]

### Make a drawing in code

`make_drawing.py OUT` makes a blank drawing with `new_file()`, adds a layer `Outline` and makes it current, adds a rectangle
from 0,0 to 4000,3000 and a circle of radius 500 at 2000,1500 on it, applies them with one `commit()`, and saves the drawing
as `OUT`, DXF or DWG by its extension.

```text
objects 2
layers 0, Outline
saved plan.dwg
```

Opened again, the file holds a polyline of area 12 000 000 and a circle of radius 500 on `Outline`, as DXF and as DWG.

[!code-python[](../src/python/examples/library/make_drawing.py)]

### Union, difference and offset of regions

`regions.py` takes two squares of 100, A from 0,0 and B from 50,50, and prints the area of the union, of A less B, of what
they share, and of A grown and shrunk by 10 with mitred corners (`OffsetJoin.Miter`).

```text
union 17500.000
subtract 7500.000
intersect 2500.000
offset +10 14400.000
offset -10 6400.000
```

The squares share 50 by 50, 2 500, so the union is 10 000 plus 10 000 less 2 500. A grown to 120 by 120 is 14 400, shrunk
to 80 by 80 6 400. Each call comes back as an array of faces or polygons, and the script adds their areas.

[!code-python[](../src/python/examples/library/regions.py)]

### A box with a hole measured

`solids.py` makes a box 1 000 by 500 by 200 and cuts a hole of 100 by 100 through it with a second box that sticks out
of both faces by 100, so that no face of the cutter lies on one of the box, and prints the volume, the surface area and the
centroid of what is left.

```text
volume 98000000.000
area 1660000.000
centroid 500.000,250.000,100.000
```

The volume is the box's 100 000 000 less 2 000 000 for the hole, 100 by 100 by 200. The area is the box's 1 600 000, less
the two openings of 10 000 each, plus the four walls of the hole at 100 by 200 each, 80 000. The measures and the cut take
`Tolerance.Default`. `TrySubtract` takes it by name, `plate.TrySubtract(cutter, tolerance=tolerance)`, because a parameter
that comes after an out parameter is keyword-only in Python, so the script gives the same numbers whatever
`Tolerance.Global` is: run with a global tolerance of 120, wider than the hole, a cut that read the global one would leave
the box uncut, 100 000 000 and 1 600 000.

[!code-python[](../src/python/examples/library/solids.py)]

### A CSV report of a folder

`batch_folder.py FOLDER REPORT.csv` opens every `.dxf` and `.dwg` file of the folder, not those of its subfolders, in the
order of their names, and writes one row each to a CSV file: the file, its version, the objects of model space, the layers,
and an error. A file that cannot be read is a row with its error and the next one is read. The script prints how many files
there were and how many failed, and exits with 1 when any failed.

Run on a folder with the test drawing as `a.dxf` and `b.dwg`, a file `broken.dxf` that holds the words `not a drawing`, a
text file and a subfolder with another drawing, it prints

```text
3 files, 1 failed
```

and writes

```text
file,version,objects,layers,error
a.dxf,AC1015,6,4,
b.dwg,AC1032,6,4,
broken.dxf,,,,"This is not a DXF that can be read: line 1 should be a group code, a whole number, and is 'not a drawing'."
```

The version is the one the file says it is: a drawing made in code with `DxfWriter` is AC1015 (AutoCAD 2000), and
`DwgWriter` writes AC1032 (AutoCAD 2018) unless it is told another. The `except Exception` takes the .NET exception that a file which is no drawing
raises; its first line is the error.

[!code-python[](../src/python/examples/library/batch_folder.py)]

### Convert between DXF and DWG

`convert.py IN OUT [--version ACnnnn]` reads a DXF or DWG and writes `OUT` as a DXF or DWG by its extension, in the version
given or, where none is, that of `IN`. The DXF writer returns what it could not keep in the version, and each is printed
first as a line `warning: ...`; then the file written and its version.

```text
$ python convert.py plan.dxf plan.dwg
wrote plan.dwg (AC1015)
$ python convert.py plan.dwg plan2.dxf --version AC1024
wrote plan2.dxf (AC1024)
```

The test drawing converted from DXF to DWG and from there to DXF again keeps its six objects on four layers. A version the
writer does not write, AC1009 (R12) for a DWG, is a line on the standard error, `error: ...`, and the exit code 1. A drawing
with an MText converted to AC1009 prints `warning: Written as the lines, texts and polylines each breaks into, which AC1009
holds: MTEXT 1.` The DWG writer says nothing of what it leaves out: it logs it.

[!code-python[](../src/python/examples/library/convert.py)]

### Pictures of a drawing with no window

`render_views.py DRAWING OUT_FOLDER` opens a DXF or DWG, finds the box that holds every object of model space, and writes
`whole.png` (the whole drawing, 800 pixels wide with the margin ZOOM Extents leaves), `detail.png` (the lower left quarter of
that box, fitted to a picture 800 wide with no margin), `whole.svg` and `plan.pdf` (one page, fitted to the sheet where the
drawing has none) into the folder, and prints the size of each picture. The test drawing spans 4 000 by 6 500, so a picture
800 wide is 1 300 high:

```text
$ python render_views.py plan.dxf views
whole.png 800 by 1300 pixels
detail.png 800 by 1300 pixels
whole.svg and plan.pdf written to views
```

The pictures are the canvas of the app's, drawn by the same renderer with no window ([Rendering without a window](python.md#rendering-without-a-window)).
A drawing with nothing in model space says so on the standard error and exits with 1.

[!code-python[](../src/python/examples/library/render_views.py)]

### A dataset for a vision model

`coco_dataset.py OUT_FOLDER` builds three drawings in code (a room with a hatched slab, a dimension, a text, an MTEXT and some
bolts inserted, laid out by a seed), and for each writes `images/drawingN.png` (the picture, 1 200 pixels wide), `masks/drawingN.png`
(the class of each pixel as a grey level from 0 to 5, so it looks black: multiply by 50 to see it) and the objects of all three in
one `annotations.json` in COCO detection format, with the ids of the objects renumbered so that they are unique in the file. It
prints the size of each picture and how many objects it holds:

```text
$ python coco_dataset.py dataset
drawing1.png 1200 by 1173 pixels, 9 objects
drawing2.png 1200 by 1025 pixels, 9 objects
drawing3.png 1200 by 1098 pixels, 8 objects
3 pictures and 26 objects written to dataset
```

The picture and the objects come from one `to_coco` call and the mask from `render_masks` with the same arguments, so all
three are of one picture ([Labelled renders](python.md#labelled-renders-for-training-vision-models)). It needs numpy.

[!code-python[](../src/python/examples/library/coco_dataset.py)]

## Tools of the app

The three tools that ship with the app, `DrawingInfo` (PYINFO), `DrawRandom` (PYRANDOM) and `HelloWorld` (PYHELLOWORLD), are not among the sixteen: they
are in the app's `Pythons` folder and in `examples\Pythons` of the repository, and [Python commands and
tools](presenter/python.md#where-tools-load-from) shows what they write. Each tool below is run in a `python.exe` of its own, with a
venv of its own that its `requirements.txt` fills (these need no package, so their files are empty).

These are folders, each a Python tool with no `main`: the app loads one (PYLOAD of its `requirements.txt`, or the Pythons folder),
shows the commands its `plugin.json` names, and starts its process at the first command typed. They use
[`opencad2d.cad`](presenter/python.md) for the prompts and the `Drawing` API, and each command is one undo step, so one `U`
takes back all it drew. A test of the repository (`PythonToolExampleTests`) loads each folder and types its commands in a
window's model, answering the prompts, so what the page says is what the tools do.

### Number the points you pick

`number_points`, run with its command NUMBERPOINTS, asks `First number <1>:` and then `Specify point:` until Enter, and adds a
text of height 2.5 at each point with `add_text`, the numbers counting up; the texts are applied when the run ends. After a first number of 1 and three points it writes

```text
3 points numbered, 1 to 3.
```

The three texts read 1, 2 and 3 at the points picked. Enter at the first question and at the first point writes
`No point picked.` and draws nothing.

[!code-python[](../src/python/examples/tools/number_points/__init__.py)]

### Commands of your own

`drawing_tools`, loaded with PYLOAD, defines two commands. RECTC (alias RC) asks for a centre, a width (`<1000>`) and a
height (`<500>`) and draws a closed polyline rectangle about the centre; LAYERCOUNT writes the number of objects on each layer
that has any, sorted by name. PYLOAD writes

```text
drawing_tools loaded: LAYERCOUNT, RECTC.
```

RECTC with the centre 0,0, a width of 200 and a height of 100 draws a rectangle from -100,-50 to 100,50; with Enter at both
questions it is 1 000 by 500 about the centre. RECTC adds the rectangle with `add_polyline(..., closed=True)` and writes nothing itself, except for a width or a height of 0 or
less, which it refuses with `The width and the height must be more than 0.` and draws nothing (a rectangle of no width is a
polyline with nothing inside it). On a drawing with one rectangle on layer `0`, LAYERCOUNT writes
`0: 1`; an object on `walls` counts with the layer the table calls `Walls`.

[!code-python[](../src/python/examples/tools/drawing_tools/__init__.py)]

### The length of a selection

`selection_lengths`, run with its command SELECTIONLENGTHS, asks `Select objects:` and adds up the length of the lines, arcs, circles (their
circumference) and polylines, and the area of the circles and closed polylines. Anything else is counted as skipped. On a
line from 0,0 to 300,400, a circle of radius 120, a closed polyline rectangle 100 by 50 and a text, all selected, it writes

```text
4 objects: length 1553.982, closed area 50238.934, 1 skipped
```

The length is the line's 500, the circle's 753.982 and the rectangle's 300; the area the circle's 45 238.934 and the
rectangle's 5 000. With nothing selected it writes `Nothing selected.`

[!code-python[](../src/python/examples/tools/selection_lengths/__init__.py)]

### A snap, a setting and a button for circles

`circle_marks` is the one tool here that adds to the window, with the three files of a tool and what its `plugin.json` says besides its
command. Its snap CTOP is the top of a circle: `@snap("CTOP")` is bound to the `"snaps"` entry with that keyword, which names it
"Circle top", limits it to `CIRCLE` and gives it the marker of the quadrant snap, and the app asks the tool about a circle the cursor
comes near once, until the drawing changes. For a circle of radius 25 about (100, 50) the point is (100, 75). Its setting `textHeight`
(2.5, from 0.1 to 1000) is shown in Load/Unload Applications, Settings..., and the command MARKTOPS, which asks `Select objects:` (or
takes the objects selected), writes a text `R25` of that height at the top of each selected circle: 2.5 high until the user changes it.
A ribbon tab Circles holds one large button, Mark tops, that starts MARKTOPS. With one circle selected it writes

```text
Marks written: 1, 2.5 high.
```

Anything but a circle in the selection is passed over, and with no circle selected it writes `No circle selected.` and draws nothing. The
snap and the setting need the app and its `plugin.json`; a test of the repository starts the tool's process, asks the snap as the app does
and runs the command.

[!code-python[](../src/python/examples/tools/circle_marks/__init__.py)]

## Driving a running app

These files connect to OpenCAD2D from another process with `connect()` ([Driving the app from another program](presenter/remote.md)),
so the app has to be running with "Allow other programs to connect" on ([the setting](presenter/remote.md#the-setting)). Each
has a `main` that works on the `App` it is given, the `Drawing` API over the connection,
and a `__main__` block that makes the connection with `with connect() as cad:`, which closes it at the end. [From Python](presenter/remote.md#from-python) tells how
the .NET calls look in Python. The numbers below come from the test server of the repository, an app with no window.

### Draw a grid and read it back

`draw_and_read.py [OUT]` starts a new drawing, adds a layer `Grid` and 11 vertical and 11 horizontal lines over a
square of 1 000 at a spacing of 100, commits them as one undo step, reads them back with `drawing.entities(DxfLine, layer="Grid")`
and, when `OUT` is given, saves the drawing.

```text
22 lines on Grid, total length 22000.000
saved grid.dxf
```

[!code-python[](../src/python/examples/remote/draw_and_read.py)]

### Pick a point and mark it

`pick_and_mark.py` asks the user for a point (`Pick the centre`) and waits up to 60 seconds, then draws a circle of radius 50
there and zooms to the extents. The pick is made on `drawing.net`, the `CadRemoteDocument`, because `PickPoint` there takes the time limit that `drawing.pick_point` does not. A point of 120,80, clicked or typed on the app's command line, prints

```text
circle at 120.000,80.000
```

and the script returns 0. Esc in the app throws a `CadRemoteException` whose `IsCancelled` is true; the script prints
`Cancelled.` and returns 1. Any other exception, a timeout among them, is raised again. The time limit is there because
Ctrl+C does not end a call into .NET that waits for the user ([From Python](presenter/remote.md#from-python)).

[!code-python[](../src/python/examples/remote/pick_and_mark.py)]

### The open drawings

`open_documents.py` lists the drawings the app has open, in the app's order: the name, with ` *` after it for the drawing the
user is working in, the number of objects in model space, and the count of each class of object, the classes sorted by name.
With a drawing of two lines and a circle and a new, empty one shown on top it prints, tab-separated,

```text
Drawing1.dxf	3	DxfCircle:1, DxfLine:2
Drawing2.dxf *	0	
```

[!code-python[](../src/python/examples/remote/open_documents.py)]
