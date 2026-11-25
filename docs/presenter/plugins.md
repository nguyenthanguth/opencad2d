# Command plugins

A command written in C#, loaded into the presenter while it runs, as AutoCAD's NETLOAD loads one, and unloaded or loaded
again without a restart where the runtime allows. A plugin is a class whose methods are marked as commands; it asks its
prompts as the presenter's own commands do, edits through the document, and is held to the same gate and the same undo
steps. It is given as a .dll, or as a folder of source with a `.csproj` that the app compiles itself ([a plugin as C#
source](#a-plugin-as-c-source), which may name NuGet packages). Put it in the app's `Plugins` folder and it is loaded at
every start, as [below](#where-a-plugin-lives) says. Three samples are there already, `HelloWorld`, `DrawingInfo` and
`Stairs` ([the sample plugins](#the-sample-plugin)).

## A command

The sample below numbers the circles selected: it takes them (those selected before are taken, else it asks "Select
objects:"), asks for the number to start at, and writes a TEXT of each number at the circle's centre.

```csharp
using System.Collections.Generic;
using System.Globalization;
using GeometryHelper.Dxf;
using OpenCAD2D;
using OpenCAD2D.Plugins;

[assembly: CadPluginApi("1.0")]

public sealed class CircleTools
{
    [CadSetting("Text height", Minimum = 0.1, Maximum = 1000)]
    public double TextHeight { get; set; } = 2.5;

    [CadCommand("NUMCIRCLES", Alias = "NC", Description = "Number the selected circles", UsesSelection = true)]
    public IEnumerable<CadPrompt> NumberCircles(CadCommandContext c)
    {
        foreach (CadPrompt prompt in c.SelectObjects())
        {
            yield return prompt;
        }

        var circles = new List<DxfCircle>();

        foreach (string handle in c.Selected)
        {
            if (c.Document.FindEntity(handle) is DxfCircle circle)
            {
                circles.Add(circle);
            }
        }

        if (circles.Count == 0)
        {
            c.Write("No circle was selected.");
            yield break;
        }

        yield return CadPrompt.ForInteger("Start at", null, "1");
        int number = c.Input.Kind == CadInputKind.Number ? (int)c.Input.Number : 1;
        double height = TextHeight;

        // One call is one undo step; a refusal is written on the command line by Apply itself.
        c.Apply(null, scope =>
        {
            foreach (DxfCircle circle in circles)
            {
                var text = new DxfDBText();
                text.SetDatabaseDefaults(c.Database);
                text.Height = height;
                text.TextString = (number++).ToString(CultureInfo.InvariantCulture);
                text.Justify = DxfAttachmentPoint.MiddleCenter;
                text.AlignmentPoint = circle.Center;
                scope.Append(c.Space, text);
            }
        });

        c.Write("Numbered " + circles.Count + " circles.");
    }
}
```

The method is an iterator, like the presenter's own commands: it yields each [`CadPrompt`](xref:OpenCAD2D.CadPrompt)
and reads the answer from `c.Input` when it is resumed, so it runs inside the command line with no thread of its own. That
is the one difference from AutoCAD's `[CommandMethod]`, which blocks on its prompts: a method that returns `void` and blocks
is refused, because it would have to run on a thread while the window waits and then reach the canvas from that thread,
which WPF does not allow.

| The attribute | Says |
|---|---|
| [`[CadCommand("NAME")]`](xref:OpenCAD2D.Plugins.CadCommandAttribute) | the method is a command; `Alias`, `Description`, and `UsesSelection` for a command that takes the objects selected before it, as MOVE does |
| [`[CadSetting("Label")]`](xref:OpenCAD2D.Plugins.CadSettingAttribute) | a public property is a setting the presenter shows; `Description`, `Minimum`, `Maximum` |
| [`[assembly: CadPluginApi("1.0")]`](xref:OpenCAD2D.Plugins.CadPluginApiAttribute) | the least version of the plugin API the assembly needs; none is taken as 1.0 |

A command method may be static, or an instance method of a public class that is not generic and has a public constructor
that takes nothing, of which the plugin makes one when it is loaded. A setting is a property with a public get and set of
type `double`, `int`, `bool`, `string` or an enumeration, which the presenter draws a box for itself, so a plugin never
touches the window. A method or a class that cannot be one is left out, and each is a line in [`CadPlugin`](xref:OpenCAD2D.Plugins.CadPlugin)`.Problems`.

What the plugin can use is the public API of [`CadCommandContext`](xref:OpenCAD2D.CadCommandContext) and
[`CadDocument`](xref:OpenCAD2D.CadDocument): `SelectObjects()`, `Selected`, `Input`, `Write`, `Apply`,
`OpenUndoStep()`, `Remembered`, the document's entities, layers and selection, the `CadPrompt.For...` methods for a point, a
distance, an angle, an object, a keyword, a string or an integer, and the [`CadLayerEditor`](xref:OpenCAD2D.CadLayerEditor), [`CadGrips`](xref:OpenCAD2D.CadGrips) and [`CadPropertyEditor`](xref:OpenCAD2D.CadPropertyEditor)
calls. It reaches no internals.

## Running it

```csharp
CadPlugin plugin = CadPlugin.FromAssemblies("Circle tools", new[] { typeof(CircleTools).Assembly }, "1.0.0");
var registry = CadCommandRegistry.CreateDefault();
plugin.AddTo(registry);                          // NUMCIRCLES, and NC as its alias
var session = new CadCommandSession(document, registry);    // a document of three circles

session.Feed("NC");      // Select objects:
session.Feed("ALL");
session.Enter();         // Start at <1>:
session.Feed("7");       // three circles: TEXTs 7, 8 and 9, "Numbered 3 circles."
```

The three circles are numbered 7, 8 and 9 in one undo step, `NUMCIRCLES (3)`. This is also how a plugin's own tests run it:
`FromAssemblies` makes the plugin from assemblies already loaded, and a [`CadCommandSession`](xref:OpenCAD2D.CadCommandSession) runs its commands with no window.

Every run is one undo step whatever it edits through the document. A run that throws has every change of it taken back, the
error is written on the command line with the plugin's name and logged as a Warn, and the document is as it was.
Entities that have an [owner](owners.md) are refused by the owner's rule, not by the plugin: the gate is the document's.

A setting is read from and written to the plugin's property through [`CadPluginSetting`](xref:OpenCAD2D.Plugins.CadPluginSetting)`.Value`, and a value out of its range
is refused, the plugin keeping the one it had: setting the text height to 0.05 throws `The setting Text height lies from 0.1
to 1000.`

## The manifest

A plugin says what it is in a `plugin.json`, read without loading any code:

```json
{ "id": "circle-tools", "name": "Circle tools", "version": "1.0.0", "author": "Nguyen Thang",
  "kind": "command", "minimumApiVersion": "1.0", "commands": [ "NUMCIRCLES" ], "icon": "icon.png" }
```

Only the name is needed: the id is the name where none is given, the kind "command", the API version 1.0.
A command is a name or an object, `{ "name": "NUMCIRCLES", "alias": "NC", "description": "Numbers the circles", "usesSelection": true }`
(`CommandEntries`; `Commands` is the names), and a `"ribbon"` of tabs of panels of buttons that start a command (`Ribbon`) is
what a [Python tool](python.md#pluginjson) declares for the window; a C# plugin adds to the window in code, as
[below](#what-a-plugin-adds-to-the-window).
[`CadPluginManifest.Parse(json)`](xref:OpenCAD2D.Plugins.CadPluginManifest) reads it, and `Refusal()` says
why the presenter will not load it: a kind other than "command", or a newer plugin API than this build's, which is 1.3
(`CadPlugin.ApiVersion`): `The plugin Future needs version 2.0 of the plugin API, and this presenter has 1.3: it needs a
newer presenter.` A plugin that adds to the window, as [below](#what-a-plugin-adds-to-the-window), states 1.1; one that
uses slide-outs, dialog launchers, macro buttons or `CadSnapRequest.BlockReferences` states 1.2; one that uses the
[editor](#the-editor-the-view-selection-and-variables) states 1.3.

A plugin targets netstandard2.0 and references `OpenCAD2D` only, without copying it: the presenter gives a
plugin its own, so that the `CadCommandContext` it is handed is of the type it was built against.

## Where a plugin lives

The app has a `Plugins` folder beside `OpenCAD2D.exe`, as it has `Fonts`, `Hatches`, `Linetypes` and `Lisps`
([`CadSupportKind.Plugins`](xref:OpenCAD2D.CadSupportKind)). The build copies it, with a `README.txt`, to the
output of every project that uses the presenter. To install a plugin, put it there:

- **A .dll** directly in `Plugins`: one plugin, named as the file without its extension (`NumberCircles.dll` is the plugin
  `NumberCircles`). A `plugin.json` beside it is not read here, since one file could not name every .dll of the folder.
- **A folder** in `Plugins`, at any depth, that holds .dll files directly: one plugin made of those .dll files, its own and
  the libraries it needs, and the walk does not go below it. Where the folder has a `plugin.json`, that gives the plugin its
  name, version and API, as above; where it has none, the plugin is named after the folder.
- **A folder with a `.csproj`** is one plugin given as C# source ([below](#a-plugin-as-c-source)), the walk not going below
  it either; a folder that holds only `.cs` files is refused, and says it needs a `.csproj`.
- **A folder with no .dll, no `.csproj` and no `plugin.json`** is walked into: `Plugins\Company\ToolA\ToolA.dll` is the plugin `ToolA`,
  and `Plugins\Company\ToolB\ToolB.dll` the plugin `ToolB`. A subfolder of a plugin's own folder is not looked at, since the
  plugin is the folder above it. A subfolder that is a junction or a symbolic link, or is hidden or a system folder, is not
  entered, and one that cannot be read is passed over.

Everything there is loaded once, when the app starts, in the order of the walk: the .dll files directly in `Plugins` first,
by the order of their names (either case the same), then the folders, depth first, the folders of each level in the same
name order; then the plugins of the startup list below. Loading goes through the manager
described next, so a plugin of `Plugins` is copied to the shadow folder, loaded into its own load context on .NET 8, added
to the command line of every drawing, written in the load log and given the plugin API's window extensions. The folder
lies under the program folder, so it is a trusted folder, and no question is asked. A file that is not a plugin (a library
with no command, say) or that cannot be loaded is one line of the log, and the next file loads; a plugin loaded already is
not loaded twice. A .ghplugin put there is not read: a plugin is no longer a package.

A .dll or a folder at a place of your own, such as a build output, loads for the session with `PLUGINLOAD` (a `plugin.json`
beside a .dll is read there, and the name is the assembly's where there is none), and at every start from
the startup list below.

## A plugin as C# source

A command plugin can be given as a folder of C# source with a `.csproj`, and no build: the app reads the `.csproj`, compiles
the `.cs` files when it loads the folder, and the plugin then goes the way a .dll goes. This is the app on .NET 8 only;
hosted in Tekla on .NET Framework, such a folder is refused with `C# source plugins need the .NET 8 app; ...\Stairs was not loaded.`

```
Plugins\Stairs\
    Stairs.csproj
    StairsCommands.cs
    Geometry\Steps.cs          (any depth)
    plugin.json                (optional, as for a .dll)
```

- **The folder** holds exactly one `.csproj` directly in it, and the plugin is named after the folder (or its `plugin.json`).
  Two `.csproj` files are refused with `<folder> holds 2 .csproj files: a plugin folder holds one.` Whatever `.dll` files lie
  in the folder, it is a source plugin: they are only the libraries the `.csproj` names.
- **The sources** are every `.cs` below the folder, as the SDK takes them: but `bin\` and `obj\` of the folder, and what
  `<Compile Remove="...">` takes out (`*`, `**` and `?` globs, relative to the folder).
- **A lone `.cs`** (in `Plugins`, or given to `PLUGINLOAD`) is not a plugin: it is refused with
  `Stairs.cs is a lone source file: a C# plugin is a folder with a .csproj. Put it in Plugins\Stairs\ with Stairs.csproj (see the plugin guide).`
  A folder of `.cs` files with no `.csproj` is refused the same way.

They load as a .dll does: from the `Plugins` folder at every start (the folders with a `.csproj`, a .dll or a `plugin.json`,
after the .dll files), with `PLUGINLOAD` (a folder, or its `.csproj`), from the startup list, and again with
`PLUGINRELOAD`, which compiles the edited source. The trusted folders and the question about a folder that is not trusted
are the same, and the question comes before anything is compiled.

```csharp
using System.Collections.Generic;
using OpenCAD2D;
using OpenCAD2D.Plugins;

namespace Stairs
{
    public sealed class StairsCommands
    {
        [CadCommand("STAIRS")]
        public IEnumerable<CadPrompt> Draw(CadCommandContext c)
        {
            c.Write("Steps: " + (int)System.Math.Ceiling(3000.0 / 175.0)); // Steps: 18
            yield break;
        }
    }
}
```

The `.csproj` is the smallest a Visual Studio user writes, and Visual Studio builds the same folder:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net8.0-windows</TargetFramework>
        <UseWPF>false</UseWPF>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="OpenCAD2D.Core" Version="1.0.0" />
    </ItemGroup>
</Project>
```

Saved as `Plugins\Stairs\Stairs.csproj` and `Plugins\Stairs\StairsCommands.cs`, it loads at the next start with
`Loaded Stairs 1.0.0: STAIRS.`, and `STAIRS` writes `Steps: 18`. The app ships it there, as the folder `examples/Stairs` of
the repository (see [the sample plugin](#the-sample-plugin)). The version is the `.csproj`'s `Version` (1.0.0 where it has
none), written into the assembly as the SDK writes it.

**What the `.csproj` says.** It is read with no MSBuild, and these are honoured:

| In the `.csproj` | What the app does |
|---|---|
| `AssemblyName` | the assembly's name; the name of the `.csproj` without its extension where there is none |
| `Version` | the assembly version, file version and informational version (`1.2.3-beta.1` gives `1.2.3.0` and `1.2.3-beta.1`); `GenerateAssemblyInfo` false leaves them out, and a source with its own `[assembly: AssemblyVersion]` then does not clash |
| `LangVersion` | `latest` (the default), `default`, `preview`, `13.0`, `7.3`...; one the compiler does not know is an error of its line |
| `Nullable` | `enable`, `disable` (the default), `warnings` or `annotations` |
| `ImplicitUsings`, `UseWPF`, `UseWindowsForms` | the SDK's global usings: `System`, `System.Collections.Generic`, `System.IO`, `System.Linq`, `System.Net.Http`, `System.Threading` and `System.Threading.Tasks`, less `System.IO` and `System.Net.Http` for `UseWPF`, plus `System.Drawing` and `System.Windows.Forms` for `UseWindowsForms` |
| `<Using Include="...">`, `<Using Remove="...">` | a global using; `Static="true"` and `Alias="..."` as in the SDK; `Remove` takes out an implicit using (`System.Net.Http`) and an `Include` of the same name before it |
| `DefineConstants`, `AllowUnsafeBlocks` | as in the SDK |
| `<PackageReference>` to `OpenCAD2D.Core`, `OpenCAD2D.Remote`, `OpenCAD2D.Wpf` or `GeometryHelper` | the app's own: the plugin is built against the app's copy and nothing is downloaded, never asked of nuget.org. A version above the app's gives a line and the compile goes on against the app's: `Stairs was written for OpenCAD2D.Core 1.2.0; this app has 1.0.0.` |
| any other `<PackageReference>` | a NuGet package, restored from nuget.org after asking (see *NuGet packages* below); one with `PrivateAssets="all"`, or an `IncludeAssets` with none of `compile`, `runtime` and `all` (an analyzer, `CSharpier.MsBuild`), is for the build alone and is skipped |
| `<Reference Include="...">` | a name of the app's own (the four above, or an assembly of .NET or WPF) is the app's, and its `HintPath` is ignored, so a project that points at `C:\Program Files\OpenCAD2D\OpenCAD2D.Core.dll` loads unchanged; any other needs a `HintPath` to a .dll inside the plugin's folder |
| `<Compile Remove="...">` | takes files out of the sources |

The preprocessor symbols are the SDK's for `net8.0-windows` in Release (`TRACE`, `RELEASE`, `NET`, `NET8_0`, `NETCOREAPP`,
`WINDOWS`, `WINDOWS7_0`, and every `NETx_OR_GREATER`, `NETCOREAPPx_OR_GREATER` and `WINDOWS7_0_OR_GREATER` up to .NET 8),
then the project's own `DefineConstants`. Warnings of the compiler are not shown.

**What it does not say.** Each of these is a line of the load log, and the plugin loads:

- an element with a `Condition` (a property, an item, or a whole group) is skipped:
  `Stairs.csproj: <PropertyGroup Condition="'$(Configuration)' == 'Debug'"> was not read (conditions are not evaluated).`
  (only where it holds something above; one that holds only a `Description` is passed over without a word);
- `Directory.Build.props`, `Directory.Build.targets` and `Directory.Packages.props` in the plugin's folder are not read:
  `Stairs.csproj: Directory.Build.props is not read.`;
- a value with `$(...)` in it is taken as written: `Stairs.csproj: <AssemblyName> holds $(...), which is not expanded; '$(MSBuildProjectName)Tool' is taken as written.`;
- a `TargetFramework` that is not `net8.0`, `net8.0-windows...` or `netstandard2.x`: `Stairs.csproj: TargetFramework net6.0 is not net8.0 or netstandard2.x; the app compiles for its own .NET 8.`

**What it cannot take.** The plugin is not loaded, and each is one error line in the form of a compiler's,
`Stairs.csproj(7,10): error OC0002: ...`: `EnableDefaultCompileItems` false, a `<Compile Include>` naming a file outside the
folder, a `<ProjectReference>`, a `<PackageReference>` with no version, a `<Reference>` with no `HintPath`, and a
`<Reference>` whose `HintPath` is outside the folder or not a .dll (`error OC0001: <Reference> takes a .dll inside the tool's folder: '..\x.dll'`);
one whose .dll is missing is `error CS0006: Metadata file 'libs\x.dll' could not be found`. A file that is not an
SDK-style project (`<Project Sdk="...">`) is refused too.

**What the source can use.** The .NET 8 runtime the app runs on, WPF, `GeometryHelper`, `OpenCAD2D.Core` and
`OpenCAD2D.Wpf`, as a .dll plugin can, the libraries the `.csproj` names and the NuGet packages it asks for.

**NuGet packages.** A `<PackageReference Include="Foo" Version="1.2.0" />` that is not the app's own is a package of nuget.org. The app restores
it with no .NET SDK, as `dotnet restore` does, and compiles and runs the plugin against it.

- **The version** is the lowest the source has that meets the reference, never the newest: `Version="13.0.3"` means 13.0.3 or
  higher, so it is 13.0.3 where nuget.org has it, on every computer. The dependencies of the version chosen are taken for
  `net8.0-windows` the same way, and a package met twice (two packages that both need `Foo`) is taken at the higher of the
  two lowest versions. Then every range is checked, and what no version meets is refused with a line of the
  `.csproj`'s, in the form of a compiler's:
  `Stairs.csproj(6,10): error NU1107: Stairs needs Foo [2.0.0, 3.0.0); Bar 1.0.0 needs Foo [1.0.0, 2.0.0); no version meets both.`
  A reference lower than what another package needs is refused as the SDK refuses it (NU1605):
  `Stairs asks Foo 1.0.0, and Bar 2.0.0 needs Foo 1.2.0 or higher.` A version or package the source lacks is NU1102 or
  NU1101. A floating range, `Version="1.*"`, takes the highest 1.x, once: the line
  `Stairs.csproj: Foo 1.* is a range: Foo 1.4.2 was chosen and stays until the .csproj changes.` says which, and the choice is
  kept (PLUGINRELOAD does not choose again) until the `.csproj` changes.
- **The assets** are the nearest framework's, as Visual Studio picks them: `Newtonsoft.Json` 13.0.3 gives `lib/net6.0`, not
  `lib/netstandard2.0`; a package with only `lib/net48` is refused (`Foo 1.0.0 has nothing for net8.0-windows7.0 (it has net48).`).
  The plugin compiles against `ref/` where a package has it, else `lib/`.
- **Asking.** Nothing is downloaded without a yes. Before the first download of a plugin's packages, and again when its
  list changes, the app lists them (the plugin's own references first, then the rest, each in order of the id, those
  already in the cache marked) and asks to download them from nuget.org. The app asks with a window, **Packages for
  Stairs**, over the main window: one sentence that names the plugin and its `.csproj` and counts the packages still to
  download, a table (Package, Version, State) with the plugin's own packages first, in bold, then what they need, each State
  `download` (blue) or `in the cache`, the sentence that loading runs the plugin's code and its packages' code with your
  rights and that the packages are kept in `%LocalAppData%\OpenCAD2D\NuGet`, and at the foot `From nuget.org` with the
  buttons **Don't load** and **Download and load**. Esc and the close button are Don't load. The restore itself runs on the
  window's thread, as the compile does, so the window waits for it as it waits for a compile. With `FILEDIA` 0 (as AutoCAD's
  commands fall back to the command line) a typed `PLUGINLOAD`, which then asks its path on the command line, and `PLUGINRELOAD`
  ask on the command line instead of with the window: the load stops before anything is downloaded, the packages still to
  download are written, one to a line (`Stairs needs these packages from nuget.org:`, then `  Newtonsoft.Json 13.0.3`, and
  `  Foo 1.0.0 (needed by another)` for one the `.csproj` does not name), and `Download these packages? [Yes/No] <No>:` is
  asked. Yes downloads them and loads the plugin; No or Enter leave it not loaded, with the same badge **needs packages**
  in Load/Unload Applications as the window's Don't load; Esc cancels the command and leaves the plugin as it was. A load with
  `FILEDIA` 1 asks the window, as before. A no leaves the plugin not loaded
  (`Stairs was not loaded: its packages were not downloaded.`), and so does a host that has no way to ask. No question is put
  where every package is in the cache already.
- **Offline.** A source that cannot be reached, or a download that fails, leaves the plugin not loaded:
  `Stairs was not loaded: nuget.org could not be reached (<why>). Connect and run PLUGINRELOAD Stairs.` A package cut short
  does not count: a package is in the cache once its `.sha512` file is written, after everything else.
  A connection to the source is given up after 5 s, so the line comes after about 5 s even where a firewall drops the
  packets and nothing answers (5 s in two runs, to an address no router carries; Windows alone waits 42.8 s over its two tries).
- **Where.** The packages are extracted into `%LocalAppData%\OpenCAD2D\NuGet\packages` in the layout of NuGet's global-packages
  folder (`<id>\<version>\`), shared by every plugin, and their signatures are checked as `dotnet restore` checks them. The
  source is nuget.org only: no `nuget.config` is read. The list a plugin resolved to (ids, versions, the SHA-512 of each
  package, the files used) is kept in `%LocalAppData%\OpenCAD2D\PluginCache\<plugin>\packages.<key>.json`, the key the
  SHA-256 of the `.csproj`: a start with an unchanged `.csproj` and every package in the cache reads it and asks nothing of
  nuget.org, so the 1 to 3 s of version lists are not spent again. The packages' ids, versions and SHA-512 are part of the
  build's cache key.
- **Two versions of one package.** Each plugin has a load context of its own and loads a package's assembly from the
  cache folder in place, after its own folder, so a plugin with `Foo 1.0.0` and another with `Foo 2.0.0` both run, each with
  its own. This holds for a library the app does not carry itself. A name the app has (the .NET runtime and its libraries,
  WPF, SkiaSharp, Roslyn's and NuGet's own, `Newtonsoft.Json` among them) is **shared**: the app's copy is compiled against and
  runs (also where the app's copy lies in the app's folder and in no .NET folder: a plugin that asks `Newtonsoft.Json` 13.0.3
  compiles `JsonConvert.SerializeObject(5)` against the app's own `Newtonsoft.Json.dll`), and a package that asks a higher assembly version gets a line: `Stairs asks System.Text.Json 9.0.0; the app runs 8.0.0.`
- **Native files.** A package's `runtimes\win-x64\native\*.dll` (or `runtimes\win\native`) is loaded by its full path in the
  cache folder when the plugin's code asks for it by name. .NET cannot unload a native library, so it stays loaded until the
  app exits, also after the plugin is unloaded. Two plugins with two different native files of one name (`foo.dll`) load
  side by side and each calls its own: the second plugin's call reaches its own file, not the first's (tested with two files
  of Windows given the one name). So PLUGINRELOAD with a new native version loads the new file beside the old one, which
  stays in memory until the app is closed.
- **Size.** The NuGet client in the app is `NuGet.Protocol` 7.9.0 (Apache-2.0), the last that targets `net8.0`, with its
  libraries and `Newtonsoft.Json` (the runtime of the app carries `System.Security.Cryptography.Pkcs` already): 7 files and
  3 060 832 bytes (2.9 MB) more in the app's folder ([Installing with the setup](app.md#installing-with-the-setup) has
  the whole setup). Its notice is in `THIRD-PARTY-NOTICES.md`, which the setup installs beside the app.

**The cache.** The build is kept in `%LocalAppData%\OpenCAD2D\PluginCache\<plugin>`, named by the SHA-256 of the `.csproj`,
each source's path from the folder and bytes, the referenced libraries, each package's id, version and SHA-512, the app's version and the runtime's version. A
start with unchanged files takes it from there and does not load the compiler at all; a change of one byte, even in the
`.csproj`, compiles again, and the build it replaces is deleted. The first compile of a session takes about 0.9 s (the
compiler's own start: a plugin of two files, 220 lines, measured on .NET 8.0.28), a compile after that about 20 ms. The PDB
holds the sources, so a stack trace names the file and the line.

**Errors.** A source that does not compile is not loaded, and the command line says why, a line per error, the first 20
by file, line and column, a file below the folder by its path from it (`Geometry\Steps.cs(5,81)`); warnings of the compiler
are not shown:

```
Plugin Stairs was not loaded: 1 error
Stairs.cs(12,9): error CS0103: The name 'x' does not exist in the current context
```

There is no file watcher: after saving a change, `PLUGINRELOAD` takes it.

**In the window.** [Load/Unload Applications](lisp.md#appload) lists a source plugin with the Kind `C# source`, in light
purple, and counts it in a chip of its own, `C# source 1`, after `Plugins`, which counts the .dll plugins only. A
`.csproj` loaded with Load… or dropped on the table loads its folder, and so does **Load folder…**, which asks for the
folder with a folder dialog (a folder with a `requirements.txt` is a [Python tool](python.md#where-tools-load-from) and
loads as one). The Open dialog of Load… and of NETLOAD offers `C# plugin (*.csproj)`; a lone `.cs` has no file type of its
own, and one chosen with All files is refused with the line that says how to make it a folder. A source that does not compile is not loaded (a
reload leaves its row unticked), and the red line under the table reads
**Stairs.csproj** `did not compile: 2 errors. Stairs.cs(12,9): error CS0103: ...` with `Every error is on the command line`
on the right; its tooltip holds the whole message.

A plugin held back by its packages keeps its row, unticked, with a badge after its name. **needs packages** (amber) is a
plugin whose question was answered No: ticking its row asks again, and Yes loads it. **install failed** (red) is a plugin
whose packages could not be restored (nuget.org could not be reached, or NU1101, NU1102, NU1107 and the like): the red line
reads **Stairs** `was not installed: ` and the first error, with `Every line is on the command line` on the right, and the
tooltip holds the whole message; ticking the row tries again. A plugin that loads has no badge, and Remove takes the row
out. The badge is kept for the session; a row remembered from an earlier session has none, and ticking it asks as ever.

## Loading and unloading

[`CadPluginManager`](xref:OpenCAD2D.Plugins.CadPluginManager) keeps the plugins of a presenter. Its commands
are the ones AutoCAD users know:

| Command | Alias | Does |
|---|---|---|
| `PLUGINLOAD` | `NETLOAD` | loads a .dll, a .csproj or the folder of a plugin, for this session |
| `PLUGINUNLOAD` | | unloads a plugin by name: its commands go from every drawing |
| `PLUGINRELOAD` | | loads it again from its file: change the code, build, reload, without restarting |

The window's PLUGINLOAD shows a file dialog; the manager's own, which `Attach(registry)` adds to a command line, asks for a
path on the line. `SetLoadAtStartup(name, true)` puts a plugin on the startup list, which a plugin of the `Plugins` folder
needs not, as it loads at every start anyway; `LoadAtStart()` loads each .dll and each folder of the plugin folder, in the
order [above](#where-a-plugin-lives), then the startup list. `Remove(name)` forgets a plugin that is not of the `Plugins` folder:
it is unloaded, taken out of `Plugins` and off the startup list, and Remove of Load/Unload Applications calls it. A plugin of the
`Plugins` folder is taken out of the app by deleting its file or folder: there is no install or uninstall command.

**Trusted folders.** Loading a plugin runs its code with the user's rights, so, as AutoCAD's TRUSTEDPATHS does, a file
outside `TrustedFolders` is loaded only when `ConfirmUntrusted` says yes, and never where it is not set. The plugin folder (`Plugins` beside the app)
is always trusted (`AddTrustedFolder`, `RemoveTrustedFolder`, `IsTrusted`). Without a confirmation, `Load` gives
`...CircleTools.dll is outside the trusted folders, and loading it was not confirmed.`; with one, `Loaded Circle tools 1.0.0:
NUMCIRCLES.` Every load is a line of `Log`, with the full path of the file and whether it was trusted. A yes to a folder outside the trusted ones holds for that folder for the life of the manager, so a reload does not ask again; it is never written to disk, and a no is asked again. Load/Unload Applications has no page for the trusted folders: a .dll from anywhere but `Plugins` is asked about at its load by the question above.

**A plugin loaded twice.** `Load` of a plugin already loaded is refused: `The plugin Circle tools is loaded already, from
...; PLUGINRELOAD loads it again.`

**The shadow copy.** The manager copies a plugin's files to a folder of its own, `ShadowFolder` (under the temporary
folder, one for each process, those of processes that have ended deleted at the next start), and loads the copies, so the
.dll its author builds is never locked: build again, and PLUGINRELOAD picks it up.

**What unloading can do** depends on the runtime, and the loader given to the manager is what differs. The window gives
[`CadPluginLoader`](xref:OpenCAD2D.Plugins.CadPluginLoader)s of its own (`CadPluginAssemblyLoader`):

| Runtime | Loading | `PLUGINUNLOAD` |
|---|---|---|
| .NET 8, the standalone app | each plugin into a collectible load context of its own, so two plugins' dependencies do not clash | frees the code; a new build is taken without a restart |
| .NET Framework, hosted in Tekla | every plugin into the one AppDomain | only switches it off: `Switched Circle tools off: its commands are gone, and its code stays loaded until the presenter restarts.` A reload says the build loaded first runs |

The presenter's own assemblies (`GeometryHelper`, `OpenCAD2D` and the controls) are never loaded from a
plugin's folder, on either runtime. The manager's files are `plugins.json`, the startup list and the trusted folders; the
app keeps them in a folder of its own, OpenCAD2D in `%AppData%\OpenCAD2D`; the `Plugins` folder is not
there but beside the app, so that every user of an install has the same plugins. A plugin that earlier versions installed
into `%AppData%\OpenCAD2D.Core\Plugins` is not loaded now: move its folder to `Plugins` beside the app.

## The sample plugin

Three sample plugins ship in the `Plugins` folder, each the C# source of a plugin, a folder with a `.csproj` and one `.cs` file
(see [a plugin as C# source](#a-plugin-as-c-source)): `Plugins\HelloWorld`, the smallest plugin there can be;
`Plugins\DrawingInfo`, which reads the drawing; and `Plugins\Stairs`, [the guide's example](#a-plugin-as-c-source). They are
the folders `examples/HelloWorld`, `examples/DrawingInfo` and `examples/Stairs` of the repository, which the build of
`OpenCAD2D.App` copies beside the exe, and each opens in Visual Studio as a project of its own: its `.csproj` asks for the
`OpenCAD2D.Core` package, which builds it there, and the app builds it by itself. Each is one plugin, named after its folder,
with one command, and each can be removed by deleting its folder; to start a plugin of your own, copy one under another
name.

**HelloWorld.** The command `HELLOWORLD` writes one line on the command line and changes nothing:

```
Command: HELLOWORLD
Hello, World!
```

Its `HelloWorldCommands.cs` is a class with one method marked `[CadCommand("HELLOWORLD")]` that calls
`c.Write("Hello, World!")` and returns no prompt.

**DrawingInfo.** The command `GHDWGINFO` reads the drawing and writes what it holds on the command line, and changes nothing.
It is the smallest plugin that does something useful, one class with one command that takes no prompt, in the one file
`DrawingInfoCommands.cs`. The app loads it at every start with no question, and its command is typed at the command line,
as every command is. A drawing of three circles and a line in model space, with one layer and no block, gives

```
Command: GHDWGINFO
Drawing: circles.dxf
Model space: 4 objects
  Circle   3
  Line     1
Layers: 1, blocks: 0
Extents: (-10.00,-40.00) to (110.00,30.00)
```

The folder holds `DrawingInfo.csproj` and `DrawingInfoCommands.cs`: it has no `plugin.json`, so the folder gives the plugin
its name, DrawingInfo, and the `.csproj` its version, 1.0.0. It once shipped as a built .dll,
`OpenCAD2D.Plugins.DrawingInfo.dll`; the command is the same, and a .dll left in the folder is not read once a `.csproj` is
there.

**Stairs.** The folder `Plugins\Stairs` holds `Stairs.csproj` and `StairsCommands.cs`, the files of the guide's example, whose
command `STAIRS` writes `Steps: 18`.

**What the first start costs.** The app compiles the three samples at the first start after an install or an upgrade, and
keeps the builds in [the cache](#a-plugin-as-c-source), so later starts compile nothing: the cache key holds the app's version,
which an upgrade changes. Measured on the .NET 8 app, with the cache in a folder of its own, the three samples together take
1.09 s to 1.34 s at the first start (1.09 s, 1.12 s, 1.13 s, 1.34 s in four runs, most of it the compiler's own start) and 61 ms to 82 ms at every start after it (61, 65, 66, 82 ms), where
the three plugins are loaded from the cache.

## What the window adds

The window has no ribbon tab of its own for plugins and no button per command: a plugin's commands are typed at the command
line, or started by name. A plugin is loaded from the Applications panel of the Manage tab: Load Plugin runs NETLOAD, the
Open dialog for a `.dll` or a `.csproj`, and Load Manager opens [Load/Unload Applications](lisp.md#appload) (APPLOAD: a table of the
plugins and the AutoLISP files, with On, Commands, File and At start, Load…, Reload, Unload and Remove), with a dialog before a load
outside the trusted folders. The plugin's load
lines go to the command history, and a plugin that fails to load is also told by the red [load-result line](lisp.md#the-load-result-line) of that window. A plugin's `CadSetting` attributes remain in the plugin API, but the window has no page
that shows them now.

A command that works long shows it on the status bar's progress meter through `c.Progress`:
`using (var meter = c.Progress.Start("Checking blocks")) { meter.SetLimit(n); ... meter.MeterProgress(); }`, as
[the app](app.md#the-progress-meter) describes.

## What a plugin adds to the window

A plugin may add to the window what AutoCAD's plugins add to theirs: ribbon tabs and panels, rows of PROPERTIES, buttons
and items of the DRAWINGS list, items of the canvas's right-click menu, and object snaps of its own. It describes them
with the types of `OpenCAD2D.Plugins`, none of them WPF's, so the same .dll runs on .NET 8 and .NET Framework;
the window draws them in its own look and takes them away when the plugin is unloaded.

A plugin tab's panels shrink as the window narrows, as the built-in ones do: right-most first, each panel to its standard
items' icons alone (large items keep their text), then each collapsed to one button with its title and the icon of its
first item, whose drop-down runs its buttons and closes after a click unless pinned. A panel may hold
`SlideOutItems` (laid out as its `Items`, opened by its title "Number ▾"), a `DialogLauncher` (the small arrow at the
right of its title; its tooltip is the button's, grey when `CanRun` is false) and macro buttons made with
`CadRibbonButton.FromMacro("Circle R10", @"^C^C_CIRCLE;\10;")`, which run as AutoCAD's CUI macros: `^C` cancels, `;`, a
space and `^M` are Enter, `\` waits for the user, `_` and `.` as typed, `(...)` is AutoLISP; `*`, `$M=` and the other
`^` pairs are refused with a line saying so. The coordinate rows of a point made with `CadPropertyRow.Point` and a setter
show a pick button on the row being edited: `Pick a point in the drawing:`, a rubber band from the point, the setter
called once with the point picked and the row's Z.

A public class that implements [`ICadPluginApplication`](xref:OpenCAD2D.Plugins.ICadPluginApplication), as
AutoCAD's `IExtensionApplication`, is made once when the plugin is loaded and given a
[`CadPluginHost`](xref:OpenCAD2D.Plugins.CadPluginHost) in `Initialize`; `Terminate` is called when the plugin
is unloaded or reloaded, and when the manager is disposed. What `Initialize` adds is all the plugin adds: an `Add...`
call after it has returned throws. A class that also holds commands uses the same object for both, and a plugin with no
command but such a class is loaded. The sample below, cut short from the test plugin `OpenCAD2D.SampleUiPlugin`,
adds a tab, a group of Properties, a DRAWINGS button and a snap:

```csharp
[assembly: CadPluginApi("1.1")]

public sealed class CircleUi : ICadPluginApplication
{
    public void Initialize(CadPluginHost host)
    {
        var number = new CadRibbonPanel("Number");
        number.Items.Add(new CadRibbonButton("Number circles", "NUMCIRCLES2")
        {
            Size = CadRibbonItemSize.Large,
            Icon = CadPluginIcon.FromPathData("M4 6h16M4 12h16M4 18h10"),
            Description = "Writes a number at the centre of each circle selected.",
        });
        number.Items.Add(new CadRibbonButton("Count", c => c.Write(Count(c.Document) + " circles."))
        {
            CanRun = c => c.Document != null,
        });
        var tab = new CadRibbonTab("Circles", "Circles");
        tab.Panels.Add(number);
        host.AddRibbonTab(tab);

        host.AddProperties(Describe);
        host.AddDrawingsButton(new CadPluginButton(CadPluginIcon.FromPathData("M4 12H20M12 4V20"), "Count the circles", Count));

        var top = new CadCustomSnap("Circle top", "CTOP", r =>
            r.Entity is DxfCircle circle
                ? new[] { new GeoPoint2(circle.Center.X, circle.Center.Y + circle.Radius) }
                : null);
        top.EntityTypes.Add(typeof(DxfCircle));
        host.AddObjectSnap(top);
    }

    public void Terminate() { }
}
```

An `Initialize` that throws leaves the plugin's commands loaded and everything it added out, with a line in
`CadPlugin.Problems` and a Warn in the log.

**Ribbon.** [`CadRibbonTab`](xref:OpenCAD2D.Plugins.CadRibbonTab)s come after the Plugins tab, in the order the
plugins were loaded; `AddRibbonPanel` puts a panel on the Plugins tab instead, which shows only while a plugin has added a
panel and holds nothing but those panels, plugin by plugin. The presenter has no "Command plugins" or "Manage" panel any more,
so `ShowsCommandButtons` changes nothing. A panel's items are buttons and split
buttons. A `Large` item has its icon in a slot of 32 pixels over its text, alone in its column; `Standard` items have a
slot of 16 beside their text, or the icon alone where `ShowText` is false, and stand three to a column, as AutoCAD's row
panels do. An icon is a path on a square of 24 drawn 2 wide like the presenter's own (`FromPathData`), or a PNG, BMP,
JPEG, GIF or ICO from bytes, an embedded resource or a file by its full path (`FromImage`, `FromResource`, `FromFile`,
`Path.Combine(host.Folder, "icons", "count.png")`): its bytes are copied when it is made, so no file stays locked. An icon
the window cannot draw shows the plugin icon. A button runs a command, started as the presenter's ribbon buttons start
theirs, or a callback given a [`CadPluginContext`](xref:OpenCAD2D.Plugins.CadPluginContext) (the document, the
handles selected, `Write`, `RunCommand`); `CanRun` greys it, asked as the selection, the drawing and the command change.
A callback that throws writes its message on the command line after the plugin's name. A split button shows and runs the
one used last, as AutoCAD's Circle button does. A tab with `ShowWhen` is contextual: shown in green after the presenter's
contextual tabs while it says true, as "Circle" while only circles are selected, and taken up when it shows.

**Properties.** A [`CadPropertyProvider`](xref:OpenCAD2D.Plugins.CadPropertyProvider) is asked, after the
extensions, with the document, the handles shown and what Properties shows, and returns what it shows, usually the same
groups and one of its own. Its rows are the extension's: a setter returns null where it made the change and the words of
the refusal otherwise. `CadPropertyRow` makes rows of a kind: `Number`, `Length` and `Angle` (written and read in the
drawing's units), `Point` (three rows, "Center X", "Center Y" and "Center Z"), `Color`, `Layer`, `Linetype` and
`Lineweight`, the last four drop-downs of the window's own lists. Text that is not one is refused in the presenter's words
before the setter is called: "Diameter is a number." A setter edits the drawing through `CadDocument.ApplyEdit`, one undo
step a change.

**DRAWINGS and the canvas.** `AddDrawingsButton` puts an icon button first in the header of the list;
`AddDrawingsMenu` adds items after a separator at the end of a file row's right-click menu, given the row's path, its
document where it is open and the rows selected; `AddCanvasMenu` adds items to the canvas's menu for the objects
selected, after the extensions' and before the presenter's own. [`CadPluginMenuItem`](xref:OpenCAD2D.Plugins.CadPluginMenuItem)
makes an item, a grey or ticked one, a submenu and a separator.

**Object snaps.** A [`CadCustomSnap`](xref:OpenCAD2D.Plugins.CadCustomSnap), as AutoCAD's
`CustomObjectSnapMode`, has a name, a keyword, the entity types it applies to (all where none is given), a glyph drawn
as a snap marker (a square with a dot where none is given) and a finder that gives its points for an entity under the
aperture. It is listed in the Object Snap tab of Drafting Settings under "Plugin snaps", in the OSNAP menu and in the
Shift+right-click menu, and is on while OSNAP is, as the built-in snaps are; the choice is kept in the settings by its
keyword (`GlobalSettings.PluginSnaps`), and a snap never chosen starts as its `IsOnByDefault`, off unless set. Its keyword
typed at a point prompt, `CTOP` or `_ctop`, takes it for the next point alone: "Specify next point: _ctop of". A keyword is
2 to 16 letters and digits, and one that is or starts with one of AutoCAD's snap keywords (END, MID, CEN and the rest), or
is FROM, TT, M2P or MTP, is refused. A point of a plugin's snap competes with the built-in snaps by distance, the built-in
one taken at a tie: "Circle top" is the circle's 90 degree quadrant, so it shows with Quadrant off, or typed. A finder that
throws switches its snap off with one line, `The snap CTOP of Circle tools failed and is off: ...`, until it is chosen on
again. A snap is asked about model space seen through a viewport of a layout (in model coordinates, layers frozen in the
viewport left out), about the entities inside blocks under the aperture (each as a copy placed where the references draw
it, up to eight levels, `CadSnapRequest.BlockReferences` naming the references outermost first; external references and
arrays are not gone into), and about a point typed after its keyword (the entities within the aperture of the typed
point, the nearest point taken however far; `No Circle top found for specified point.` where there is none).

Two plugins that add a tab of the same id, or snaps of the same keyword, keep the first loaded; the second's is left out
with a line in its `Problems`. After PLUGINUNLOAD the window shows Home where the plugin's tab was shown, and its panels,
rows, buttons, items and snaps are gone; on .NET 8 the plugin's code is freed with them.

## The editor: the view, selection and variables

A command, a ribbon callback and a plugin test reach the drawing's [`CadEditor`](xref:OpenCAD2D.CadEditor) the way an AutoCAD
plugin reaches `Editor`: `c.Editor` in a command (`CadCommandContext.Editor`), `context.Editor` in a callback
(`CadPluginContext.Editor`, null where no drawing is open) and `session.Editor` for a `CadCommandSession` of your own. A
plugin that uses it states `[assembly: CadPluginApi("1.3")]`. Each call does what the command of its name does, through the
code the command runs, so what it shows is what the typed command shows and U takes it back the same way.

| AutoCAD | `CadEditor` | The command it is |
|---|---|---|
| COM `ZoomExtents()`, `ZoomAll()` | `ZoomExtents()` | ZOOM Extents (ZOOM All is the same here) |
| COM `ZoomWindow(ll, ur)` | `ZoomWindow(GeoPoint2, GeoPoint2)` | ZOOM Window |
| `ZoomObjects(handles)` (no AutoCAD call) | `ZoomObjects(IEnumerable<string>)` | ZOOM Object |
| COM `ZoomCenter(center, magnify)` | `ZoomCenter(GeoPoint2, double height)` | ZOOM Center |
| COM `ZoomScaled(scale, relative)` | `ZoomScaled(double)` | ZOOM nX |
| COM `ZoomPrevious()` | `bool ZoomPrevious()` | ZOOM Previous |
| -PAN | `Pan(GeoVector2)` | -PAN, the drawing moves by the displacement |
| `Editor.Regen()` | `Regen()` | REGEN without its line |
| `Editor.GetCurrentView()`, `SetCurrentView` | `DisplayBox GetCurrentView()`, `SetCurrentView(DisplayBox)` | the box the canvas shows; ZOOM Window with no margin |
| `ViewTable`, -VIEW | `NamedViews`, `SaveView`, `RestoreView`, `DeleteView` | -VIEW ?, Save, Restore, Delete |
| `Editor.SelectAll` | `SelectAll(Func<DxfEntity, bool> filter = null)` | "Select objects:" All |
| `Editor.SelectWindow`, `SelectCrossingWindow` | `SelectWindow(p1, p2)`, `SelectCrossingWindow(p1, p2)` | "Select objects:" Window, Crossing |
| `GetSystemVariable`, `SetSystemVariable` | `GetVariable(name)`, `SetVariable(name, value)` | getvar, setvar |

Points are of the world (WCS), not of the UCS, as AutoCAD's .NET API takes them, and in the units of the drawing. A drawing
shown in the window is zoomed on the canvas; a session with no window (a test of your plugin) has a view of its own, 942
by 534 pixels showing 10 units of height about the origin, which the view calls work on, and `HasWindow` says false. On a
drawing with a line from (0, 0) to (100, 0) and a circle about (50, 50) of radius 25, that view after `ZoomExtents()` shows
(-23.502, -4.167) to (123.502, 79.167); after `ZoomWindow(new GeoPoint2(0, 0), new GeoPoint2(10, 10))`, (-3.820, 0) to
(13.820, 10); then `Pan(new GeoVector2(10, 0))` shows (-13.820, 0) to (3.820, 10); `ZoomCenter(new GeoPoint2(50, 50), 20)`
shows (32.360, 40) to (67.640, 60).

```csharp
[CadCommand("ZOOMTOPLAN", Description = "Zoom to the plan and save the view")]
public IEnumerable<CadPrompt> ZoomToPlan(CadCommandContext c)
{
    c.Editor.ZoomWindow(new GeoPoint2(0, 0), new GeoPoint2(4000, 3000));
    c.Editor.SaveView("Plan");
    c.Editor.SetVariable("LTSCALE", 20.0);
    yield break;
}
```

- **Undo.** Inside a running command the call joins the command's undo step (the command above is one step, `ZOOMTOPLAN`);
  at rest each call is a step of its own named as the command is: `ZOOM`, `PAN`, `VIEW` (`SaveView`, `RestoreView`,
  `DeleteView`) or `SETVAR` (a variable the drawing keeps). `ZoomPrevious()` goes back through the same list ZOOM Previous
  does, 20 deep, and says false, writing nothing, where there is none.
- **Through a viewport** (MSPACE on a layout) `ZoomExtents`, `ZoomWindow`, `ZoomScaled`, `ZoomPrevious` and `Pan` change the
  viewport's view, as ZOOM and -PAN do there; `ZoomCenter` and `ZoomObjects` throw `InvalidOperationException`, as ZOOM does
  not offer them there. `ZoomObjects` is ZOOM Object (a margin of 5 %, the space shown only), not the Find dialog's zoom.
- **Selection** gives handles in the order the space lists its objects and leaves the pickfirst selection as it was:
  `c.Document.Selection.Set(handles)` makes the result the selection. `SelectAll` takes the working space's objects, the
  first viewport of a layout left out; `SelectWindow` the objects wholly inside and `SelectCrossingWindow` those it crosses,
  held to the view where the session has a window, as the same options are. Objects on locked layers are included. Five lines
  at y 0, 10, 20, 30 and 40 give 10, 20 and 30 for the window (-1, 5) to (101, 35), and 20 for the crossing (50, 15) to (60, 25).
- **Variables** are `getvar` and `setvar`: an `int`, a `double` (an angle in radians), a `string` or a `double[]` of two or
  three coordinates. An unknown name, a read only variable or a value the variable refuses throws `ArgumentException`:
  `The system variable NOSUCH is not known.`, `The system variable EXTMIN is read only.`, `The value 7 is refused for LUNITS.`
- **Named views** are the views of the drawing as -VIEW ? lists them; `SaveView` saves the view on screen with no layer
  snapshot, a name that is a view's already redefines it, and `RestoreView` and `DeleteView` of a name the drawing has not
  throw `ArgumentException`.
- **The window** shows a view the editor moved at once, and a new canvas size no longer refits it, as it does not one moved by
  a typed ZOOM.

## The editor: edits

`CadEditor` also moves, copies, turns, scales, mirrors, offsets and erases objects, each as the command of its name does it
to the same objects, through the code the command runs: the drawing a call makes is the drawing the typed command makes,
which the tests compare group by group in the ENTITIES section.

| AutoCAD | `CadEditor` | The command it is |
|---|---|---|
| `Entity.TransformBy(Matrix3d.Displacement)` | `Move<T>(IEnumerable<T>, GeoVector2)` | MOVE, a displacement |
| `Entity.GetTransformedCopy` | `Copy<T>(IEnumerable<T>, GeoVector2)` | COPY, a displacement typed |
| `Entity.TransformBy(Matrix3d.Rotation)` | `Rotate<T>(IEnumerable<T>, GeoPoint2 basePoint, double angle)` | ROTATE |
| `Entity.TransformBy(Matrix3d.Scaling)` | `Scale<T>(IEnumerable<T>, GeoPoint2 basePoint, double factor)` | SCALE |
| `Entity.TransformBy(Matrix3d.Mirroring)` | `Mirror<T>(IEnumerable<T>, GeoPoint2, GeoPoint2, bool eraseSource = false)` | MIRROR |
| `Curve.GetOffsetCurves(distance)` | `Offset(DxfCurve, double distance, GeoPoint2 side)` | OFFSET, one pick |
| `Entity.Erase()` | `Erase(IEnumerable<DxfEntity>)` | ERASE |

The objects are the drawing's own (`c.Document.FindEntity(handle)`; from handles, `c.Selected.Select(c.Document.FindEntity)`),
and a call gives back what the type of its argument says: `Copy(lines, ...)` of an `IEnumerable<DxfLine>` is an
`IReadOnlyList<DxfLine>` of the copies, in the order given, and `Move`, `Rotate`, `Scale` and `Mirror(..., eraseSource: true)`
give the same objects, changed where they stand. A mirror keeps its sources (`eraseSource` false) as MIRROR's "Erase source
objects? <No>" does.

```csharp
[CadCommand("STACK", Description = "Copy the selection up 50 and turn the copies a quarter")]
public IEnumerable<CadPrompt> Stack(CadCommandContext c)
{
    foreach (CadPrompt prompt in c.SelectObjects())
    {
        yield return prompt;
    }

    IReadOnlyList<DxfEntity> picked = c.Selected.Select(c.Document.FindEntity).ToList();
    IReadOnlyList<DxfEntity> copies = c.Editor.Copy(picked, new GeoVector2(0, 50));
    c.Editor.Rotate(copies, new GeoPoint2(0, 0), Math.PI / 2.0);
}
```

On a line from (0, 0) to (100, 0), `Move` by (10, 5) leaves it from (10, 5) to (110, 5); `Rotate` about (0, 0) by pi / 2 ends it at
(0, 100); `Offset(line, 5, new GeoPoint2(50, 10))` adds a line from (0, 5) to (100, 5) on the line's layer; and a circle of radius 10
about (200, 0) is one of radius 20 about (400, 0) after `Scale` about (0, 0) by 2.

- **Refused whole.** An edit changes nothing and throws `InvalidOperationException` with the first object it will not take:
  one of another drawing, or one that an undo or a redo read back since you held it (`The object A1 is not in the drawing
  Plan.dxf.`: look it up again by its handle), an erased one, one inside a block, one on a locked layer (`The object A8 is on
  the locked layer Locked.`), or one whose owner does not allow the change (`This line belongs to the model and can only be
  moved.`; an owner that allows only moving still lets the object be copied and mirrored without erasing it, as the commands
  do). An empty list is allowed: it gives an empty list and records nothing; an object given twice is taken once.
- **Numbers.** A factor that is not above nought or not finite, a distance that is not above the session's `EqualPoint`, an
  angle or a displacement that is not finite throw `ArgumentOutOfRangeException` or `ArgumentException`; a mirror line whose two
  points are not more than `EqualPoint` apart, or so near that the library cannot give the line a direction (about 2E-5 mm),
  throws `ArgumentException`. The typed MIRROR says the same of the second point: `Invalid point: the mirror line is too short.`
  `EqualPoint` is read from the session at the call, never from `Tolerance.Global`.
- **Angles and points.** Radians, counter-clockwise about the world Z, whatever ANGDIR and ANGBASE say; the angle is taken from
  nought up to a whole turn as ROTATE takes it (-30 degrees is 330, 2 pi is none), which a hatch's pattern lines show.
  Points and displacements are of the world, not of the UCS.
- **Offset** takes one curve (a line, arc, circle, ellipse, polyline, spline, xline or ray: `ArgumentException`, `Cannot offset
  that object.` for any other), a distance and the point that names the side, as one pick of OFFSET with a typed distance: the
  copies go on the curve's layer and the curve is kept (its Erase, Layer and Through options are not taken). A point on the
  curve names no side and throws `ArgumentException`; an offset that makes no curve (a circle of radius 10 offset inward by 15)
  throws `InvalidOperationException`, `Cannot offset that object.`
- **Undo.** Inside a running command the edit joins the command's step; at rest each call is a step of its own named `MOVE`,
  `COPY`, `ROTATE`, `SCALE`, `MIRROR`, `OFFSET` or `ERASE`. The call writes nothing on the command line and leaves the
  pickfirst selection as it was.
- **Mirror and text.** A text, an attribute and an INSERT's attributes stay readable under MIRRTEXT 0 (the drawing's variable),
  a dimension and a multileader keep their plane, and a hatch keeps its pattern under MIRRHATCH 0 (the session's), as MIRROR
  does them.

## See also

- [Editing as AutoCAD does](editing.md): the command line, `CadCommand` and the undo steps a plugin runs in.
- [Owners](owners.md): why a plugin cannot change what a model drawing owns.
- [Settings, autosave and the window](app.md): the folders the plugins and the settings live in.
