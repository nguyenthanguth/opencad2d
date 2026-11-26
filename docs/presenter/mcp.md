# Letting an AI agent drive OpenCAD2D (MCP)

`OpenCAD2D.Mcp.exe` is an [MCP](https://modelcontextprotocol.io) server: a program an AI agent (Claude Desktop, Claude
Code, Cursor, or any client of the Model Context Protocol) starts and talks to over its standard input and output, to open
a drawing, read what is in it, draw and change things, look at the result as a picture and check it. It is installed beside
`OpenCAD2D.exe` (by default `%LocalAppData%\Programs\OpenCAD2D\OpenCAD2D.Mcp.exe`) and needs nothing else: no Python, no
.NET installed, no window, no network port. Windows, 64-bit, as the app.

The server is a console program of its own, `src/OpenCAD2D.Mcp`, on .NET 8. The packages of the MCP SDK it uses are not in
`OpenCAD2D.Core`, in its NuGet package or in the Python wheel. It draws a picture with the same renderer as the app's
canvas, and answers "where is it drawn" with the same spatial query as [`CadSpatial`](xref:OpenCAD2D.CadSpatial), so an agent
checks a drawing against what the app shows.

## Registering it

Give the client the path of the exe and, in the arguments, how it gets its drawing.

**Claude Desktop** — `claude_desktop_config.json` (Settings › Developer › Edit Config), then restart Claude Desktop:

```json
{
  "mcpServers": {
    "opencad2d": {
      "command": "C:\\Users\\you\\AppData\\Local\\Programs\\OpenCAD2D\\OpenCAD2D.Mcp.exe",
      "args": ["--file", "C:\\plans\\plate.dxf"]
    }
  }
}
```

**Claude Code** — one command, or a `.mcp.json` in the project folder:

```
claude mcp add opencad2d -- "C:\Users\you\AppData\Local\Programs\OpenCAD2D\OpenCAD2D.Mcp.exe" --file C:\plans\plate.dxf
```

```json
{
  "mcpServers": {
    "opencad2d": {
      "command": "C:\\Users\\you\\AppData\\Local\\Programs\\OpenCAD2D\\OpenCAD2D.Mcp.exe",
      "args": ["--attach", "--read-only"]
    }
  }
}
```

**Cursor** — `.cursor/mcp.json` in the project, or `~/.cursor/mcp.json`, with the same `mcpServers` block.

A command line that is not understood stops the exe with a reason on standard error (exit code 2); a file that cannot be
opened stops it with exit code 1. `OpenCAD2D.Mcp.exe --help` lists the options and `--version` the version.

## Two ways to get a drawing

