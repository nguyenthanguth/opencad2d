# Changelog

Older versions, from 2.0.0 back, are in [CHANGELOG-ARCHIVE.md](CHANGELOG-ARCHIVE.md).

## Unreleased

- **NEW.** The DRAWINGS list can show the open drawings only: a 20 by 20 button left of Open in its header (two stacked sheets)
  turns it. Off, the list shows every DWG and DXF file of the folders as before; on, the button is pressed and the list holds
  just the drawings open in the app, each once under its folder (folders in the order their first drawing was opened, drawings
  in the order they were opened), a drawing never saved under "Not saved", every row with its close cross (the drawing in front is the selected row).
  The chips count these rows only; search, double click, the right-click menu and plugin items work as in the
  full list, and Refresh rereads the folders in the full view only. Opening or closing a drawing updates the list at once. The
  choice is kept in the global settings (`GlobalSettings.DrawingsShowOpenOnly`, off by default) and set from code with
  `MainViewModel.DrawingsShowOpenOnly`.
- **CHANGED.** The New, Modify and Override dimension style dialog lays each tab out in group boxes as AutoCAD 2021 does, not as one
  column of rows: a 1 px bordered box per group with its title in the top line (Dimension lines, Extension lines, Arrowheads, Centre
  marks, Text appearance, Text placement, Linear dimensions, Angular dimensions, Alternate units, Tolerance format and the rest),
  the preview at the top right (300 wide) with the right column's boxes under it (Arc length symbol, Radius jog dimension, Text
  alignment, Scale for dimension features, Fine tuning, Angular dimensions, Placement), the left column's boxes beside it, and the
  Extension lines of the Lines tab across the width in two columns. The two Suppress pairs sit on one row each ("Suppress: [ ] Dim
  line 1 [ ] Dim line 2", "Suppress: [ ] Ext line 1 [ ] Ext line 2"), the variables of both beside the caption. The window is 860 by
  660 (it was 960 by 660), with the Description across its width; a tab that does not fit scrolls. The lists of the right column and
  the two long lists of the Fit tab sit under their labels at the width of their box. Tab goes through the left column, the right
  column and the wide boxes, then the buttons: the preview's Fit button, in every style dialog that has a preview, is no longer a tab stop (it still works with
  the mouse and a double click). Fields, bindings and variables are unchanged, but four labels changed: DIMZIN, DIMALTZ and DIMTZIN now read
  "Leading and trailing" (under boxes titled Zero suppression, Zero suppression and Tolerance zero suppression; the angular DIMAZIN
  keeps "Zero suppression"), and DIMLFAC reads "Scale factor" (under Measurement scale, it was "Measurement scale factor"). Group
  titles changed or were added: "Radius jog dimension" (was "Radius dimension jog"), and the new boxes Display (the Display alternate
  units box), Measurement scale, Zero suppression (linear and alternate) and Tolerance zero suppression. Group boxes have one shared style (`StyleGroupBox`).
- **FIXED.** Esc did nothing after a routine's dialog (`start_dialog`, such as a LISP edit-text command asking for the next
  object): the main window is switched off while a dialog is shown, which took the keyboard from the element that held it, and
  nothing took it back, so keys reached no part of the workspace until a click on the canvas gave it focus. The element that
  held the keyboard when the dialog opened holds it again when it closes, and where that cannot be (it was in a popup or another
  window, or nothing held it) the canvas takes it, so that typing reaches the command line.
- **FIXED.** An MTEXT of Korean (or Chinese, Japanese) words could be hovered, picked and crossed only over the left part of
  its lines. A run in a TrueType font whose file the core does not read (Gulim and Batang are `.ttc` collections) was given a pick
  box of 0.6 of its height a letter, where the canvas draws a Hangul syllable a full em wide, an em being 1 / 0.72 of the height
  (about 1.39, a box 2.3 times too narrow for a line of syllables): a point or a crossing window over the right of a line found
  nothing. The box now takes the width the layout set the run by. A TEXT or ATTRIB of such letters (Hangul, Han including the
  letters past the first plane, kana, full-width forms) in a TrueType font had the same guess and is measured the same way, by the
  file's advances brought to the canvas's (as an MTEXT run is) or by the canvas's measure where the file is not read; words of other
  letters keep the guess. The width reaches beyond picking: the box of a QTEXT, the grey ground of a field, the frame TEXTALIGN
  sets texts by, the width MIRROR turns a centred TEXT about with MIRRTEXT 0, the extents that ZOOM Extents fits and the list reports, and the
  `box` of the Remote and JSON export are those of the new width for such a TEXT, and for any MTEXT run in a face whose file is
  not read.
