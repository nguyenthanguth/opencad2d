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
| `--read-only` | every tool that writes is refused with a message that names the flag: `add_layer`, the `add_*` tools, `move`, `rotate`, `scale`, `erase`, `set_properties`, `set_variable`, `reset`, `undo`, `save` and `run_command`. Opening, reading, selecting, measuring, rendering and taking or releasing a snapshot still work |
| `--allow-scripts` | allows `run_command`, which types free command, AutoLISP or script text on the command line. Without the flag it is refused, whatever the text is |

- Only `save` writes a file: to the drawing's own file, which is in the folder it was opened from, or to the path the agent
  gives it. `render_png` makes its picture in memory (a temporary file under the system's temporary folder, deleted at once).
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
| `get_entity(handle)` | one entity in full: layer, colour, linetype, lineweight, its drawn box, the values of its kind (text and height, polyline vertices with bulges, a dimension's measurement and text, an INSERT's block and attributes, a hatch's pattern, an MTEXT's width and columns) and the Properties palette rows |
| `count_entities(group_by=, kinds=, layers=, colors=, linetypes=, box=, layout=)` | counts by `kind`, `layer`, `block` (the block references, by block name) or `color` (the colour each is drawn in); filters narrow what is counted |
| `aggregate(measure, group_by=, ...)` | the sum, smallest, largest and average of the `length` (mm) or `area` (mm²) of the entities in each group, and how many had no such measure; the filters of `count_entities` |
| `list_layouts`, `list_linetypes`, `list_multileader_styles`, `list_table_styles` | the layouts in tab order with the sheet's paper size and units and each viewport's place, view centre and scale (`1:10`); the linetypes with their dashes; the multileader and table styles with their key settings. Attached, these read a copy of the app's drawing |
| `get_variables(names=)`, `set_variable(name, value)` | system variables as `getvar` and `setvar` give and take them (an angle in degrees). `get_variables` reads any variable the engine knows, or the 38 documented ones where no names are given; `set_variable` sets only the documented ones the drawing keeps (units, scales, the current layer and styles, point look: the list is in `get_variables`), as one undo step |
| `get_texts(contains=, styles=, layers=, box=, layout=)`, `get_dimensions(types=, styles=, layers=, box=, layout=)` | the words of a layout (TEXT, MTEXT, multileader text and the values of block attributes) with place, height, rotation, style and drawn box; the dimensions with type, style, measured value, the words drawn, the text typed over it and the defining points |
| `get_block(name)`, `get_attributes(handle)` | a block's definition (base point, attribute definitions, its entities); the attributes of a block reference with the tags the block defines that it has no value for |
| `measure_distance(a, b)`, `measure_area(points= or handle=)`, `measure_length(handle)` | DIST (distance, angle in the XY plane, changes in x and y), AREA (area and perimeter of points or of a closed shape) and the length of a curve |
| `list_properties(handles)`, `id_point(point, aperture=)` | the lines AutoCAD's LIST writes; ID, with the entities that pass within the aperture of the point, the one on top first |
| `select(mode, points=, kinds=, layers=, colors=, linetypes=, layout=)`, `select_similar(handle, match=)` | handles by `window`, `crossing`, `fence`, `polygon`, `crossing_polygon`, `all` or `previous` (what is selected in the drawing now: the user's selection in the app), not held to the app's view and never changing the user's selection; the entities like one by kind, layer, colour, linetype, text style or block |
| `snapshot`, `reset(snapshot)`, `release_snapshot(snapshot)` | keeps the drawing as it stands under an id (`s1`), puts it back to that (the steps since are forgotten, not made one more undo step; a writer, refused when read-only) and lets go of it; the drawing's snapshots go when it is closed or replaced |
| `server_status`, `describe_kinds` | how the server stands (mode, flags in force, version, the drawing, the snapshots held; works with no drawing open) and the kinds of entity it reads and writes, with the fields and the tool that makes each |
| `render_png(box=, layout=, width=, height=, dpi=)` | the layout, or a part of it, as a picture the agent sees (an MCP image) |
| `add_layer(name, color=, linetype=, lineweight=, on=, frozen=, locked=)` | a layer with the look given (a colour index, a linetype of the drawing, a lineweight in hundredths of a millimetre such as 25); refused for a name the drawing has (either case), a name AutoCAD refuses, or a look it does not take |
| `add_line`, `add_polyline`, `add_circle`, `add_arc` | geometry; `layer`, `color` (a colour index) and `linetype` for each |
| `add_text`, `add_mtext` | words, with a `text_style` of the drawing |
| `add_linear_dim`, `add_aligned_dim`, `add_radial_dim`, `add_diameter_dim`, `add_angular_dim` | dimensions made as DIMLINEAR, DIMALIGNED, DIMRADIUS, DIMDIAMETER and DIMANGULAR make them, in a `dim_style` of the drawing (the current one by default); the picture is drawn by the engine |
| `add_hatch`, `add_insert` | a hatch of a closed outline with a pattern of the built-in set, or a block of the drawing placed |
| `move`, `rotate`, `scale`, `erase`, `set_properties` | change entities by handle (refused on a locked layer); `set_properties` sets `layer`, `color`, `linetype` |
| `undo` | takes back the last step |
| `run_command(text)` | types on the command line; only with `--allow-scripts` |

`kinds` are DXF names, either case: `LINE`, `CIRCLE`, `ARC`, `LWPOLYLINE`, `TEXT`, `MTEXT`, `DIMENSION`, `INSERT`, `HATCH`.

A refusal comes back as a tool error with the reason in plain words and the next step where there is one, such as
`The drawing has no text style NOTE: list_text_styles shows the ones it has.` An unexpected failure is written to standard
error as well.

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

## Trying a layout and rolling it back

```
snapshot                                  -> { "snapshot": "s1", "entities": 7 }
add_text text "NOTE A" at [80,48] height 5   -> { "handle": "114" }
add_text text "NOTE B" at [82,49] height 5   -> { "handle": "115" }
overlaps                                  -> { "total": 1, ... }      the two notes lie over each other
reset snapshot "s1"                       -> { "done": "reset", "entities": 7 }
query_entities kinds ["TEXT"]             -> only the text that was there
add_text text "NOTE A" at [80,48] height 5   -> { "handle": "114" }   the handle comes back too
release_snapshot snapshot "s1"
```

## Limits

- One drawing at a time: the tools work on the one `open_drawing`, `new_drawing`, `--file` or the app's active drawing names.
- `query_entities` gives a handful of numbers a kind, not every property: read one entity in full with `get_entity`, or with the Python or C#
  API ([Driving the app from another program](remote.md), [Python](../python.md)).
- Attached, a `run_command` that is still asking something after 10 seconds is left waiting in the app; the next
  `run_command` answers it, or Esc in the app ends it.
- Attached, `render_png` draws a copy of the app's drawing taken at the call: the header, tables, blocks, layouts and objects as the
  app has them (a 10 000-entity drawing is 1 MB of text and the request takes about 20 ms); what the app shows beyond the drawing
  (its grid, grips, selection) is not in the picture, as on a file.
- Attached, `list_layouts`, `list_linetypes`, `list_multileader_styles` and `list_table_styles` read a copy of the app's drawing, as
  `render_png` does (changes not saved included); `get_entity`, `select`, `id_point` and `list_properties` ask the app for the
  Properties palette, the entities in an area and LIST (the requests `properties`, `selectGeometry` and `list` of
  [Remote](remote.md#the-palette-the-entities-in-an-area-and-list)), and leave the app's command line and selection as they were.
- `select` is not held to what the app's window shows: a `window` takes every entity wholly inside the box, on screen or not.
- The attributes of a block reference and the parts of a block's definition have no handle of their own in the answers (the
  attributes are listed under the block reference's handle): an app answers from copies, whose numbers for them are not the app's.
- A `snapshot` is tied to the drawing it was taken of. In a server on a file it is let go when another drawing is opened or made;
  attached, the app keeps it until `release_snapshot` or until the drawing is closed. A `reset` is not an undo step: `undo` after it
  takes back what was done before the snapshot, not the reset.
