# Changelog

## Unreleased

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
- **CHANGED.** The core library is GeometryHelper 13.13.0 (on nuget.org), which holds the core fixes and members
  2.0.0 shipped in Lib under 13.12.1; the NuGet packages of OpenCAD2D name it as their dependency.
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