- **CHANGED.** A double click on a style in the Styles list of the Dimension Style Manager, the Text Style, Multileader Style, Table Style
  and Multiline Style dialogs makes that style current, as the dialog's Set Current button does (a click on a row only, not on the empty area
  under the rows or the scroll bar; one undo step and the same words in the dialog, except in the Multiline Style dialog, where Set Current
  only marks the style and OK applies it), where the Dimension Style Manager, the Multileader Style Manager and the Multiline Style
  dialog opened Modify and the Text and Table Style dialogs did nothing; Modify… stays the button for that. The foot of the two managers says so: "14 styles · double-click a
  style to make it current".
- **FIXED.** A light line along the top edge of the Modify Dimension Style window, and of the Modify Multileader Style, Modify Table
  Style, Sheet Set Manager and Transmittal Report windows: a window with no frame of the system that can be resized is given a thin
  frame by Windows 10 and 11 unless it says it has none. Each now carries a `WindowChrome` with no caption and no glass frame, as the Text
  Window and the palettes already did.
- **CHANGED.** The canvas draws a text under 5 pixels high as a solid bar of its colour the size of its box, never under a pixel each way, as
  AutoCAD 2021 draws the lines of a sheet zoomed out to its extents; before, a text under 3 pixels high was left out (so the small words of
  a sheet vanished when it was zoomed out) and one of 3 to 5 pixels was a speck of letters. Text of 5 pixels or more is drawn in its
  letters as before; a plot and a PDF are unchanged, and the pictures of `render_png`/`render_svg`, `render_masks` and the Remote screenshot follow the canvas (their guides now say a bar where they said "left out"). A zoom-extents redraw of 50 000 small
  texts takes about 27 ms (9 ms with them left out) at under 3 pixels high and 28 ms (was 150 ms) at 3 to 5 pixels. A text of several lines is a bar for each line, each as wide as its own line.
