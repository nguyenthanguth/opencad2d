# Changelog

## Unreleased

- **NEW.** The run of a tool's command, the base of tools that run in a process of their own (Tools as folders, TF4a). A
  program the app starts reaches the app on a second pipe, `OpenCAD2D.<process id>.tools`, which is never announced in the
  instance folder, stays open whatever "Allow other programs to connect" says, and takes a client only with the token of
  its tool (`hello` carries it; 32 random bytes as 64 hex letters; a connection without or with a revoked one is refused
  and closed). The command the user types is the run: the drawing waits at `STAIRSPY is working... (Esc to stop)`, which
  takes no typed text, point or Enter, only Esc. The tool joins the run (`app.JoinRun(7)`) and every request of it is part
  of the command: `run.Ask(CadAskKind.Point, "Specify the first step")` asks on the command line and waits for the user,
  with no lock held, so another client is served meanwhile; `CommitChanges` goes into the command's one undo step named
  after it; `run.End()` keeps the step, `run.Fail(line)` takes it back and writes the line; Esc takes back everything the run
  committed, answers a question open as cancelled (`IsCancelled`) and refuses every later request of the run. A request of
  an ended run is refused, `The run 7 of STAIRSPY has ended.`; `pick` and `command` are refused inside a run. For the app's
  host: `CadRemoteServer.ForTools`, `IssueToken`, `RevokeToken`, `CreateRunCommand` and `CadRemoteToolRun` (Core);
  `new CadApplication(pipe, token)`, `JoinRun`, `CadRemoteRun` and `CadRemoteAnswer` (OpenCAD2D.Remote);
  `CadPrompt.ForWaiting` and `IsWaiting`. The app does not start a tool with it yet.

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
- **NEW.** A command plugin can be given as C# source: a `.cs` file, or a folder of them, loads as a `.dll` does (the
  `Plugins` folder, PLUGINLOAD/NETLOAD, the startup list, PLUGINRELOAD), compiled by Roslyn when it loads, on .NET 8 only.
  Other libraries are named with `//#r "Name.dll"` lines. The build is cached in `%LocalAppData%\OpenCAD2D\PluginCache` by
  the SHA-256 of the sources, so a start with unchanged sources compiles nothing; the first compile of a session takes
  about 0.9 s and a later one about 20 ms for a plugin of 220 lines. A source that does not compile is refused with a line
  per error, `Stairs.cs(12,9): error CS0103: The name 'x' does not exist in the current context`, the first 20.
  `CadPluginLoader.CanCompile` and `Compile`, `CadPluginBuild`, `CadLoadedPlugin.IsSource` and
  `CadPluginAssemblyLoader(cacheFolder)` are new. The screenshot mode (`--screenshot ... --plugin x.cs`) keeps its
  builds in its own temporary folder, never `%LocalAppData%\OpenCAD2D\PluginCache`. The app ships a sample,
  `Plugins\Stairs.cs` (the command `STAIRS` of the plugin guide), compiled at the first start after an install or an
  upgrade and cached after; delete the file to remove it.
- **NEW.** Load/Unload Applications takes C# source: Load… (now `Load… (.lsp, .dll, .cs)`), NETLOAD's Open dialog and a
  drop on the table load a `.cs`; its row has the Kind `C# source` in light purple and a chip of its own, `C# source 1`
  (`Plugins` counts the .dll plugins only). A source that does not compile shows **Stairs.cs** `did not compile: 2 errors.`
  and the first error on the red line, with `Every error is on the command line` on the right and every error in its tooltip.
- **FIXED.** `OpenCAD2D.exe --screenshot ... --plugin x.dll` (and `x.cs`) rewrote the user's `%AppData%\OpenCAD2D\settings.json`
  with default settings plus the application it remembered. A screenshot now keeps its settings, tool palettes, plot
  styles and LISP registry in a folder of its own, `%TEMP%\OpenCAD2D\ScreenshotSettings\<process id>`, and never reads
  or writes the user's.
- **CHANGED.** The setup carries the C# compiler for plugins given as source (Roslyn 4.14.0, with
  System.Collections.Immutable and System.Reflection.Metadata 9.0.0 in place of the runtime's 8.0): 82,037,149 bytes
  (78.2 MB) in place of 79,946,201 (76.2 MB), 2.6 % more, and the install folder holds 288 files in place of 286, 8.17 MB
  more (setups built by `make-installer.ps1` at 4f4a83cc and at 3f059fb7). Installed from that setup, the guide's `Stairs.cs` and the
  DrawingInfo sample as source compiled and ran.
- **CHANGED.** PLUGINLOAD asks `Enter the path of a plugin (.dll, .cs or its folder)`, and a path that is no plugin is refused
  with `... a plugin is a .dll, a .cs, or the folder of one.`
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
