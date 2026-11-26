# Driving the app from another program

Another program can read and edit the drawings of a running OpenCAD2D, run commands on its command line, ask
the user to pick a point or objects on the canvas and save, in the way a Tekla Open API program drives a running Tekla
Structures. The client is the project `OpenCAD2D.Remote` (netstandard2.0, referencing only GeometryHelper, so a
Tekla 2020 plugin on .NET Framework 4.8 can use it as well as a console program or a tool on .NET 8). Nothing is
installed on the app's side: the app opens the channel when it starts, and Global settings can close it
([the setting](#the-setting)).

Entities travel as DXF text, so the program works with the library's own types, `DxfLine`, `DxfPolyline`, `DxfMText`,
`DxfDimension`, `DxfBlockReference` and the rest, and nothing new has to be learnt: what a program reads it can edit, and
what it makes in code it can put in the drawing.

## Beside Tekla's Open API

| In Tekla Structures | Here | What differs |
|---|---|---|
| `new Model()` | [`new CadApplication()`](xref:OpenCAD2D.Remote.CadApplication) | connects to the app the user activated last; `CadApplication.GetRunning()` lists the apps |
| `model.GetConnectionStatus()` | the constructor throws [`CadRemoteException`](xref:OpenCAD2D.Remote.CadRemoteException) when no app takes it | |
| the model open | `app.ActiveDocument`, `app.Documents`, `app.Open(path)`, `app.New()` | an app holds several drawings; each is a [`CadRemoteDocument`](xref:OpenCAD2D.Remote.CadRemoteDocument) |
| `beam.Insert()` | `drawing.Insert(entity)` | queued; returns a ticket such as `~1` until the commit |
| `beam.Modify()` | `drawing.Modify(entity)` | queued, on an object read from the drawing |
| `beam.Delete()` | `drawing.Delete(handle)` or `Delete(entity)` | queued |
| `model.CommitChanges()` | `drawing.CommitChanges()` | applies everything queued as one undo step, `REMOTE` |
| `ModelObjectSelector` | `drawing.GetObjects(filter)`, `GetObject(handle)`, `GetSelectedObjects()` | copies, not live objects |
| `Picker.PickPoint(prompt)` | `drawing.PickPoint(prompt)` | waits for the user; Esc throws with `IsCancelled` |
| `Picker.PickObjects(...)` | `drawing.PickObjects(prompt)`, `PickObject(prompt)` | |
| `Operation.RunMacro` | `drawing.SendCommand("ZOOM E ")` | types on the command line |

## Which app and which drawing

`new CadApplication()` connects to the app whose window the user activated last. When two apps are open,
`CadApplication.GetRunning()` lists them, the one activated last first, each with its process id, its window's title and the
drawings it has open, and `new CadApplication(running)` connects to the one chosen; `CadApplication.ForDocument(path)`
finds the app that has a file open.

Every open drawing has an id that holds as long as the app runs. A `CadRemoteDocument` always means its own drawing, however
the user switches tabs meanwhile: a drawing the user has closed makes the next call throw
`The drawing A-101.dwg is no longer open.`, and no call ever falls through to another drawing. `app.Documents` lists the
drawings (`Id`, `Name`, `Path`, `IsActive`), `app.Documents.Get("A-101.dwg")` finds one by its path or name, `app.Open(path)`
returns the drawing at once where the app has the file open, and `drawing.Activate()` shows it as clicking its tab does.
A request to a drawing that is not shown does not show it, except the ones that work on what the user sees: a command, a
pick and the switch of a layout.

## Reading and writing

Reads are live and give copies. `GetObject(handle)`, `GetObjects(new CadObjectFilter { Kind = typeof(DxfLine), Layer = "Steel" })`
and `GetSelectedObjects()` ask the app now and return the library's entities made from the objects' DXF; changing one changes
nothing in the app. The `Handle` of a copy is its handle in the copy's own small drawing, so `drawing.HandleOf(entity)` gives
its handle in the app's drawing.

Writes are queued. `Insert`, `Modify`, `Delete`, `AddLayer` and `ModifyLayer` keep what they were given as it is at the call,
and `CommitChanges()` sends the lot:

- the app makes the layers first, then applies the objects in the order they were queued, **as one undo step** named
  `REMOTE`, and draws the canvas again; one `U` takes the whole commit back, layers included;
- it applies all or nothing. If any change is refused, nothing is applied, the queue is emptied and `CommitChanges` throws with
  the reason: a layer that exists, an object that is gone, a command running in the drawing, or an
  [owner](owners.md) that does not take the change (a dimension of a model drawing, say) with its own words;
- the handles come back in the order of the `Insert` calls. Until then `Insert` returns a ticket (`~1`, `~2`...) that stands
  for the object in `Delete`, `GetObject` and `Select` once the commit is done; `HandleOfTicket(ticket)` gives its handle
  after a commit or an [edit call](#edits-in-one-call) with no request of its own.

A layer the drawing lacks is made when an object on it is inserted, white and continuous; `AddLayer` makes it the way the
program wants. A dimension made in code is drawn by the app with the drawing's current dimension style, unless it names one.
A hatch, an MTEXT and a block reference with attributes travel by value and come back as they went.

## Blocks the program defines

An INSERT made from a block of the program's own, in a `DxfDatabase` it holds, brings the block's definition with it when the
drawing lacks the block:

```csharp
var mine = new DxfDatabase("AC1027");
var bolt = new DxfBlockTableRecord { Name = "BOLT" };
mine.BlockTable.Add(bolt);
bolt.AppendEntity(new DxfCircle(new GeoPoint3(0, 0, 0), new GeoVector3(0, 0, 1), 5.0));
bolt.AppendEntity(new DxfLine(new GeoPoint3(-5, 0, 0), new GeoPoint3(5, 0, 0)));

drawing.Insert(new DxfBlockReference(new GeoPoint3(100, 100, 0), bolt));
drawing.Insert(new DxfBlockReference(new GeoPoint3(200, 100, 0), bolt));   // the same block, made once
drawing.CommitChanges();                                                    // BOLT is now in the drawing's block table
```

Three rules. A block the drawing has is the drawing's own, whatever the program's block of that name holds, as AutoCAD binds
a block by its name. An INSERT read from another drawing of the same app brings its block the same way. And an INSERT of a
block that is neither in the drawing nor defined with at least one object is refused with the reason, nothing applied.

## Styles, linetypes and blocks in a commit

An object that names a text style or a linetype the drawing has (`new DxfDBText { TextStyleName = "NOTE" }`, a line with
`Linetype = "DASHED"`) is inserted with the drawing's own record, which the commit keeps: the client asks the app for the names once for
a commit, and an object that names one nobody has is refused by `Insert` as the library refuses it. `AddRecords(database)` queues the
text styles, linetypes, blocks (each with its objects) and dimension styles of a `DxfDatabase` of yours whose model space is empty:

```csharp
var mine = new DxfDatabase("AC1027");
mine.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = "NOTE", FileName = "romans.shx", XScale = 0.8 });

drawing.AddRecords(mine);
drawing.Insert(new DxfDBText { TextString = "Plate", Height = 3.0, TextStyleName = "NOTE" });
drawing.CommitChanges();     // one undo step: the text style made, then the text in it
```

The records are written before the layers and the objects, so a layer can have a linetype of the same commit and an object the style
or block. A record of a name the drawing has is the drawing's own and the one given is left out, as `Insert` of a drawing merges it.
`drawing.Linetypes` lists the names of the linetypes in the order of the table, and `drawing.CurrentDimensionStyle` reads and sets the
dimension style new dimensions are drawn in (one undo step, `DIMSTYLE`, at once). Python's `drawing.text_styles.add`,
`dimension_styles.add`, `linetypes.load` and `blocks.add` ([the Drawing API](../python.md#styles-linetypes-dimensions-hatches-and-blocks))
are made of these calls from outside the app.

## Where things are drawn

`drawing.GetBounds(layout)` gives the box of every entity of model space (null) or a layout as the app draws it, as a
`CadRemoteBounds`: `Handles` and `Boxes`, four numbers a row. `drawing.GetBoundsOf(handles)` gives those of some handles, in the
order asked, each in the space that holds it. `drawing.GetOverlaps(layout, words, over, handles, tolerance)` gives the entities whose
words overlap what is drawn near them, as a `CadRemoteOverlaps` (`Words`, `Others`, `Boxes`); the kinds are DXF names (`TEXT`,
`MTEXT`, `DIMENSION`, `MULTILEADER`, `LINE`) and null takes the defaults. They are the requests `bounds` and `overlaps` and the
answers of `CadSpatial` in the app, whose [guide](display.md#where-things-are-drawn) says what a box and an overlap are. The arrays
are the whole answer, which a program copies as it does: Python gives them as NumPy arrays (`drawing.bounds()`,
`drawing.overlaps()`, `entity.bounds`). A layout that is not there is a `CadRemoteException`.

## The drawing as JSON and as a graph

A program that learns from drawings (a model that lays them out, a dataset of real projects) reads them better with the blocks opened
and the units one. `drawing.ExportJson(layout)` gives the drawing as one JSON text, `drawing.ExportGraph(layout, tolerance)` as a
graph; `layout` is the name of a layout (`"Model"`) or null for model space and every layout, `tolerance` the millimetres two ends may
be apart to touch (0.001 is the library's own):

```csharp
string json = drawing.ExportJson(null);               // schema opencad2d.drawing, version 1
string graph = drawing.ExportGraph(null, 0.001);      // schema opencad2d.graph, version 1
```

The app writes them with the core's own `CadFlatten.Walk` (block references, nested ones and arrays of them included, walked down to
the shapes they draw, in world coordinates, with the path of blocks and the transformation of each), `CadDrawingJson.Write` and
`CadDrawingGraph.Build`; a program that holds a `CadDocument` calls them directly. The request is `export`, and a layout the drawing
lacks, or a tolerance that is not above nought, is refused in words. The JSON is described by
[`docs/schemas/opencad2d-drawing-v1.schema.json`](https://github.com/nguyenthanguth/OpenCAD2D/blob/main/docs/schemas/opencad2d-drawing-v1.schema.json),
and Python's `drawing.flatten()`, `to_json()`, `to_dict()` and `to_graph()` ([the guide](../python.md#preparing-real-drawings-for-ai-flatten-json-and-graph))
read the same text on a file, in the app and from outside it.

## The drawing as the app has it

`drawing.GetDatabase()` gives a copy of the whole drawing as the app has it now, with the changes not saved: the header, the tables,
the blocks, the layouts with their viewports and every object, as a `DxfDatabase` read from the DXF text the app writes of its own
database (the request `database`). The file of the drawing is not written and the app's undo is not touched. A program that draws or
checks the drawing in its own process opens the copy with `CadDocument.From(database)` and works on it as on a file: the MCP server
renders an attached app's drawing so. The copy is a snapshot; a change made in the app afterwards is not in it. A drawing of 10 000
entities (1 070 KB of text) is asked in about 20 ms and read into a document in about 5 to 25 ms.

## Layers and layouts

`drawing.Layers` lists the layers in the order of the table, each a [`CadRemoteLayer`](xref:OpenCAD2D.Remote.CadRemoteLayer)
with `Name`, `IsOn`, `IsFrozen`, `IsLocked`, `IsPlottable`, `Color` (an AutoCAD index or a true colour), `Linetype`,
`LineWeight` and `Description`. They are copies, asked of the app each time.

```csharp
drawing.AddLayer(new CadRemoteLayer("Bolts") { Color = DxfColor.FromRgb(10, 200, 30), Description = "Bolt marks" });
drawing.Insert(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(50, 0, 0)) { Layer = "Bolts" });

CadRemoteLayer steel = drawing.Layers.Single(l => l.Name == "Steel");
steel.IsLocked = true;
drawing.ModifyLayer(steel);

drawing.CommitChanges();     // one undo step: the layer made, the line on it, Steel locked
```

The app refuses, with nothing applied: a name AutoCAD does not take for a layer (`A<B`) or a layer of that name already (either
case), a linetype the drawing does not list, a layer to modify that is not there, a layer of an external reference
(`Base|Steel`) and the freezing of the current layer.

`drawing.CurrentLayer` reads and sets the layer new objects are drawn on, as the layer box does; setting a frozen layer is
refused. `drawing.Layouts` lists the tabs in the order they are shown (`Model`, `Layout1`...) and `drawing.CurrentLayout` reads
and sets the tab shown, as clicking it does: the switch is an undo step of its own (`LAYOUT`), the drawing is shown first, and
a command running in it refuses it. `GetObjects(new CadObjectFilter { Layout = "Layout1" })` reads a layout's objects, and
`Insert(entity, "Layout1")` puts one there.

`drawing.Blocks` reads copies of the blocks with their objects, sorted by name without regard to case; the blocks of the layouts
and those AutoCAD names itself (`*D1`) are left out. An external reference attached and loaded brings the blocks of its drawing,
as AutoCAD lists them in its block table: `grid|TAG` for the block TAG of the drawing `grid`, its `IsDependent` and `IsResolved`
true and its objects the drawing's own (a line of TAG on WALL is on WALL there, not on `grid|WALL`). They are held while the
reference is loaded: none for one unloaded or whose file is gone, and none in the app's own drawing or the file it saves, as
AutoCAD writes none. Python's `drawing.blocks` gives the same, from outside and from inside the app.

`drawing.Extents` reads the extents the header of the drawing holds (`EXTMIN` and `EXTMAX`) as a `CadRemoteExtents` with `Min`,
`Max` and `IsEmpty`: a drawing with nothing drawn has the empty box a header starts with, whose least corner is above its
greatest. It is the request `extents` of the channel, and what the `DrawingInfo` tool of the Pythons folder writes on its
`Extents:` line (`drawing.net.Extents` in Python).

## The view, selection and variables

A program can move the drawing's view, keep named views, take objects by an area and read and set system variables as the app's
own plugins do through [`CadEditor`](plugins.md#the-editor-the-view-selection-and-variables): each call is the command of its
name run in the app, and the app answers as the command would. The view calls work on the drawing the app shows (`Activate()`
it first), since only its session holds the canvas's view: asked of another drawing they are refused with
`The drawing B.dxf is not the drawing shown: activate it first.` The rest (`Regen`, `NamedViews`, `DeleteView`, the variables
and the selections by area) work on any open drawing.

```csharp
drawing.Activate();
drawing.ZoomExtents();                                           // ZOOM Extents
drawing.ZoomWindow(new GeoPoint2(0, 0), new GeoPoint2(10, 10));  // ZOOM Window, no margin
drawing.Pan(new GeoVector2(10, 0));                              // -PAN: the drawing moves 10 to the right
drawing.ZoomCenter(new GeoPoint2(50, 50), 20);                   // ZOOM Center, a height of 20
drawing.ZoomScaled(2);                                           // ZOOM 2X
bool back = drawing.ZoomPrevious();                              // false where none is saved
CadRemoteExtents shown = drawing.CurrentView;                    // the part of the space the canvas shows

drawing.SaveView("Plan");                                        // -VIEW Save
drawing.RestoreView("plan");                                     // -VIEW Restore, letters of either case alike
IReadOnlyList<string> views = drawing.NamedViews;                // -VIEW ?'s order

IReadOnlyList<DxfEntity> inside = drawing.SelectWindow(new GeoPoint2(-1, 5), new GeoPoint2(101, 35));
IReadOnlyList<DxfEntity> across = drawing.SelectCrossingWindow(new GeoPoint2(50, 15), new GeoPoint2(60, 25));
IReadOnlyList<DxfEntity> all = drawing.SelectAll();

drawing.SetVariable("LTSCALE", 2.5);                             // setvar
double scale = (double)drawing.GetVariable("LTSCALE");           // getvar: an int, a double, a string or a double[]
```

The points and vectors are 2D, in the world and the units of the drawing. In a drawing with a line from (0, 0) to (100, 0) and a
circle about (50, 50) of radius 25 that the app shows with no window (942 by 534 pixels, 10 units high about the origin: the
Remote test server and the tests of the app), `ZoomExtents()` makes `CurrentView` (-23.502, -4.167) to (123.502, 79.167) and
`ZoomWindow((0, 0), (10, 10))` (-3.820, 0) to (13.820, 10). `HasWindow` says whether the app shows the drawing in a window
(the `view` answer's `window`): false there, true in the app, where the canvas has its own shape and so a view of its own.

- **Undo.** Each view call is a step of the app named `ZOOM`, `PAN` or `VIEW`, a variable the drawing keeps `SETVAR`; inside a
  [run](#the-channel-of-the-tools-and-runs) they join the run's step, which U takes back whole.
- **Selection** gives copies as `GetObjects` does, with `HandleOf` and `Modify` working on them, in the order the space lists its
  objects, and leaves the objects the user selected selected: `Select(handles)` changes them. `SelectWindow` and
  `SelectCrossingWindow` are held to the view where the app has a window, as the Window and Crossing options are.
- **Refusals** come in the words of the editor: `The window has no width or no height.`, `The system variable NOSUCH is not
  known.`, `The value 7 is refused for LUNITS.`, `The drawing has no view named Corner.`
- **The requests** are `zoom` (`how`: `extents`, `window`, `objects`, `center`, `scaled`, `previous`), `pan`, `regen`, `view` (reads;
  sets where `min` and `max` are given), `namedViews`, `saveView`, `restoreView`, `deleteView`, `variable` and `selectArea`
  (`how`: `all`, `window`, `crossing`). They are new words of protocol version 1: an older app answers `OpenCAD2D does not
  know the request zoom: it is of an older version.`

## Edits in one call

A program can move, copy, turn, scale, mirror and offset objects as the app's own commands do, with the calls of
[`CadEditor`](plugins.md#the-editor-edits) run in the app:

```csharp
DxfLine line = (DxfLine)drawing.GetObject("2F");
IReadOnlyList<DxfLine> moved = drawing.Move(new[] { line }, new GeoVector2(10, 5));        // MOVE: (10, 5) to (110, 5)
IReadOnlyList<DxfLine> copies = drawing.Copy(new[] { line }, new GeoVector2(0, 50));       // COPY: new objects, new handles
drawing.Rotate(copies, new GeoPoint2(0, 0), Math.PI / 2.0);                                // ROTATE, radians
drawing.Scale(copies, new GeoPoint2(0, 0), 2.0);                                           // SCALE
IReadOnlyList<DxfLine> mirrored = drawing.Mirror(copies, new GeoPoint2(0, 0), new GeoPoint2(0, 10)); // MIRROR, sources kept
IReadOnlyList<DxfCurve> offsets = drawing.Offset(line, 5.0, new GeoPoint2(50, 10));        // OFFSET, one pick, distance typed
```

- **One commit.** What is queued (`Insert`, `Modify`, `Delete`, the layers) and the edit are one `commit` request, so one undo
  step of the app, `REMOTE`, and the new copies come back in its answer: a copy has no handle until it is applied, so an edit
  cannot be a queued entry. Inside a [run](#the-channel-of-the-tools-and-runs) the edits join the run's step, which U takes
  back whole. A refusal anywhere applies nothing, the queued changes included, and empties the queue, as `CommitChanges` does.
- **What it takes and gives.** The objects are copies this drawing gave (`HandleOf` finds their handles; one made in code, one
  of another drawing or one still queued by `Insert` is an `ArgumentException`), and the return is fresh copies as the app has
  them after the edit: the same handles for `Move`, `Rotate`, `Scale` and `Mirror(..., eraseSource: true)`, new handles for
  `Copy`, `Mirror` with the sources kept and `Offset`, each of its own type (`DxfLine`, not `DxfEntity`). An empty list is no
  request.
- **The same meaning as in the app.** The numbers and the refusals are those of [`CadEditor`](plugins.md#the-editor-edits): an
  object erased since it was read, inside a block, on a locked layer (`The object 2F is on the locked layer Steel.`) or
  whose owner allows only moving (`This line belongs to the model and can only be moved.`) is refused with these words in
  a `CadRemoteException`; a scale factor or an offset distance that is not above nought, or a number that is not finite, is
  an `ArgumentOutOfRangeException` before anything is sent; a mirror line shorter than the app's tolerance is refused by the
  app. The angle is in radians, counter-clockwise, and a point is of the world.
- **The request.** Edits are two new operations of `commit`, still protocol version 1: `{op: "transform", how: "move" | "copy" |
  "rotate" | "scale" | "mirror", handles, dx, dy, x, y, angle, factor, x2, y2, eraseSource}` and `{op: "offset", handle,
  distance, x, y}`. The answer adds `edited`, one list of handles per edit operation in order (the objects it gives back), and
  `objects`, what `get` answers for all of them. A ticket of an object not committed is refused as a handle the drawing has
  not, as `Delete` refuses it in the client.
- **The handles of what was queued.** The answer to a `commit` carries the handles of the objects inserted in it, in the order
  of the `Insert` calls, with an edit as without one. After `Copy`, `Offset` or any edit, `drawing.HandleOfTicket(ticket)` gives
  the handle of an `Insert` sent with it, from that answer and with no request, where `GetObject(ticket)` and `HandleOf` ask
  the app once for each: a thousand inserts and a `Copy` are one request, and their thousand handles are read in under a
  millisecond (about 280 ms by `GetObject`).

## Commands

`drawing.SendCommand("ZOOM E ")` types on the drawing's command line as the user does, words parted by spaces going in one by
one, and returns when the command ends or after 30 seconds, with what the command line wrote. The drawing is shown first,
since a command works on the drawing the user sees. A command that asks the user for a point stays running after the time,
the result says so, and the command goes on in the app; type the answer with another `SendCommand`, or let the user give it.
`drawing.Save()` and `drawing.SaveAs(path, version)` write the file (`SaveAs("A-101.dwg", "AC1024")` writes a DWG of
AutoCAD 2010); `SaveAs` makes the new path the drawing's name from then on.

## Asking the user to pick

A pick starts a command of its own on the drawing's command line. The drawing is shown, the prompt appears, and the
user answers as they answer any command: a click on the canvas with the snaps on, a point typed, a window drag, Enter.

```csharp
GeoPoint3 corner = drawing.PickPoint("Pick the corner of the frame");
GeoPoint3 end = drawing.PickPoint("Pick the end", corner);        // a rubber band from the corner
IReadOnlyList<DxfEntity> bars = drawing.PickObjects("Select the bars to dimension");
DxfEntity plate = drawing.PickObject("Select the plate");
```

- `PickPoint` gives a point in world coordinates, its Z nought (the presenter draws in two dimensions).
- `PickObjects` asks "Select objects:" until Enter, with every way the user selects: click, window, crossing. Objects the user
  had selected before the call are taken at once, as the app's commands take them (and the selection is cleared when the
  pick ends). The objects come back in the order of their handles, as copies that `HandleOf` and `Modify` work on.
- `PickObject` ends on one click; if exactly one object was selected before, it is taken at once.
- **Esc** ends the pick and the call throws `CadRemoteException` with `IsCancelled` true. So does Enter at `PickPoint`.
  Every other failure has `IsCancelled` false.
- An optional time (`PickPoint(prompt, null, TimeSpan.FromSeconds(30))`): when it is up the call throws
  `The user did not pick in time`, and the pick is ended in the app with Esc. The default is no limit.
- A command already running in the drawing is not interrupted: the pick throws `busy with the command LINE`. End the command,
  or send it Esc with `SendCommand`.

While a pick waits, the app is free: its window answers, the user can pan, zoom and use the ribbon, and nothing is locked on
the UI thread. The server looks every 20 milliseconds whether the pick has ended, and each look is a short piece of work on
the UI thread of its own. **Another program that asks the app meanwhile is not made to wait:** it is told at once
`OpenCAD2D is busy: it is waiting for the user to pick in a drawing, for a program that asked it to`, and its
`CadApplication` is still good for the next request; `CadApplication.GetRunning()` lists such an app with no drawings.

## The setting

Global settings › Save and export › Other programs: **Allow other programs to connect**, on by default and kept in the settings
file as `"remote": { "allowRemoteConnections": true }`. It is applied when the settings are, with no restart:

- off: the app closes its pipe, deletes its instance file and drops the connections it has; no program finds it, and a program
  that tries gets `No OpenCAD2D is running. Start the app, or check that it allows other programs to connect.`;
- on: the app opens a new pipe and writes the file again.

The line under the switch says which: `Other programs can connect: OpenCAD2D.12840` or `Other programs cannot connect.`

## The channel of the tools, and runs

A program the app itself starts (a tool that runs in a process of its own) does not find the app through the instance folder.
The app gives it the name of a second pipe, `OpenCAD2D.<process id>.tools`, and a token. That pipe is never announced, stays open
when "Allow other programs to connect" is off, and takes a client only with a token: the first message is `hello` with the token
(32 random bytes written as 64 hex letters, a new one for each tool process), and a client without one, with a wrong or revoked
one, is refused and its connection closed.

```csharp
using (var app = new CadApplication(pipe, token))              // the pipe and token the app gave the tool
{
    CadRemoteRun run = app.JoinRun(7);                         // the run of the command the user just typed
    CadRemoteAnswer first = run.Ask(CadAskKind.Point, "Specify the first step");   // the user types 12,5: Point (12, 5, 0)
    CadRemoteAnswer steps = run.Ask(CadAskKind.Integer, "How many steps");         // the user types 18: Number 18
    CadRemoteDocument drawing = app.Documents.First(d => d.IsActive);
    drawing.Insert(new DxfLine(first.Point, new GeoPoint3(12, 5 + 18 * 175, 0)));
    drawing.CommitChanges();                                   // inside the command's undo step
    run.End();                                                 // the step stays, named after the command
}
```

The command is the run. When the user types `STAIRSPY` the drawing's command line waits at `STAIRSPY is working... (Esc to stop)`
and takes nothing else, so no other command starts in that drawing. Everything the tool does on the connection that joined the run
belongs to the command:

- `Ask` takes a `CadAskKind` (point, distance, angle, integer, real, string, keyword, entity, selection), a message, an optional base
  point, keywords and default, and gives a `CadRemoteAnswer` of the kind the user answered with: a point, a number (an angle in
  radians), a keyword or text, or objects with their handles. Enter takes the default where it is a keyword; otherwise the answer is
  `None` and the tool applies its own default. The question waits for the user without holding any lock of the server: a second
  program is served meanwhile, and a second question of the same run is refused until the first is answered.
- `CommitChanges` and the reads of the drawing work as outside a run; the commits are applied in the command's one undo step, and a
  commit into a drawing other than the one the command was typed in is refused.
- `Write(line)` writes on the command line. `End()` keeps the step. `Fail(line)` writes the line and takes the step back.
- `RunCommand("LINE", "0,0", new GeoPoint2(50, 0), "")` runs a command inside the run, as AutoLISP's `command` does: the answers go
  to its prompts in turn (text is typed, `""` is Enter, a `GeoPoint2` is a point picked, a list of handles answers "Select
  objects:"), its changes join the run's one undo step, and nothing is written about the run resuming. `CadRemoteRun.Pause`
  (one backslash) leaves a prompt to the user and the call waits for them, holding no lock; Esc then ends the run. A name that is no
  command, a command of a tool, an answer the command refuses and a command still asking after the last answer are refused
  with the prompt in the words, the command ended and the run going on.
- **Esc** at the waiting prompt (or at a question) ends the run and takes back every change it committed: a drawing of 2 lines
  that had a third committed by a run is a drawing of 2 lines again, with no undo step left. A question open is answered as
  cancelled (`IsCancelled` true), and every later request of the run is refused the same way.
- A request of a run that has ended is refused, `The run 7 of STAIRSPY has ended.`, and one of another tool's token
  `belongs to another tool`. `pick` and `command` are refused inside a run: the run asks with `Ask`.

- A tool reads and sets the settings its `plugin.json` declares (see [the manifest](plugins.md#the-manifest)) with
  `app.GetToolSetting("textHeight")` (a `double` for a number, an `int` for an integer, a `bool`, or a `string` for a text and
  for a choice) and `app.SetToolSetting("textHeight", 4.5)`, by name, in a run or out of one. The value is the one the user last
  gave in the settings window, else the default. A value the setting refuses is refused in its own words,
  `The setting Text height lies from 0.1 to 1000.`, and an unknown name with `The tool Circles declares no setting textHeight.`
  The two are answered at once, with no wait for the app's window, which may be busy or waiting for this very tool, and are
  refused on the app's own channel: `toolSetting works on the channel of the tools only.`

The app's host makes the channel with `CadRemoteServer.ForTools`, gives each tool process `IssueToken()` and takes it back with
`RevokeToken`, and registers `CreateRunCommand(name, usesSelection, token, started)` as the tool's command; `started` receives the
`CadRemoteToolRun` (its `Id` and `DocumentId` are what the tool is told), and `Ended` tells how it ended (`Completed`, `Failed`,
`Cancelled`); `Cancel()` ends it as Esc does when the process is gone.

## Security

The pipe is `OpenCAD2D.<process id>`, open to the Windows user who runs the app and to nobody else, and to this computer
only: other users on the machine and other machines cannot connect. Any program that user runs can drive the app, as any
program can drive the user's Tekla; turn the setting off where that is not wanted. The app announces itself in
`%LocalAppData%\OpenCAD2D\Remote\<process id>.json` (process id, window title, time last activated) and deletes the file
when it stops; the title is written again each time the window's title changes (the drawing the command line gave opens a moment
after the app starts, so `OpenCAD2D  plan.dxf` is there at once, not only after the next click on the window), and the time last
activated only when the user activates the window; a file left by a process that died is ignored and removed by the next client that finds it. No new package
is used, and nothing is installed on the machine.

## Errors

Everything the app or the channel refuses is a `CadRemoteException` with the reason in the words the app's own command line
would use: no app running, the connection lost, a drawing closed, an owner's refusal, a block that is not there, a busy command,
a busy app, a cancelled pick. An argument that is wrong in the caller's own code (`null`, a negative time) is the usual
`ArgumentNullException` or `ArgumentOutOfRangeException`, thrown before anything is sent.

## Threads

A `CadApplication` is for one thread at a time: its requests go one after the other on one pipe, and a second thread that calls it
waits for the first, a pick included. Use one `CadApplication` for each thread that needs the app at once; the app then serves
them one at a time, and tells a client that asks during a pick that it is busy.

The app runs each request on its UI thread, between two events of the window; a long command (a big hatch, a regen) holds the
next request until it ends, as it holds the user.

## A full example

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using OpenCAD2D.Remote;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

using (var app = new CadApplication())                         // the app the user worked in last
{
    CadRemoteDocument drawing = app.ActiveDocument ?? app.New();

    // A layer, a framed title and a line on it, as one undo step.
    drawing.AddLayer(new CadRemoteLayer("SAMPLE") { Color = DxfColor.FromColorIndex(DxfColorMethod.ByAci, 3) });

    var frame = new DxfPolyline { Layer = "SAMPLE" };                         // 200 by 60
    frame.AddVertexAt(0, new GeoPoint2(0, 0), 0, 0, 0);
    frame.AddVertexAt(1, new GeoPoint2(200, 0), 0, 0, 0);
    frame.AddVertexAt(2, new GeoPoint2(200, 60), 0, 0, 0);
    frame.AddVertexAt(3, new GeoPoint2(0, 60), 0, 0, 0);
    frame.Closed = true;
    drawing.Insert(frame);
    drawing.Insert(new DxfLine(new GeoPoint3(10, 35, 0), new GeoPoint3(190, 35, 0)) { Layer = "SAMPLE" });
    drawing.Insert(new DxfMText { Contents = "Title", Location = new GeoPoint3(10, 50, 0), TextHeight = 8, Layer = "SAMPLE" });
    IReadOnlyList<string> handles = drawing.CommitChanges();                  // 3 handles

    // The user picks where the circle goes.
    try
    {
        GeoPoint3 centre = drawing.PickPoint("Pick the centre of the circle");
        drawing.Insert(new DxfCircle(centre, new GeoVector3(0, 0, 1), 25) { Layer = "SAMPLE" });
        drawing.CommitChanges();
    }
    catch (CadRemoteException ex) when (ex.IsCancelled)
    {
        Console.WriteLine("Cancelled.");
    }

    drawing.SendCommand("ZOOM E ");
}
```

The same program is in the repository as `tests/OpenCAD2D.Remote.Sample`, and the remote tests run its code against a
headless app with the pick answered by the test. To try it by hand, open OpenCAD2D with a drawing, run
`dotnet run --project tests/OpenCAD2D.Remote.Sample`, and click a point in the drawing when the command line asks
`Pick the centre of the circle:`.

## From a Tekla plugin

A Tekla plugin or macro on .NET Framework 4.8 references `OpenCAD2D.Remote.dll` and `GeometryHelper.dll` (the client has
no other dependency) and draws what it finds in the model into the drawing the user has open. The code below is the shape of it,
for Tekla 2020, 2025 and 2026 alike, since the client does not touch Tekla's assemblies; it has not been run against Tekla.

```csharp
using System.Collections.Generic;
using OpenCAD2D.Remote;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Tekla.Structures.Model;

public static class BeamsToCad
{
    /// <summary>Draws the centre line of each selected beam in the plan of the drawing the user has open in OpenCAD2D.</summary>
    public static int Send()
    {
        var selected = new Tekla.Structures.Model.UI.ModelObjectSelector().GetSelectedObjects();
        using (var app = new CadApplication())
        {
            CadRemoteDocument drawing = app.ActiveDocument;
            drawing.AddLayer(new CadRemoteLayer("TEKLA-BEAMS"));

            while (selected.MoveNext())
            {
                if (selected.Current is Beam beam)
                {
                    drawing.Insert(new DxfLine(
                        new GeoPoint3(beam.StartPoint.X, beam.StartPoint.Y, 0),
                        new GeoPoint3(beam.EndPoint.X, beam.EndPoint.Y, 0)) { Layer = "TEKLA-BEAMS" });
                }
            }

            return drawing.CommitChanges().Count;      // one undo step in the CAD for all of them
        }
    }
}
```

## From Python

A Python script drives the app with `connect()` of `opencad2d.cad`, which wraps the same client,
[`CadApplication`](xref:OpenCAD2D.Remote.CadApplication), in the `Drawing` that a [Python tool](python.md#the-drawing) of the
app uses, which drives the app through the same client: a script written for one runs in the other with only `connect()`
or `active_drawing()` changed. It needs the `opencad2d` wheel ([Installing](../python.md#installing)), Windows, the Windows
user who runs the app (the pipe admits nobody else), and [the setting](#the-setting) on.

```python
from opencad2d.cad import connect

with connect() as app:                                  # the only OpenCAD2D running; the block's end disconnects
    drawing = app.new()                                 # a new drawing, shown
    drawing.layers.add("Steel", color=1)
    drawing.add_line((0, 0), (100, 0), layer="Steel")
    drawing.commit()                                    # one undo step
    plan = app.documents["Drawing1.dxf"]                # a drawing of the list of open drawings, active or not
    app.run_command(plan, "U
")                        # typed on that drawing's command line: "
" is Enter
```

`connect(pid=None, drawing=None)` takes the only app, the one with process id `pid`, or the one that has the file `drawing` open.
`app.documents` lists the drawings in the app's order and takes a name or a path; `app.open(path)` and `app.new()` show a drawing;
`drawing.pick_point`, `pick_object` and `pick_objects` ask the user, as the `PickPoint`, `PickObject` and `PickObjects` below do.
`drawing.close()` refuses a drawing with changes not saved (`CadRemoteException`), and `close(discard=True)` closes it without
saving; the app never shows a dialog for a remote close. Anything still queued when the block ends is dropped.

Below that, the client is run by pythonnet with its .NET names, the calls of the sections above, and `drawing.net` is the
`CadRemoteDocument` of a `Drawing`. Everything in the rest of this section works the same through `with CadApplication() as app:`.

```python
import opencad2d
from System import Array, String, TimeSpan
from OpenCAD2D.Remote import CadApplication, CadObjectFilter, CadRemoteException, CadRemoteLayer
from GeometryHelper.Dxf import DxfColor, DxfColorMethod, DxfLine, DxfMText
from GeometryHelper.Geometry import GeoPoint3

with CadApplication() as app:                              # the app the user worked in last; Dispose at the block's end
    drawing = app.ActiveDocument
    if drawing is None:
        drawing = app.New()

    layer = CadRemoteLayer("SAMPLE")
    layer.Color = DxfColor.FromColorIndex(DxfColorMethod.ByAci, 3)
    drawing.AddLayer(layer)
    for x in range(0, 100, 10):                            # ten vertical lines, 50 long, 10 apart
        line = DxfLine(GeoPoint3(x, 0, 0), GeoPoint3(x, 50, 0))
        line.Layer = "SAMPLE"
        drawing.Insert(line)
    title = DxfMText()
    title.Contents = "Title"
    title.Location = GeoPoint3(0, 60, 0)
    title.TextHeight = 8
    title.Layer = "SAMPLE"
    drawing.Insert(title)
    handles = drawing.CommitChanges()                      # one undo step
    print(handles.Count)                                   # 11

    only_lines = CadObjectFilter()                         # no CadObjectFilter(Layer="SAMPLE"): set the properties after
    only_lines.Kind = DxfLine                              # a Python class stands for the .NET Type
    only_lines.Layer = "SAMPLE"
    print(drawing.GetObjects(only_lines).Count)            # 10

    drawing.Select(Array[String](list(handles)[:3]))       # an array of strings, not a Python list
    print(drawing.GetSelectedObjects().Count)              # 3

    result = drawing.SendCommand("ZOOM E\n")               # "\n" is Enter
    print(result.Completed)                                # True

    try:
        centre = drawing.PickPoint("Pick the centre", timeout=TimeSpan.FromSeconds(30))
        print(centre)
    except CadRemoteException as error:
        print("cancelled" if error.IsCancelled else error.Message)

    drawing.SaveAs("sample.dxf")
```

The script runs here against a headless app in a process of its own, with the pick answered by typing `120,80` on its command
line: it prints `11`, `10`, `3`, `True` and `(120, 80, 0)` in turn, on .NET 8 and on .NET Framework 4.8 alike, and
`sample.dxf` is a file of 6 046 bytes. In the app the `ZOOM E` shows the drawing's extents; the headless app has no window,
so the zoom moves a view of its own that nothing shows (the one `-VIEW Save` keeps), and answers `Completed`.

Where Python differs from the C# of the sections above, measured on .NET 8 and on .NET Framework 4.8 with the same result:

- **`with` closes the connection.** pythonnet makes every .NET type that is `IDisposable` a context manager, so
  `with CadApplication() as app:` calls `Dispose`, which closes the pipe, at the end of the block and also when the block
  raises: a `ValueError("boom")` raised inside came out of the block as it was. Without `with`, call `app.Dispose()`. A call
  made on the object, or on a drawing of it, after `Dispose` raises `System.ObjectDisposedException` (`The connection to
  OpenCAD2D (process 4120) was closed by Dispose, or by the end of the with block that held it: make a new CadApplication to
  work with the app again.`) and opens no pipe; a connection the app lost is still opened again at the next call. Make a new
  `CadApplication` (a new `with connect()`) to work on.
- **A collection argument is an array.** `drawing.Select(["1A", "1B"])` is a `TypeError: No method matches given arguments
  for Select: (<class 'list'>)`, since `Select` takes an `IEnumerable<string>` and pythonnet converts a Python list only to
  an array. Write `Select(Array[String](["1A", "1B"]))`, or a `List[String]()` filled by `Add` (`List[String](["1A"])` is
  refused as well); `Select(Array[String]([]))` selects nothing. The stubs say so: an editor marks `Select(["1A"])` before
  the script runs ([Lists: only for arrays](../python.md#lists-only-for-arrays)).
- **Set a filter's properties after making it.** `CadObjectFilter(Layer="SAMPLE")` is a `TypeError`, because pythonnet
  has no keyword initialiser for a property, only for a constructor's own arguments (`GeoPoint3(x=1, y=2, z=3)` works).
  Make the filter, then assign. `Kind` takes the Python class itself (`only_lines.Kind = DxfLine`) or
  `clr.GetClrType(DxfLine)`.
- **A command ends on Enter.** `SendCommand("LINE 0,0 100,0\n\n")` draws the line and ends, and its `Output` holds the
  prompts with the answers (`Command: LINE`, `Specify first point: 0,0`, `Specify next point or [Undo]: 100,0`). The same
  text ending in one space leaves LINE waiting for another point, and the call returns after its time (30 seconds when
  none is given) with `Completed` false. Give a shorter time to leave a command open on purpose:
  `SendCommand("LINE 0,0", TimeSpan.FromMilliseconds(200))` returns at once with `Completed` false, and the next
  `SendCommand("100,50\n\n")` finishes it.
- **Ctrl+C waits for the call.** Python takes the interrupt between two of its own instructions, and a call into .NET is one
  instruction. A pick with no time limit therefore goes on until the user answers in the app, and a
  `KeyboardInterrupt` raised at 1 second of a `PickPoint(..., timeout=TimeSpan.FromSeconds(4))` came at 4 seconds, when the
  call returned with its timeout exception. In a script, give every pick and every long command a `timeout`.

What it costs, on this machine with the app in another process, and a drawing of 10 000 lines:

| | .NET 8 | .NET Framework 4.8 |
|---|---|---|
| starting the runtime and the three assemblies, once per script | 211 to 224 ms | 282 to 288 ms |
| connecting and the hello that checks the version | 7.7 to 8.3 ms | 18.9 to 19.5 ms |
| one small request (the number of open drawings) | 26 to 43 us | 37 us |
| `GetObjects()` of the 10 000 lines, as `DxfLine` objects | 315 to 348 ms | 298 to 304 ms |
| 1 000 `Insert` of a line, queued, then their `CommitChanges` | 91 to 93 ms, then 38 to 50 ms | 107 to 144 ms, then 55 to 69 ms |
| `SendCommand("LINE 0,0 100,0\n\n")` | 16.9 ms | 17.5 to 19.4 ms |

Most of the 10 000-line read is the app's, which writes the drawing as DXF text: a client that took only that text, with no
Python or .NET types made from it, waited 260 to 281 ms for it.

**A plugin on the app's thread does not connect to that app.** A request from the UI thread to the same app would wait for that
thread, which is the plugin itself, for good. The app refuses such a connection before anything is sent, with a
`CadRemoteException`. A Python tool is not on that thread: it runs in a process of its own and `connect()` inside it connects to the
channel of the tools ([The channel of the tools and runs](#the-channel-of-the-tools-and-runs)), so it needs no setting.

Three scripts that drive the app, one that draws a grid and reads it back, one that waits for a pick and one that lists the
open drawings, are in [Python examples](../python-examples.md#driving-a-running-app).

## See also

- [The 2D presenter](index.md) for the projects and the command line the channel types on.
- [Owners](owners.md) for the refusals an owner makes of an edit.
- [Settings, autosave and the window](app.md) for Global settings.
