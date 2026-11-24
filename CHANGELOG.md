# Changelog

## Unreleased

- **CHANGED.** The application menu button at the top left of the window shows the OpenCAD2D mark (the white tile with the
  O and the C, as the icon frames below 24 px) in place of the letter G. Under the pointer, pressed, or while the menu is
  open, it has a 2 px ring of light blue around it, where it used to turn lighter blue.
- **FIXED.** Convert to Line in the hover menu of a polyline's arc-segment midpoint grip asked "Pick to convert to line
  segment:" and waited for a point; as in AutoCAD 2021 it now makes the segment straight at once (bulge 0, vertices
  unchanged, one undo step), for LWPOLYLINE and 2D POLYLINE alike. Convert to Arc still asks for the arc's midpoint, and
  Remove Vertex still acts at once.
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
