# Changelog

## Unreleased

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

- **NEW.** Three sample plugins and two sample tools ship with the app, each a folder that opens as it is (Tools as folders).
  The C# plugins are `Plugins\HelloWorld` (`HelloWorld.csproj` and `HelloWorldCommands.cs`: the command `HELLOWORLD` writes
  `Hello, World!`, the smallest plugin there can be), `Plugins\DrawingInfo` (`GHDWGINFO`, the drawing's name, objects, layers,
  blocks and extents) and `Plugins\Stairs` (`STAIRS`, the command of the plugin guide); Visual Studio opens each from
  `examples/HelloWorld`, `examples/DrawingInfo` and `examples/Stairs`. The first start after an install or an upgrade
  compiles the three once, 1.09 s to 1.34 s together on .NET 8 (four runs), and a start from the cache takes 61 ms to 82 ms.
  The Python tools are `Pythons\HelloWorld` (`PYHELLOWORLD` writes `Hello, World!`) and `Pythons\DrawingInfo` (`GHPYINFO`,
  the same lines as `GHDWGINFO`, its `Extents` line read over the channel), in `examples\Pythons`. Delete a folder to remove
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