| | `--file <path>` or none | `--attach` |
|---|---|---|
| What it works on | a DXF or DWG file, opened in the server's own process by the engine itself (the way the Python `open_file` does) | the drawing of a running OpenCAD2D, through its [Remote](remote.md) channel (the way a program outside the app does) |
| The app | not needed; nothing is shown | must be running with "Allow other programs to connect" on ([the setting](remote.md#the-setting)); the changes appear in its window |
| Undo | the file's own undo steps, one for each call | the app's: `undo` types `U` into the app, which also takes back what the user drew last |
| `render_png` | draws the drawing as it is now, changes not saved included | draws the drawing as the app has it now, **unsaved changes included**: the server asks the app for a copy of its whole drawing (the request `database` of [Remote](remote.md#the-drawing-as-the-app-has-it)), reads it into memory and draws it there; the app's file is never written |
| Nothing written | until `save` | until `save`; the app's own autosave still runs |

With neither, no drawing is open: the agent calls `open_drawing` with a path, or `new_drawing`. With `--attach` the drawing
is the one the user has active (or the one `open_drawing` or `new_drawing` opened in the app); `--pid <n>` picks one of
several running apps, otherwise the one activated last is used. The app is looked for when the first tool is called, so the
server may be started before the app is.

## Safety

| Flag | Effect |
|---|---|
| `--read-only` | every tool that writes is refused with a message that names the flag: `add_layer`, the `add_*` tools, `move`, `rotate`, `scale`, `erase`, `set_properties`, `undo`, `redo`, `save`, `save_as`, `plot_pdf`, `export_svg`, `export_to_file`, `purge`, `audit` with `fix`, `run_command` and every tool of the groups below that creates, modifies, or sets up layers and layouts; `run_plan` when a step of the plan writes; attached, also the three `zoom_*` tools, which move the view the user has on screen. Opening, rendering, the reading, measuring and checking tools, the check of `audit`, `dump_drawing` and the debug tools still work, and so does a plan of such tools |
| `--allow-scripts` | allows `run_command`, which types free command, AutoLISP or script text on the command line. Without the flag it is refused, whatever the text is |

- Files are written by `save` (the drawing's own file, which is in the folder it was opened from, or the path the agent gives
  it), by `save_as`, `plot_pdf`, `export_svg` and `export_to_file` (the path the agent gives; for the first three a file that
  is there is refused unless `overwrite` is true), and by `dump_drawing`, which writes only inside the debug folder.
  `render_png` makes its picture in memory (a temporary file under the system's temporary folder, deleted at once). Every
  other tool, the measuring, scoring and export tools included, reads and writes nothing.
- `open_drawing` and `new_drawing` refuse to replace a drawing that has changes not saved, unless the call says
  `discard: true`.
- The server listens on no port. It speaks only on its standard input and output, and writes nothing else on standard output.
- A text or INSERT that names a text style, linetype or block the drawing has not is refused with the names it has; a layer
  the drawing has not is made with the default look, as DXF does it, so make it first with `add_layer` for a look of your own.

`--allow-scripts` hands the agent everything the command line can do, a file written anywhere the app can write included.
Give it only to an agent you would give the keyboard to.

## The tools

Units are **millimetres**, angles are **degrees**, counter-clockwise from the +x direction, a point is `[x, y]`, a handle
is the drawing's own (`2F`). Every writing call is one undo step. Every answer is small: a long list comes in pages of 100
(`page_size` up to 500).

| Tool | What it does |
|---|---|
| `open_drawing(path, discard=)` | opens a DXF or DWG file as the drawing the tools work on |
| `new_drawing(discard=)` | makes a blank drawing (AutoCAD 2013 format, layer 0 only) |
| `save(path=)` | saves to the drawing's file, or to `path` (a name ending in `.dwg` is written as a DWG, any other as a DXF) |
| `info(layout=)` | name, file, units, layouts, the box everything of the layout is drawn in, the entity count of each kind |
| `list_layers`, `list_text_styles`, `list_dimension_styles`, `list_blocks` | the names the drawing has, with their key settings |
| `query_entities(kinds=, layers=, box=, layout=, page=, page_size=)` | handle, kind, layer, key geometry and drawn box of each entity, in the order the drawing draws them |
| `bounds(handles=, layout=)` | the box each entity takes as it is drawn: the letters of a text and not its insertion point, a dimension's picture, a block however deep |
| `overlaps(kinds=, over=, of=, layout=, tolerance=)` | the words (TEXT, MTEXT, dimension text, multileader text) that overlap a line, an arc, a polyline, a hatch edge, a filled area or another word, with the box of each meeting |
| `render_png(box=, layout=, width=, height=, dpi=)` | the layout, or a part of it, as a picture the agent sees (an MCP image) |
| `measure_text(text, at, height, rotation=, text_style=, justify=)`, `measure_mtext(text, at, height, width=, rotation=, text_style=, justify=)` | the box a text would take and the frame of its words, found by the code that draws it in the drawing's own text style and fonts, **before it is added**: nothing is added, no undo step is made |
| `measure_dimension(kind, points, dim_style=, text=, angle=)`, `measure_leader(arrow, landing, text, style=, scale=)` | the same for a dimension (`linear`, `aligned`, `radial`, `diameter`, `angular`) and for the multileader MLEADER would make |
| `free_space(frame, kinds=, margin=, min_width=, min_height=, layout=, tolerance=)` | the empty rectangles of a frame, the largest first: no line or word of the `kinds` runs through them or comes within `margin`. Give the size of the thing to place as `min_width` and `min_height` to get only the places it could stand in. Paged |
| `fits(candidate, kinds=, margin=, layout=, tolerance=)` | whether one place is free, tested on the real lines, arcs, fills and word frames and not on their boxes. `candidate` is a box `[minX, minY, maxX, maxY]` or a frame `[x, y, width, height, rotation]` as the `measure_*` tools give it |
| `check_standard(standard, model_scale=, tolerance=)` | scores the drawing against a company standard: a score from 0 to 1 for each rule, the total `score` and the violations (rule, handle, place, found, allowed), a page at a time. `standard` is a JSON object of the schema `docs/schemas/opencad2d-standard-v1.schema.json` or the path of a file of one |
| `export_json(layout=)`, `export_graph(layout=, tolerance=, part=)` | what [`to_json` and `to_graph`](../python.md#batch-and-ai-pipelines) write: a summary (the members that are not lists, how many rows each list has, how many entities or edges of each kind) and a page of the entities, or of the edges or nodes of the graph (`part`) |
| `export_to_file(what, path, layout=, tolerance=)` | writes the whole JSON (`json`) or graph (`graph`) to `path`, for a program that reads the file and not the agent; refused by `--read-only` |
| `add_layer(name, color=, linetype=, lineweight=, on=, frozen=, locked=)` | a layer with the look given (a colour index, a linetype of the drawing, a lineweight in hundredths of a millimetre such as 25); refused for a name the drawing has (either case), a name AutoCAD refuses, or a look it does not take |
| `add_line`, `add_polyline`, `add_circle`, `add_arc` | geometry; `layer`, `color` (a colour index) and `linetype` for each |
| `add_text`, `add_mtext` | words, with a `text_style` of the drawing |
| `add_linear_dim`, `add_aligned_dim`, `add_radial_dim`, `add_diameter_dim`, `add_angular_dim` | dimensions made as DIMLINEAR, DIMALIGNED, DIMRADIUS, DIMDIAMETER and DIMANGULAR make them, in a `dim_style` of the drawing (the current one by default); the picture is drawn by the engine |
| `add_hatch`, `add_insert` | a hatch of a closed outline with a pattern of the built-in set, or a block of the drawing placed |
| `move`, `rotate`, `scale`, `erase`, `set_properties` | change entities by handle (refused on a locked layer); `set_properties` sets `layer`, `color`, `linetype`, `linetype_scale`, `lineweight` (hundredths of a millimetre) and `transparency` (per cent) |
| `undo`, `redo` | take back the last step, or do the step taken back again |
| `run_plan(steps, dry_run=, stop_on_error=)` | runs a list of calls of the tools above in one call, as one undo step, each step able to take a value from an earlier step's answer; see [Plans](#plans-many-steps-in-one-call) |
| `run_command(text)` | types on the command line; only with `--allow-scripts` |

A frame is `{x, y, width, height, rotation}`: the first corner of a word, its length along the baseline, its height across it,
and the angle the length runs at, **in degrees** here (the Python library gives radians). A `justify` of `measure_text` is
`left`, `center`, `right`, `middle` or a code `TL`, `TC`, `TR`, `ML`, `MC`, `MR`, `BL`, `BC`, `BR`; of `measure_mtext` a code alone.

### Create

| Tool | What it does |
|---|---|
| `add_rectangle(corner1, corner2, fillet=, chamfer=)` | one closed polyline, as RECTANG; `fillet` rounds the corners with arcs, `chamfer` cuts them |
| `add_polygon(center, radius, sides, inscribed=, rotation=)` | a regular polygon as one closed polyline, as POLYGON (inscribed: the radius is to the corners) |
| `add_ellipse(center, major_axis_end, ratio, start_angle=, end_angle=)` | an ellipse or an elliptical arc |
| `add_spline(fit_points= or control_points=, degree=, closed=)` | a spline through points, or shaped by a control polygon (a closed spline takes fit points) |
| `add_point`, `add_donut(center, inner_diameter, outer_diameter)`, `add_xline(through, angle)`, `add_ray(start, angle)`, `add_leader(points)` | the objects of POINT, DONUT, XLINE, RAY and LEADER (no annotation) |
| `add_mleader(arrow, landing, text, style=, scale=)` | a multileader note, as MLEADER; `scale` multiplies the style's text height and arrow (10 for a sheet plotted at 1:10) |
| `add_table(at, columns, rows, cells=, column_width=, row_lines=, style=)` | a table, as TABLE: a title row, a header row and `rows` data rows; `cells` are the words by row from the top left cell |
| `add_text_style(name, font=, width_factor=, height=)`, `add_dimension_style(name, text_style=, scale=, text_height=, arrow_size=, variables=)`, `load_linetype(name)` | a text style, a dimension style (from ISO-25, any other variable by its library name such as `Dimdec`) and a linetype of the built-in set; refused for a name the drawing has |
| `add_attribute_definition(tag, at, height, ...)`, `define_block(name, base_point, handles, source=)`, `insert_block_with_attributes(name, at, values=)`, `set_attribute(handle, tag, value)` | a block with attributes: define the ATTDEF and the shapes as a block (`source`: `keep`, `convert` to one insert, or `delete`), insert it with `values` by tag, change an attribute later |

### Modify

Each of these types the command on the drawing's own command line with the numbers, handles and names the tool has checked
(never text of the caller's, except the words of a note, an attribute or a layout name, which are one answer to a prompt for
words), picking objects by handle, so the result is what the command makes. They work in the current layout
(`set_current_layout`). The answer is `created` (handles made), `erased`, `changed` (given objects whose box moved) and the last
lines the command line wrote; a command that changed nothing is refused with those lines.

| Tool | What it does |
|---|---|
| `copy(handles, displacements)`, `mirror(handles, line_start, line_end, keep_source=)`, `offset(handle, side, distance=, erase_source=)` | COPY, MIRROR and OFFSET (by a distance, or through the point `side`) |
| `array_rectangular(handles, rows, columns, row_distance, column_distance)`, `array_polar(handles, center, count, angle=, rotate_items=)` | -ARRAY Rectangular and Polar |
| `explode(handles)`, `join(handles)`, `break_at(handle, point, second_point=)` | EXPLODE, JOIN and BREAK |
| `trim(pick_points, edges=)`, `extend(pick_points, boundaries=)` | TRIM and EXTEND: each pick point is on the object, with the cutting edges or boundaries given (every object where left out) |
| `fillet(first, radius, second=)`, `chamfer(first, second, distance1, distance2=)`, `stretch(window, displacement)`, `lengthen(handle, mode, value, end_point)` | FILLET (every corner of one polyline with no `second`), CHAMFER, STRETCH with a crossing window, LENGTHEN by `delta`, `total` or `percent` |
| `set_draw_order(handles, where, reference=)` | DRAWORDER: `front`, `back`, `above` or `under` a reference |
| `edit_text(handle, contents=, height=, rotation=, style=, justify=)`, `edit_dimension(handle, text=, style=, overrides=)` | change a text, an MTEXT or a dimension as the Properties palette does (`overrides` by the palette's row names such as `Text height`) |
| `match_properties(source, targets, which=)` | the `layer`, `color`, `linetype`, `linetype_scale` and `lineweight` of one object set on others |

### Layers and layouts

| Tool | What it does |
|---|---|
| `set_current_layer(name)`, `set_layer(name, on=, frozen=, locked=, color=, linetype=, lineweight=, plot=)` | the current layer, and the look of a layer as the Layer manager sets it |
| `rename_layer(name, new_name)`, `delete_layer(name, move_to=)` | rename a layer (its objects follow); delete one, refused while objects are on it unless `move_to` names the layer they go to |
| `create_layout(name, paper=, landscape=)`, `set_current_layout(name)` | a layout with a sheet of `ISO A0` to `ISO A4`, `ANSI A` to `ANSI E` or a size such as `420x297`, in millimetres, plotted at 1:1; show a tab |
| `add_viewport(layout, frame, scale, center=, freeze_layers=)`, `set_viewport_scale(handle, scale, center=)`, `lock_viewport(handle, locked=)` | a viewport on a sheet at `1:scale` (`frame` in millimetres of the sheet, `center` a point of model space), with layers frozen in it alone; its scale changed; its display locked |

A drawing from nothing to a sheet: `new_drawing`, `add_layer`, `add_text_style`, `add_dimension_style`, the shapes and the
modify tools, `define_block` and `insert_block_with_attributes` for a title block, `create_layout` with `add_viewport` at 1:10 and
1:5, then `render_png` of the layout and `save` as DWG. The test `McpPlateSessionTests` runs exactly that in a drawing file and in
a drawing app, in 47 calls; `OPENCAD2D_MCP_PICTURES=<folder>` keeps the picture of the sheet it renders.

`kinds` are DXF names, either case: `LINE`, `CIRCLE`, `ARC`, `LWPOLYLINE`, `TEXT`, `MTEXT`, `DIMENSION`, `INSERT`, `HATCH`.

A refusal comes back as a tool error with the reason in plain words and the next step where there is one, such as
`The drawing has no text style NOTE: list_text_styles shows the ones it has.` An unexpected failure is written to standard
error as well.

### Output, files and housekeeping

| Tool | What it does |
|---|---|
| `plot_pdf(path, layout=, paper=, scale=, fit=, overwrite=)` | plots a layout (the one the drawing is in now by default, or `Model`) to a PDF of one page, as PLOT to the PDF plotter does: the layout's own sheet and scale, lines at their lineweights, text as text. `paper` (`A3`, `ISO A3`, `ANSI B`), `scale` (`1:10`, `2:1`) and `fit` change the setup for this one plot and are not written into the drawing; a space that was never set up is fitted to an A3 sheet. The text of an SHX font is also written as invisible text over its strokes, so the PDF reader's search, and a program that reads the plot back, find the words. Answers the file, the sheet in millimetres, the scale and the part of the drawing on the page |
| `export_svg(path, box=, layout=, width=, height=, dpi=, overwrite=)` | the layout, or a part of it, as an SVG, drawn as `render_png` draws it (vectors, text as text). Without a `box` it draws the view `zoom_window` or `zoom_object` set |
| `save_as(path, format=, version=, overwrite=)` | writes a **copy** of the drawing as a `dxf` or `dwg` (`format` is taken from the extension where left out) of a version: `2000`, `2004`, `2007`, `2010`, `2013`, `2018` or its code (`AC1027`), and `12` for a DXF. The open drawing keeps its own file, its name and its unsaved changes; `save` with a path is the tool that moves it. Answers what the version could not hold |
| `purge(kinds)` | purges what nothing uses, as -PURGE does, repeating until nothing more goes: `blocks`, `layers`, `linetypes`, `text_styles`, `dimension_styles`, `groups`, `multileader_styles`, `table_styles`, `plot_styles`, `shapes`, or `all` of them; `zero_length` erases the lines, arcs and polylines of no length and `empty_text` the texts of no content. It types fixed -PURGE input into the command session, never free text. Answers how many of each |
| `audit(fix=)` | the errors AUDIT finds, counted by kind with its first sentences; with `fix` it also fixes what it can as one undo step and audits again (`found`, `remaining`). The check alone writes nothing |
| `zoom_extents`, `zoom_window(box)`, `zoom_object(handles)` | attached: ZOOM Extents, Window and Object on the app's window, moving what the user sees. On a file: the view `render_png` and `export_svg` draw when they are given no `box`, kept for the layout in this server (a `box` given still wins). They answer the part of the drawing now shown, `[minX, minY, maxX, maxY]` |

- A file that is there is refused unless `overwrite` is `true`; the drawing's own file is named in the refusal. The folder is made
  where it is not there. An extension other than the tool's is refused (`plot_pdf` writes `.pdf`, `export_svg` `.svg`).
- Attached, `plot_pdf`, `export_svg` and `save_as` work on a copy of the app's drawing as it stands (the request `database`, [unsaved
  changes included](remote.md#the-drawing-as-the-app-has-it)), made in the server's process; the app's drawing, its file and its
  name are never touched. `purge` and `audit` with `fix` type on the app's command line, and are refused while a command is
  running there (answer it or press Esc first).
- `purge` and `audit` with `fix` are the undo steps of the command (a `purge` of two kinds is two commands); `undo` takes each back.

## Debugging with MCP

The server also helps find what is wrong in a drawing you are working in. Start it attached and read-only, so that nothing it
does can change the drawing, and register it as in [Registering it](#registering-it):

```
OpenCAD2D.Mcp.exe --attach --read-only
```

The app must allow other programs to connect ([the setting](remote.md#the-setting)). Everything below works under `--read-only`;
only the `zoom_*` tools are refused there, because they move the view the user has on screen.

| Tool | What it tells |
|---|---|
| `app_screenshot(max_side=)` | the **picture of the app's canvas** as the window last drew it (what the user sees under the cursor, grips and command line), as an image, with its size in pixels and the part of the drawing it shows. `render_png` with that `box` and size draws the same part from the drawing, to compare what the user sees with what the drawing holds. The app shows nothing and draws nothing again for it. Attached only: a file has no canvas, and an app with no window (a test's) says so |
| `command_history(count=)` | the last lines of the drawing's command line, the oldest first: each command, each prompt with its answer, each message, whoever typed them, and whether a command is running and at what prompt. On a file it is this server's own session: what went through the command line (`run_command`, `purge`, `audit` with `fix`), not the edits the typed tools make |
| `last_errors(count=)` | what went wrong lately. `app`: the entries of the app's crash log (`%AppData%\OpenCAD2D\crash.log`, then `crash.old.log`: the exception nobody caught, where it was caught, the command running then, the head of its stack) and the requests the app could not answer since it started. `server`: the tool calls this server could not do, newest first, each with its message and, for a fault that is no plain refusal, the head of the stack |
| `dump_drawing(path=)` | saves the drawing **as it is now**, unsaved changes included, as a DXF in the debug folder `%TEMP%\OpenCAD2D\mcp-debug` (a name, or a path inside that folder; the drawing's name and the time where left out), and answers the path. It writes nowhere else and never the drawing's own file, so it is allowed with `--read-only` |
| `compare_drawings(a, b=, max_rows=)` | the objects **added**, **removed** and **changed** between two states, by handle: for a changed one the names of what differs (`layer`, `end`, `box`, `text` ...), the box before and after and how far it moved. `a` and `b` are `current` (the drawing as it is now) or a DXF or DWG file, a dump of the debug folder by its name or its path. The lists are cut at `max_rows` (100, up to 500) and the counts are of everything |
| `audit` | the errors AUDIT finds in the app's drawing as it stands (the check never writes) |
| `timings(what, runs=, layout=)` | milliseconds of `regen` (REGEN of the current space), `render` (a `render_png` of a layout) or `open` (reading the drawing's file into a new document), each run and the median. The first run often includes warming up: ask for 3 |

Files and the debug folder: `dump_drawing` makes `%TEMP%\OpenCAD2D\mcp-debug` on its first call and leaves its files there;
delete them when you are done. The comparison reads the drawings as the AI export does ([the JSON](remote.md#the-drawing-as-json-and-as-a-graph)),
every entity walked down to its shapes in millimetres, so a block's pieces are told apart by their place in the block.
It is meant for two states of one drawing; two drawings drawn separately share no handles, and everything of one is added and removed.

### A session that finds a misplaced text

The user has the plate open in the app and has typed a text over the line across it. The agent, attached read-only:

```
info                         -> { "mode": "app", "readOnly": true, "name": "plate.dxf", "modified": true, "entityCount": 6 }
overlaps                     -> { "total": 3, "rows": [
                                   { "word": "107", "other": "104", "box": [80.001, 50, 94.749, 50] }, ... ] }
query_entities kinds ["TEXT"]-> 107: TEXT "OVER" at (80, 48), 5 high
dump_drawing path "before.dxf" -> { "path": "C:\\Users\\...\\Temp\\OpenCAD2D\\mcp-debug\\before.dxf", "bytes": 6188, "mode": "app" }
command_history count 12     -> ... "Command: TEXT", "Specify start point of text or [Justify/Style]: 80,48",
                                    "Specify height <2.5000>: 5", "Enter text: OVER", "Command: LINE" ...
app_screenshot               -> an image of the canvas: "PNG W by H pixels ... It shows [minX, minY, maxX, maxY] of the drawing"
render_png box [minX, minY, maxX, maxY] width W height H   -> the same part drawn from the drawing
```

The picture the user sees and the picture of the drawing are the same part at the same size, so a difference between them (a
layer frozen in one view only, an object that is not drawn) is the fault. The user moves the text; the agent asks `compare_drawings
a "before.dxf"` and reads `changed: [{ "handle": "107", "properties": ["box", ...], "movedBy": [0, 12] }]`, or
`overlaps` again, which is `0`.

## A session

An agent asked to "draw a plate 200 by 100 with a hole, dimension it and check that nothing overlaps", started with no arguments (the answers are shortened):

```
new_drawing                          -> { "mode": "file", "name": "Drawing", "modified": false }
info                                 -> millimetres, layouts Model and Layout1, current dimension style ISO-25
add_polyline points [[0,0],[200,0],[200,100],[0,100]] closed true layer "Plate"   -> { "handle": "101", "kind": "LWPOLYLINE" }
add_circle center [100,50] radius 20 layer "Holes"                                -> { "handle": "103", "kind": "CIRCLE" }
add_text text "PLATE 10 THK" at [10,105] height 5                                 -> { "handle": "104", "kind": "TEXT" }
add_linear_dim first [0,0] second [200,0] location [100,-20]                     -> { "handle": "108", "kind": "DIMENSION" }
add_radial_dim center [100,50] radius 20 location [130,70]                        -> { "handle": "116", "kind": "DIMENSION" }
overlaps                             -> { "total": 0, "rows": [] }
render_png width 800                 -> an image, PNG 800 by 525 pixels
save path "C:\plans\plate.dxf"          -> { "name": "plate.dxf", "modified": false }
```

## Placing annotation: measure, find room, score, refine

An agent that lays words out has the loop the [Python guide](../python.md#batch-and-ai-pipelines) describes, with the same
engine behind each call: measure a word before adding it, ask where there is room, put it there, check the result and score it,
and look at the picture. The tools are the ones in the table; `overlaps` finds the words that sit on something, `check_standard`
gives the number to push up, and `render_png` is the look.

A model asked to "put the two tags TAG-1 and TAG-2 on the plate where nothing touches them, and score the sheet against our
standard", over the plate of the session above (a line across it at y 50 and a circle of radius 20 at its middle), with
`--file plate.dxf` (the numbers are from a run; the answers are shortened):

```
add_text text "TAG-1" at [80,48] height 3.5           -> { "handle": "107", "kind": "TEXT" }
add_text text "TAG-2" at [20,48.5] height 3.5          -> { "handle": "108", "kind": "TEXT" }
overlaps of ["107","108"]                              -> { "total": 3 }              the tags sit on the line, TAG-1 on the circle too
check_standard standard {...}                          -> { "score": 0.778, "violations": [ gap-word-line 107, gap-word-line 108 ... ] }
measure_text text "TAG-1" at [80,48] height 3.5        -> { "box": [...], "frames": [ { "width": 12.25, "height": 4.6, "rotation": 0 } ] }
free_space frame [0,0,200,100] kinds ["LINE","CIRCLE","LWPOLYLINE","TEXT"] margin 2 min_width 12.25 min_height 4.6
                                                       -> { "total": 90, "boxes": [ [2,71.95,198,98], ... ] }   the largest first
bounds handles ["107"]                                 -> { "rows": [ { "handle": "107", "box": [ ... ] } ] }     where the tag stands now
move handles ["107"] delta [13.9, 35.8]                -> centre of the largest place
fits candidate <the box bounds gave after the move> kinds ["LINE","CIRCLE","LWPOLYLINE"] margin 1   -> { "fits": true }
... the same for TAG-2 ...
overlaps of ["107","108"]                              -> { "total": 0 }
check_standard standard {...}                          -> { "score": 1.0, "violations": [] }
render_png width 400                                   -> an image
```

Measuring a word before it is added is exact: the box `measure_text` gives equals, to a thousandth of a millimetre, what `bounds`
gives for the text once added in the same server. Measure and score through the one connection (a file, or `--attach`), and do
not mix the numbers of a file with those of an app.

## Plans: many steps in one call

An agent that draws a plate calls a tool, reads the answer, calls the next one: the loop above takes 19 calls. `run_plan` takes
the calls as a list and runs them in order in one call, and gives every step's answer back. It is generic: a step may name any
tool the server has, `add_*`, `move`, `measure_*`, `free_space`, `check_standard`, `render_png`, and a tool added later works in a
plan with no change to `run_plan`.

```
run_plan steps [
  { "tool": "add_line",   "id": "l", "arguments": { "start": [0,0], "end": [50,0] } },
  { "tool": "add_circle", "id": "c", "arguments": { "center": [25,25], "radius": 5 } },
  { "tool": "move",       "arguments": { "handles": ["$l.handle", "$c.handle"], "delta": [10,10] } } ]

-> { "ok": true, "changed": true, "message": "Ran 3 steps; what they wrote is one undo step.",
     "totals": { "steps": 3, "ok": 3, "failed": 0, "skipped": 0, "writers": 3 },
     "steps": [ { "id": "l",  "tool": "add_line",   "ok": true, "result": { "handle": "107", "kind": "LINE" } },
                { "id": "c",  "tool": "add_circle", "ok": true, "result": { "handle": "108", "kind": "CIRCLE" } },
                { "id": "s3", "tool": "move",       "ok": true, "result": { "changed": 2, "handles": ["107", "108"] } } ] }
```

The steps are `{tool, arguments, id}`. `arguments` are the ones the tool takes alone; `id` is optional and is `s1`, `s2` ... (the
step's number) where left out.

### Taking a value from an earlier step

A string argument that is **exactly** `$id` or `$id.path` is replaced by that value of the answer of the earlier step with that id:

| Written | Gives |
|---|---|
| `"$l.handle"` | the member `handle` of the answer of step `l`: `"107"` |
| `"$free.boxes[0]"` or `"$free.boxes.0"` | the first of the list `boxes`: `[12, 30, 88, 41]` |
| `"$m.frames[0].width"` | a number, as a number: the value is put in with its own type, a text, a number, a list or an object |
| `"$m"` | the whole answer of step `m` |
| `"$$5 each"` | the text `$5 each`: a string that starts with `$$` is the text without the first `$` |

A tool that answers a list (`list_layers`, `list_blocks`) gives the list itself, so the first layer's name is `"$ll[0].name"`. A value put
in is checked against the tool's schema again before the step runs, and it is only ever a value of its argument: a layer named
`(setq x 1)` or `'ZOOM` that `add_text` takes from `list_layers` is the text drawn, nothing is evaluated.

A name is found as written, else ignoring case; `[n]` and `.n` count from 0. A string such as `$5` or `$ 5` that does not start like a
reference is a text. Only a step before the one that uses it can be named. A reference that is written wrongly (`$a..handle`), names
no step, or names a later step is found **before anything runs**; a path the answer does not have (`$a.handel`) fails its step when it is
read, with what the answer has: `has no member handel (it has handle, kind)`.

### One undo step, and the way back

- **Checked first.** Every step is checked before any runs: the tool exists, its arguments against its schema (type, required
  names, names it does not take, allowed values, limits), the references. A plan with any wrong step runs nothing and answers with
  every problem at once.
- **One undo step.** All the steps that write are one undo step: one `undo` takes them all back. On a file the steps are one group of
  edits of the engine. Attached, the app has no group in its Remote API, so the server types the two lines of AutoCAD's own UNDO group
  on the app's command line, `UNDO BEgin` before the first step and `UNDO End` after the last (fixed text, nothing the agent writes;
  they show in the app's command history), and U then takes the whole group back. The server first asks the app for a snapshot of the
  drawing (see [Keeping a drawing and putting it back](remote.md#keeping-a-drawing-and-putting-it-back)), which is refused while a command is running in the app: the plan then says the app is busy
  and runs nothing. Before each of the two lines the server reads `CMDACTIVE` and types nothing while it is not 0, so a line is never
  an answer to a command the user is in the middle of (text typed at a prompt that is not a text prompt would be read as AutoLISP or a
  transparent command).
- **A failure stops the plan and takes it back** (`stop_on_error`, true by default). The step that failed has the reason in `error`,
  the steps after it are `skipped`, and the drawing is put back as it was before the plan (`rolledBack: true`): on a file the group is
  cancelled, attached the group is ended and the snapshot is reset, which forgets the steps the plan made, so what the user did before
  stays undoable one step at a time. The answers of the steps that ran before the failure are still reported; their handles are of
  objects that no longer exist. With `stop_on_error` false the plan goes on, the steps that worked are kept as one undo step and each
  failure is in its step.
- **A tool that takes its own group back** (a failed `set_properties` on a locked layer does) takes the whole plan back with it, which the
  answer says.
- **Dry run.** `dry_run: true` runs the plan, then puts everything back: the answers are what a real run gives, handles included (the
  drawing's handle counter is put back too, so the real run that follows gives the same handles). A plan with steps that only read is
  simply run. A dry run on an app draws the plan in the user's window for a moment (about 30 ms for a drawing of 10 000 entities) and
  puts it back; a drawing file in memory is not seen by anyone.
- **Refused by name in a plan:** `run_plan` (no plan holds a plan), `open_drawing`, `new_drawing`, `undo`, `redo`, `save` and `save_as`
  (they replace the drawing, step through its history, or write its file, which a plan that stops could not take back). `run_command` is
  refused unless the server has `--allow-scripts`, as it is alone, and is never allowed in a plan on an app (a command it starts may
  wait for the user's answer, and the plan could not end its group while it does). On a file its text is typed **as written**: no
  reference is read in it, so no earlier answer can reach the command line. A plan with **any** step that writes is refused by `--read-only` before
  anything runs, naming those steps; a plan of reading tools runs on a read-only server.
- At most 200 steps. A picture a step makes (`render_png`) is reported by its type and size, not its data: call `render_png` alone to
  see it.

### The placement loop in 4 calls

The loop above, as plans: what needs the agent's own arithmetic (where the middle of a free place is) stays between calls, the rest goes
in one.

| Call | Steps |
|---|---|
| 1 | `add_text` TAG-1, `add_text` TAG-2, `overlaps` of both (`"of": ["$t1.handle", "$t2.handle"]`), `check_standard` |
| 2 | `measure_text` TAG-1, `free_space` with `"min_width": "$m1.frames[0].width"` and the height likewise, `bounds` of TAG-1, `measure_text` TAG-2 |
| 3 | `move` TAG-1, `bounds`, `fits` with `"candidate": "$now1.rows[0].box"`, `free_space` for TAG-2, `bounds` of TAG-2 |
| 4 | `move` TAG-2, `bounds`, `fits`, `overlaps`, `check_standard`, `render_png` |

Four calls where the single-call loop makes 19, with the same result (0 overlaps, the score of the standard higher), over a file and over
an app. Each call is one of the model's turns, which is where the time goes: the 3 writing steps of the first example take 5 ms in a
plan and 5.6 ms as three calls on a file, 9.5 ms and 8.7 ms attached, so a plan does not cost time of its own; it saves the turns.

## Limits

- One drawing at a time: the tools work on the one `open_drawing`, `new_drawing`, `--file` or the app's active drawing names.
- `query_entities` gives a handful of numbers a kind, not every property: read one entity in full with the Python or C#
  API ([Driving the app from another program](remote.md), [Python](../python.md)).
- Attached, the modify tools send their command, with the objects picked by handle, to the app and end it with Esc where it is still
  asking. A tool of several steps (a table and its cells, a viewport and its scale) is one undo step, made with `UNDO Begin` and
  `UNDO End`; a failure between the steps leaves the steps made (undo takes them back), where in a drawing file the whole call is
  taken back.
- The words of a note, an attribute value or a layout name are typed as one line at the prompt for words, which the app takes whole;
  the name of a block, a layer, a style or a tag has no space. `add_mleader` and `add_table` draw in the drawing's current styles,
  whose sizes are those of a drawing plotted at 1:1: give a note a `scale`.
- Not covered by the tools: the plot style of an object (`set_properties` has no `plot_style`), a closed spline of control points, an
  array along a path, a revision cloud and a wipeout (use `run_command` with `--allow-scripts`).
- Attached, `query_entities` cannot read a viewport or another object the app cannot copy into a small drawing; `add_viewport`
  gives the viewport's handle all the same.
- Attached, a `run_command` that is still asking something after 10 seconds is left waiting in the app; the next
  `run_command` answers it, or Esc in the app ends it.
- `export_json` and `export_graph` page the document: the whole of it is megabytes for a drawing of thousands of entities (3 MB
  for 10 000), which no model reads. `export_to_file` writes it all to a path for a program that does.
- The labelled renders of Python (`render_masks`, `to_coco`, PW7.4) are not tools: a mask is an array of a million numbers and a
  COCO file is for a training pipeline, which uses the Python library.
- Attached, `render_png` draws a copy of the app's drawing taken at the call: the header, tables, blocks, layouts and objects as the
  app has them (a 10 000-entity drawing is 1 MB of text and the request takes about 20 ms); what the app shows beyond the drawing
  (its grid, grips, selection) is not in the picture, as on a file.
