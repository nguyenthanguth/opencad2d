# Changelog

## Unreleased

- **NEW.** A venv for each Python tool, and pip to fill it (Tools as folders, TF5). A tool's packages are installed into
  `%LocalAppData%\OpenCAD2D\python\envs\<name>-<8 hex>`, made from the Python of Global settings or the newest 3.11 to
  3.15 of the registry (the finder of OpenCAD2D.Python, moved to the window's project without its DLL checks, since a tool needs
  only `python.exe`), so two tools may pin two versions of one package and both run. The app lists what pip's dry run would
  install and asks before anything is downloaded (a hook the window sets; with none, nothing is installed and the tool is
  refused with the line to run by hand). `numpy>=1.26` is installed as `numpy==1.26`, as written where pip has no `1.26` for
  the Python in use, with no warning; an option line in `requirements.txt` is refused. A missing version or a clash gives pip's
  lines and changes nothing; a change of `requirements.txt` builds a new venv beside the old and swaps it in, and a failed
  install keeps the old one. `opencad2d` is not installed in each venv: a `.pth` file puts the app's copy and a shared base
  (pythonnet, clr_loader, cffi, pycparser, installed once for a Python version from the wheels the app ships in
  `python\wheels`, 1.3 MB) after the venv's own packages. A venv with its `.pth` takes 75 to 98 ms and 0.54 MB, the base
  1.7 s once and 2.7 MB. The window does not start tools yet.

- **FIXED.** A .NET 8 plugin could fail with "AssemblyLoadContext is unloading or was already unloaded" when one of its
  commands first used a library of its folder after a garbage collection: the app held the plugin's collectible load
  context only weakly, so a collection began unloading it while the plugin still ran. The app now keeps every plugin's
  context until the plugin is unloaded. This was in 1.0.0 for a `.dll` plugin with a dependency it loads late.