- **NEW.** Global settings `smallTextAsBars` and its row "Draw text too small to read (as AutoCAD; off hides it when zoomed out)" under Appearance, Display (`GlobalSettings.SmallTextAsBars`, `CanvasOptions.SmallTextAsBars`, on by default) and the
  variable `SMALLTEXTBARS` (OpenCAD2D's own, `ON`/`OFF`): off, the canvas goes back to leaving a text under 3 pixels high out and drawing
  one of 3 to 5 pixels in its letters.
- **NEW.** Remote `drawing.GetLayouts()`, `drawing.GetLinetypes()` and `drawing.GetBlocks()` (the requests `layoutDetails`,
  `linetypeDetails` and `blockSummaries`): the layouts in tab order with paper name, size, units (`millimetres`, `inches` or `pixels`),
  plot rotation, entity count, current/model flags and each paper-space viewport's frame, view centre, height, scale and frozen layers;
  the linetypes with description, pattern length and dashes; and the insertable blocks with their entity counts (layouts, anonymous
  blocks, external references and `xref|block` records left out), all without copying the drawing or every block's objects.
- **CHANGED.** MCP attached to an app: `list_layouts`, `list_linetypes` and `list_blocks` ask the app for those summaries alone instead of
  copying the whole drawing (`GetDatabase`) or every block's objects (`Blocks`); `list_blocks` leaves external references and
  xref-dependent blocks out in both modes, so file and app modes give the same rows.
- **NEW.** Python `opencad2d.synth.generate(..., layout=, masks=, classes=)` and `Sample.masks`: COCO annotations (`to_coco`) and class
  and instance masks (`render_masks(instances=True)`) for the A3 layout `synth.SHEET` (`"Plate A3"`, with its border, filled `TITLE`
  block and viewport) as well as model space. `generate` now checks `width` (a whole number above nought) and `classes` even where it
  writes no picture.
- **NEW.** Remote `drawing.GetMultileaderStyles()` and `drawing.GetTableStyles()` (the request `annotationStyles`): the settings of each
  multileader style (text style and height, arrowhead and size, landing length and gap, scale, annotative, current) and table style (text
  style, title, header and data heights, margins, current), sorted by name, without a copy of the drawing. A multileader or a table read
  with `GetObject` now carries the name of its style.
- **CHANGED.** MCP attached to an app: `list_multileader_styles`, `list_table_styles` and the scale of `measure_leader(style=)` ask the app
  for the styles alone, and a multileader or table is read with its style name from the app's own answer, so none of them copies the whole
  drawing (a viewport still does). The rows are the same in a file and in an app; `list_multileader_styles` gives `annotative` too.
- **FIXED.** MCP `add_hatch(handles=)` from objects: an ellipse or elliptic arc at a height (z 5) was refused by the library ("not in the plane of
  the HATCH") where lines, arcs, polylines and circles at that height were flattened: all are flattened now, and a line whose ends differ in
  height is refused (it was flattened silently). A piece shorter than the tolerance (a line of no length, an arc of 359.9997 degrees of a
  small radius) no longer closes on itself and refuses the whole hatch: it is left out, and a hatch of nothing else is refused; an outline
  with an elliptic arc is refused for no area as one of lines and arcs is.
- **FIXED.** MCP attached to an app: a multileader or table was read from a copy of the whole drawing for every handle (`find_at` of 20 handles,
  `select` previous); they now come with their style name from the app itself (see the `annotationStyles` entry), and every way of
  reading them gives the same name.
- **FIXED.** Python `run_many(timeout=)`: a worker that ended before it was ready (a replacement after a timeout too) raised `RuntimeError` and
  lost the outcomes collected, a worker ended while idle gave the next item a `BrokenPipeError`, and a worker that hung while it started hung
  the run. A run with a timeout never raises once its workers are started: a worker lost outside an item is replaced (it has `START_TIMEOUT`,
  120 s, to say it is ready), and after 3 the items not yet sent get a `WorkerStartError` each. `timeout=math.inf` raised `OverflowError`; a
  timeout must be finite. Ctrl+C in a timed run stops the workers.
- **NEW.** MCP `add_hatch(handles=)` takes the objects of a boundary as AutoCAD does, not closed polylines and circles only: whole
  ellipses, and lines, arcs, open polylines and elliptic arcs that join end to end (within 0.001 mm, any order, any direction) into a
  closed loop, which are one outline together at the place of their first handle (four lines 100 by 60 hatch an area of 6000; two lines
  and two half circles of radius 10 an area of 2000 + 100 pi). Pieces that do not close are refused, naming the handles and the point
  where the chain stops. A part of an ellipse is kept as the exact spline of it (the library has no elliptic arc edge).
- **FIXED.** MCP `offset` in an app left the Erase option OFFSET remembers at the value the call typed, so the user's next OFFSET
  offered it: the setting the user had is put back (read from the `Current settings: Erase source=...` line OFFSET writes).
- **NEW.** Python `run_many(..., timeout=)`: the seconds one item may take. An item over it is an `Outcome` whose `error` starts
  `TimeoutError`, its worker process is stopped and replaced, and the others go on, in the order of the items; the time counts from
  the item's start in a worker that has loaded .NET. A run with a timeout uses worker processes even for `workers=1`.
- **FIXED.** MCP `get_entity` of a MULTILEADER or a table attached to an app gave a null `style`: the app's copy of the object has no
  style table to find the name in; the entity is read from a copy of the drawing's database (only for these two kinds). `describe_kinds`
  named `linetype` as an argument of `add_mleader`, `add_table` and `add_attribute_definition`, and `color` and `linetype` of
  `add_viewport`, which those tools do not have; a new test holds every argument the table names to the tools that write the kind.
- **NEW.** MCP tools that make and change multileader styles and table styles: `add_multileader_style`, `set_multileader_style`,
  `set_current_multileader_style`, `add_table_style`, `set_table_style` and `set_current_table_style` (6 more tools, 137 in all), in a
  drawing file and attached to an app, one undo step each and refused by `--read-only`. A style is a copy of another (`from`) with the
  settings given: for a multileader style `text_style`, `text_height`, `arrow`, `arrow_size`, `landing_gap`, `landing_length`, `landing`
  and a `scale` or `annotative`; for a table style `text_style`, `title_height`, `header_height`, `data_height`, `horizontal_margin`,
  `vertical_margin` and `description`; every length in millimetres as drawn, so a note on a sheet plotted at 1:10 is `scale=10` with
  `text_height=2.5` (words 25 high in model space). A refusal part way (a text style the drawing lacks, a height of 0, `scale` with
  `annotative`) leaves no style behind. They are four new edits of `CadMake` (`add_mleader_style`, `set_mleader_style`,
  `add_table_style`, `set_table_style`), which the Remote `make` request carries, built on `CadMLeaderStyleEditor` and
  `CadTableStyleEditor`. The acceptance of the server now draws its note and a parts table in styles made for the 1:10 sheet (the
  table's words 2.5 on paper, 25 in model space, where the default table's 4.5 would print 0.45 mm).
- **FIXED.** MCP `add_mleader(style=)` in a drawing with no multileader style Standard (a new one) left the style it used current, and with it
  MLEADERSCALE (a later note without a style came out 10 times too big at a style of scale 10): the scale is kept whenever a style is
  given, and the style and then the scale are put back, a drawing with no style being given its Standard first, as MLEADER does
  (`current_style` of `Standard` makes it). `add_multileader_style` and `add_table_style` refused nothing for a `from` the drawing lacks and
  started from the default; they refuse it (`Standard` excepted). `set_multileader_style(annotative=false)` without a `scale` did
  nothing; it makes the style a fixed one again at the scale it holds. `measure_leader` of a style of scale 0 measures at 1, as
  `add_mleader` draws.
- **FIXED.** MCP `add_mleader` with a `style` failed in a drawing that had no multileader style named Standard yet (a new drawing): the call
  tried to put back the style that was current, which the drawing did not have, and a file took the note back; it puts back only a style
  the drawing has. `measure_leader` with a `style` the drawing has failed in such a
  drawing ("no multileader style Standard in ACAD_MLEADERSTYLE for the MULTILEADER to take"; `CadMeasure.Leader` now gives its scratch drawing
  a Standard), and measured at the scale left in MLEADERSCALE and not the style's, which `add_mleader` draws in (a style of scale 10
  measured 3.25 high where the note is drawn ten times that); with a `style` and no `scale` it is measured at the style's scale.
- **FIXED.** Python and Remote: an INSERT of a block whose attribute definitions use a text style of the drawing, not Standard, failed at the
  commit with "The text style SYN is not in the file's text style table". The attributes an INSERT carries are texts of their own, in the
  style (and linetype) of their definitions, and the step that gives the commit's small drawing the styles an object names (`needs` in
  `opencad2d.cad`, `CadRemoteDocument.Provide`) did not look at them. `add_insert(..., attributes=)` of a block of NOTE attributes
  worked only in the same commit as the Remote `AddRecords` of the block; a file failed at once. The MCP server was not affected: its
  inserts are made in the drawing itself. `opencad2d.synth` gives its title block's attributes its own style SYN again.
