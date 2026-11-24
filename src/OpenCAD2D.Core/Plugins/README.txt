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
     A <PackageReference> in the .csproj to a NuGet package is restored from nuget.org, with no
     .NET SDK, but only after the app has listed the packages and you have said yes; they are kept
     in %LocalAppData%\OpenCAD2D\NuGet, and two plugins may use two versions of one package.

A .dll that is not a plugin, or that cannot be loaded, is one line of the plugin load log (the
Plugin manager shows it) and the next one loads. A plugin loaded already is not loaded twice.
Files other than .dll, and files below a folder that is a plugin, are not loaded. A
subfolder that is a junction or a symbolic link, or is hidden or a system folder, is not read.

This folder lies under the program folder, so it is a trusted folder: its plugins load with no
question. A plugin anywhere else is loaded with PLUGINLOAD (NETLOAD) for one session, or put on
the "Load at every start" list of the Plugin manager.

Three folders here are sample C# source plugins that come with the app, each a .csproj and one .cs file:

  HelloWorld    its command HELLOWORLD writes "Hello, World!": the smallest plugin there can be.
  DrawingInfo   its command GHDWGINFO writes the name, objects, layers, blocks and extents of the drawing.
  Stairs        the example of the plugin guide; its command STAIRS writes "Steps: 18".

The app compiles them at the first start after an install or an upgrade (1.1 to 1.3 s for the three)
and keeps the builds, so later starts do not compile them again (61 to 82 ms for the three, from the
cache). Delete the folder of one if you do not want it, or copy one under another name to start a
plugin of your own. The sources are also in examples\ of https://github.com/nguyenthanguth/opencad2d,
where each opens in Visual Studio as a project.

A plugin is loaded from a copy, so the file here is never locked: change it, then PLUGINRELOAD.
Loading a plugin runs its code with your rights: put here only what you trust.

This folder holds no search path: fonts, hatch patterns, linetypes and AutoLISP files are not
looked for here.
