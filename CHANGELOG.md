# Changelog

## Unreleased

- **NEW.** The MCP server's `run_plan(steps, dry_run, stop_on_error)` runs a list of tool calls in one call and gives each step's answer
  (`{id, tool, ok, result or error}` and totals), which cuts an agent's round trips: the place, render, score, refine loop of a plate takes 4
  calls as plans where it took 19 as single calls, over a file and over an app, with the same result. A step may name any tool the server
  serves, so tools added later work in a plan unchanged; its arguments are checked against the tool's schema first and a plan with any
  wrong step runs nothing and says all of them. A string argument that is exactly `$id.path` (`"$tag1.handle"`, `"$free.boxes[0]"`)
  is replaced by that value of an earlier step's answer. All the writing steps are one undo step (one group of edits on a file; attached,
  the app's own UNDO BEgin and End typed on its command line, with a snapshot taken first), so one undo takes them all back; the first
  step that fails stops the plan and the drawing is put back as it was (`stop_on_error` false keeps what worked), and `dry_run` runs the plan
  and puts it all back to show what it would give, handles included. `run_plan`, `open_drawing`, `new_drawing`, `undo`, `save`
  and `save_as` are not allowed inside a plan; `run_command` only with `--allow-scripts`, never on an app, and its text is typed as written with no reference read in it; an app that is busy with a command is never typed into (the plan is refused); `--read-only` refuses a plan with a writer before
  anything runs.
- **NEW.** The MCP server gives an AI agent the place, render, score, refine loop that Python already had (PW7.5), on a file and on an
  app attached with `--attach`. `measure_text`, `measure_mtext`, `measure_dimension` and `measure_leader` give the box and the word
  frames a thing would take before it is added, nothing added and no undo step made (for a text the box equals what `bounds` gives once it
  is added, to 0.001 mm); `free_space` lists the empty rectangles of a frame, the largest first, paged; `fits` tests one box or one
  turned frame; `check_standard` scores the drawing against a company standard given as a JSON object or the path of a file, with the
  score of each rule and the violations a page at a time; `export_json` and `export_graph` give a summary of the drawing's document or
  graph and a page of its entities, edges or nodes; `export_to_file` writes the whole of either to a path and is the one new tool
  `--read-only` refuses. A frame's rotation is in degrees, as every angle of the server is. A scripted session in the tests adds two
  tags across a line (3 overlaps, score 0.778), measures them, finds the free places, moves each into the largest, and ends with 0
  overlaps, score 1.000 and a PNG, over a file and over an app. The server has 43 tools; the guide shows the loop as a session.
- **FIXED.** The description of `render_png` said an attached server draws the saved file; it draws the app's drawing as it is, changes
  not saved included, as the guide says.
- **NEW.** A fast snapshot and reset of a drawing in memory, for a program that runs thousands of episodes of edits from the same start
  (PW7.3). `drawing.snapshot()` keeps the drawing as it stands (a few milliseconds the first time) and `drawing.reset(snapshot)` puts it
  back to the byte: the JSON export, the objects and their handles (the next object made gets the handle it got the first time, as the
  handle seed comes back), the layers, styles, linetypes, blocks and layouts, and the header variables such as the current layer and
  styles. The display, the cached spatial index and the pictures of dimensions are drawn again only where the episode touched them: a
  drawing of 10 000 objects after 100 random edits resets in about 0.2 ms and draws again in 1 to 3 ms (the first reset to a snapshot
  takes 4 to 6 ms; opening the file and drawing it takes 20 to 40 ms), and 1 000 episodes of 100 edits each, ended by a reset, leave the
  managed memory exactly where the first hundred put it. It works on the undo list the document already keeps: a snapshot is a point of
  it and a reset one jump back to the point, the steps since forgotten (not made one more step, so memory does not grow, and what the
  user did before the snapshot stays undoable), with the drawing of the next reset read ahead on a worker thread while the episode runs.
  Where the list no longer holds the point (the drawing was undone below it and edited again) the reset reads the drawing back from
  the snapshot's own bytes: as exact, every display list drawn again, the undo list started afresh. Refused while a command runs in the
  drawing and while the Block Editor is open. On a file, in the app and from outside it (the requests `snapshot`, `reset` and
  `releaseSnapshot`; `snapshot.release()` lets the app forget one). In C#: `CadDocument.TakeSnapshot`, `CadDocument.Reset`,
  `CadSnapshot`, and `CadRemoteDocument.TakeSnapshot`, `Reset`, `Release` with `CadRemoteSnapshot`.
- **CHANGED.** The layouts of a new drawing are in millimetres on a real sheet. The library's own new drawing has Layout1 with the paper
  in inches and a sheet of 0 by 0, which the export (`toMillimetres` 25.4), the standards checker (text heights on paper 25.4 times
  too large) and the renders read as inches. The app's NEW (the presenter's metric template, `CadTemplate.CreateMetric`), Python's `new_file()`
  and the MCP server's new drawing now give Layout1 as AutoCAD's acadiso.dwt has it: ISO A3 on its side, 420 by 297 mm, in
  millimetres, plot area the layout, through the new `CadTemplate.UseMillimetreSheet`; a text 4 high on it is 4 on paper and its
  `toMillimetres` is 1. The imperial template and every drawing read from a file keep their layouts as they are, and a space with
  no sheet (model space, a layout of a drawing made in code) is still fitted to the page in a PDF.
- **CHANGED.** The `box` of an entity in the JSON export (`to_json`, `to_dict`, `flatten`, the nodes of `to_graph`, and
  `CadDrawingJson`, `CadDrawingGraph`) is the box the entity is drawn in, the one `entity.bounds` and `CadSpatial.Bounds` give: a
  word by its letters, a dimension by its picture, a polyline by its width, to 1e-6. A piece of a block reference, which the display
  list holds no box of apart from its reference's, and an entity its space draws nothing of (on a frozen layer) keep the library's
  extents. The schema stays version 1 (the shape is unchanged; the description of `box` says what it is).
- **NEW.** `to_graph(max_edges=None)` caps the edges of the graph, so a dense drawing does not take as many edges as it has crossings
  (600 lines across 600 others: 360 000 touches in 0.7 s, or the first 50 edges in 1 ms). The graph keeps the first `max_edges`
  edges of the whole one in its order (kind, source, target), stops once it has found more, and says so: `truncated` and
  `edgesFound` (the edges found when it stopped) are in every graph, `False` and the number of edges where it was not cut. The same
  on a file, in the app and from outside. In C#: `CadDrawingGraph.Build(document, layout, tolerance, maxEdges)` and
  `Build(flat, tolerance, maxEdges)` with `Truncated` and `EdgesFound`, the request member `maxEdges` of `export`, and
  `CadRemoteDocument.ExportGraph(layout, tolerance, maxEdges)`.