- **NEW.** Parallel workers with the same answers, and seeded synthetic drawings (PW7.8, PW7.9). `opencad2d.parallel.run_many(function, items,
  workers, seed)` runs `function(item, item_seed)` in spawned worker processes, each loading .NET once, with the seed of an item a hash of
  the run's seed and its place in the list, so the values are the same for one worker or eight and for any order of finishing; the
  `Outcome`s come back in the order of the items, an item that raises (or returns what cannot be pickled) is reported with its traceback and
  the others finish. `opencad2d.synth.generate(seed, crowding, image)` makes a steel plate detail on an A3 layout (holes, a corner notch, a
  weld as an offset, dimension chains, notes, a title block, a viewport at 1:5 or 1:10, layers of a small standard `synth.STANDARD`) with its
  ground truth: `to_json()`, the score and rules of `check`, the overlaps and, with a picture, the COCO annotations; the same seed gives the
  same JSON and picture byte for byte, `crowding` 0 to 1 adds notes across the holes and lines for a placement agent to clear (none at 0,
  for example 11 to 55 over the seeds 0 to 39 at 1). A drawing takes 41 ms without a picture and 76 ms with one on .NET 8 (21 and 58 ms on .NET
  Framework 4.8); 240 drawings with pictures and labels take 16.1 s alone and 6.7 s on 4 workers. A run uses at most 61 processes (Windows'
  limit) and Ctrl+C cancels the items not yet started.
  Guide: Parallel workers with the same answers, Synthetic drawings for training data.
- **FIXED.** MCP server and the sheets of the Python package, from the third independent review. (1) Attached, a plan was not one undo step
  when a step grouped itself: the app does not nest UNDO groups, so the UNDO End a tool such as `add_table`, `add_mleader`, `define_block`,
  `create_layout`, `add_viewport`, `fillet` or `offset` typed closed the plan's group, and one undo took back only the steps after it
  (a plan of `add_line`, `add_table`, `add_line` left 2 of 3 after one undo); a tool types no group of its own inside a plan now. (2) A
  dry run, or a plan taken back, still wrote the files of a step in the middle of it (`export_svg`, `plot_pdf`) and `export_to_file` and
  `dump_drawing` were not known as writers at all; every tool that writes a file says so (an attribute, not a list of names), such a step is
  allowed only at the end of a plan, where it is written after the edits are kept and not in a dry run, and is refused anywhere else, in a
  dry run too. A plan whose file step at the end failed said "it had changed nothing" while its edits were kept; it says they were kept as
  one undo step. (3) `add_hatch` of the handle of a polyline that faces down (normal -Z) hatched the mirror place, from the polyline's own
  coordinates; it takes the world points and turns its arcs the right way, and refuses an outline in a plane that is not the plane of
  the drawing, a handle given twice, a handle of another layout, and two corners whose arcs go there and back. (4) A `--read-only` server
  ran `snapshot`, `release_snapshot`, `audit` (without `fix`), `dump_drawing` and, on a file, `zoom_*` alone but refused them in a plan; a
  plan decides call by call as a call alone does. `snapshot` and `reset` always failed inside a plan on a file and are refused by name.
  (5) A `null` for an argument that has a default was answered "An error occurred invoking"; it leaves the argument out, in a call and in a
  plan. (6) The app was asked for no second look at a running command where a tool typed: the first line of a script that starts a
  command is refused by the app itself when one has started since the tool looked (the Remote request `strict`), and the plan's two UNDO
  lines are typed so. (7) A lineweight of `-0.5` set 0.00 mm (only the codes -1, -2 and -3 are negative weights now), the refusal of a whole
  number named the weights in hundredths where the description says millimetres, `add_layer` refused the `-3` its description names, a viewport
  scale below 0.000000000000001 was written as 1:0 (also `Layout.add_viewport` in Python), a path that names the open drawing's file another
  way (a hard link, a short name) was not seen as its own, and a `plot_pdf` that was refused still made its folder. (8) `run_command` in a
  plan on a file may not start `U`, `UNDO`, `REDO`, `QSAVE`, `SAVE`, `SAVEAS`, `OPEN`, `NEW`, `CLOSE` or `QUIT`, which defeat the plan's
  undo step. (9) `CadLayouts.Add` and `AddViewport` inside a group of the caller kept what they had made when they failed; they take it
  back, and the caller's own edits stay. (10) Python: `add_insert(attributes={})` for a block with no attribute to fill is a `ValueError`,
  as its docstring says; a preset attribute keeps its default as the insert command does; `entities(DxfClass, layout=Layout)` passes the
  layout as the plain name; the `Layout` class says why its equality is that of `str`.

