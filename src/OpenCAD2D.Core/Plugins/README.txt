OpenCAD2D - Plugins folder
===================================

Put command plugins here, and they are loaded when the app starts, as AutoCAD loads what its
Startup Suite names. A plugin is a .NET .dll with commands marked [CadCommand], or the C# source
of one: a folder with a .csproj, which the app reads and compiles when it loads the folder; see
the plugin guide.

What loads at start, in this order:

  1. Every .dll directly in this folder, in the order of their names, ignoring case.
  2. Every folder below this one that holds a .csproj, a .dll or a plugin.json directly, at any
     depth, in the order of their names, as one plugin: a plugin made of several .dll files (its own
     and the libraries it needs) lives in a folder of its own, and the folders below it are not
     read. A folder with none of these is walked into, so Company\ToolA\ToolA.dll is the plugin
     ToolA. If the folder has a plugin.json, that gives the plugin its name, version and API; if
     not, the plugin is named after the folder and all the .dll files directly in it are loaded.
     A folder with a .csproj is one plugin compiled from the .cs files below it, as Visual Studio
     builds them; its .dll files are the libraries the .csproj names in <Reference HintPath>.
     A folder holds one .csproj; a .cs alone, or a folder of .cs files with no .csproj, is not a
     plugin, and is refused with the way to make it one.

A .dll that is not a plugin, or that cannot be loaded, is one line of the plugin load log (the
Plugin manager shows it) and the next one loads. A plugin loaded already is not loaded twice.
Files other than .dll, and files below a folder that is a plugin, are not loaded. A
subfolder that is a junction or a symbolic link, or is hidden or a system folder, is not read.

This folder lies under the program folder, so it is a trusted folder: its plugins load with no
question. A plugin anywhere else is loaded with PLUGINLOAD (NETLOAD) for one session, or put on
the "Load at every start" list of the Plugin manager.

The folder Stairs is a sample C# source plugin that comes with the app, the example of the plugin
guide: Stairs.csproj and StairsCommands.cs, and its command STAIRS writes "Steps: 18". The app
compiles it at the first start after an install or an upgrade (about 0.9 s) and keeps the build,
so later starts do not compile it again. Delete the folder if you do not want it.

The folder DrawingInfo is a sample that comes with the app: its command GHDWGINFO writes the
name, objects, layers, blocks and extents of the drawing. Delete the folder if you do not want it.
Its source is the one file DrawingInfoCommands.cs in examples\OpenCAD2D.Plugins.DrawingInfo of
https://github.com/nguyenthanguth/opencad2d; put it in this folder's DrawingInfo in place of the
.dll, with a DrawingInfo.csproj as small as the one of Stairs, and the app compiles it at the
next start.

A plugin is loaded from a copy, so the file here is never locked: change it, then PLUGINRELOAD.
Loading a plugin runs its code with your rights: put here only what you trust.

This folder holds no search path: fonts, hatch patterns, linetypes and AutoLISP files are not
looked for here.