- **FIXED.** What Python measures of words on a drawing of a file no longer depends on whether a picture was drawn first in the
  process. `measure_text`, `measure_mtext`, `measure_dimension`, `measure_leader`, `free_space`, `fits`, `bounds`, `entity.bounds`,
  `overlaps` and `check` read a drawing that had not been set up as the app sets one up on opening it (the
  fonts of the default settings, the canvas's measure of TrueType letters), and the first `render_*` call of the process set that
  measure for every drawing in it: an MTEXT 50 wide of 4 high words in an SHX style of a `new_file()` drawing was 45.6 wide before the
  first picture and 31.2 after, where the app gives 45.8. The calls now set the drawing up first (what `CadRender.Prepare` does), so the
  box is 45.8 before and after, and the same as the app's; the Remote test server sets its drawings up the same way.
- **NEW.** A guide and two examples for batch and AI pipelines (PW6.4). The section "Batch and AI pipelines" of the Python guide shows
  how reading, normalising, feeding a network, rendering, evaluating and placing fit together in one small loop that runs as it is
  written, with the timings of each step for a drawing of 2 000 objects on .NET 8 and on .NET Framework 4.8, and a paragraph on driving
  OpenCAD2D from an LLM through the MCP server. `batch_export.py` prepares a folder of DXF and DWG files for a model: for each, the
  JSON (`flatten` and `to_json`), the graph edges and nodes and the polyline vertices as NumPy arrays (`opencad2d.arrays`), a PNG,
  and a line of `manifest.jsonl` with the counts, the unit the drawing was drawn in (read from its header with `GeometryHelper.Dxf`),
  the seconds of each step and the error of a file that failed, the next file going on. `layout_loop.py` is the generate, render,
  evaluate, refine loop of an AI that places annotation: twelve tags and two dimensions on a sheet are scored by a `reward` built from
  `check` and `overlaps` and moved to the nearest free place found with `measure_*`, `free_space` and `fits`, seeded, until the score
  stops rising (0.321 to 1.000 in one round for the seed 1), with `before.png` and `after.png`. Each is run by a test on drawings built
  in code. The examples are now eighteen.
- **CHANGED.** The Python render calls work on a drawing that is in the app: `render_png`, `render_svg`, `render_pdf`, `render_image`,
  `render_transform`, `render_masks` and `to_coco` of a drawing from `connect()` or of `active_drawing()` in a tool no longer raise
  `RuntimeError("... works on a drawing of a file ...")`. The call asks the app for its whole drawing (the request `database`, the
  changes not saved included), reads it into a `CadDocument` of the Python process and draws it with the code a file is drawn by, so the
  PNG of a drawing in the app is, byte for byte, the PNG of the same drawing saved and opened as a file; a line added and not yet
  committed is in it, a call naming no layout draws the tab the app shows, and the app's file is never written. A copy is taken at each
  call: for a drawing of 10 000 objects a PNG takes 0.16 s against 0.08 s for a file, `render_image` 0.07 s against 0.02 s. `to_coco` takes one
  copy for its labels and its picture.
- **NEW.** Labelled renders for training vision models on drawings (PW7.4). `drawing.render_transform(...)` is the 3 by 3 matrix, a
  numpy array, from the drawing to the pixels of the picture `render_png` and `render_image` make: the view the renderer draws by,
  taken from the same code path, so a point of a line maps to a pixel the line coloured (a drawing 400 by 300 at 1 600 by 1 200 pixels:
  `[[3.6, 0, 80], [0, -3.6, 1140], [0, 0, 1]]`). `drawing.render_masks(classes=..., instances=False, ...)` gives a `RenderMasks`: the
  class of every pixel (`words` 1, `dimensions` 2, `lines` 3, `hatches` 4, `blocks` 5, 0 the ground; a `uint8` array of the picture's
  shape) and with `instances=True` the object that draws it (an `int32` array, `handles` naming the objects), drawn by the canvas's own
  renderer in flat ids with no antialiasing and in the order the drawing draws, so what is on top in the picture is on top in the
  masks. `drawing.to_coco(image_path, classes=..., segmentation=False, ...)` writes the picture and gives its objects as a COCO
  detection `dict` (images, annotations with `bbox` in pixels from the objects' bounds through the matrix and widened to hold every
  pixel of the object, `area`, `iscrowd` 0 and optionally the turned frames of the words, and the five fixed categories). A 10 000
  object drawing at 1 600 by 1 200 pixels: the masks in 0.08 s and the COCO file with its PNG in 0.2 s on .NET 8 and on .NET
  Framework. In C#: `CadRender.Transform`, `CadRender.DrawLabels`, `CadRenderLabels` and `CadLabelClass` in `OpenCAD2D.Rendering`.
  On a drawing of a file and, since the render calls reach the app, of the app. The example `coco_dataset.py` writes
  pictures, class masks and one COCO file of three drawings built in code.
- **CHANGED.** The renderer of the canvas has an internal id mode (`CanvasOptions.IdOf`) the labelled renders use; it is off by
  default and no picture of the app, the PNG, the SVG or the PDF changes by a byte.
- **NEW.** A drawing standards checker, for the reward of a program that lays a drawing out (PW7.2). A company writes its standard once
  as a JSON file (`docs/schemas/opencad2d-standard-v1.schema.json`, `opencad2d.standard` version 1, with an example beside it) and
  `drawing.check(standard)` scores any drawing against it: a score from 0 to 1 for each of 14 rules (`layer-name`, `layer-properties`,
  `layer-zero`, `by-layer-color`, `by-layer-linetype`, `by-layer-lineweight`, `text-style`, `text-font`, `dim-style`, `dim-font`,
  `text-height`, `gap-word-line`, `gap-word-word`, `dim-chain`), a total with the weights of the file, and every violation with its rule,
  objects, box, the number found and the number allowed, in a fixed order. A rule scores `1 - offenders / subjects`; a rule the file
  does not mention is not run. Words are measured on paper: in a layout as drawn, in model space through each viewport that shows
  them at its scale (annotative objects at the annotation scale the viewport draws them at), or at the `model_scale` given (`0.01`
  or `"1:100"`); gaps are exact distances between a word's frame and a line, an arc, a hatch or another word, found through the
  spatial index; a chain is dimensions of one direction that meet end to end, whose dimension lines should lie on one line. 10 000
  objects are checked against every rule in about 0.4 s on .NET Framework 4.8. Python gives a `CheckResult` (`scores`, `total`,
  `rules`, `violations`, NumPy `boxes`) on a file, in the app and from outside it. In C#: `CadStandard`, `CadStandards.Check`,
  `CadStandardResult`, and `CadRemoteDocument.CheckStandard` (the request `check`).
- **NEW.** Python measures before it places, for a program that lays a drawing out: `drawing.measure_text`, `measure_mtext`,
  `measure_dimension` and `measure_leader` give the box a thing would take and the frame of each of its words (a `Measurement`:
  `box` and `frames`, each a `Frame` of a first corner, a width, a height and an angle), without adding it, by the code that draws
  and measures the thing once it is added, so the box equals `entity.bounds` of the thing added to 1e-6 (SHX and TrueType words,
  turned and justified, the lines of an MTEXT, a dimension's picture in its style, a multileader with its landing).
  `drawing.free_space(box, kinds, margin, min_width, min_height)` gives the empty places of a frame, the maximal empty rectangles
  (a box lies in one exactly when it fits), the largest first, and `drawing.fits(candidate, kinds, margin)` says whether a box, a
  turned frame or a measurement is free of what is drawn, tested on the real lines, arcs, fills and word frames and not their boxes.
  On a file, in the app and from outside. 100 000 objects: `fits` 0.2 ms a call, a TEXT measured in 0.6 ms and a dimension in
  2.3 ms, the empty places of a 300 by 300 frame in 0.4 s and those at least 20 by 20 of the whole drawing in 1 s.
- **NEW.** `CadMeasure` in the core: `Of(document, entity)` and `Leader(document, style, arrow, landing, words, scale)` give a
  `CadMeasurement` (a `DisplayBox` and the `CadFrame` of each word) of a thing not in the drawing, without touching the drawing;
  `CadSpatial.FreeSpace(document, space, frame, options, tolerance)` gives the maximal empty rectangles as a `CadFreeSpace`
  and `CadSpatial.Fits(document, space, candidate, options, tolerance)` whether a `CadFrame` is free, with the kinds, margin
  and least size of a `CadFreeSpaceOptions`. The client library of a running app has `Measure`, `MeasureLeader`, `GetFreeSpace` and
  `Fits` (`CadRemoteMeasurement`, `CadRemoteFreeSpace`), which are the new requests `measure`, `measureLeader`, `freeSpace` and
  `fits` of the channel.
- **NEW.** `OpenCAD2D.Mcp.exe`, an MCP server over stdio, installed beside `OpenCAD2D.exe`: an AI agent (Claude Desktop, Claude
  Code, Cursor) opens a DXF or DWG file (`--file`, or `open_drawing`, in the server's own process, no app and no window) or
  attaches to the running app through its Remote channel (`--attach`), and reads (`info`, `list_layers`, `list_text_styles`,
  `list_dimension_styles`, `list_blocks`, `query_entities`, `bounds`, `overlaps`), draws (`add_line`, `add_polyline`, `add_circle`,
  `add_arc`, `add_text`, `add_mtext`, the five `add_*_dim`, `add_hatch`, `add_insert`), changes (`move`, `rotate`, `scale`,
  `erase`, `set_properties`, `undo`), looks at the result (`render_png`, an image drawn as the app's canvas draws it) and saves.
  Each writing call is one undo step. `--read-only` refuses every tool that writes, and `run_command`, which types free
  command or AutoLISP text, is refused unless the server was started with `--allow-scripts`; only `save` writes a file. It is a
  project of its own, `src/OpenCAD2D.Mcp` on .NET 8, with the official C# SDK `ModelContextProtocol.Core` 2.4.0 (Apache-2.0),
  which stays out of `OpenCAD2D.Core`, its NuGet package and the Python wheel. `add_layer` makes a layer with a look. Attached,
  `render_png` draws the app's drawing as it is now, unsaved changes included, and never writes its file. See
  [Letting an AI agent drive OpenCAD2D](docs/presenter/mcp.md).
- **NEW.** `CadRemoteDocument.GetDatabase()` gives a copy of the whole drawing of a running app as it is now, unsaved changes included
  (header, tables, blocks, layouts with their viewports, every object), read from the DXF text the app writes of its database: the new
  request `database` of the channel. The app's file is not written. 10 000 entities, 1 070 KB of text: about 20 ms.
- **NEW.** Real drawings read for AI pipelines (PW6.3). `drawing.flatten()` walks every block reference of a drawing, nested ones and
  arrays of them (MINSERT) included, to the objects it draws, in world coordinates and millimetres, each with its handle, the path of
  block names it came out of, the 4 by 4 matrix that placed it, and the layer, colour and linetype it is drawn with (what is on layer 0
  or ByBlock takes the reference's, as BURST gives it); the shapes are the ones EXPLODE gives, so a circle under a scale unlike along
  its axes is an ellipse, and the drawing is not changed. `drawing.to_json()` (and `to_dict()`) is the whole drawing as one document
  of schema `opencad2d.drawing`, version 1, with a JSON Schema in `docs/schemas`, validated by a test: entities in handle order,
  tables by name, numbers to a nanometre, the same text on every run and on both runtimes. `drawing.to_graph(tolerance=0.001)` is
  the drawing as a graph for a GNN or a transformer, a node for each object and edges that `touches` (ends within the tolerance, or
  crossing), `contains`, `dimensions` (DIMASSOC, else the points of the dimension) and `annotates` (a leader's arrow), found with a
  grid over the boxes (10 000 objects in 0.25 s on .NET 8, 0.4 s on .NET Framework), with `edge_index` and `edge_kind` numpy arrays. Units are normalised to millimetres
  from INSUNITS (paper space from the plot paper units); a drawing of no unit is left as it is and says so; a block drawn in another
  unit, a clipped or external reference and a reference that could not be walked are listed in the document's `report`. The same on a
  file, in the app and from outside it. In C#: `CadFlatten`, `CadUnitScale`, `CadDrawingJson` and `CadDrawingGraph` of the core,
  and `CadRemoteDocument.ExportJson` and `ExportGraph` (the request `export`).
- **FIXED.** On a drawing file with no app, a layer switched off or frozen by Python (`drawing.layers["Steel"].is_on = False`, then
  `commit()`) left the drawing's display list as it was, so what the display list feeds (`entity.bounds`, `drawing.bounds()`,
  `drawing.overlaps()`, picks) still counted the layer's objects: the commit wrote the layer but told the document nothing for a
  layer that already existed. It now tells it, and the spaces are drawn again: the objects of a layer switched off or frozen have no
  bounds and no overlaps, and are back when it is switched on or thawed. In the app and from outside the app this already worked.
- **NEW.** Python asks where things are drawn, for a program that lays out a drawing and scores it: `entity.bounds` is the box an
  object takes (`Box(min_x, min_y, max_x, max_y)`, `None` where nothing is drawn), `drawing.bounds(layout=None)` the boxes of a
  whole space as a `BoundsTable` (`handles` and `boxes`, a NumPy array of n by 4), and `drawing.overlaps(kinds, over, layout, of,
  tolerance)` the pairs whose words overlap what is near them as an `OverlapTable` (`words`, `others` and the `boxes` of the
  places they meet). The boxes are the display list's, so an SHX or TrueType word is its letters and not its insertion point, a
  dimension its lines, arrows and words, a block however deep and scaled its contents, an MTEXT its columns and a wide polyline
  its width; a TrueType TEXT, whose display width is a guess of 0.6 of the height a letter, is measured by its font. A word is
  its turned frame (an MTEXT run by run, so the gap between two columns is no word); it overlaps a line, arc, polyline, hatch
  edge or filled area that runs through it and another word it shares an area with, and not what only touches it within the
  tolerance or a part of its own object. On a file, in the app and from outside. 100 000 objects: the overlaps in 0.3 to 0.6 s,
  the pairs of one object in a few milliseconds.
- **NEW.** `CadSpatial` in the core: `Bounds(document, space)`, `Bounds(document, handles)` and `Overlaps(document, space, options,
  tolerance)` give `CadBoundsTable` and `CadOverlapTable` (handles and four numbers a row, as arrays), over an STR-packed R-tree
  made once for each display list. The client library of a running app has `GetBounds`, `GetBoundsOf` and `GetOverlaps`
  (`CadRemoteBounds`, `CadRemoteOverlaps`), which are the new requests `bounds` and `overlaps` of the channel.
- **NEW.** `CadRender`, `CadRenderOptions` and `CadRenderImage` in `OpenCAD2D.Rendering` (namespace `OpenCAD2D`): a drawing of a
  file drawn with no window as the app's canvas draws it. `CadRender.Draw` gives the pixels (red, green and blue, rows from the
  top), `WritePng` a PNG of colour type 2, `WriteSvg` the same picture in vectors (at the screen's level of detail: an item
  under half a pixel is a dot, a text under 3 pixels is left out) and `WritePdf` the plot of EXPORTPDF, one page, full detail.
  A layout or model space, a part of it (`Box`), a size in pixels or a `Dpi` (an A3 sheet at 96 is 1 587 by 1 123 pixels), and
  a black or white ground. `CadRender.Prepare` gives a drawing the fonts and measure the app gives it on opening. The pixels
  are the canvas's: ten drawings of every kind of entity differ by 0 bytes from the app's.
- **NEW.** `--screenshot out.png plan.dxf --canvas canvas.png` also writes the drawing canvas's own picture, the pixels under
  the UCS icon, grips and crosshair, as a PNG, and says where the canvas lies in the window; with it an image is read before
  the picture is drawn, so the image is in it and not its frame alone.
- **CHANGED.** The canvas renderer and the PDF and raster writers moved from `OpenCAD2D.Wpf.dll` into a new
  `OpenCAD2D.Rendering.dll` (netstandard2.0, no WPF), so they can draw without a window. Their namespace stays
  `OpenCAD2D.Wpf` and `OpenCAD2D.Wpf.dll` forwards the five public types (`CanvasOptions`, `SkiaCanvasRenderer`,
  `CadPdfWriter`, `CadPdfBookmark`, `CadRasterWriter`), so no plugin, built or given as source, breaks.
- **NEW.** `drawing.render_png(path)`, `render_svg(path)`, `render_pdf(path)` and `render_image()` of `opencad2d.cad` draw a drawing of a
  file (`open_file`, `new_file`) with no app and no window, as the app's canvas draws it, on .NET 8 and on .NET Framework 4.8: a part
  of the space (`box`), a size in pixels (`width`, `height`) or pixels to the inch of a layout's sheet (`dpi`), a `"black"` or
  `"white"` ground, a layout; `render_image` gives a numpy array of shape `(height, width, 3)`, `uint8`. A drawing of the app
  (`active_drawing()`, `connect()`) is drawn too, since the render calls reach the app. The renderer, SkiaSharp and its native
  library load at the first render call and not at `import opencad2d`; they add 5.85 MB zipped to the wheel (16.4 MB).
  `opencad2d.arrays.to_numpy` also copies a .NET `Byte` array, as `uint8`. The stubs carry `CadRender`, `CadRenderOptions` and
  `CadRenderImage` and leave out the `OpenCAD2D.Wpf` namespace of `OpenCAD2D.Rendering`. The example `render_views.py` writes two
  PNGs, an SVG and a PDF of a drawing; the guide has "Rendering without a window".
- **FIXED.** `CadRender.WritePdf` of a space that was never given a sheet (a drawing made in code, `new_file()`) plotted at 1 inch to
  the unit and ran off the page: it is fitted to ISO A3, as AutoCAD fits a page setup it has not been given (a 400 by 300 drawing is
  at 0.9567 millimetres to the unit, not 25.4); a space with a page setup keeps its scale.
- **FIXED.** `CadRender` sized the picture of a layout whose paper units are inches as if they were millimetres: a sheet of 16.54 by
  11.69 inches at 96 dpi was 63 by 44 pixels and is 1 587 by 1 123, the size of the same sheet in millimetres.
- **FIXED.** `drawing.add(entity)` of a text, an MTEXT or a line that names a text style or a linetype the drawing has failed at
  `commit()` (`The text style NOTE is not in the file's text style table.`, `The linetype DASHED is not in the file's linetype
  table`), on a file, in the app and from outside: the commit appended the copy to a small drawing that had none of the
  drawing's records. The small drawing is now given a record of each name the object brings and the drawing has (or the commit
  adds), and the drawing keeps its own record of the name when the copy goes in: a text in NOTE (romans.shx, 0.8 wide) is in NOTE
  after the commit, the style unchanged and the table still of two styles. The client library of a running app (`CadRemoteDocument.Insert`)
  did the same and does it no more. An object that names a style or linetype nobody has is a `ValueError` naming the call that adds
  it, the same on every backend. (A dimension in a dimension style the drawing has did not fail on this code.)
- **NEW.** Python makes styles, linetypes, dimensions, hatches and blocks with the Drawing API, queued with the objects that use them and
  applied as one undo step on a file, in the app and from outside: `text_styles.add(name, font, x_scale, height)`,
  `dimension_styles.add(name, text_style, scale, text_height, arrow_size, **variables)` (it starts from ISO-25's variables; `Dimdec=1`
  or any other variable by its library name sets one more), `linetypes.load(name, file)` (the built-in set, in the units of the
  drawing, or a `.lin` file), `blocks.add(name, entities, base_point)`, `current_text_style`, `current_dim_style`,
  `add_text` and `add_mtext` with `text_style`, `add_linear_dim`, `add_aligned_dim`, `add_radial_dim`, `add_diameter_dim`,
  `add_angular_dim` (the `*D` picture is drawn as DIM draws it), `add_hatch(boundary, pattern, scale, angle)` and `add_insert`. A commit of
  the records, the block and the objects that use them is one `U`. A guide section ("Styles, linetypes, dimensions, hatches and
  blocks") with code that is run as a test, and how to write below the calls inside the undo step (`CadDocument.ApplyEdit`).
- **CHANGED.** `drawing.text_styles`, `dimension_styles` and `blocks` are tables with `add` (`blocks.add`, `text_styles.add`...), not
  lists: they iterate, take `len`, `in` and `[index]` or `[name]` (ignoring case) as before, but are no longer a `list` (`.sort()`, `+`).
- **NEW.** The client library of a running app writes table records in a commit: `CadRemoteDocument.AddRecords(database)` queues the
  text styles, linetypes, blocks and dimension styles of a drawing of yours, written before the layers and the objects, and
  `Linetypes` and `CurrentDimensionStyle` read the linetypes and read and set the current dimension style. The core has
  `CadTableRecords.Add`, which brings named records of one `DxfDatabase` into another without the defaults a new one is made
  with, and `CadDimStyleEditor.Write` is public: it writes a dimension style's variables by their names.

- **NEW.** The Edit Reference panel of AutoCAD 2021's REFEDIT tab: while a reference is open for editing the ribbon shows it at
  the right end of whichever tab is shown (Home, Insert, any), four large buttons under a green title, Save Changes and Discard
  Changes (REFCLOSE Save, REFCLOSE Discard), a line, Add to Working Set and Remove from Working Set (REFSET Add, REFSET Remove),
  with four new icons from the A13 kit. The strip of the tab measures in the width left of it (Home at 1 920 takes steps it did
  not before) and the panel goes at REFCLOSE. `MainViewModel.IsRefEditOpen` (new, read only) says whether the drawing shown has
  an edit open; it is raised as an edit opens or closes and as another drawing is shown.
- **CHANGED.** Every dialog that draws its own footer ends in one order, AutoCAD 2021's: the primary answer first, then the other
  answers of a question, Cancel or Close, Apply, Help. 54 footers were reordered (OK before Cancel in most; Save, Don't
  save, Cancel in the save question; Yes before No in the package question; Apply, OK, Cancel kept in the Attribute Editor and
  the Block Attribute Manager as AutoCAD has them; OK, Cancel, Apply in Global settings and in the View Manager). Every footer
  text button is 75 wide at least with a padding of 14 and 5 (the 94 of 318 that had another padding, and the 7 of the UCS
  dialogs and the plugin settings with a fixed width of 75, are changed), and the 13 footer Close buttons that were the accent
  or plain are the orange-red Close style the managers have.
- **CHANGED.** One colour for a problem, `GhProblem` (#FF8A7A, 6.2 to 1 on the dialog panel and 5.5 to 1 on the footer): the
  red of `GhError` (#F85149, 4.2 and 3.7 to 1) and the orange #F26B3A and #FF8A7A written by hand or kept as a local brush in
  seven dialogs are gone, 81 uses in 56 files, and `GhError` and `GhErrorColor` are removed from `Dark.xaml`. The two Plot
  Styles messages that report a failure were yellow and are the problem colour. Open folder of the Plot Styles dialog is Open
  Folder.
- **FIXED.** Ten dialogs that draw their own header could not be dragged by it (Save question, Column Settings, Compare Dimension
  Styles, Background Mask, Text Size, New Layer State, Paragraph, Edit Scale, Style Name, the UCS notice). The Plot Style Table
  Editor's Table View header started 8 pixels left of its cells, did not scroll with them, and a style with no colour shifted its
  row 19 left; its Form View used a mono box with no gap between rows. The lists of the Plot Styles dialogs and the Select
  Template list were black or unframed where the other dialogs' are on the box ground.
- **NEW.** The ribbon is laid out at 120 and 144 dots per inch in the tests: Home takes the same steps at the same strip
  widths, no label of any tab, slide-out, collapsed panel's drop-down or the Insert gallery is cut, and the pin and Esc work.
  Home's panels are no wider than their design (Block 161 against 160; sum 1 695).
- **NEW.** Ctrl cycles the options of a multi-functional grip, as AutoCAD 2021 does with GRIPMULTIFUNCTIONAL's bit 1 set
  (1 or 3; the hover menu is bit 2, so 2 has the menu alone and 0 neither). With a grip clicked and the edit waiting for its
  first point, each tap of Ctrl (down and up with no other key or mouse button between, the cycle made at the key up) goes on to the next item of the grip's menu and round again: a polyline's vertex Stretch,
  Add Vertex, Remove Vertex, Stretch (`** ADD VERTEX **`, `** REMOVE VERTEX **` with `Pick to remove vertex:`); the middle of
  a straight segment Stretch, Add Vertex, Convert to Arc (`Specify midpoint of arc segment:`), of an arc segment Convert to
  Line (`Pick to convert to line segment:`); a line's end Stretch and Lengthen. The grip stays hot, the drawing is untouched
  until a point is given, and a grey item (Remove Vertex on a polyline of two vertices) is skipped: a vertex of a rectangle
  100 by 50, Ctrl once, then a point at (120, 25), makes a fifth vertex there; Ctrl twice and any point takes the vertex out.
  Ctrl held for a click still turns Copy on, and Ctrl+Z or any other shortcut does not cycle. The sequence follows AutoCAD's help; what AutoCAD's window does at each tap has not been checked there.
- **FIXED.** MTEXT line spacing set by a paragraph (`\pxsm`, `\pxsa`, `\pxse`) is laid out as AutoCAD 2021 lays it (accoreconsole, 1 000+ cases of
  lines and heights within 1E-4). A multiple is the factor times 5/4 of the line's box, so a tall run in the middle of three lines at
  2x moved the next line 6.67 too little (57.30 against 50.63 apart); "at least" puts the first baseline at (4 x factor - 1) / 3
  of the height (23.33 at 2, 36.67 at 3, not 13.33 and 33.33) and the leading at a quarter of the line's box; the MTEXT's own
  line spacing factor and style no longer scale a paragraph that sets its own; the last line of a multiple under 0.8 and of
  exactly at a factor under 1 ends at its cell.
- **FIXED.** The mask and the frame of an MTEXT reach the defined height where it is taller than the lines (a mask 5 beyond a box
  60 high was 33 high), and an MTEXT in columns has a mask box for each column, as high as the column is set or its lines are
  and hung by its own height, as AutoCAD draws it.
- **FIXED.** An MTEXT in dynamic columns of their own heights puts the lines the columns do not hold in one column more with no
  height, as AutoCAD does (they all went in the last column), counts as many columns as the lines fill, and hangs each column by
  its own lines at the middle and the bottom (the columns shorter than the tallest were 5 and 10 too high). That column has a
  height grip at the height of its lines, and dragging it sets its height.
- **NEW.** A dimension on a layout clicked through a viewport onto the model is tied to the model, as AutoCAD 2021's
  trans-spatial association: a DIMLINEAR or DIMALIGNED (DIMCONTINUE after one too) whose points were snapped
  through a viewport at DIMASSOC 2 writes a DIMASSOC of group 70 = 1 that names the viewport before each object, and shows the
  size of the model (its DIMLFAC the negative of the viewport's scale over one: -2 at 1:2). It follows an edit of the model
  at once (a line moved 10 up takes the first origin from (95, 57.5) to (95, 62.5) and the dimension line from 40 to 45), and
  the new command DIMREGEN takes such dimensions to where the viewport shows their objects after a zoom or pan, their lines at
  the distance they kept on the paper and their size to the scale of the viewport now. The viewport erased unties the
  dimension. A DXF of one opens in AutoCAD 2021 with AUDIT 0 errors, the dimension still associative, and a model line moved,
  the viewport zoomed and DIMREGEN gave the same points. A dimension of a point of the paper and one through a viewport is
  tied to nothing, as AutoCAD leaves it.
- **NEW.** An object snap typed at a point prompt on a layout (END, MID, INT ...) takes the model objects a viewport shows
  under the typed point, the aperture in the viewport's scale and the point however far from the typed one, as it takes the
  objects of the paper: END and 95.4,57.1 give (95, 57.5), the end of a model line from (0, 0) shown at 1:2 in a frame about
  (120, 70). It was the paper's own objects alone; a click with the override chosen already did.
- **NEW.** Object snap tracking acquires through a viewport: the cursor rested on a model endpoint, midpoint ... a viewport
  shows acquires it at the place the viewport shows it, and the tracking lines run on the paper from it. Extension and Parallel
  are not acquired through a viewport.
- **NEW.** The previews of the Modify Table Style dialog are drawn to scale and linked to the settings (A14, the table half). The
  table preview shows five columns of 25 mm, a title, a header and three rows of data, each as tall as its text with the margins
  (a text height of 4.5 and margins of 1.5 make a row 9 high, the table 125 by 47), and the cell style preview one row of the cell
  style picked in three columns, the words at their text height times the scale instead of 6 to 16 pixels as before (4.5 is 11
  pixels in the table preview and 32 in the cell style one); the rows of a table that runs up are drawn with the title at the
  bottom and the lines of each group of rows where they belong. The setting being edited or hovered lights what it sets in the
  accent and fades the rest: the words (Alignment with a dot in each cell at its point, Format, the margins, the text style,
  height and colour), the fill (a dashed ghost and the chip Suppressed where there is none), the lines the Borders tab's buttons
  give, the lineweight and colour of the lines of the button pressed last, every row for the table direction, with a chip that
  names it. The margins and the text height are measured in place (`Horizontal margin = 1.5`, `Text height = 4.5`), the small
  ones with a magnifier. Hovering a border button lights the lines it would give, dashed for No Border; pointing at a row of the
  table lights it with a tooltip (`Header · cell style`) and a click picks that cell style; pointing at the words, a line or the
  fill in the cell style preview lights them and the settings that set them (an accent bar on the row) and a click goes to the first
  of the tab shown, else the first in tab order, switching the tab and focusing the box. The dialog is 940 by 660 (was 840 by 640)
  and can be resized. New public members: `TableStylePreviewViewModel`, `TableStyleModifyViewModel.Preview` and `ActiveSetting`,
  `TableStylePreview.Model` and the attached `TableStyleFieldLinks.Key` and `RowKeys`. The Table Style, Insert Table, Sheet List
  Table and Purge dialogs draw the table to scale too.
- **NEW.** The previews of the dimension and multileader style dialogs are readable and linked to the fields (A14, the presenter
  half). They are drawn at the real size of their frame and the real scale of the display, sharp at 150 %, every line at least 1.5
  pixels wide, with a black or a white ground, a wheel zoom about the pointer and Fit. The field being edited or hovered lights its
  part of a sample made for its tab (a baseline pair for Lines, a radius, centre mark, arc length, jogged radius and leader for
  Symbols and arrows, a narrow dimension for Fit, 28.375 and an angle for the units tabs) in the accent with the rest faded, a
  callout of the distance it sets (DIMEXE = 1.25), a magnifier for a small one, a chip for a field that has no part
  (Every part · DIMSCALE = 1) or formats the words (DIMDEC = 0.00 · 28,38), and a dashed ghost with Suppressed for a part the
  style leaves out. Pointing at a part of the preview lights its fields (an accent bar on the row, a dot on the tabs that hold
  others) with a tooltip, and a click goes to the first one. The New, Modify and Override Dimension Style dialogs and the Modify
  Multileader Style dialog are 960 by 660 (were 800 by 640) and can be resized. New public members: StylePreviewViewModel,
  StylePreviewGround, StylePreviewView, StylePreviewPanel, CadStylePreview.PartAt, PreviewModel on the four style view
  models, and ActiveField, PreviewKey, IsActive, IsLit and HasLitField on the dialogs, fields and tabs; Preview stays
  an image.
- **CHANGED.** A multileader style that does not include the landing is previewed without it, as AutoCAD draws it: the leader line
  runs on to the words. The DIMLDRBLK field of the dimension style dialogs lights the line of the leader as well as
  its arrowhead.
- **NEW.** The samples of the style previews, with their parts told apart (A14, the core half; the previews themselves follow):
  `CadDimStyleEditor.Sample` draws the sample of one tab of the dimension style dialogs (`CadDimSampleKind`: the Overview of the
  Manager, Lines with a baseline pair, Symbols and arrows with a radius, centre mark, arc length, jogged radius and leader, Text,
  Fit with a narrow dimension and one whose words were moved by hand, and the units tabs with 28.375 and an angle of 37.5), made by
  the builders the DIM commands use, each part of each dimension an entity of its own with its own handle (extension line 1 or 2,
  dimension line 1 or 2, arrowhead 1 or 2, the words, their frame and fill, the centre mark, the arc symbol, the jog, a leader's
  arrowhead). The dimension line of a dimension is both halves where one line spans its middle. The Overview sample draws what
  `Samples` draws, item for item, so that nothing drawn changes; the samples copy the linetypes of DIMLTYPE, DIMLTEX1 and DIMLTEX2
  into the drawing, which `Samples` leaves out, so that a dashed dimension line shows. `CadDimStyleEditor.PartsOf` and `KeysOf` say
  which parts each of the 68 fields of the four dimension style dialogs sets (and the reverse, in the dialog's order), and `Measures`
  measures what a field sets on the sample as a callout: with DIMSCALE 1 DIMEXE is 1.25 along the first extension line past the
  dimension line, DIMEXO 0.625, DIMASZ 2.5, DIMGAP 0.625 (Text tab), DIMDLI 3.75 (Lines tab), at DIMSCALE 25 each 25 times as long,
  its label still `DIMEXE = 1.25`; a part the style leaves out (DIMSE1, DIMSD2, no centre mark) comes as a ghost measure of where it
  would be. `CadMLeaderStyleEditor.PreviewSample`, `PartsOf`, `KeysOf` and `Measures` do the same for the Modify Multileader Style
  dialog: the pieces of the multileader (words or block, landing, leader line, arrowhead) are entities of their own, with
  ArrowSize 2.5, LandingDistance 8 and LandingGap 2 as callouts. New public members: `CadStylePart`, `CadDimSampleKind`,
  `CadStyleSample`, `CadStyleSamplePart`, `CadStyleMeasure`, `CadDimStyleEditor.Sample`, `PartsOf`, `KeysOf`, `Measures`, and the
  same four on `CadMLeaderStyleEditor` (`PreviewSample` for `Sample`); `CadDimStyleEditor` and `CadMLeaderStyleEditor` are now
  `partial`.
- **CHANGED.** PDFs carry only the glyphs their text uses (P4), as AutoCAD's and every PDF writer's do. SkiaSharp embedded each
  TrueType font whole, so a plot of 100 lines and one text in Arial was 543 KB (10 375 bytes now), a plan with a line in
  Malgun Gothic 8 MB (47 KB), and one with Chinese text in Microsoft YaHei, MS Gothic or SimSun, which SkiaSharp embeds as
  the whole .ttc, 10 to 20 MB (15 to 17 KB). The font keeps the glyphs of the text and those a composite glyph is made of, under
  the tag of a subset in its name (`MYREES+ArialMT`); the glyphs keep their numbers, so the text is found and copied as before,
  Vietnamese, Korean and Chinese included, and the page draws as it did (the pixels are equal). A font whose OS/2 table
  forbids subsetting, one of CFF outlines, or one that cannot be read stays whole. The 18 pages of a publish share one subset.

- **BREAKING.** A `CadApplication` (and the app of a Python `with connect()`) used after `Dispose` raises
  `ObjectDisposedException` and opens no new pipe; it once reconnected without a word. A connection the app lost is still
  opened again. Make a new `CadApplication` to work on.
- **CHANGED.** The Python stubs say where `None` is allowed. A settable reference-type property takes `None` unless its comment
  documents `ArgumentNullException` (or `ArgumentException` with a summary that does not speak of null): 306 setters, from 140.
  A property whose summary says it can give null is `T | None` (359 more getters), so `CadApplication.ActiveDocument` is
  `CadRemoteDocument | None` and pyright asks a Remote script to check it. An optional parameter whose default is null takes
  `None` (152 more). The rule was measured on 223 properties set to null: none of the 91 that refused it is typed to take `None`.
- **NEW.** Global settings, Folder preference, shows the Pythons folder (with Open folder) and the Python Startup Suite, one tool
  to a line, where the app uses Python; the Remove tooltip and hint of Load/Unload Applications name the Pythons folder too.
- **NEW.** A script that imports `opencad2d` freezes with PyInstaller with no option given: the package brings a hook that names
  pythonnet and clr_loader and takes the .NET libraries into the frozen folder.
- **FIXED.** A failed edit inside a Python run took the run's whole undo group back without a word when the tool caught the
  error; the command line now says `<COMMAND>: an edit failed, and what this command changed so far was taken back: <why>`.
- **FIXED.** The title the app announces to other programs follows its window; it stayed the one at start, before the drawing
  given on the command line opened, until the window was next activated.
- **NEW.** -PLOT chooses a printer of Windows by name, as AutoCAD's command-line PLOT does: `?` at "Enter an output device
  name or [?]" lists the installed printers after the three OpenCAD2D plotters, a name (letters of either case, spaces
  allowed) chooses one, `Enter paper size or [?]` lists and takes the printer's own sheets by name, no file is asked
  ("Write the plot to a file" answered Yes is refused as the Plot dialog refuses it), and the plot is printed as the Plot
  dialog's OK prints it ("Plot sent to Office printer."). The prompts of the device and the paper size now take a space as
  part of the name in the window too. Not run on a paper printer.
- **NEW.** Python in the setup (S13): the setup carries python.org's own Python 3.12.10 (64-bit, with `venv`, `pip` and its
  PSF licence; the `python` package of nuget.org, pinned by version and SHA-512, fetched once into `artifacts\cache` by
  `tools\installer\fetch-python-runtime.ps1` and never held in the repository) in `python\runtime` beside the exe, so Python
  tools run on a computer with neither .NET nor Python. The app takes the Python of Global settings, else the machine's
  (PEP 514), else this one (Global settings names it as **Bundled with OpenCAD2D**); a tool's venv is made from it as from
  any Python. The setup grows from 83 886 219 to 94 125 007 bytes (83.9 to 89.8 MB; 41 MB installed, 1 322 files), and the
  uninstaller removes the folder with the `__pycache__` folders Python writes in it.
- **FIXED.** MLEDIT's CA at mitred corners (P8, M1): a cut of a closed multiline whose first pick lies off the stretch of an element (the
  inner element at a concave corner starts and ends 20 away from the vertex at scale 20, so a pick within 20 of the corner is not on it)
  was cut for that element from the pick through the sides to the second pick, as for the others; AutoCAD leaves the first side and the sides
  between whole and cuts the element at the second pick alone, from it on to the end of its side (up to it on the closing side of a
  closed multiline, from the first vertex up to it on the first side), and leaves the element whole where both picks lie off it on one
  side. The stretch of an element before the first vertex of a closed multiline stays where a cut runs on round past that vertex, and
  where the two ways round between the picks are equal the cut does not run past the first vertex. The staircase (0, 0)-(120, 0)-(120, 60)-(70, 60)-(70, 120)-(0, 120)
  cut from (70, 78) to (0, 60) now holds `{0}` for the second element on its third and fourth sides, `{80}` on the closing one. 700 cuts of six closed shapes
  of the earlier round: 57 differed from AutoCAD before and 0 now (of 2 368 such rows 89 and 0; 3 moves of a vertex afterwards still differ in the core's
  `MoveVertexAt`, which writes `{0, 0}` where the presenter writes `{}`); 600 new random cuts of open and closed shapes: 117 differed before and 87 now
  (the 87 are the last vertex of open multilines of 5 or more vertices and a closed shape that runs back over itself), none that matched before changed.
- **CHANGED.** The core library is GeometryHelper 13.13.0 (on nuget.org), which holds the core fixes and members
  2.0.0 shipped in Lib under 13.12.1; the NuGet packages of OpenCAD2D name it as their dependency.
- **FIXED.** BATTMAN counted model space where AutoCAD counts the current space: "Found in model space" said the INSERTs of
  the block in model space also on a layout's tab. The second count is now that of the current space, labelled as AutoCAD's
  own strings have it ("Found in model space" on the Model tab, "Found in current layout" on a layout's); a block with 2
  INSERTs in model space and 1 in Layout1 says 3 and 2 on Model, 3 and 1 on Layout1. New
  `CadAttributeManagerRequest.FoundInCurrentSpace`; `FoundInModelSpace` keeps counting model space.
- **FIXED.** Quick Select with "Append to current selection set" ticked and Apply to: Current selection looked among the
  selection alone, which it can only add what it holds already to. It now looks among every object of the space, and the
  dialog's types and values follow (back to the selection's when Append is unticked).
- **FIXED.** While a reference was edited in place the window's Save button, Ctrl+S and the Save As dialog wrote the drawing with
  the working set standing among its own objects; QSAVE and SAVEAS were refused already. They say `** Drawing save not allowed
  during reference editing **` now and write nothing.
- **FIXED.** REFEDIT's "Prompt to select nested objects" took the object whose box held the point, so a point in the middle of a
  circle or in the box of a slanting line took it though it lay far from the line. It takes the one whose own line is nearest the
  point within the pick aperture (a text, a hatch or a reference by its box), and a point that reaches nothing is the first corner
  of a window (opposite corner to the right: objects wholly inside) or a crossing (to the left: objects the box meets), asking
  `Specify opposite corner:`; `CadDocument.FindRefEditObjects` is new.
- **FIXED.** -HATCH's draW boundary flagged copies of a shape drawn over each other by their depth alone (1 and 1 for a square
  drawn twice); AutoCAD 2021 gives 17 and 17, and 0, 17 and 17 for three (16 16 and 1 0 16 16 for the same inside a bigger square).
  The flags follow AutoCAD's numbers for 1 to 6 copies, alone and inside other squares.
- **FIXED.** The documentation and XML documentation of the settings file, the tool palettes, the crash log, the plot style folder,
  the Template folder, the plugins folder, FILTER's filter.nfl and the Remote announcement file named `%AppData%\OpenCAD2D.Core\...`
  and `%LocalAppData%\OpenCAD2D.Core\...` where the app uses `%AppData%\OpenCAD2D` and `%LocalAppData%\OpenCAD2D` (11 files, 16 places).
- **FIXED.** A layout viewport whose view direction is not the world's Z (a viewport set up in AutoCAD to a south-east
  isometric, a front or a view from below, group 16) was drawn and plotted as a plan: its direction was read nowhere. Model
  space is now taken through such a viewport along its direction as AutoCAD's display coordinates take it (TRANS 0 to 2:
  the view's target, the arbitrary axis algorithm for the X and Y axes, the depth dropped, then the twist), as a parallel
  wire-frame projection, in the display, on paper and in the PDF, printer and raster plots: seen from (1, -1, 1), the box
  100 by 60 by 40 from the origin has its corner (100, 60, 40) at (113.137, 16.330) in the view. Lines, arcs, circles,
  polylines, ellipses, splines, points and faces keep their heights; text, hatches, dimensions and blocks stay where the
  plan of the world puts them. A viewport looking straight down is drawn as before, item for item. Such a viewport is
  still not entered (MSPACE says so on the command line) and nothing is snapped through it. New public members:
  `CadViewports.HasPlanView` and `CadViewports.ToView(DxfViewport, GeoPoint3)`.

## 2.0.0 (2026-11-26)

- **NEW.** eTransmit of a sheet set (A12): the eTransmit item of the Sheet List's set, subset and sheet menus opens
  Create Transmittal, the reduced dialog of AutoCAD's. Its Sheets tab ticks the sheets to send, its Files Table lists
  what they need (the .dst, the drawings, their external references, images, PDF underlays and data-link files, the
  sheet creation templates, the page setup overrides file, the plot style tables of the layouts, and font files found
  beside the drawings), and OK writes one zip, `<set folder>\Transmittals\<set name>.zip` unless changed, with a
  `transmittal.txt` report. The zip keeps the folders of the set; a file outside the set's folder goes in `Files`, and a
  path that would then be wrong is written again in the copy (the original is never touched). Unzipped elsewhere, the
  set opens and every sheet and external reference is found. New public members: `CadTransmittal`,
  `CadTransmittalFile`, `CadTransmittalFileKind`, `CadTransmittalResult`.
- **FIXED.** The fields of the attributes of a title block were lost when a layout was brought in with a template whose chosen layout
  is not the first (LAYOUT Template, New Sheet and Import of a sheet set, Save As template): the layout was copied without the FIELD
  behind each attribute, so the words stayed as they were and a `\AcVar` or `\AcSm` field never updated again. The copy keeps the
  fields (the cause was in the core library: its `WblockCloneObjects` dropped the field of a copied text, attribute or dimension, which
  Paste and Insert of a drawing suffered too); Lib brings the fix.
- **FIXED.** The fields and formulas in the cells of tables were never evaluated again on open, save or regen: they were looked for
  only through the drawing's list of fields, which the fields of a table's cells are not in (a cell given a field with Insert Field, or a
  table AutoCAD saved, which owns them by the text of its block). The tables of the spaces are looked through as well.

- **NEW.** OPENSHEETSET and NEWSHEETSET (A12): OPENSHEETSET shows the palette's Open dialog or, with FILEDIA 0 or no window, asks
  "Enter name of sheet set to open:" as AutoCAD 2021's -OPENSHEETSET does (also a command of its own), answering "File can not be
  opened." and "Sheet Set file is not valid." in AutoCAD's words; NEWSHEETSET asks the wizard's pages on the command line (an example
  set or existing drawings, the name, the description, the folder of the `.dst`, a folder hierarchy of the subsets), AutoCAD having no
  typed form of it. The set made or opened is opened in the Sheet Set Manager.
- **NEW.** The variables SSLOCATE and SSMAUTOOPEN (0 or 1, 1), SSMPOLLTIME (20 to 600, 60), SSMSHEETSTATUS (0 to 2, 2), SSMSTATE (1 while the
  palette is shown) and SSFOUND (the set found for the drawing), with the defaults, ranges and messages AutoCAD 2021 gave in its
  console, shared by the drawings and kept in the settings file. A drawing that is a sheet opens its set when it opens (SSLOCATE), found
  again where the set was moved with its sheets, and shows the palette (SSMAUTOOPEN).
- **NEW.** The lock file of a sheet set (A12): a set opened for editing writes `Name.DBL` beside it as AutoCAD does (58 bytes of text with
  `\r\r\n` line ends: the user and the machine) and deletes it when closed; a set whose file is read-only, or whose lock is another
  user's, opens read-only with the lock and the user's name in its tooltip, and refuses every change. A lock of this user on this
  machine is taken over.
- **NEW.** The sheet list table (A12, engine): `CadSheetListTable.Insert` puts a table of the set's sheets in set order in a layout, with the
  subsets as merged header rows where asked, the number and title cells as sheet set fields bound to their sheets with AutoCAD's
  hyperlink switch, and its settings kept in the table; `Update` lists the set again after a sheet was added or removed, in one undo step.
- **NEW.** Insert Sheet List Table and its table's right-click items (A12): the item of the Sheet List's menus opens a dialog in the
  design's two tabs, Table Data (the title text, a column for each line with its data type and heading, Add, Remove, Move Up and Move
  Down; the data types include the custom properties of the sheets) and Subsets and Sheets (the set's tree with check boxes, a checked
  subset taking in the sheets added to it later), with the table style and its preview and Include Subsets as Headers in Output; OK asks
  "Specify insertion point:" and draws the table in the sheet's layout in one undo step. A cell of the table has Update Sheet List
  Table (lists the set again, the cells typed by hand are lost, said on the command line) and Edit Sheet List Table Settings… (the same
  dialog on the settings the table keeps) at the top of its right-click menu. `CadSheetListTable.Update` takes new settings and
  `ReadSetFile` names the set a table was made from.
- **NEW.** Example sheet sets (A12): New Sheet Set and OpenCAD2D Metric Sheet Set (the subsets General, Plans and Details, the properties
  Client, Project Address, Drawn By and Checked By) with the template OpenCAD2D-sheets-mm.dwt (an A1 layout with a frame and a title
  block of sheet set fields), in a SheetSetExamples folder beside the program; our own, not AutoCAD's.
- **CHANGED.** New Sheet and Import Layouts as Sheets bind the `\AcSm` fields of the new sheet's layout to the sheet once the set is saved
  (the fields of a title block show the sheet's own words, and AutoCAD shows them too); the fields and the operations read a drawing's
  sheet set mark with one reader, `CadSheetSetHint`.
- **NEW.** The Sheet Views and Model Views tabs of the Sheet Set Manager (A12): Sheet Views lists the views placed on the
  sheets, by category or by sheet (the scale each was placed at in grey), with New View Category, Rename and Remove Category,
  Set Category (by menu, or by dragging a view onto a category), Rename & Renumber View with Previous and Next, and Display,
  which opens the sheet and shows the view. Model Views lists the set's resource locations (folders), the drawings in them
  and their named model views, read from the closed files, with Add New Location, Remove Location, Refresh and Open. Place on
  Sheet, from the menu or by dragging a view or a drawing onto the drawing area, attaches the drawing as an xref (Attach, as AutoCAD's own sample sheets do) in
  model space of the sheet's drawing (once), makes a viewport on the current layout at a scale of the drawing's list (the
  view's last scale, else 1:100; the Scale option changes it), saves a named view round the viewport, and records a sheet view
  in the set (the next number on the sheet, the view's name for a title), as one undo step; the named view is what AutoCAD's
  own sheet views point at (checked on its sample sheets). New public members: `CadSheetSetPlacement`,
  `CadPlaceOnSheetPlan`, `CadPlaceOnSheetResult`, `CadSheetSetResources`, `CadModelView`, and a setter for
  `CadSheetView.Category`. The label block (Place View Label Block) and callout blocks are left out.
- **NEW.** Publish a sheet set (A12): the Publish items of the Sheet List write the sheets of the set, a subset or the
  selected sheets to one multi-sheet PDF or a PDF for each sheet, in set order, as the set's Sheet Set PDF Publish
  Options say (folder, one file or a file each, file name asked for or not). A sheet left out of publishing, and the
  sheets of a subset whose Publish Sheets in Subset is off, are told on the command line and not written. A file for
  each sheet is named "A-101 Ground Floor Plan.pdf" as AutoCAD names it. The PDF carries AutoCAD's bookmarks: "Sheets
  and Views" with a bookmark for each page. Publish Using Page Setup Override and Publish in Reverse Order work; the
  progress window's Cancel stops at the next sheet. The `.dsd` of Publish Dialog Box… says `IsSheetSet=TRUE`. New
  public members: `CadSheetSetPublish`, `CadSheetSet.PublishOptions`, `CadSheetSubset.PublishSheets`,
  `CadPublishOptions.CreateBookmarks` / `IncludeHyperlinks`, `CadPublishList.AddLayout` / `SheetSetName`,
  `CadPdfBookmark`, and overloads of `CadPdfWriter.WritePages` and `CadPublisher.Publish` (with progress and cancel).
  An existing publish writes the same bytes as before (18 pages, same SHA-256).
- **NEW.** The Create Sheet Set wizard (A12, NEWSHEETSET's window): New Sheet Set… of the Sheet Set Manager opens a 660 by 470
  wizard with the pages of the route down the left. From **an example sheet set**: Sheet Set Example (the examples listed, or
  browse to any `.dst`), Sheet Set Details (name, description, the folder of the `.dst`, Create a folder hierarchy based on
  subsets, Sheet Set Properties…) and Confirm. From **existing drawings**: Sheet Set Details, Choose Layouts (a tree of folder,
  drawing and layout with tick boxes, mixed boxes above, a layout with a `#` or a sheet of another set grey with its reason) with
  Import Options (Prefix sheet titles with file name, Create subsets based on folder structure, Ignore top level folder, all on
  and kept in the settings), and Confirm with the Sheet Set Preview. Next is grey while the page's red line says what is wrong
  (an empty or unfit name, a `.dst` that is there already, an example that is no sheet set, no layout ticked); nothing is written
  before Finish, which makes the set, opens it in the palette and puts it first in Recent. The route, the folder and the
  import options are kept for the next time. The guide is `docs/presenter/sheet-sets.md`.
- **NEW.** The Sheet Set Manager (A12, the palette): SHEETSET shows it as a floating window beside the drawing and
  SHEETSETHIDE closes it. The sheet set control lists the sets open (the current one ticked), Recent (the last ten `.dst`
  files, newest first) and New Sheet Set… / Open…; the Sheet List tab shows the set, its subsets and its sheets in set order
  as "Number - Title", each sheet with an icon for a drawing open in the session, a sheet left out of publishing and a sheet
  whose drawing or layout is not found, and a Details or Preview pane under it (the file, folder, layout, size and date of
  the sheet, or its layout drawn). Sheets and subsets move by drag and drop; the menus of the set, a subset and a sheet
  open sheets on their layout, add a subset or a sheet, import layouts, rename and renumber (a drawing open in the app is
  saved, closed, renamed and opened again), remove (the drawing is kept), put a sheet in or out of publishing and resave all
  sheets; a read-only or locked set is shown with a lock and refuses every change. The dialogs of New Sheet, Import,
  Rename & Renumber and the Properties, the Sheet Views and Model Views tabs, publishing and eTransmit follow in the
  next packages. The guide is `docs/presenter/sheet-sets.md`.
- **NEW.** Sheet set fields (A12): `\AcSm Sheet.Number`, `Sheet.Title`, `Sheet.NumberAndTitle`, `Sheet.Description`, a sheet's
  custom properties, `Subset.Name`, `SheetSet.Name`, `SheetSet.ProjectName` and the set's custom properties are evaluated as
  AutoCAD 2021 evaluates them (checked on its own FIELD objects: `A-101 - Ground floor`, `----` for a property with no value,
  `####` for a name it does not know), in their unbound form, as a title block holds it, and in the bound form AutoCAD writes into
  a sheet (`\AcSm Database("...").SheetSet("g...").Component("g...").Number`); placeholders show their own name. The field
  keeps the words it showed where the set cannot be reached, and finds a set moved with its sheets. `CadSheetSetFields.Bind` binds
  the unbound fields of a new or imported sheet to it. The Field dialog has a SheetSet category: a tree of the sheet set and its
  properties, the nine placeholder types, and the 17 CurrentSheet names, which write `\AcSm Sheet.Number` and its kin (AutoCAD
  takes `\AcVar CurrentSheetNumber` for no variable and shows `####`).
- **FIXED.** The fields of an attribute were never evaluated again on open, save, regen or plot (only those of a TEXT or an MTEXT
  were): a title block's `%<\AcVar Author>%` kept the words it had until the attribute was edited. They are now updated with the
  other fields of the drawing.
- **FIXED.** A DXF saved from a drawing opened from a DWG that OpenCAD2D wrote (a new sheet, an imported layout) is
  no longer refused by AutoCAD 2021 ("Invalid or incomplete DXF input -- drawing discarded"): the current linetype of
  the DWG is now written and read back as ByLayer, and an empty current layer or text style reads as `0` or
  `Standard` (GeometryHelper ac9b4f96).
- **NEW.** Sheet set data files: `CadSheetSetFile` opens, changes and saves AutoCAD 2021's own `.dst` (UTF-8 XML under a
  one-byte cipher), with a typed model over it: the set, its subsets and sheets in set order, a sheet's drawing and
  layout in four forms of the path (found again after the folder is moved), custom properties by owner, resource
  locations, the sheet creation template, the page setup overrides file, view categories and sheet views. What the model
  does not know is saved as it was read. Every change is saved at once through `name.dst.tmp`, the file revision goes up
  by one, and a file that moved on disk is reloaded first. The ten `.dst` files AutoCAD 2021 installs read and write
  back to the same bytes, and AutoCAD's own sheet set API reads a set made here (names, subsets, sheets, custom
  properties, views) as this model does (the engine of A12; no command or window yet).
- **NEW.** Sheet set operations (A12, engine only): a set made from an example `.dst` (its subsets, custom properties
  and settings, no sheets, every file named again relative to the new set, optionally a folder per subset) or from
  existing drawings (`CadSheetImportSource`, subsets from folders); `NewSubset`; `NewSheet`, which makes a drawing from
  the layout of the sheet creation template, "Number Title" as its layout and file name, saved as a DWG of AutoCAD 2018
  where the subset or set puts new sheets; `ImportLayouts` and `CheckLayouts`, which refuse a layout whose name holds a
  `#` or that is a sheet of this set or of another set that is there; `CadSheetSetSheet.FindLayout` by handle and then
  name, `Associate`, `RenameAndRenumber` (renames the layout and moves the drawing, the other sheets of that drawing
  following) and `CadSheetSet.ResaveAllSheets`; custom properties by owner (`AddCustomProperty`, a sheet property given
  to every sheet, `DeleteCustomProperty`). Each drawing a sheet is made, imported, renamed or resaved in carries AutoCAD's
  `AcSheetSetData` mark (`CadSheetSetHint`), which AutoCAD 2021 reads back with the same values; layout handles survive
  our DWG save.
- **NEW.** The sheet set dialogs (A12, window; the palette opens them): Sheet Set Properties, Subset Properties
  (New Subset, Rename Subset, Properties) and Sheet Properties as property grids in AutoCAD 2021's sections, with the
  description of the row clicked under the grid and a read-only form for a locked set; Custom Properties and Add Custom
  Property (an add or a delete waits for OK, which writes the set once, a property of every sheet being given to every
  sheet); New Sheet with Select Layout as Sheet Template; Rename & Renumber Sheet with Previous and Next; and Import
  Layouts as Sheets. A value the set cannot take is refused in red in the dialog's own words (a subset name a sibling has,
  a file that is there already, a layout name with a `#`) with OK greyed; what the core refuses is said in the same line.
  Subset Properties has a new row, Publish Sheets in Subset (`CadSheetSubset.PublishSheets`, kept as the opposite of
  AutoCAD's `OverrideSheetPublish`). `--dialog sheetset`, `subset`, `sheet`, `customprops`, `addprop`, `newsheet`,
  `sheettemplate`, `renumber` and `importlayouts` draw them for the screenshot tool, some with a state after a hyphen.
- **CHANGED.** The Python guide says what a read sees after a write in one command: `drawing.add_line(...)` queues, so
  `drawing.entities()` right after it lists the drawing as the last commit left it (10 objects, where the line makes
  11), and `commit()` first makes the read see the line; a command that never commits has its line when it ends. It is so
  on every backend and both runtimes; a test pins it in a tool (`test_tool_read_after_add.py`).
- **FIXED.** A Python tool did not run on a Windows with no .NET 8 installed (the app brings its own .NET, a tool's
  process did not): it now runs on the .NET Framework 4.8 of Windows, and importing `opencad2d` with no .NET at all
  chooses it under `auto` instead of raising "Can not determine dotnet root". Found by the setup's test in Windows
  Sandbox.
- **FIXED.** Uninstalling OpenCAD2D left `python\opencad2d\__pycache__` and `python\opencad2d\cad\__pycache__` in the
  install folder.
- **CHANGED.** The ribbon's icons are drawn in colour (A13), one drawing for each of 378 commands in AutoCAD 2021's dark
  look and the program's own hand: light lines, blue points and arrows, teal windows and data, yellow results, red for
  delete and markup, green for add and ok. Each has a large drawing for 32 pixels and a small one for 16, lines whole
  pixels at 100 %; a slot of 16 at 150 % (24 pixels) shows the large drawing at 0.75. Home, Insert, Annotate, Parametric,
  View, Manage, Output, Settings, the Text Editor, Table Cell, Block Editor, Hatch, Array, Image and PDF Underlay tabs, the
  Quick Access Toolbar and the layer state glyph. The seven Block Editor buttons that had no icon (Block Table, Delete,
  Authoring Palettes, Parameter Manager, Construction, Make Visible, Make Invisible) have one, and the Close Text Editor
  button takes its icon like the others. The collapsed panel button is 4 pixels taller in its icon slot (32, from 28).
  The monochrome `GhIcon...` keys stay for the command line, the palettes and the dialogs.
- **NEW.** `RibbonIcon` (`Large` and `Small`, two drawings on a 32 grid), `IconView.Icon` (a colour icon drawn instead
  of `Data`, the drawing chosen by the device pixels of its size) and `RibbonPanel.RibbonIcon` (the colour icon of a
  collapsed panel's button). Plugins keep `IconView.Data` and `RibbonPanel.Icon`. The seven colours are the theme brushes
  `GhIconInk`, `GhIconGrey`, `GhIconBlue`, `GhIconTeal`, `GhIconYellow`, `GhIconRed` and `GhIconGreen`.
- **NEW.** Plugin Settings: Settings..., after Remove in Load/Unload Applications, opens a dialog for a loaded C#
  plugin's `[CadSetting]` properties or a Python tool's `"settings"`: a row per setting by kind, its description and
  range in grey, a refused value in red under it with OK greyed; OK applies every value at once and writes
  `pluginSettings` once; a slider mark follows the name of such a row. A C# plugin that loads now gets its kept
  values (they were not kept before). `--dialog plugin-settings` shows it to the screenshot tool.
- **NEW.** The window answers a Python tool's object snaps: listed under the tool's name, asked once per entity after
  the hover pass and kept until the drawing changes; a point typed after the keyword waits at most 1 s; a snap starts
  the tool's process only where its venv needs no question and no pip (2000 circles under the aperture: first pass
  8.7 ms, the question 23 ms, later passes 8.8 ms).
- **CHANGED.** The tools channel answers `hello`, and reads a tool's `ready`, with no wait for the window thread.
  `"snaps"` and `"settings"` in a C# plugin's `plugin.json` are ignored.
- **NEW.** Python tools get custom object snaps: `@snap("CTOP")` with a `"snaps"` entry in `plugin.json`, and
  `SnapRequest`; the finder runs in the tool, is asked once per entity and its points are kept until the drawing changes
  (one question 0.29 ms; 20 entities 0.85 ms). The snaps show in the window once its side is in (PW3 C2).
- **NEW.** Python tools get settings: a `"settings"` entry in `plugin.json`, read with `setting(name)` and written with
  `set_setting(name, value)`.
- **NEW.** The example tool `circle_marks`: the snap CTOP, the setting textHeight, the command MARKTOPS and a ribbon button.
- **CHANGED.** The remote Python examples take `cad: App` and connect with `with connect() as cad:`.
- **NEW.** `CadPluginManifest.Snaps` and `Settings`: a Python tool declares object snaps and settings in `plugin.json`,
  read without running it (PW3).
- **NEW.** `GlobalSettings.PluginSettings` (`pluginSettings` in the settings file): the values of plugin settings, one
  entry per setting, as `Circles/textHeight=2.5`.
- **NEW.** `CadApplication.GetToolSetting` and `SetToolSetting`: a tool reads and sets its settings over the channel of
  the tools, answered with no wait for the window.
- **FIXED.** The Layer box of Home › Layers and the Color, Linetype and Lineweight boxes of Home › Properties follow the
  selection as AutoCAD 2021's do. With nothing selected they show, and a pick sets, the current layer and CECOLOR, CELTYPE
  and CELWEIGHT, as before. With one object selected, or several that agree, they show that object's layer, colour,
  linetype and lineweight (a red ByLayer circle on COLUMNS shows COLUMNS and ByLayer with a red swatch), and a pick is
  applied to the selected objects as one undo step (the circle on COLUMNS, Lineweight 0.50 mm picked: the circle is
  0.50 mm, CELWEIGHT is what it was); with several that differ the box is blank and a pick applies to all of them. Before,
  the four boxes always showed the current settings, and a pick changed the current setting whatever was selected. The
  boxes follow every change of the selection and of the drawing, and Esc brings the current values back. The Color box
  draws a swatch before each colour, as the Properties palette does (ByLayer in the colour of the layer of the selected
  objects, of the current layer with none selected, white where they are on several; ByBlock white; White split black and
  white), and More colors... in its list has the colour wheel.
- **NEW.** A sample that draws ships with the app, as a C# plugin and as a Python tool: `Plugins\DrawRandom` (`CSRANDOM`) and
  `Pythons\DrawRandom` (`PYRANDOM`), from `examples/DrawRandom` and `examples/Pythons/DrawRandom`. Each asks `Number of objects
  <50>:` (1 to 10000) and the two corners of an area (Enter at the first takes the view shown, or the extents of the drawing
  where there is no window), makes the layers `RANDOM-1` to `RANDOM-5` where they are missing, each of a random colour, and
  draws that many LINEs, CIRCLEs, ARCs, LWPOLYLINEs (3 to 6 vertices, closed or open), TEXTs and rectangles inside the area, on a
  random one of the layers, ByLayer half the time and else of a random colour from 1 to 255. The layers and the objects are one
  undo step, and it ends with `Drew 50 objects on 5 layers.` Setting the environment variable `OPENCAD2D_RANDOM_SEED` to a whole
  number makes the same drawing every time.
- **CHANGED.** The `Stairs` sample plugin is no longer shipped: the app's `Plugins` folder holds `HelloWorld`, `DrawingInfo`
  and `DrawRandom`, and `examples/Stairs` is gone. The example of the plugin guide (a plugin named Stairs, command STAIRS) stays
  in the guide, and a test still compiles and runs it from the guide's text; it is no longer a folder of the app. The README of
  the `Plugins` folder no longer gives the compile and start times of the three samples, which were measured for the old three.
- **FIXED.** A routine that writes its prompts with AutoCAD's Unicode escapes (`(getpoint "\nCh\U+1ECDn \U+0111i\U+1EC3m
  \U+0111\U+1EA7u HCN :")`) showed `Ch\U+1ECDn \U+0111i...` on the command line, in the history and in the box of dynamic
  input; AutoCAD 2021 shows `Chọn điểm đầu HCN :`. Measured with accoreconsole: a string keeps its seven characters
  (`(strlen "\U+0111")` is 7) and the escape becomes the character where it is shown (`princ`, `prompt`, `alert` and the
  message of every `get` function) and where a routine hands it to the drawing (the text of an `entmake`d, `entmod`ed or
  `command`ed TEXT or MTEXT, a layer or block name, extended data, a name given to `tblsearch`). The messages of the `get`
  functions and `alert` showed the seven characters; a TEXT made with `(1 . "a\U+0111b")` held nine characters, and a layer
  named `L\U+0111` was refused. Four hex digits count, in either case: `\U+01111` is `đ1`, `\U+1F600` is `ὠ0`, `\U+0000`
  ends a stored text. The same escape typed at the `Enter text:` prompt of TEXT, or a layer name, is the character; text
  typed to `getstring` is not changed.
- **FIXED.** Every prompt for text took a space as part of the answer, in a script and on the command line, where AutoCAD
  2021 takes it only at a few (measured in accoreconsole with a .scr, 228 runs with a space in the answer): `-LAYER N A B` made
  one layer named `A B` (AutoCAD makes `A` and reads `B` as the next option), `-RENAME LA A B` looked for a layer `A B`
  and the window typed the space into a name. A name, a file name or a list of names now ends at a space (new, make, set,
  rename, on, off, freeze, thaw, lock and list of -LAYER, -RENAME, -VIEW, UCS, -STYLE, -LINETYPE, -GROUP, -PURGE,
  -DIMSTYLE, -LAYOUT, -VPORTS, MLINE, -OSNAP, SETVAR's variable names and the variables CLAYER and TEXTSTYLE, -WBLOCK,
  DXFOUT, SCRIPT, OPEN, -XREF, -IMAGE, -PLOT's file name), and the prompts that take the whole line are the text of TEXT,
  -MTEXT, MLEADER and LEADER, dimension text, a description (layer, group, linetype), an attribute's value, prompt and
  strings, a scale's name and ratio, a parameter's name and expression, CAL's expression, a page setup name and the text
  variables DIMPOST, DIMAPOST, POLARADDANG, CANNOSCALE, TRUSTEDPATHS, HPLAYER, DIMLAYER, TEXTLAYER, CENTERLAYER and
  CENTERLTYPE. `CadPrompt.ForString` ends at a space as `PromptStringOptions` does, and the new `AllowingSpaces()` makes
  one a prompt for a line of text (a plugin's text that took spaces before needs it); AutoLISP's `getstring` with and
  without `cr` and `getkword` are as they were.
- **NEW.** The blocks of the drawing of a loaded external reference (`grid|TAG` for the block TAG of the drawing `grid`),
  which AutoCAD 2021 holds in its block table while the reference is loaded and writes to no file (measured with
  accoreconsole: `tblnext` says `70 48`, -BLOCK ? says `Xdep: "grid"`, they go with -XREF Unload and Detach and come back
  with Reload, and its DXF reader refuses a drawing that names one), are carried where AutoCAD shows them. Remote `Blocks`
  and Python's `drawing.blocks`, from outside and inside the app, list them with their objects, flagged dependent and
  resolved (a drawing of the blocks TAG and PLAN attached as `grid` gives `grid|PLAN` and `grid|TAG` after `Bolt` and
  `grid`); `CadXrefManager.GetDependentBlocks()` gives them as copies; -BLOCK ? lists the references (`"grid"   Xref:
  resolved`, `unloaded`, `not found`) and their blocks (`"grid|TAG"   Xdep: "grid"`) with the counts of what matched, which
  it counted from the whole table before; -INSERT of one says `Cannot insert dependent block.`. None is in the drawing's own
  table or its saved file. This closes the first item of the PW1 parked note: the core library (GeometryHelper, built from
  the commit of its branch `feat/xref-dependent-blocks`) can now make such a record in the small drawings the Remote
  channel carries.
- **FIXED.** -INSERT's `?` wrote the table of four columns of -BLOCK's (user blocks, external references, dependent blocks,
  unnamed blocks); AutoCAD 2021 lists the blocks it can insert alone, with a table of two columns, `User` and `Unnamed`
  (`    1        0` for a drawing of one block of its own).
- **FIXED.** `save()` and `save_as()` inside a tool's run, and inside an `undo_group` of a script on a file, are one
  undo step with the edits around them. In a script on a file the group was ended for the save and opened again, which made
  two steps (a line, a `save()`, a circle: two `Undo()`s); the document now saves through the new
  `CadDocument.SavingInGroup(save)`, and one `Undo()` takes back the line and the circle. In a tool's run a save was
  refused with "A command is running: end it before saving." in the headless server, and in the window the save began with
  the Esc of Ctrl+S, which ends the run; the run now saves what it has made, the tool goes on, and the whole run is still
  one step named after its command (a run of three lines with a `save_as` after the first and a `save` after the
  second: the file has two of them, the drawing three, one `U` takes all three back). A run that fails after a save keeps what
  the file holds.
- **CHANGED.** `Entity.dxf_type` in Python is the `DxfName` of the entity, a new member of the core library's `DxfEntity`
  (GeometryHelper core commit pending), where it was the first word of `str(entity.net)`.
- **FIXED.** SCRIPT fed a whole line as one word, so `LINE 0,0 10,0 ` and `LINE END 60,1` were refused and only a line
  that was itself a snap word was a snap. A line is read as AutoCAD 2021 reads a .scr (measured in accoreconsole): a space,
  a tab and the end of a line are each Enter, so `LINE 0,0 10,0 ` draws a line from (0, 0) to (10, 0) and ends it, and two
  spaces in a row are two Enters (`LINE 0,0 10,0  CIRCLE 5,5 3` draws both). At a prompt for text (TEXT's `Enter text:`,
  -MTEXT) the rest of the line is the text, `Hello  World ` with both spaces and the one at the end, and a form of AutoLISP
  in parentheses, its strings and a form run over several lines keep their spaces and are followed by the next word of the
  line. A line is a comment only when its first character is a semicolon (` ;c` and `LINE 0,0 ; c` are not).
- **FIXED.** An object snap word typed with AutoCAD's underscore, `_END`, `_mid`, `_CEN`, `_NONE`, is the override of the
  next point as `END` is (it was refused): on the command line, in `-OSNAP`'s list, in a plugin's macro (which trimmed
  the underscore itself), and now also where a word is fed as typed, which took no snap word at all: a routine's
  `(command "_LINE" "_END" '(60 1))`, a script line and Remote. With END and a line from (0, 0) to (100, 0), 60,1 is
  (100, 0) in each. `TAN` typed at CIRCLE's 3P is now the circle's own tangent, as `_tan` is, so Tan, Tan, Tan typed
  with `tan` draws the circle (it took the snap and drew none).
- **CHANGED.** Measuring a text, an MTEXT or an attribute in the document's fonts (`(textbox)`, `vla-GetBoundingBox`,
  MIRROR, MLEADERALIGN, MLEADEREDIT, the table editor) borrows one set of fonts for each path (`DxfDatabase.BorrowFonts`)
  and keeps it for the life of the drawing, where each text read the font file again: `(textbox)` of 2000 texts in an SHX
  font took 212 ms and takes 126 ms (.NET Framework 4.8), MIRROR of 2000 texts 69 ms and 62 ms, and MLEADERALIGN of 200
  multileaders 45 ms and 40 ms, every result the same to the bit. A font file added to a folder the drawing already
  measured in is read again once the document's fonts are set, which Apply in Global settings does. The core library is
  updated to GeometryHelper 13.12.1 (core commit 4f575b09), which brings `DxfDatabase.BorrowFonts`.
- **FIXED.** Copying to the Windows clipboard (Copy Full File Path, the text window's Copy, the command line's Copy,
  copying objects) failed on a machine where a program watches the clipboard (clipboard history, a remote-desktop or phone
  sync): the watcher opens the clipboard at the change and asks the app for the text, and the app answers only while its
  thread takes messages, which the waits between its tries never did, so the copy gave up after about 5 s and left the
  clipboard unflushed. The waits now answer what other programs send, and the flush that keeps the copy after the app is
  closed is tried every 100 ms the same way: the copy goes through at the second try (about 100 ms).
- **NEW.** `CadEditor.Check(Action<CadEditor>)` (plugin API 1.5): the dry run of an edit. It runs the calls it is given
  with every check they make (the numbers, the objects, the plan of an offset, the owner's gate), writes nothing, and
  gives null or the words the call would throw: `Check(e => e.Offset(circle, 15, side))` of a circle of radius 10 inward
  gives `Cannot offset that object.`
- **FIXED.** Python on a file: an edit refused only while it is planned (an offset that makes no curve, a side point on
  the curve, a distance under the tolerance) applied what was queued with it first, inside the script's own
  `undo_group`. The edit is now checked by `CadEditor.Check` before anything queued is applied.
- **CHANGED.** Remote `CadRemoteDocument.HandleOfTicket(ticket)` gives the handle of an `Insert` after a commit or an
  edit call with no request; Python's remote edit calls no longer read each insert back (a thousand `add_line` and a
  `copy`: about 0.07 s, where 0.7 s on .NET 8 and 1.3 s on .NET Framework before).
- **FIXED.** The fractional arc of an SHX shape (code 11) is read as AutoCAD 2021 reads it, measured by `(textbox)` on
  1926 pairs of glyphs: each offset is rounded to whole degrees half up (an offset of 128 is 23 degrees, not 22), an end
  that rounds to 0 is the whole octant, an end before the start turns the long way in the octant byte's sense, equal ends
  are a whole circle, and a count of 0 is 8 octants with the sense from bit 7 of the octant byte (`0x80` is a whole
  circle clockwise). An arc of radius 100 with offsets 0 and 0 and octant byte `0x20` is now a whole circle, its box
  200 by 200, where it drew nothing. Codes 10 and 12 are as before.
- **BREAKING.** The last GeometryHelper and GH names are out of what OpenCAD2D writes and shows (A10), with no
  compatibility kept: a file or setting that carries an old name is no longer read as the new one. Renamed: the XRECORD
  `GH_REFEDIT` of a REFEDIT in progress is `OPENCAD2D_REFEDIT` (a drawing saved in the middle of an edit by an older build
  opens with no edit open), the Layer Translator's XRECORD `GH_LAYTRANS_MAP` is `OPENCAD2D_LAYTRANS_MAP` (a .dws saved
  before loads its layers and no mappings), the extended data application `GH_OWNER` is `OPENCAD2D_OWNER`
  (`CadOwnerTag.ApplicationName`; an entity tagged before reads as untagged and is free to edit until the owner tags it
  again), the sample template's dimension styles `GH` and `GH-Annotative` and its multileader style `GH` are `OpenCAD2D`
  and `OpenCAD2D-Annotative` (a drawing made from the old `OpenCAD2D-mm.dwt` keeps its own), the data extraction file's
  XML root `GeometryHelperDataExtraction` is `OpenCAD2DDataExtraction`, `CadToolPalettes.Format` is
  `"OpenCAD2D.ToolPalettes"` (a `ToolPalettes.json` of the old format is refused in words), the private clipboard
  format's header `GeometryHelper clipboard 1` is `OpenCAD2D clipboard 1`, the Lisps sample `GH-DrawingInfo.lsp` with its
  command `GHINFO` is `DrawingInfo.lsp` with `LSPINFO`, the Python sample's `GHPYINFO` is `PYINFO`, and the C# sample
  plugin's `GHDWGINFO` is `CSINFO`. Removed: the plotter names `GeometryHelper PDF`, `GeometryHelper PNG` and
  `GeometryHelper JPG` (a layout or page setup naming one is a plotter this computer lacks, and
  `CadPlotSettings.UseDevice` refuses them; saving no longer renames them), the file names `GeometryHelper.pat` and
  `GeometryHelper.lin` (now ordinary file names, found in a support folder or not at all), the template name
  `GeometryHelper-mm.dwt` (`CadTemplate.Find` no longer maps it to `OpenCAD2D-mm.dwt`), the copy of `%AppData%\GeometryHelper`
  into `%AppData%\OpenCAD2D` at the first start (`CadOldFolders` is deleted), and the removal of the ProgID
  `GeometryHelper.Drawing` by `--register` and `--unregister`. `CadPlotSettings.IsRasterDevice` and
  `CadPlotSettings.DeviceName` take only the new names. A copy installed over an older one keeps `Lisps\GH-DrawingInfo.lsp`,
  which still loads and still defines `GHINFO`: delete it by hand.
- **FIXED.** MIRROR with MIRRTEXT 0 of a TEXT in an SHX font turns it over by the box AutoCAD 2021 measures (the box
  `(textbox)` gives, where an arc is reached by the points it bulges to) and not by the strokes the display draws: the C,
  2 and 3 of the built-in `opencad2d.shx` put the copy off. `ABC123` 10 high at the origin mirrored across `y = 50` now
  starts at y = 89.9236, not 90.0; across `x = 50` at x = 56.7512, not 57.5046. Words with no arc (`HILT`), TrueType
  fonts and fonts the drawing does not find are as before.
- **NEW.** Python `Drawing.move`, `copy`, `rotate`, `scale`, `mirror` and `offset` (A8): the commands as calls on `Entity`
  objects, typed (`copy([circle], (0, 50))` is a `list[Entity[DxfCircle]]`, `offset` gives `list[Entity[DxfCurve]]`).
  What is queued goes with the edit in one undo step (`REMOTE` from outside the app, `PYTHON` for a file, the run's own in
  a tool), and an edit the drawing refuses applies none of it. In-place edits read the `Entity` objects given again.
- **FIXED.** `(textbox)` of a TEXT in the built-in font `opencad2d.shx` gave `nil`, and so did the other measures that take
  a text's box from the drawing's fonts the same way (`vla-GetBoundingBox` of a TEXT or MTEXT, the breaks of a dimension
  or a multileader, MLEADERALIGN): they looked in the folders a user set and Windows' Fonts folder but not in
  the Fonts folder shipped beside the program, where `opencad2d.shx` is. `ABC123` 10 high gives (1.0, -0.6299) to
  (42.2488, 10.7063) and 100 high (10.0, -6.2992) to (422.4882, 107.063), as AutoCAD 2021 does.
- **NEW.** `CadEditor.Move`, `Copy`, `Rotate`, `Scale`, `Mirror`, `Offset` and `Erase` (A8): the edits as calls for
  plugins, each doing what the command of its name does, through the code the command runs; refused whole with the
  object's words; one undo step named as the command, or joining the running command's step.
- **NEW.** Remote `Move`, `Copy`, `Rotate`, `Scale`, `Mirror` and `Offset` on `CadRemoteDocument`: queued changes and
  the edit in one commit (one step REMOTE), the new copies in its answer; `commit` takes the operations `transform` and
  `offset` (protocol version 1).
- **FIXED.** MTEXT format codes `\H`, `\W`, `\Q` and `\T` are read as AutoCAD 2021 reads them: a number (with a sign, a
  point or an exponent), an `x` for "times" and a semicolon, each optional, the number ending where it ends. A `\H` with
  nothing after it was laid out as the letter H, 115 by 120 at height 100; it is a code that lays out nothing, 0 wide by
  100 high, as are `\H;`, `\Hx;`, `\H2`, `\W`, `\Q15` and `\T`. `\H2B` is `\H2;B` (B 2 high), `\Hab;A` is `\H` and the
  text "ab;A", `\H2xA` doubles the height. A `\W` with no number or one below 0.1 sets the width factor 0.1, and a `\T`
  below 0.75 the tracking 0.75, as AutoCAD does.
- **FIXED.** Every reader of an MTEXT's format codes (the layout, the display's font and width, FIND, REPLACE and SPELL)
  now reads them as AutoCAD 2021 does, through one tokenizer, so they cannot disagree. `\C`, `\c` and `\A` take digits and
  no semicolon: `\C1A` shows A in colour 1 (it showed "C1A" and needed `\C1;A`), `\c255A` in true colour 255,0,0, and
  `\A1B` stands B in the middle of the line. No digits is 0 (`\CA` is ByBlock, `\cA` black); a colour above 255 or
  below 0, a true colour above 16 777 215 and an `\A` other than 1 or 2 now reset to ByLayer or the baseline, where the app
  kept the one before. `\f`, `\F`, `\S` and `\p` run to the next semicolon wherever it is, and to the end of the text where
  there is none (`\fArialA` shows nothing, `\SA/B` is a stack), where the app showed the letter and went on. FIND, REPLACE
  and SPELL found no word after `\H2xWord` or `\C1Word`; they do. An integer after `\H`, `\W`, `\Q` or `\T` that is
  followed by a space, a hyphen or a slash is a fraction (`\H2 1/2B` is 2.5 high, `\H2-1/2B` the same, `\H2/3B` 0.667) or
  no number: `\H2 B` is `\H` and the text "2 B" at the height of the MTEXT, where the app made B 2 high. MTextFormatCodeTests
  and MTextCodeReadersTests: 266 cases at AutoCAD's numbers, 184 failed before the fix. Laying out 10 000 MTEXTs takes
  about 490 ms, where it took 640 to 990 ms.
- **FIXED.** MIRROR with a second point 1E-8 to 2E-5 from the first no longer ends with "Cannot normalize a zero-length
  vector." after the Erase prompt: it says "Invalid point: the mirror line is too short." and asks again.
- **BREAKING.** The built-in font is now OpenCAD2D ISO (`opencad2d.shx`; `CadFonts.BuiltInName` is `"opencad2d"`,
  `CadFonts.BuiltInTitle` is `"OpenCAD2D ISO"`), our own single-stroke upright lettering in the proportions of ISO 3098
  type B (TCVN 7284), 242 glyphs including the 134 Vietnamese letters, in a compiled file of 8 685 bytes, replacing OpenCAD2D
  Simplex (`ghsimplex`, the Hershey Roman Simplex).
  `ghsimplex` and `ghsimplex.shx` are removed, with no alias: `CadFonts.IsBuiltIn("ghsimplex")` is false, and a text style
  naming `ghsimplex.shx` is a missing font, drawn in the FONTALT replacement. A settings file that saved the FONTALT
  replacement as exactly `ghsimplex` or `ghsimplex.shx` is read as `opencad2d` when the settings load (any other saved
  value, a list with a big font among them, is read as it was). The sample template `OpenCAD2D-mm.dwt` has its styles in
  `opencad2d.shx`. A plugin compiled against `CadFonts.BuiltInName` has the old name inlined and must be rebuilt. Text
  set in the new font has other widths than in the old one (ABC123 10 high is 42.5 wide), so the row heights of a table,
  the lines an MTEXT wraps to and the words of a dimension that were measured in the old one come out different, as
  AutoCAD 2021 gives them with `opencad2d.shx`; and a stacked fraction (`\SH/H;H`) in the built-in font moves the pen on
  by 0.1 of the text height past its ink, as AutoCAD does with `opencad2d.shx`, where other SHX fonts keep a seventh.
- **FIXED.** The ink of a half circle drawn by a bulge arc of exactly 180 degrees (code 12, bulge 127 or -127) in an SHX
  font was measured by the square about its circle when it started above the X axis, as an octant arc is; AutoCAD
  measures it by the rectangle of its chord, bulging to the arc's side. A one-glyph unifont `1 12 40 -30 127 0` has its
  box (-15, -50) to (40, 0), not (-5, -40) to (45, 10), and the `&` of `opencad2d.shx` 100 high (0, -10) to (70,
  103.685), not (10, 0) to (70, ...): text boxes, justification and wrapping of any SHX text with such an arc follow.
- **NEW.** `CadDocuments` for C# plugins, as AutoCAD's `DocumentManager` (A9): `CadPluginContext.Documents`,
  `CadCommandContext.Documents` and `CadCommandSession.Documents` list the open drawings, make one active, open a DWG or
  DXF, add a new one from the template, and close one with `Close`, `CloseAndDiscard` or `CloseAndSave`. A drawing with
  changes not saved, or with a command running, is refused as Remote's close refuses it, and a session with no window
  says so. Opening, adding and showing are refused while a command runs in the drawing shown. Plugin API 1.4.
- **NEW.** Python `Drawing.zoom_extents`, `zoom_window`, `zoom_objects`, `zoom_center`, `zoom_scaled`, `zoom_previous`,
  `pan`, `regen`, `view` (a `Box`) and `has_window`, the named views (`named_views`, `save_view`, `restore_view`,
  `delete_view`), the selection by area (`select_all`, `select_window`, `select_crossing`) and `get_variable` /
  `set_variable`, over `CadEditor` and the Remote calls (A6). A drawing of a file has no view: its view calls raise
  `RuntimeError`; the selections, the variables and `regen` work there.
- **CHANGED.** Python `Entity` is generic and `Entity.net` is typed: `drawing.entities(DxfCircle)` and the `add_*` calls
  give an `Entity[DxfCircle]` (a class asks for its subclasses too, and a class that is no `DxfEntity` raises `TypeError`),
  while an entity read by name, handle, selection or pick has a `DxfEntity` for `net`. A type checker now asks for
  `entities(DxfCircle)`, `if isinstance(e.net, DxfLine):` or `match e.net: case DxfCircle() as c:` before `e.net.Radius`,
  as C#'s `if (obj is DxfLine line)`; nothing changes at run time.
- **NEW.** `CadEditor`, reached as `CadCommandSession.Editor`, `CadCommandContext.Editor` and `CadPluginContext.Editor`
  (A6): zoom extents, window, objects, center, scaled and previous, pan, regen, the current view, named views, selection of
  all, by window and by crossing window, and system variables. Each call does what the command does, through the same
  code, as one undo step or inside the running command's step. Plugin API 1.3.
- **NEW.** Remote: `ZoomExtents`, `ZoomWindow`, `ZoomObjects`, `ZoomCenter`, `ZoomScaled`, `ZoomPrevious`, `Pan`, `Regen`,
  `CurrentView`, `SetCurrentView`, `HasWindow`, `NamedViews`, `SaveView`, `RestoreView`, `DeleteView`, `GetVariable`,
  `SetVariable`, `SelectAll`, `SelectWindow` and `SelectCrossingWindow` on `CadRemoteDocument`; the view requests are
  refused for a drawing not shown.
- **FIXED.** A typed -VIEW Restore in a session with no window now shows the view it restores.
- **FIXED.** Global settings > Folder preference: the kind of a default folder ("Drawing templates") was cut at 96 pixels and ran into its path; the kind column is now as wide as the longest kind of the page, with a gap of 16 pixels before the path, and the paths of Fonts, Hatch patterns, Linetypes, Lisps and Drawing templates start at one place. The Python block's "Passed over" list showed `3.10: no InstallPath` twice when HKCU and HKLM both register 3.10; where a tag is registered in both hives each line now names its hive (`3.10 (HKCU): no InstallPath`, `3.10 (HKLM): no InstallPath`, `3.13 (HKLM): 3.13 (HKCU) was chosen instead`).

- **NEW.** UCSMAN (UC, DDUCS) opens the UCS dialog as AutoCAD 2021's: Named UCSs (Unnamed, World, Previous and the named systems, with Set Current, Rename in place, Delete and Details to four decimals relative to World, Unnamed or another system), Orthographic UCSs (Top, Bottom, Front, Back, Left and Right with their depth) and Settings (the UCS icon, Allow Selecting UCS icon, Save UCS with viewport, Update view to Plan). Nothing reaches the drawing until OK, and OK is one undo step named UCSMAN; Cancel changes nothing. Names are refused with AutoCAD's three messages, and World, Previous and Unnamed as taken. UCSMAN with no window now says the window shows it, in place of listing the named systems.

- **BREAKING.** Python no longer runs inside the app: every Python tool runs in a `python.exe` process of its own, with a
  venv of its own, and a `.py` file directly in `Pythons` or in the Python Startup Suite no longer loads (Tools as folders).
  `OpenCAD2D.Python` (the pythonnet embedding) and its 22 test files are gone, `Python.Runtime.dll` and
  `OpenCAD2D.Python.dll` are no longer in the setup, and nothing of Python is in the app's process; the app brings no Python,
  and a tool runs on the CPython 3.11 to 3.15 (64-bit, with pip) of the machine or the one set in Global settings. A Python
  tool is now a folder with a `requirements.txt` (it may be empty), an `__init__.py` and a `plugin.json`. A lone `.py` in
  `Pythons`, in the Startup Suite, given to APPLOAD or PYLOAD or dropped on the window is refused with `Rings.py is a lone
  Python file: a Python tool is a folder with requirements.txt. Put it in Pythons\Rings\ as __init__.py (see the Python
  guide).`; to convert one, move it into a folder as `__init__.py`, add an empty `requirements.txt` and a `plugin.json` that
  names its commands. A `.py` entry left in the Startup Suite is refused at each start with the same line. Gone with the
  embedding: `add_ribbon_tab` and the other functions that added to the window (`ribbon_button`, `ribbon_panel`,
  `ribbon_tab`, `split_button`, `menu_item`, `separator`, `icon_from_*`, `add_ribbon_panel`, `add_drawings_button`,
  `add_drawings_menu`, `add_canvas_menu`, `add_properties`, `show_command_buttons`, `Context`) of `opencad2d.cad`, which a
  tool declares in its `plugin.json` instead (tabs of panels of buttons that start a command); `session()`, the raw .NET
  command session, which has no meaning in another process; the `PYTHONNET_PYDLL` setting; and the python3XY.dll checks of
  the finder (a process needs `python.exe` only). `@command`, the `get_*` prompts, `cmd`, `PAUSE`, `write`,
  `active_drawing`, `documents`, `Cancelled`, `@on_unload`, `connect()`, `open_file` and `new_file` keep their names. A tool
  that needs a ribbon callback, a menu, a Properties row, an object snap or a side panel is a C# plugin.

- **NEW.** A plugin's object snap works through a layout viewport (its point taken to the sheet, the snap's glyph and tip
  shown), on entities inside blocks (the finder gets a copy placed where the references draw it, up to eight levels;
  `CadSnapRequest.BlockReferences` lists the references, outermost first) and for a point typed after the snap's keyword
  (the point nearest the typed one is taken; `No Circle top found for specified point.` where there is none).
- **CHANGED.** The Block Editor works in the World UCS, as AutoCAD 2021's does: UCS and UCSMAN there say
  `** UCS command not allowed in block editor. **`. BCLOSE (Save, Discard, or with no change) puts the model's UCS back,
  unnamed; UCS Previous then gives the World UCS, then that UCS.
- **NEW.** A plugin's ribbon panels shrink as the window narrows (right-most first, each to icons, then each collapsed to
  one button with its title and its first item's icon) and may have a slide-out (`CadRibbonPanel.SlideOutItems`, opened
  by "Title ▾"), a dialog launcher (`DialogLauncher`) and macro buttons (`CadRibbonButton.FromMacro`, AutoCAD's
  `^C^C_CIRCLE;0,0;\` form). Plugin API 1.2: a plugin using them states `[assembly: CadPluginApi("1.2")]`.
- **NEW.** The coordinate rows of a point a plugin gives (`CadPropertyRow.Point` with a setter) show a pick-point button on
  the row being edited: `Pick a point in the drawing:`, a rubber band from the point, the setter called once.
- **NEW.** The coordinate rows of the presenter's own Properties show the pick-point button too, as AutoCAD 2021: Start and
  End of a line, Center of a circle, an arc, an ellipse and a viewport, Position of a text, an MTEXT, a block reference and an OLE
  object, Vertex of a polyline. The pick asks `Pick a point in the drawing:`, sets X and Y of every selected object as one
  undo step and keeps each Z; Radius, Scale and the read-only rows have none.
- **CHANGED.** Color rows in Properties and Quick Properties draw a swatch before the value, as AutoCAD 2021: the layer's
  colour for ByLayer, white for ByBlock, White (7) split black and white, none for *VARIES*; the list draws one before each
  colour and a wheel before Select Color.... The drop-down of a collapsed ribbon panel with a slide-out also shows the
  slide-out, after a line.
- **NEW.** A C# plugin is a folder with a `.csproj`, compiled by the app (Tools as folders; C# source plugins). A folder with
  a `.csproj` loads as a `.dll` does (the `Plugins` folder, PLUGINLOAD/NETLOAD of the folder or its `.csproj`, the startup
  list, PLUGINRELOAD), compiled by Roslyn when it loads, on .NET 8 only, and Visual Studio builds the same folder. The app
  reads the `.csproj` with no MSBuild: the sources are every `.cs` below the folder but `bin\` and `obj\`, less `<Compile
  Remove>`; `AssemblyName`, `Version` (written into the assembly as the SDK does, 1.0.0 where there is none, so the plugin
  loads as `Stairs 1.0.0`), `LangVersion`, `Nullable`, `ImplicitUsings`, `UseWPF`, `UseWindowsForms`, `<Using>` (`Remove`
  too), `DefineConstants`, `AllowUnsafeBlocks` and `GenerateAssemblyInfo` are read, the symbols are the SDK's for
  `net8.0-windows` Release, a `<Reference>` takes a `HintPath` to a `.dll` inside the folder (a name of the app's own is its
  own, whatever its `HintPath`), and a `<PackageReference>` to `OpenCAD2D.Core`, `OpenCAD2D.Remote`, `OpenCAD2D.Wpf` or
  `GeometryHelper` is the app's own and downloads nothing (a version above the app's gives a line and the compile goes on
  against the app's), and so does one for the build alone (`PrivateAssets="all"`, or an `IncludeAssets` with no `compile` or
  `runtime`). What is not read is a line of the load log, and the plugin loads: an element with a `Condition`
  (`Stairs.csproj: <PropertyGroup Condition="..."> was not read (conditions are not evaluated).`), a `Directory.Build.props`
  in the folder, a `$(...)` in a value, a target framework that is not .NET 8. What cannot be read is refused with a line
  per error in the form of a compiler's (`Stairs.csproj(7,10): error OC0002: ...`): a `<ProjectReference>`, a package with
  no version, `EnableDefaultCompileItems` false, a `<Compile Include>` or a `HintPath` outside the folder. A lone `.cs`, a
  folder of `.cs` files with no `.csproj`, and a folder with two `.csproj` files are refused, saying how to make a plugin of
  them. The build is cached in `%LocalAppData%\OpenCAD2D\PluginCache` by the SHA-256 of the `.csproj`, the sources, the
  libraries and the packages, so a start with unchanged files compiles nothing; the first compile of a session takes about
  0.9 s and a later one about 20 ms for a plugin of 220 lines. A source that does not compile is refused with a line per
  error, `Stairs.cs(12,9): error CS0103: The name 'x' does not exist in the current context`, the first 20.
  `CadPluginLoader.CanCompile` and `Compile(name, projectPath)`, `CadPluginBuild` (with `Warnings`),
  `CadLoadedPlugin.IsSource` and `CadPluginAssemblyLoader(cacheFolder)` are new. The screenshot mode
  (`--screenshot ... --plugin Stairs`) keeps its builds in its own temporary folder, never
  `%LocalAppData%\OpenCAD2D\PluginCache`.

- **NEW.** A C# plugin may use NuGet packages (Tools as folders). Any `<PackageReference>` that is not the app's own is a
  package of nuget.org, restored with no .NET SDK, as `dotnet restore` does: the lowest version that meets each reference
  (`Version="13.0.3"` is 13.0.3), its dependencies for `net8.0-windows`, cousins at the higher of their lowest versions,
  every range checked (`Stairs.csproj(6,10): error NU1107: Stairs needs Foo [2.0.0, 3.0.0); Bar 1.0.0 needs Foo [1.0.0,
  2.0.0); no version meets both.`, and NU1605 for a reference lower than another package needs), a floating `1.*` taking
  the highest once and keeping it until the `.csproj` changes, and the nearest framework's assets (`lib/net6.0` over
  `netstandard2.0`). Nothing is downloaded without a yes: before the first download and when the list changes the app lists
  the packages (the plugin's own first, each marked `download` or `in the cache`) in a window, **Packages for Stairs**, with
  the buttons `Don’t load` and `Download and load` (closing it is a no); a no, or an unreachable nuget.org, leaves the plugin
  not loaded with the line to reconnect and PLUGINRELOAD. A connection is given up after 5 s, so a source whose address
  drops packets is reported in 5 s (two runs), where Windows alone waits 42.8 s over its two tries. With FILEDIA 0 the question
  is asked on the command line: a typed PLUGINLOAD (which asks its path there) or PLUGINRELOAD stops before anything is
  downloaded, writes `Stairs needs these packages from nuget.org:` and a line `  Newtonsoft.Json 13.0.3` for each package
  not in the cache, and asks `Download these packages? [Yes/No] <No>:`; Yes downloads and loads, No or Enter leaves the
  plugin not loaded with the badge **needs packages**, and FILEDIA 1 asks the window as before. Packages are kept in
  `%LocalAppData%\OpenCAD2D\NuGet\packages` and the resolved list in `PluginCache`, so a start asks nothing of the network;
  two plugins with `Foo 1.0.0` and `Foo 2.0.0` both run, each in a load context of its own. A library the app carries itself
  (the .NET runtime, WPF, Roslyn, NuGet's own, `Newtonsoft.Json` among them) is shared: the source is compiled against the
  app's copy, as it runs on it, also where that copy lies in the app's folder and in no .NET folder, and a package that asks
  a higher assembly version gets a line (`Stairs asks System.Text.Json 9.0.0; the app runs 8.0.0.`). A package's native
  `.dll` is loaded by its full path, two of one name side by side. A package's library first used after a garbage
  collection still loads. The app carries NuGet.Protocol 7.9.0 (Apache-2.0): 7 files, 3 060 832 bytes more in its folder.
  `CadPluginAssemblyLoader.ConfirmDownload`, `CadPackage` and `CadPackageQuestion` are new.

- **NEW.** Load/Unload Applications takes C# source and Python tools (Tools as folders). A `.csproj` loaded with Load… or
  dropped on the table loads its folder; the row has the Kind `C# source` in light purple and a chip of its own, `C#
  source 1` (`Plugins` counts the .dll plugins only). A source that does not compile shows **Stairs.csproj** `did not
  compile: 2 errors.` and the first error on the red line, with `Every error is on the command line` on the right and every
  error in its tooltip. A new button, **Load folder…**, chooses a tool's folder: a folder with a `.csproj` loads as a C#
  plugin, and one with a `requirements.txt` as a Python tool. The file types of Load… (and of NETLOAD's Open dialog) offer
  **C# plugin (\*.csproj)**, Load… offers **Python tools (requirements.txt)**, the button reads `Load… (.lsp, .dll)`, and the
  foot says `Each tool run is one undo step. A tool runs its code, and its packages’ code, with your rights.` A plugin whose
  packages were not downloaded (the question answered No) stays in the table, unticked, with the amber badge **needs
  packages** after its name, and ticking it asks again; one whose packages could not be restored (nuget.org not reached, a
  package or version not found) has the red badge **install failed** and the red line says `Stairs was not installed:` and
  the first error, with `Every line is on the command line` on the right. `AppLoadViewModel.LoadFolder`, `AskFolder`,
  `LoadFolderCommand`, `AppLoadRow.Badge` and `AppLoadBadge`, `CadPluginAssemblyLoader.CompilingProject` and
  `CadPackageQuestion.Question` are new; the question's window, `PackageQuestionWindow`, takes its texts from
  `PackageQuestionViewModel`, so that the pip question of a Python tool can use it. A Python tool's row shows the blue badge
  **installing…** while pip installs its packages (the window stays free), **needs packages** after a No (the tool stays loaded
  and its next command asks again) and **install failed** with the red line `Grid was not installed: ERROR: ...` (pip's first
  error, every line on the command line); a change of `requirements.txt` that failed but left the tool on its old venv gives the
  red line and no badge. A tool's folder dropped on the table loads (a `.csproj` or a `requirements.txt` in it); other folders
  are still passed over. `MainViewModel.AskPythonPackages` and `AppLoadBadge.Installing` are new.

- **NEW.** Python tools in the app (Tools as folders). The Pythons folder, the Startup Suite, APPLOAD, PYLOAD and a drop
  load a folder with a `requirements.txt`; its commands and ribbon buttons come from its `plugin.json` (`"commands"` take
  names or objects with an alias, a description and `usesSelection`; `"ribbon"` takes tabs of panels of buttons that start a
  command), so they show at once with no Python running. The tool's process starts at its first command: the drawing waits
  at `NAME is working... (Esc to stop)`, the first command takes 0.37 s and the next 16 ms, and a process of about 66 MB stays
  until the tool is unloaded or the app closes: the app starts it suspended, in a job object, reads its standard output and error
  (what it `print`s goes to the command line of the run), and talks to it over two anonymous pipes of lines of JSON (`run`,
  `cancel`, `unload`; `ready`, `ended`). A command the `plugin.json` names and the tool does not define fails its run
  with `GHOST is declared by the tool, but it has no @command of that name.` PYRUN runs a lone `.py` once in a new process
  of the default venv (0.65 to 0.94 s), as a run named `PYRUN` (one undo step, Esc takes it back). A tool that raises
  (`STAIRSPY: ValueError: no stairs here`), exits (`... ended unexpectedly (exit code 3).`) or hangs (`... did not stop within
  5 s and was ended.`) leaves the drawing as it was and the next command works; Esc in `while True: pass` stops the loop at
  once, and the process is put in a job object that ends it, and every process it starts, even when the app is killed.
  `cmd()` inside a run nests the command as AutoLISP's `command` does, with no "Resuming" line. The examples of
  `src/python/examples/in_app` are now tool folders in `tools`, still run by tests.
  `--screenshot ... --plugin Pythons\HelloWorld --run PYHELLOWORLD` loads and runs a tool folder with no window, so that
  the installed app can be probed headless: its venvs are made under the run's own temporary folder and removed with it,
  the question of pip is answered no (a tool that needs packages exits 2 with `... its packages were not approved`; one with
  an empty `requirements.txt` runs, in about 3 s), and the lines the command wrote are on the output; a command that ends
  with an error (the tool raises or exits, no Python, no venv) exits 2 as well, its lines on the error output.

- **NEW.** A venv for each Python tool, and pip to fill it (Tools as folders). A tool's packages are installed into
  `%LocalAppData%\OpenCAD2D\python\envs\<name>-<8 hex>`, made from the Python of Global settings or the newest 3.11 to 3.15
  of the registry (the finder needs only `python.exe`), so two tools may pin two versions of one package and both run (numpy
  1.26.4 and 2.3.5 side by side). The app lists what pip's dry run would install and asks before anything is downloaded, in
  a dialog, `Packages for Python tool Survey` (a table of Package, Version and Asked as: the `requirements.txt` line, or `for
  shapely` for a package another needs; the tool's own packages first, in bold; `Don’t install` and `Install`), or on the
  command line, `Install with pip [Yes/No] <No>:`, with FILEDIA 0 (with no way to ask, nothing is installed and the tool is
  refused with the line to run by hand). `numpy>=1.26` is installed as `numpy==1.26`, as written where pip has no
  wheel of `1.26` for the Python in use (the pinned dry run takes wheels only, so a version that has just a source archive
  fails in 1.2 s instead of being built in minutes), with no warning; an option line in `requirements.txt` is refused. A
  missing version, a clash or no network gives pip's lines and the command to run by hand (where the venv is not made yet,
  `"<python>" -m venv --without-pip "<venv>", then "<python>" -m pip --python "<venv>\Scripts\python.exe" install -r
  "<folder>\requirements.txt"`) and changes nothing; a change of `requirements.txt` builds a new venv beside the old and
  swaps it in, and a failed change keeps the old venv, which the tool goes on running on, with the failure lines given beside
  it, on the command line and in the log. `opencad2d` is not installed in each venv: a `.pth` file puts the app's copy and a
  shared base (pythonnet 3.2.1, clr_loader 0.3.1, cffi 2.1.1, pycparser 3.12, installed once for a Python version from the
  wheels the app ships in `python\wheels`, 1 273 451 bytes) after the venv's own packages. A venv with its `.pth` takes 75 to
  98 ms and 0.54 MB, the base 1.7 s once and 2.7 MB; a tool with one package, 2.1 s.

- **NEW.** The run of a tool's command, for tools that run in a process of their own (Tools as folders). A program the app
  starts reaches the app on a second pipe, `OpenCAD2D.<process id>.tools`, which is never announced in the instance folder,
  stays open whatever "Allow other programs to connect" says, and takes a client only with the token of its tool (`hello`
  carries it; 32 random bytes as 64 hex letters; a connection without or with a revoked one is refused and closed). The
  command the user types is the run: the drawing waits at `STAIRSPY is working... (Esc to stop)`, which takes no typed text,
  point or Enter, only Esc. The tool joins the run (`app.JoinRun(7)`) and every request of it is part of the command:
  `run.Ask(CadAskKind.Point, "Specify the first step")` asks on the command line and waits for the user, with no lock held, so
  another client is served meanwhile; `CommitChanges` goes into the command's one undo step named after it; `run.End()` keeps
  the step, `run.Fail(line)` takes it back and writes the line; `RunCommand(name, answers)` runs a command inside the run and
  `CadRemoteRun.Pause` leaves a prompt to the user (the `feed` request); Esc takes back everything the run committed, answers
  a question open as cancelled (`IsCancelled`) and refuses every later request of the run. A request of an ended run is
  refused, `The run 7 of STAIRSPY has ended.`; `pick` and `command` are refused inside a run. `python -m opencad2d.tool
  <folder>` runs a folder's `@command` functions as commands of the app, and `OPENCAD2D_LIB` names the folder `import
  opencad2d` loads the assemblies from. For the app's host: `CadRemoteServer.ForTools`, `IssueToken`, `RevokeToken`,
  `CreateRunCommand` and `CadRemoteToolRun` (Core); `new CadApplication(pipe, token)`, `JoinRun`, `CadRemoteRun` and
  `CadRemoteAnswer` (OpenCAD2D.Remote); `CadPrompt.ForWaiting` and `IsWaiting`.

- **NEW.** `CadRemoteDocument.Extents` and the request `extents` of the channel read the extents a drawing's header holds
  (`EXTMIN`, `EXTMAX`) as a `CadRemoteExtents` with `Min`, `Max` and `IsEmpty`: a tool has no `drawing.net.Database`.
  `CadPluginManifest` gains `CommandEntries` and `Ribbon` (the commands as declared and the ribbon tabs, panels and buttons of
  `plugin.json`); its `commands` takes names or objects, and `Commands` stays the names.

- **NEW.** Three sample plugins and three sample tools ship with the app, each a folder that opens as it is (Tools as
  folders). The C# plugins are `Plugins\HelloWorld` (`HelloWorld.csproj` and `HelloWorldCommands.cs`: the command
  `HELLOWORLD` writes `Hello, World!`, the smallest plugin there can be), `Plugins\DrawingInfo` (`CSINFO`, the drawing's
  name, objects, layers, blocks and extents) and `Plugins\DrawRandom` (`CSRANDOM`); Visual Studio opens each from
  `examples/HelloWorld`, `examples/DrawingInfo` and `examples/DrawRandom`. The Python tools are `Pythons\HelloWorld`
  (`PYHELLOWORLD` writes `Hello, World!`), `Pythons\DrawingInfo` (`PYINFO`, the same lines as `CSINFO`, its `Extents`
  line read over the channel) and `Pythons\DrawRandom` (`PYRANDOM`), in `examples\Pythons`. Delete a folder to remove
  its sample.

- **CHANGED.** `Plugins\DrawingInfo` is the C# source of its plugin, and the built `OpenCAD2D.Plugins.DrawingInfo.dll` is no
  longer in the app's `Plugins` folder; the project `examples/OpenCAD2D.Plugins.DrawingInfo` (a netstandard2.0 library, in
  `OpenCAD2D.slnx`) is the folder `examples/DrawingInfo`, a plugin folder that Visual Studio opens, and is no longer built
  with the solution. The command keeps its name, `GHDWGINFO`, and writes the same lines; the Python sample
  `Pythons\GH-DrawingInfo.py` is the folder `Pythons\DrawingInfo`, with `GHPYINFO` as before.

- **FIXED.** An SHX octant arc (shape code 10) read its radius byte as unsigned, where AutoCAD 2021 reads it as a signed
  byte: from 128 on the radius is negative and the arc is reflected through the pen. A quarter arc (start octant 0, two
  octants, counter-clockwise) with the byte 200 now ends at (+56, -56), as AutoCAD measures it, where it ended at (-200,
  +200); the byte 128 ends at (+128, -128) and 255 at (+1, -1), and their ink boxes, which AutoCAD's TEXTBOX gives as
  -11.598 -67.598 to 56 0 for the byte 200, are the same. A radius byte below 128 draws as before.
- **FIXED.** A .NET 8 plugin could fail with "AssemblyLoadContext is unloading or was already unloaded" when one of its
  commands first used a library of its folder after a garbage collection: the app held the plugin's collectible load
  context only weakly, so a collection began unloading it while the plugin still ran. The app now keeps every plugin's
  context until the plugin is unloaded. This was in 1.0.0 for a `.dll` plugin with a dependency it loads late.

- **FIXED.** Two or more drawings dropped on the window opened only one of them: selecting `app.dwg` and `cad.dwg`
  (about 4 MB each) in Explorer and dropping them on the DRAWINGS list opened the first of the drop and refused the
  other, the command line saying "Cannot open app.dwg yet: cad.dwg is still opening." The app opens one drawing at a
  time in the background and refused any other asked for meanwhile. A drawing asked for while another opens now waits
  ("app.dwg will open once cad.dwg is shown.") and opens when the one before it is shown, so every drawing of the drop
  opens as its own drawing, in order, the last shown. A file that cannot be read is said and the rest still open; one
  open already is shown in its turn; Esc stops the one opening and those that wait. The same holds for the Select File
  dialog (OPEN, Ctrl+O), which now lets several files be chosen, and for several files on the command line
  (`OpenCAD2D.exe a.dwg b.dwg`, or files dropped on the exe's icon), of which only the first was opened or handed to the
  running app. A drop on the drawing area of a window with a drawing open still inserts the first drawing, as before.

- **CHANGED.** The Settings tab of the ribbon has a button for every page of Global settings. It had Options,
  Appearance, Canvas, Shortcuts and Save and export; it now has Options, Appearance, Canvas, Units, Text and fonts,
  Folders, Security, Shortcuts and Save and export, in the order of the dialog's list, each opening the dialog on its
  page (Units and rounding, Text and fonts, Folder preference, Security were reachable only from the dialog's own list
  or `--dialog settings:`). The tooltip of each names the page in full.

- **FIXED.** Load/Unload Applications (APPLOAD) dropped an unloaded file of the `Lisps` or `Pythons` folder beside the
  app from the table once the window was closed: a table of 20 rows came back with 14, with the unticked Lisp and Python
  rows gone. Only an unloaded file from elsewhere was kept in the known list. The files of those folders are now kept
  there too, so an unticked row stays, with its commands, in a new window and after a restart, and ticking it loads it.
  Remove still refuses these rows, since they load at every start.

- **CHANGED.** The old name is gone where a user can see it. The window's title-bar name, when a host gives none, is
  `OpenCAD2D` (it was `GeometryHelper Drawing`); Paste Special names the source of copied objects `OpenCAD2D entities`; the
  PSOUT file's `%%Creator:` line says `OpenCAD2D PSOUT`; the plugin load contexts in a debugger or a crash dump are
  `OpenCAD2D plugin <name>`; the Plot and Lisp "Help" links open the guides at `nguyenthanguth.github.io/opencad2d/guides/`,
  not the old site; and the NuGet description of `OpenCAD2D.Wpf` says OpenCAD2D. The private clipboard formats are now
  `OpenCAD2D.Entities`, `OpenCAD2D.DesignCenter`, `OpenCAD2D.ToolPaletteTool` and `OpenCAD2D.Objects`, so objects copied in
  an older GeometryHelper build do not paste into this one, and the other way round. The built-in plotters, font, pattern
  and linetype files and sample template are renamed too (next entry).
- **CHANGED.** The last built-in names of the old product are OpenCAD2D's, and the old ones are still read. The plotters
  are `OpenCAD2D PDF`, `OpenCAD2D PNG` and `OpenCAD2D JPG` (the values of `CadPlotSettings.PdfDevice`, `PngDevice` and
  `JpgDevice`), and the PDF's Creator and Producer say `OpenCAD2D PDF`. A layout or page setup that names `GeometryHelper
  PDF`, `PNG` or `JPG` plots to the new device, a raster one on its size in pixels, and `UseDevice`, `DeviceName` and
  `-PLOT` take the old names; saving a drawing writes the new names into every layout and page setup that held an old one,
  and a printer's name as it was. The built-in font is titled `OpenCAD2D Simplex` (`CadFonts.BuiltInTitle`, the header of
  `ghsimplex.shx`, now 4 835 bytes, the Font Name list and the missing-font line); its file name `ghsimplex`, which drawings
  and settings store, is unchanged. The built-in sets are `OpenCAD2D.pat` and `OpenCAD2D.lin` (`CadPatternFile.BuiltInName`,
  `CadLinetypeFile.BuiltInName`, and the copies in `Hatches` and `Linetypes`); `GeometryHelper.pat` and `GeometryHelper.lin`,
  typed at `-LINETYPE` or given as a hatch's pattern file, still mean the built-in sets. The sample template is
  `OpenCAD2D-mm.dwt` (`CadTemplate.SampleFileName`), described as an "OpenCAD2D metric sample"; a Default template for QNEW
  saved as `GeometryHelper-mm.dwt` finds it, where no file of the old name is there. No public member is renamed; a plugin
  compiled against the old constants passes the old strings, which are read as the new ones.
- **CHANGED.** The application menu button at the top left of the window shows the OpenCAD2D mark (the white tile with the
  O and the C, as the icon frames below 24 px) in place of the letter G. Under the pointer, pressed, or while the menu is
  open, it has a 2 px ring of light blue around it, where it used to turn lighter blue.
- **FIXED.** Convert to Line in the hover menu of a polyline's arc-segment midpoint grip asked "Pick to convert to line
  segment:" and waited for a point; as in AutoCAD 2021 it now makes the segment straight at once (bulge 0, vertices
  unchanged, one undo step), for LWPOLYLINE and 2D POLYLINE alike. Convert to Arc still asks for the arc's midpoint, and
  Remove Vertex still acts at once.
- **FIXED.** A .dwg or .dxf opened from Explorer while OpenCAD2D runs opened a second window. It now opens in the running
  window as a new drawing, the window brought to the front, as AutoCAD 2021 does: the new start hands the file's full path
  to the running app on a pipe named `<AppName>.Open.<session>` and exits with 0 in 121 to 153 ms (six runs on the built
  exe). A drawing already open is shown; two opened at once, or one handed over while another opens or a dialog is up,
  wait and open one after the other. A start with no file, or one the running app does not answer within 4 s, still opens
  a window of its own. Nothing public changes.
- **FIXED.** `OpenCAD2D.exe --screenshot ... --plugin x.dll` (and a source plugin's folder) rewrote the user's `%AppData%\OpenCAD2D\settings.json`
  with default settings plus the application it remembered. A screenshot now keeps its settings, tool palettes, plot
  styles and LISP registry in a folder of its own, `%TEMP%\OpenCAD2D\ScreenshotSettings\<process id>`, and never reads
  or writes the user's.
- **CHANGED.** The setup carries what tools as folders need, and no longer the Python embedding: the C# compiler (Roslyn
  4.14.0, with System.Collections.Immutable and System.Reflection.Metadata 9.0.0 in place of the runtime's 8.0), the NuGet
  client (`NuGet.Protocol` 7.9.0 with its five libraries and `Newtonsoft.Json`: 7 files, 3 060 832 bytes), the wheels of
  pythonnet, clr_loader, cffi and pycparser in `python\wheels` (8 files, 1 273 451 bytes) and `THIRD-PARTY-NOTICES.md` beside
  the exe, with the notices of the NuGet client (Apache-2.0), `Newtonsoft.Json` and the four wheels; `Python.Runtime.dll`
  and `OpenCAD2D.Python.dll` (4 files with their `.pdb` and `.xml`, 567 453 bytes) are gone. The setup is 83 726 648 bytes
  (79.8 MB) in place of 79 946 201 (76.2 MB) before the compiler, 4.7 % more, and the install folder holds 311 files in
  place of 286, 12.1 MB more (setups built by `make-installer.ps1` at 3f059fb7 and at f7475ff2 with the notices added).
  Installed from a probe setup of the same files, the three sample plugins (`HELLOWORLD`, `GHDWGINFO`, `STAIRS`) compiled
  and ran in the installed app, and the installed Python wheels installed offline into a venv of Python 3.14, where the
  installed `opencad2d` package and both sample tools imported.
- **CHANGED.** PLUGINLOAD asks `Enter the path of a plugin (.dll, .csproj or its folder)`, and a path that is no plugin is
  refused with `... a plugin is a .dll, a .csproj, or the folder of one.`
- **CHANGED.** A plugin whose assembly states no version (0.0.0.0) is shown with none: `Loaded Stairs: STAIRS.` in place of
  `Loaded Stairs 0.0.0.0: STAIRS.`
- **CHANGED.** The app is built English only: the translations .NET ships of WPF and WinForms (13 folders, `cs`
  to `zh-Hant`, 16 MB) are no longer beside the exe, and the setup's install folder holds 286 files in place of 507.
- **CHANGED.** The icon of a drawing file (.dwg, .dxf) is the app's tile with its corner folded over like a page, in place
  of a tall page: at 16 pixels in Explorer the mark is now as large as the app's own.

## 1.0.0 (2026-11-23)

**BREAKING.** The CAD app of GeometryHelper is now OpenCAD2D, in a repository of its own. The history of its files came
along; the notes of its earlier changes are in GeometryHelper's `CHANGELOG.md` up to 13.12.0, under the names
`GeometryHelper.Cad`, `GeometryHelper.Cad.Wpf` and `GeometryHelper.Cad.App`.

- **Names.**
  - The libraries: `GeometryHelper.Cad` is the assembly `OpenCAD2D.Core` with the namespace `OpenCAD2D`, and
    `GeometryHelper.Cad.Wpf`, `.Cad.Python`, `.Cad.Remote` are `OpenCAD2D.Wpf`, `OpenCAD2D.Python`, `OpenCAD2D.Remote`
    (assembly and namespace). Class names are unchanged. A C# plugin references `OpenCAD2D.Core.dll` (and
    `OpenCAD2D.Wpf.dll` for UI) and writes `using OpenCAD2D;`.
  - The app is `OpenCAD2D.exe`, shown as OpenCAD2D in its title, Task Manager and Explorer.
- **Python.** The package is `opencad2d`: `from opencad2d.cad import command`. `from opencad2d import command` works
  too: the package hands out the names of `opencad2d.cad`. The variable that picks the runtime is `OPENCAD2D_RUNTIME`.
  The stubs of the .NET names are two packages, `GeometryHelper-stubs` and `OpenCAD2D-stubs`.
- **The user's files.**
  - Settings, plot styles, templates, tool palettes, the crash log, the plugin list and `filter.nfl` are kept in
    `%AppData%\OpenCAD2D`. The first start copies them from `%AppData%\GeometryHelper\Drawing` and
    `%AppData%\GeometryHelper\Dxf`, and leaves those folders as they were.
  - Running apps announce themselves in `%LocalAppData%\OpenCAD2D\Remote`, and the pipe of an app is
    `OpenCAD2D.<process id>`.
  - **FIXED.** Only a start that opens the window copies the old folders: `OpenCAD2D.exe --screenshot`, `--register`
    and `--unregister` leave `%AppData%\OpenCAD2D` alone. **NEW.** `CadAppHost.ShowsWindow(args)` tells which
    arguments open the window.
- **CHANGED.** A new icon, for the app and for a drawing file: on a white rounded tile, a blue O holds a C, and the C
  holds the letters AD. The docs site carries it as its logo.
- **Files in Explorer.** `.dxf` and `.dwg` files are associated through the ProgID `OpenCAD2D.Drawing`. Writing or
  removing the association takes out `GeometryHelper.Drawing`, so "Open with" lists the app once.
- **The core.** GeometryHelper (geometry, DXF, DWG) is used as `Lib\GeometryHelper.dll`, built from GeometryHelper's
  `6bb8136e`.
- **NEW.** Packages: the Python package `opencad2d` on PyPI (`pip install opencad2d`), and the API on nuget.org as
  `OpenCAD2D.Core` and `OpenCAD2D.Remote`, which depend on the `GeometryHelper` package of the version in `Lib`. The
  core's default fonts, hatch patterns, linetypes and template come with `OpenCAD2D.Core` and are copied beside the
  program that uses it. A published GitHub Release puts out the wheel and the packages together.
- **NEW.** A Windows installer, `OpenCAD2D-Setup-<version>.exe` (78 MB for 1.0.0), built by
  `tools\installer\make-installer.ps1` with Inno Setup 6 and by the workflow `publish-installer.yml`, which attaches it
  to a GitHub Release. It carries the .NET 8 runtime, so no .NET is needed on the machine; it installs for the current
  user with no administrator, into `%LocalAppData%\Programs\OpenCAD2D`, with a Start menu shortcut, a desktop shortcut
  (not ticked by default) and a ticked task that associates .dxf and .dwg files (`OpenCAD2D.exe --register`).
  Installing over a running app closes it first. Uninstalling removes only what the setup installed and takes the file
  association out where it names that install; the settings in `%AppData%\OpenCAD2D` and the files a user added to
  `Lisps`, `Pythons`, `Plugins` and `Fonts` stay. Not signed, so SmartScreen warns on the first run.