- **FIXED.** MCP server, from the end-to-end acceptance (a plate drafted and plotted with the tools alone). (1) A server attached
  with `--read-only` was not refused `open_drawing` and `new_drawing`, which replace the drawing the user has on screen; they are
  refused now (on a drawing file they still work: a read-only server starts that way). (2) A call with a missing, unknown or wrongly
  typed argument was answered "An error occurred invoking 'add_line'."; it is checked against the tool's schema first, as a step of a
  plan is, and refused with the argument and what the tool takes. (3) `overlaps` left out the attributes of a block reference and the
  text of its block, which `check_standard` counted; INSERT is one of the kinds whose words it looks at by default (the core's
  `CadSpatial.Overlaps` is unchanged: pass `INSERT` to ask for them there). (4) An `offset` without `erase_source` erased its source
  where an earlier `offset` had set OFFSET's Erase option to Yes; the option is typed every time. (5) `describe_kinds` named no tool
  for ELLIPSE, SPLINE, POINT, MULTILEADER, ACAD_TABLE, ATTDEF and VIEWPORT and left out XLINE, RAY, LEADER and the records the `add_*`
  tools make (STYLE, DIMSTYLE, LAYER); every `add_*` tool is named now, and a test keeps it so. (6) The descriptions and the server
  instructions said that nothing reaches a file until `save` and that `save` is the only tool that writes a file (`save_as`,
  `plot_pdf`, `export_svg`, `export_to_file` and `dump_drawing` write files), that `set_current_layout` sets the layout the tools work
  in (the `add_*` and reading tools take their own `layout`, model space where left out), and listed only the first tools; they say
  which space each tool acts in, name every group of tools, and give millimetres, degrees and lineweights.
- **CHANGED.** MCP `plot_pdf` plots model space where `layout` is left out, as every other tool does (it plotted the layout shown); it
  plots through `CadPlot.WritePdf`, and `create_layout` and `add_viewport` make the layout and the viewport through `CadLayouts` and not by
  typing `-LAYOUT` and `MVIEW`. A lineweight of `add_layer`, `set_layer` and `set_properties` is given in millimetres as AutoCAD shows
  it (`0.5`), the hundredths of a millimetre of before (`50`) still read; `add_layer` takes `plot`. `run_plan` may end with
  `plot_pdf`, `export_svg` and `save_as`, which are written once the plan's edits are kept (and not in a dry run or a plan that
  stopped); `save_as` is allowed nowhere else in a plan.
- **NEW.** MCP: `fits` and `free_space` take the frame object `{x, y, width, height, rotation}` a `measure_*` tool gives (so
  `"$m.frames[0]"` works in a plan); `add_hatch` takes arcs (`bulges`), a circle (`circle_center`, `circle_radius`) and the closed
  polylines and circles of the drawing by `handles`; `add_text` takes `justify`; `rename_layout` and `delete_layout` (the core's
  `CadMake` edits of the same names; not Model, the layout shown is not deleted); `add_viewport` takes `layer`; `query_entities` and
  `get_entity` give the viewports of a layout in an app, as they do on a file.
