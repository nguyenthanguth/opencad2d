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
| `--read-only` | every tool that writes is refused with a message that names the flag: `add_layer`, the `add_*` tools, `move`, `rotate`, `scale`, `erase`, `set_properties`, `undo`, `redo`, `save`, `run_command` and every tool of the groups below that creates, modifies, or sets up layers and layouts. Opening and rendering still work |
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
| `render_png(box=, layout=, width=, height=, dpi=)` | the layout, or a part of it, as a picture the agent sees (an MCP image) |
| `add_layer(name, color=, linetype=, lineweight=, on=, frozen=, locked=)` | a layer with the look given (a colour index, a linetype of the drawing, a lineweight in hundredths of a millimetre such as 25); refused for a name the drawing has (either case), a name AutoCAD refuses, or a look it does not take |
| `add_line`, `add_polyline`, `add_circle`, `add_arc` | geometry; `layer`, `color` (a colour index) and `linetype` for each |
| `add_text`, `add_mtext` | words, with a `text_style` of the drawing |
| `add_linear_dim`, `add_aligned_dim`, `add_radial_dim`, `add_diameter_dim`, `add_angular_dim` | dimensions made as DIMLINEAR, DIMALIGNED, DIMRADIUS, DIMDIAMETER and DIMANGULAR make them, in a `dim_style` of the drawing (the current one by default); the picture is drawn by the engine |
| `add_hatch`, `add_insert` | a hatch of a closed outline with a pattern of the built-in set, or a block of the drawing placed |
| `move`, `rotate`, `scale`, `erase`, `set_properties` | change entities by handle (refused on a locked layer); `set_properties` sets `layer`, `color`, `linetype`, `linetype_scale`, `lineweight` (hundredths of a millimetre) and `transparency` (per cent) |
| `undo`, `redo` | take back the last step, or do the step taken back again |
| `run_command(text)` | types on the command line; only with `--allow-scripts` |

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
- Attached, `render_png` draws a copy of the app's drawing taken at the call: the header, tables, blocks, layouts and objects as the
  app has them (a 10 000-entity drawing is 1 MB of text and the request takes about 20 ms); what the app shows beyond the drawing
  (its grid, grips, selection) is not in the picture, as on a file.