- **CHANGED.** The sample plugins ship as source (Tools as folders, TF9, the C# half). `Plugins\DrawingInfo` is now the
  folder with `DrawingInfo.csproj` and `DrawingInfoCommands.cs`, as `Plugins\Stairs` is, and the built
  `OpenCAD2D.Plugins.DrawingInfo.dll` is no longer in the app's `Plugins` folder; the project `examples/OpenCAD2D.Plugins.DrawingInfo`
  (a netstandard2.0 library, in `OpenCAD2D.slnx`) is the folder `examples/DrawingInfo`, a plugin folder that Visual Studio
  opens, and is no longer built with the solution. The command keeps its name, `GHDWGINFO`, and writes the same lines. The first
  start after an install or an upgrade compiles the three samples once: 1.09 s to 1.34 s together on .NET 8 (four runs), and
  61 ms to 82 ms at every start after it, from the cache.
- **NEW.** The sample plugin `Plugins\HelloWorld`, with `HelloWorld.csproj` and `HelloWorldCommands.cs` (`examples/HelloWorld`):
  the command `HELLOWORLD` writes `Hello, World!` on the command line. It is the smallest plugin there can be, and the
  plugin guide documents it with the other samples.
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

- **NEW.** The process of a Python tool (Tools as folders, TF4b). `python -m opencad2d.tool <folder>` runs a folder's
  `@command` functions as the commands of the app: the window side starts it (suspended, in a job object that ends it and
  every process it starts when the app lets it go, so none is left even when the app is killed) at the tool's first
  command, keeps it, reads its standard output and error, and gives it two anonymous pipes of lines of JSON (`run`,
  `cancel`, `unload`; `ready`, `ended`). The tool's `get_point` and the other prompts, `write`, `print`, `cmd` and
  `active_drawing()` are requests of the run on the channel of the tools, so a command is one undo step and Esc takes it
  back. A tool that raises (`STAIRSPY: ValueError: no stairs here`), exits (`... ended unexpectedly (exit code 3).`) or
  hangs (`... did not stop within 5 s and was ended.`) leaves the drawing as it was and the next command works; Esc in
  `while True: pass` stops the loop at once. The first command of a tool took 0.37 s, the next 16 ms. `cmd()` inside a run
  nests the command as AutoLISP's `command` does, with no "Resuming" line: `CadRemoteRun.RunCommand(name, answers)` and the
  `feed` request (`CadRemoteRun.Pause` leaves a prompt to the user). `OPENCAD2D_LIB` names the folder `import opencad2d`
  loads the assemblies from. The window does not start tools yet.

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
- **NEW.** A command plugin can be given as C# source: a folder with a `.csproj` loads as a `.dll` does (the `Plugins`
  folder, PLUGINLOAD/NETLOAD of the folder or its `.csproj`, the startup list, PLUGINRELOAD), compiled by Roslyn when it
  loads, on .NET 8 only, and Visual Studio builds the same folder. The app reads the `.csproj` with no MSBuild: the sources
  are every `.cs` below the folder but `bin\` and `obj\`, less `<Compile Remove>`; `AssemblyName`, `Version` (written into
  the assembly as the SDK does, 1.0.0 where there is none, so the plugin loads as `Stairs 1.0.0`), `LangVersion`, `Nullable`,
  `ImplicitUsings`, `UseWPF`, `UseWindowsForms`, `<Using>`, `DefineConstants`, `AllowUnsafeBlocks` and
  `GenerateAssemblyInfo` are read, the symbols are the SDK's for `net8.0-windows` Release, a `<Reference>` takes a
  `HintPath` to a `.dll` inside the folder (a name of the app's own is its own, whatever its `HintPath`), and a
  `<PackageReference>` to `OpenCAD2D.Core`, `OpenCAD2D.Remote`, `OpenCAD2D.Wpf` or `GeometryHelper` is the app's own and
  downloads nothing (a version above the app's gives a line and the compile goes on against the app's), and so does one for the
  build alone (`PrivateAssets="all"`, or an `IncludeAssets` with no `compile` or `runtime`). Any other `<PackageReference>`
  is a NuGet package restored from nuget.org after asking, with no .NET SDK: the lowest version that meets each reference
  (`Version="13.0.3"` is 13.0.3), its dependencies for `net8.0-windows`, cousins at the higher of their lowest versions,
  every range checked (`Stairs.csproj(6,10): error NU1107: Stairs needs Foo [2.0.0, 3.0.0); Bar 1.0.0 needs Foo [1.0.0, 2.0.0); no version meets both.`,
  and NU1605 for a reference lower than another package needs); the nearest framework's assets (`lib/net6.0` over
  `netstandard2.0`); the question before the first download and when the list changes (the window asks with a dialog,
  **Packages for Stairs**, that lists the packages, the plugin's own in bold and then what they need, each marked
  `download` or `in the cache`, with the buttons `Don’t load` and `Download and load`; closing it is a no), a no or an
  unreachable nuget.org leaving the plugin not loaded with the line to
  reconnect and PLUGINRELOAD (the line of the command-line form of the question,
  `Download 3 packages for Stairs from nuget.org? [Yes/No] <No>:`, is `CadPackageQuestion.Prompt`; no command asks it yet,
  so a typed PLUGINLOAD asks with the dialog too); packages kept in `%LocalAppData%\OpenCAD2D\NuGet\packages`, the resolved list in
  `PluginCache` so that a start asks nothing of the network; two plugins with `Foo 1.0.0` and `Foo 2.0.0` both run (a
  library the app carries itself is shared, with a line when a package asks a higher version); a native file loaded by its
  full path, two of one name side by side; a package's library first used after a garbage collection still loads (the
  loader holds each plugin's load context, which the runtime holds only weakly). `<Using Remove>` is read. The app carries NuGet.Protocol 7.9.0: 3 645 480 bytes
  in 9 files more in its folder. What is not read is a line of the load log, and the plugin loads: an element with a `Condition`
  (`Stairs.csproj: <PropertyGroup Condition="..."> was not read (conditions are not evaluated).`), a
  `Directory.Build.props` in the folder, a `$(...)` in a value, a target framework that is not .NET 8. What cannot be
  read is refused with a line per error in the form of a compiler's (`Stairs.csproj(7,10): error OC0002: ...`): a
  `<ProjectReference>`, a package with no version, `EnableDefaultCompileItems`
  false, a `<Compile Include>` or a `HintPath` outside the folder. A lone `.cs`, a folder of `.cs` files with no
  `.csproj`, and a folder with two `.csproj` files are refused, saying how to make a plugin of them. The build is cached
  in `%LocalAppData%\OpenCAD2D\PluginCache` by the SHA-256 of the `.csproj`, the sources and the libraries, so a start with
  unchanged files compiles nothing; the first compile of a session takes about 0.9 s and a later one about 20 ms for a
  plugin of 220 lines. A source that does not compile is refused with a line per error,
  `Stairs.cs(12,9): error CS0103: The name 'x' does not exist in the current context`, the first 20.
  `CadPluginLoader.CanCompile` and `Compile(name, projectPath)`, `CadPluginBuild` (with `Warnings`),
  `CadLoadedPlugin.IsSource`, `CadPluginAssemblyLoader(cacheFolder)`, `CadPluginAssemblyLoader.ConfirmDownload`, `CadPackage` and
  `CadPackageQuestion` are new. The screenshot mode
  (`--screenshot ... --plugin Stairs`) keeps its builds in its own temporary folder, never
  `%LocalAppData%\OpenCAD2D\PluginCache`. The app ships a sample, the folder `Plugins\Stairs` (the command `STAIRS` of the
  plugin guide, from `examples/Stairs`), compiled at the first start after an install or an upgrade and cached after;
  delete the folder to remove it.
- **NEW.** Load/Unload Applications takes C# source: a `.csproj` loaded with Load… or dropped on the table loads its
  folder; the row has the Kind `C# source` in light purple and a chip of its own, `C# source 1` (`Plugins` counts the
  .dll plugins only). A source that does not compile shows **Stairs.csproj** `did not compile: 2 errors.` and the first
  error on the red line, with `Every error is on the command line` on the right and every error in its tooltip. A new
  button, **Load folder…**, chooses a tool's folder: a folder with a `.csproj` loads as a C# plugin, and one with a
  `requirements.txt` is refused with `Survey cannot be loaded: Python tools load in a later build.` The file types of
  Load… (and of NETLOAD's Open dialog) offer **C# plugin (\*.csproj)** where they offered **C# source (\*.cs)**, the
  button reads `Load… (.lsp, .dll)`, and the foot says `Each tool run is one undo step. A tool runs its code, and its
  packages’ code, with your rights.` A plugin whose packages were not downloaded (the question answered No) stays in the
  table, unticked, with the amber badge **needs packages** after its name, and ticking it asks again; one whose packages
  could not be restored (nuget.org not reached, a package or version not found) has the red badge **install failed** and
  the red line says `Stairs was not installed:` and the first error, with `Every line is on the command line` on the right.
  `AppLoadViewModel.LoadFolder`, `AskFolder`, `LoadFolderCommand`, `AppLoadRow.Badge` and `AppLoadBadge`,
  `CadPluginAssemblyLoader.CompilingProject` and `CadPackageQuestion.Question` are new; the question's window,
  `PackageQuestionWindow`, takes its texts from `PackageQuestionViewModel`, so that the pip question of a Python tool can use it.
- **FIXED.** `OpenCAD2D.exe --screenshot ... --plugin x.dll` (and a source plugin's folder) rewrote the user's `%AppData%\OpenCAD2D\settings.json`
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