- **FIXED.** The graph export (`CadDrawingGraph.Build`, `to_graph`, the `export` request) slows with the square of the number of
  entities no more: the edge and grid-cell tables hashed their numbers by xor-ing the two halves, so that the edges (i, i+1) of a
  chain fell into some 17 buckets and the cells into some 2 000. A chain of lines each touching the next now builds in time that
  grows with its length: 10 000 lines 175 ms before and 55 ms now, 40 000 1.5 s and 0.36 s, 160 000 22.8 s and 1.7 s; the edges are
  the same.
- **FIXED.** A request sent to the app by hand that is malformed is refused in words that name the member, not answered with "could
  not do the request": `export` with a `maxEdges` of minus 4 294 967 295 was taken for 1 (a cap below 1 is refused now), and
  `maxEdges`, `tolerance`, `what` and `layout` of the wrong kind; a `frame`, `candidate`, `arrow` or `landing` holding a word was
  measured from nought; `scale`, `margin`, `minWidth`, `minHeight`, `kinds`, `words`, `over`, `handles` and `fragment` of the wrong kind,
  a negative `tolerance` of `overlaps`, `fits`, `freeSpace` or `check`, and the `records` of a `commit` with no `tables` or an
  `LTYPE` that is no list of names.
- **FIXED.** `CadMeasure.Leader` refused a landing only where it was exactly on the arrow; a landing 1E-12 mm away drew no leader line
  and was measured. It is refused within a tolerance now (the new overload takes it; the old one uses the library's, 1E-3 mm).
- **FIXED.** The labels of a wipeout: its fill blanks what is under it and is class 0, as in the picture, and only its frame (where
  `WIPEOUTFRAME` shows it) is class 4, hatches. The documentation said a wipeout's pattern and fill were class 4; no behaviour changed.
- **FIXED.** `coco_dataset.py` numbered its objects from 1 000 times the number of the picture, so a drawing of more than 1 000
  objects gave ids that clashed across images; the ids are one running count now.
- **FIXED.** Python's `Drawing.save_as` wrote DXF text to any name (`plan.txt`, `plan.pdf`); a name that does not end in `.dxf` or
  `.dwg` is a `ValueError` now, with nothing committed or written.
- **FIXED.** MCP server, from an independent review. (1) A block name that starts like AutoLISP (`(princ...)`, `!x`, `'x`) was typed at the
  `-INSERT` prompt and ran as LISP without `--allow-scripts`; `insert_block_with_attributes` and `define_block` now insert through an
  edit of the core (`CadMake` `insert_block`), so no name is typed, and a word of a command may not start with `(`, `!` or `'` nor a note
  hold a control character; names are refused over 255 characters. (2) Attached, `add_mleader`, `add_table`, `define_block`,
  `insert_block_with_attributes`, `create_layout`, `add_viewport`, `undo` and `redo` typed into the command the user had running (a `U`
  took the user's last LINE segment back); they are refused while a command runs, `UNDO Begin` and `End` are typed only if UNDO waits
  for them, and `undo` and `redo` say whether a step was taken. (3) An insert no longer depends on the block's scaling, on ATTREQ or
  on ATTDIA, which opened the Edit Attributes dialog in the user's window; an attribute value may hold spaces in the app. (4) Attached,
  `set_layer` no longer turns a true-colour layer into an index colour. (5) `export_to_file` writes a `.json` file only, refuses a file
  that is there unless `overwrite` is true, and makes the folder (it refused one that was not there). (8) `save_as`, `plot_pdf`,
  `export_svg` and `export_to_file` never write the drawing's own file, `overwrite` or not (`save_as` did with it). Also: `render_png`
  and `export_svg` with a box given by its far corner first drew the whole drawing; `save_as` of a DWG as version 12 is refused up front;
  `fillet`, `chamfer` and `offset` leave FILLETRAD, CHAMFERA, CHAMFERB and OFFSETDIST as they were; `add_mleader` and `add_table` put
  their style and scale back when they fail; the rows of `CadDrawingDiff` tie-break on the index path; the Remote server forgets the
  snapshots of a drawing when it is closed.
- **NEW.** Two drawings compared from Python (PW7.6). `drawing.compare(other)` takes a `Drawing` of any backend (a file, the app, a running
  app, read as a copy of its database as the renders read it) or the path of a DXF or DWG file, and gives a `Diff`: the objects `other` has
  and the drawing has not (`added`), those only the drawing has (`removed`), and those both have that differ (`changed`, each with the
  names of what differs and, where the box kept its size and moved, `moved_by`, listed also as `moved`), with the boxes as NumPy arrays
  (`added_boxes`, `removed_boxes`, `before_boxes`, `after_boxes`, `moved_by`). A drawing put back by `reset` compares the same as the copy
  saved before the episode. Two drawings of 10 000 objects are compared in about 0.4 s. It runs the core's `CadDrawingDiff.Compare` in
  the Python process, so the app needs no new request.
- **NEW.** Sheets from code (PW7.7). `drawing.layouts` is now a table like `layers` (iterable of `Layout`, `len`, `[index]`/`[name]`, `in`,
  `add(name, paper="ISO_A3", units="mm")`, `current`); a `Layout` is its name as a `str`, so `drawing.layouts[1]` still goes where a layout
  name does. A layout reads and sets its page setup (`paper`, `landscape`, `plot_area`, `scale` as `"1:50"` or `0.02`, `plot_style`),
  `add_viewport(frame, scale, center, frozen_layers)` puts a viewport on it at a scale, and `plot_pdf(path, paper=, scale=, fit=)` plots it
  through the plot pipeline, the text of an SHX font also written so that a reader's search and a program that reads the PDF back find the
  words. `add_insert` takes `layout=` and `attributes=` (the words of a title block by tag, the rest at the defaults of the block's
  definitions). On a file, in the app and from outside it (the new request `sheets`; the PDF of an app's drawing is plotted in the Python
  process from a copy). `examples/library/sheet_from_code.py` makes a plate, an A3 layout in millimetres with a filled title block and two
  viewports at 1:10 and 1:5, and a PDF whose words read back are the title block's. In C#: `CadLayouts` (`List`, `Add`, `SetPageSetup`,
  `AddViewport`, `ParseScale`, `ScaleText`, `Paper`) in the core, `CadPlot.WritePdf` in the rendering library (the plot the MCP `plot_pdf`
  does, now callable by name), and `CadRemoteDocument.GetLayoutSetups`, `AddLayout`, `SetPageSetup`, `AddViewport`.
- **NEW.** The MCP server reads and queries a drawing in depth, and an agent can try a layout and roll it back (MCP phase 2, group A:
  25 tools). `get_entity` gives one entity in full (its values by kind, its drawn
  box and the rows of the Properties palette), `count_entities` and `aggregate` count and add up lengths and areas by kind, layer,
  block or colour, `get_texts` and `get_dimensions` list the words and the dimensions with their places and measured values,
  `get_block` and `get_attributes` read a block's definition and a reference's attributes, `list_layouts` gives each layout's sheet
  size and its viewports with their scale and view centre, `list_linetypes`, `list_multileader_styles` and `list_table_styles` the
  styles, `measure_distance`, `measure_area`, `measure_length`, `list_properties` (LIST) and `id_point` (ID) the inquiry commands,
  `select` (window, crossing, fence, polygon, crossing polygon, all, previous) and `select_similar` the handles by where and how
  they are drawn, `get_variables` and `set_variable` the system variables (38 documented, set only for the ones the drawing keeps),
  `server_status` and `describe_kinds` how the server stands and what it reads and writes. `snapshot`, `reset` and
  `release_snapshot` keep the drawing, put it back (a writer: refused with `--read-only`; the handles come back too) and let it go,
  in a file and in the app. Three new requests of [Remote](docs/presenter/remote.md#the-palette-the-entities-in-an-area-and-list) make
  the app answer for itself: `properties` (`CadRemoteDocument.GetProperties`), `selectGeometry` (`GetHandlesIn`: window, crossing,
  fence, polygons and a point, whole and not held to the app's window, as the new `CadSpatial.Select`) and `list`
  (`ListObjects`: LIST run in a command session of its own, as the new `CadInquiry.List`). The guide is
  [MCP](docs/presenter/mcp.md).
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
- **NEW.** MCP phase 2, the tools to draw (groups B, C and D of PW7.10): `OpenCAD2D.Mcp.exe` serves 46 more tools, in a drawing file and
  attached to the app, so that an agent can take a drawing from nothing to a sheet with no command typed. Create: `add_rectangle`
  (with a fillet or a chamfer), `add_polygon`, `add_ellipse`, `add_spline`, `add_point`, `add_donut`, `add_xline`, `add_ray`,
  `add_leader`, `add_mleader` (with a `scale`), `add_table` (with its cells), `add_text_style`, `add_dimension_style`,
  `load_linetype`, `add_attribute_definition`, `define_block` (keeping, converting or deleting the objects),
  `insert_block_with_attributes` and `set_attribute`. Modify: `copy`, `mirror`, `offset`, `array_rectangular`, `array_polar`,
  `explode`, `join`, `break_at`, `trim`, `extend`, `fillet`, `chamfer`, `stretch`, `lengthen`, `set_draw_order`, `edit_text`,
  `edit_dimension`, `match_properties` and `redo`; `set_properties` also sets `linetype_scale`, `lineweight` and `transparency`.
  Layers and layouts: `set_current_layer`, `set_layer`, `rename_layer`, `delete_layer` (with `move_to`), `create_layout` (a sheet in
  millimetres), `set_current_layout`, `add_viewport` (at 1:n, with its centre and layers frozen in it), `set_viewport_scale` and
  `lock_viewport`. A modify tool types the command on the drawing's own command line with the numbers, handles and names it has
  checked, and answers with the handles it made, took away and changed; every writing tool is one undo step and is refused under
  `--read-only`. For the app the Remote channel has two new requests, `make` (an edit of the core in one call, `CadMake`) and
  `feedSelection` (answers the object prompt of a command by handle), and a line of Esc sent by `command` ends the command running.
  One scripted session draws a plate 1 000 by 600 mm (an offset, a notch with a fillet and its mirror, six holes in a polar array, a
  hatch, a block with attributes inserted twice, dimensions, a multileader, a table), then an A3 sheet with a title block and two
  viewports at 1:10 and 1:5, a picture and a DWG, in 47 calls in both modes. See
  [Letting an AI agent drive OpenCAD2D](docs/presenter/mcp.md).
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
  managed memory where the first hundred put it (measured: 101.7 MB after 100 episodes and 101.7 MB after 1 000; the test allows 8 MB of growth). It works on the undo list the document already keeps: a snapshot is a point of
  it and a reset one jump back to the point, the steps since forgotten (not made one more step, so memory does not grow, and what the
  user did before the snapshot stays undoable while the list holds the point of the snapshot), with the drawing of the next reset read ahead on a worker thread while the episode runs.
  Where the list no longer holds the point (the drawing was undone below it and edited again) the reset reads the drawing back from
  the snapshot's own bytes: as exact, every display list drawn again, the undo list started afresh, so that what the user did before the snapshot can then no longer be undone (a snapshot A, edits, a snapshot B, a reset to A and a reset to B reach it). Refused while a command runs in the
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
- **NEW.** The MCP server prints, files and checks: `plot_pdf` (a layout or model space to a one-page PDF with the layout's own
  sheet and scale, or `paper`, `scale` or `fit` for that plot only; the words of an SHX style are searchable in it), `export_svg`,
  `save_as` (a copy of the drawing as a DXF or DWG of a version; the open drawing keeps its file and its unsaved changes),
  `purge` (the kinds nothing uses, through -PURGE with fixed input), `audit` (the errors counted by kind; `fix` fixes them as one undo
  step) and `zoom_extents`, `zoom_window` and `zoom_object` (the app's window attached; the view `render_png` and `export_svg` use
  on a file). A file that is there is refused unless `overwrite`. All but the check of `audit` and the zooms on a file are refused with
  `--read-only`.
- **NEW.** The MCP server helps debug the app you are working in (`OpenCAD2D.Mcp.exe --attach --read-only`): `app_screenshot`
  (the app's canvas as it last drew it, with its size and the part of the drawing it shows, so `render_png` draws the same part to compare
  the two), `command_history` (the last lines of the command line), `last_errors` (the app's crash log and the requests it could not
  answer, and the server's own failed calls), `dump_drawing` (the drawing as it is now, unsaved changes included, as a DXF in
  `%TEMP%\OpenCAD2D\mcp-debug`; the only writer allowed read-only), `compare_drawings` (added, removed and changed objects between two
  states, by handle) and `timings` (milliseconds of a regen, a render and an open). The guide has a section, "Debugging with MCP".
- **NEW.** Remote reads what the app is doing and what went wrong in it, with no window or dialog shown: `app.GetScreenshot(maxSide)`
  (the PNG of the drawing canvas as the window last drew it, its size and the part of the drawing it shows), `drawing.GetHistory(count)`
  (the last lines of the drawing's command line, whether a command runs and its prompt) and `app.GetErrors(count)` (the entries of
  `crash.log` and the requests the app could not answer). A host that is not the app's window implements the new
  `ICadRemoteDiagnostics` to answer the first and the last; `ICadRemoteHost` is not changed and the protocol stays at version 1.
- **NEW.** `CadDrawingDiff.Compare(before, after, tolerance)` compares two states of a drawing by handle through the walk and the JSON
  of the AI export: the objects added, removed and changed, with the names of the properties that differ, the box before and after
  and how far it moved (a line that moved 10 mm in x is `box` and `start` and `end` and `[10, 0]`). The MCP's `compare_drawings`
  uses it, and it is a class of the core so that Python's drawing comparison (PW7.6) can use it later.
- **NEW.** `CadCrashLogReader.Read(path, count)` reads the newest entries of the app's crash log (and of `crash.old.log` where the log
  holds fewer): when, where caught, the last command, whether fatal and the head of the exception.
