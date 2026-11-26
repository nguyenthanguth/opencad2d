# Python examples

Eighteen examples for the three places Python works with OpenCAD2D and its core library GeometryHelper: outside the app on the libraries, as a Python tool
of OpenCAD2D (a folder with a `requirements.txt`, an `__init__.py` and a `plugin.json`), and outside it driving a running app. Each does one common task, and a test of the repository runs
each against a drawing it builds, on .NET 8 and on .NET Framework 4.8, so they keep working. They are not part of the
wheel. The guide, [Python examples](../../../docs/python-examples.md), shows what each prints.

| File | Place | Task | How to run |
|---|---|---|---|
| [library/list_layers.py](library/list_layers.py) | outside the app | the objects and the closed area on each layer of a drawing | `python list_layers.py plan.dxf` |
| [library/coordinates_numpy.py](library/coordinates_numpy.py) | outside the app | the vertices of every polyline as numpy arrays, and their extents | `python coordinates_numpy.py plan.dxf` |
| [library/make_drawing.py](library/make_drawing.py) | outside the app | a drawing made in code, with a layer, a rectangle and a circle, saved as DXF or DWG | `python make_drawing.py out.dwg` |
| [library/regions.py](library/regions.py) | outside the app | union, difference, intersection and offset of two squares | `python regions.py` |
| [library/solids.py](library/solids.py) | outside the app | a box with a hole cut through it: volume, surface area and centroid | `python solids.py` |
| [library/batch_folder.py](library/batch_folder.py) | outside the app | a CSV report of every DXF and DWG file of a folder | `python batch_folder.py folder report.csv` |
| [library/convert.py](library/convert.py) | outside the app | a DXF or DWG written as a DXF or DWG, in a version | `python convert.py in.dxf out.dwg --version AC1027` |
| [library/render_views.py](library/render_views.py) | outside the app | a drawing drawn with no window to two PNGs, an SVG and a PDF | `python render_views.py plan.dxf views` |
| [library/batch_export.py](library/batch_export.py) | outside the app | every DXF and DWG of a folder prepared for a model: JSON, graph arrays and a picture, with a manifest of counts and timings | `python batch_export.py folder out` |
| [library/layout_loop.py](library/layout_loop.py) | outside the app | an AI's generate, render, evaluate, refine loop that places tags and dimensions on a sheet, with the reward it is scored by | `python layout_loop.py out` |
| [library/coco_dataset.py](library/coco_dataset.py) | outside the app | three drawings built in code written as pictures, class masks and one COCO file, for training a vision model | `python coco_dataset.py dataset` |
| [tools/number_points/](tools/number_points/) | a Python tool | a number written at each point you pick | PYLOAD its `requirements.txt`, then NUMBERPOINTS |
| [tools/drawing_tools/](tools/drawing_tools/) | a Python tool | the commands RECTC and LAYERCOUNT | PYLOAD its `requirements.txt`, then RECTC or LAYERCOUNT |
| [tools/selection_lengths/](tools/selection_lengths/) | a Python tool | the length and closed area of the selected objects | PYLOAD its `requirements.txt`, then SELECTIONLENGTHS |
| [tools/circle_marks/](tools/circle_marks/) | a Python tool | a snap (CTOP, the top of a circle), a setting, the command MARKTOPS and a ribbon button | PYLOAD its `requirements.txt`, then MARKTOPS or the button Mark tops |
| [remote/draw_and_read.py](remote/draw_and_read.py) | a script driving the app | a grid drawn in a new drawing and read back | `python draw_and_read.py [out.dxf]` |
| [remote/pick_and_mark.py](remote/pick_and_mark.py) | a script driving the app | a circle drawn where the user picks | `python pick_and_mark.py` |
| [remote/open_documents.py](remote/open_documents.py) | a script driving the app | the open drawings and what is in each | `python open_documents.py` |

The library scripts run in the virtual environment the wheel is installed in (`pip install opencad2d[numpy]`, then
`python <file>`). Each folder in `tools` is a tool: PYLOAD its `requirements.txt` (or put the folder in the `Pythons` folder of the app), and
its commands are there; its Python process starts the first time one is typed. The
scripts in `remote` need OpenCAD2D running, with "Allow other programs to connect" on in Global settings; each takes the `App` that
`connect()` gives. `library/batch_folder.py` and `library/convert.py` stay on `DxfDatabase` and the writer: they need the DXF version and the
writer's warnings, which the Drawing API does not give.
