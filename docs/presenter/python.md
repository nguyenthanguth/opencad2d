# Python commands and tools

OpenCAD2D runs Python as tools. A **tool** is a folder: a `requirements.txt` (it names the packages the tool needs, and may be
empty), an `__init__.py` that defines commands, and a `plugin.json` that names them and the ribbon buttons the tool adds. A
function marked `@command("C1")` becomes a command of the command line, talks to the user through the `get_*` prompts and edits
the drawing as one undo step. Python does not run inside the app: each tool runs in a `python.exe` of its own, with a venv of
its own, that the app starts at the tool's first command, so two tools may use two versions of one library, and a tool that
raises, loops for ever or exits takes nothing of the app with it. A program outside the app drives the running app and any drawing
in its list of open drawings with the same names ([Driving the app from another program](remote.md)). Python here is a way to
write commands: the app has no Python console.

The Python is yours: the app does not bring one. It needs a CPython 3.11 to 3.15, 64-bit, with pip, on the machine, and finds
it as [Finding Python](#finding-python) says. With none the app works as it did, and says what to install where a tool is
used. This page is about Python tools in the app; a script that runs outside it, on the library alone or against a running
app, is on [Python](../python.md) and [Driving the app from another program](remote.md).

```text
Pythons\Stairs\
    requirements.txt     (required, may be empty: one package to a line, as pip reads it)
    __init__.py          (required: runs when the tool's process starts; defines the commands)
    plugin.json          (the commands and the ribbon the app shows with no Python running)
    steps.py             (anything else the tool imports)
```

```python
from opencad2d.cad import active_drawing, command, get_distance, get_point

@command("RING", alias="RG", description="Draws a circle of the radius asked")
def ring() -> None:
    centre = get_point("Specify centre")          # picked with the object snaps, or typed
    radius = get_distance("Specify radius", base=centre)
    drawing = active_drawing()
    drawing.add_circle(centre, radius)
    drawing.commit()
```

with the `plugin.json` of that tool:

```json
{
    "name": "Ring",
    "commands": [{ "name": "RING", "alias": "RG", "description": "Draws a circle of the radius asked" }]
}
```

Units are the drawing's, millimetres in a drawing made in them; nothing is converted. No tolerance is chosen for you: a tool
passes a [`Tolerance`](xref:GeometryHelper.Tolerance) where the C# would. The presenter is 2D, so a point asked is a
[`GeoPoint2`](xref:GeometryHelper.Geometry.GeoPoint2). Every public name of `opencad2d.cad` has a docstring with an
`Example:` block and is typed inline (the package carries `py.typed`, so an editor and pyright read the types from the code).

## Where tools load from

| How | What happens |
|---|---|
| The **Pythons** folder, beside `OpenCAD2D.Core.dll` as Lisps is | the folders directly in it that hold a `requirements.txt` load at start, by name ignoring case then ordinal; a folder with no `requirements.txt` is a package the tools import (the Pythons folder is on every tool's `sys.path`, after the packages of the tool's own venv). A `.py` directly in it is not loaded: see [A lone `.py`](#a-lone-py) |
| The **Python Startup Suite** of Load/Unload Applications (APPLOAD), saved in the settings as `support.pythonStartupSuite` | its folders load after the Pythons folder, in the order of the list |
| **APPLOAD**, Load… | a row of kind Python for a tool, with the commands its `plugin.json` names; choose the tool's `requirements.txt`. Reload loads it again, Unload takes its commands out of every drawing and ends its process |
| **PYLOAD** | the multi-select picker for the `requirements.txt` of tools, loading them as APPLOAD does so that they show in its list |
| **Drag and drop** of a tool's folder (or its `requirements.txt`) onto the window | loads it as APPLOAD's Load does; passed over while a command or a routine waits, and with no drawing open |
| **PYRUN** | runs a lone `.py` once, as `python file.py` does ([PYRUN](#pyrun)) |

Loading a tool starts no Python and installs nothing. The app reads the tool's `plugin.json` and shows its commands and ribbon
buttons at once; the tool's process starts the first time one of its commands is typed or one of its buttons pressed. The
start loads once per process, at the first drawing opened or made and after that drawing's AutoLISP start, because the
commands of a tool belong to the whole process. A tool that fails to load writes one line and the next one loads. The Pythons
folder lies under the program folder, which [the security gate](#trusted-folders) trusts with all its subfolders, so its
tools load with no question; a tool elsewhere goes through the gate as PYLOAD does.

Two samples ship in the Pythons folder, and in `examples\Pythons` of the repository:

- `DrawingInfo`: its command `GHPYINFO` writes the drawing's name and folder, the current layer, the objects of model space
  counted by type, the number of layers and blocks, and the extents, and changes nothing. For a drawing of two circles and a line:

  ```text
  Drawing: Plan.dwg in C:\Projects
  Current layer: Steel
  Model space: 3 objects
    CIRCLE   2
    LINE     1
  Layers: 2, blocks: 1
  Extents: (-10.00,-10.00) to (120.00,80.00)
  ```

  Its extents are read from the header of the drawing over the channel of the tools (`drawing.net.Extents`).
- `HelloWorld`: its command `PYHELLOWORLD` writes `Hello, World!`, the smallest tool there is.

Delete the folder of a sample if you do not want it; the app starts as one with an empty folder does.

Load/Unload Applications lists a tool with a Python chip beside Lisp and Plugins, an amber kind colour, and its commands. A tool
that loads says `Stairs loaded: STAIRSPY.` on the command line and `Stairs successfully loaded.` in the footer; one that does
not says `Stairs failed to load: <the reason>` and defines no command. A tool whose `plugin.json` declares no command loads and
says `Stairs declares no command in plugin.json.`

### A lone `.py`

A `.py` file directly in Pythons, in the Startup Suite, given to APPLOAD or PYLOAD, or dropped on the window is not a tool, and is refused with
the line that says how to make it one:

```text
Rings.py is a lone Python file: a Python tool is a folder with requirements.txt. Put it in Pythons\Rings\ as __init__.py (see the Python guide).
```

To convert one, make the folder, move the file in as `__init__.py`, add an empty `requirements.txt` and a `plugin.json` that
names the commands its `@command` functions define. A `.py` entry left in the Startup Suite of the settings is refused at each
start with the same line until it is replaced by the folder. PYRUN still runs a lone `.py` once.

### `plugin.json`

```json
{
    "name": "Stairs",
    "version": "1.0.0",
    "commands": [
        "STAIRSPY",
        { "name": "STEPS", "alias": "ST", "description": "Counts the steps", "usesSelection": true }
    ],
    "ribbon": [
        {
            "id": "stairs",
            "title": "Stairs",
            "panels": [
                {
                    "title": "Draw",
                    "buttons": [
                        { "text": "Stairs", "command": "STAIRSPY", "icon": "stairs.png", "size": "large", "tooltip": "Draws stairs" }
                    ]
                }
            ]
        }
    ]
}
```

A command is a name or an object (`name`, `alias`, `description`, `usesSelection`). The ribbon is tabs of panels of buttons; a
button is `text`, the `command` it starts, an `icon` (an image file of the tool's folder), a `size` (`standard` or `large`)
and a `tooltip`, and starts a command and nothing else. Only `name` is needed. When the tool's process is ready the app
compares what it bound with `@command` against the names of the file: a name with no `@command` fails its run with
`GHOST is declared by the tool, but it has no @command of that name.`, and an `@command` the file does not name is a warning in
the log. A `plugin.json` that cannot be read keeps the tool from loading, with the line `Stairs was not loaded: plugin.json: <why>`.
A tool shows no WPF content of its own in the window; a tool that needs a window of its own is an outside app (`connect()`).

## Finding Python

The app looks for Python when a tool's environment is made, at the tool's first command, in this order, and takes the first that
is set:

1. The Python set in Global settings, Folder preference, **Python** (`GlobalSettings.PythonPath`): a `python.exe`, of a
   virtual environment or of an install, or the folder that holds it. Empty is automatic. A setting that does not work is
   a problem to show, never replaced silently by another Python.
2. The Python launcher's registry entries (PEP 514), `HKCU` then `HKLM`, under `Software\Python\PythonCore\<tag>`: the newest
   64-bit 3.11 to 3.15 whose files are there, `HKCU` before `HKLM` at one version.

A process needs the `python.exe` alone, so nothing else of an install is checked. The registry holds more than what is installed: it may list 3.10 with no install path, 3.13 whose
`python.exe` is gone from the disk, and 3.14 (3.14.4, 64-bit). The finder checks the files, so it takes 3.14 and keeps the
others, each with its reason: `3.10: no InstallPath`, `3.13: ...\Python313\python.exe is missing`. The reasons are what
Global settings lists as "Passed over". Where both hives register the same tag, each line names its hive: `3.10 (HKCU): no InstallPath`,
`3.10 (HKLM): no InstallPath`. What is wrong is written where a tool is used, on the command line, in one of these
sentences:

| When | The sentence |
|---|---|
| nothing found automatically | `No Python 3.11 to 3.15 (64-bit) was found. Install one from python.org, or set its python.exe in Global settings > Python.` |
| the setting names nothing | `The Python set in Global settings (<path>) was not found.` |
| a file that is no Python | `<path> did not run: it is not a Python program.` or `<path> did not run as Python.` |
| another version | `<path> is Python 3.<n>: OpenCAD2D needs 3.11 to 3.15.` |
| a 32-bit Python | `<path> is a 32-bit Python: OpenCAD2D needs a 64-bit one.` |
| a Python without pip | `The Python at <path> has no pip: install Python from python.org with pip, or set another in Global settings > Python.` |

A tool never uses your own Python environment: its packages go in a venv of its own ([A venv for each tool](#a-venv-for-each-tool)).
A path set in Global settings takes effect when a tool's process starts next: where a process is running already, the setting
waits until the tool is unloaded or the app starts again, and Global settings says so.

## PYRUN

PYRUN runs a lone `.py` once. It asks for the file as SCRIPT does: the open dialog, filtered to `*.py`, where the window shows
dialogs, else `Enter Python file name:` on the command line (FILEDIA 0). A name with no extension gets `.py`, and a name that
is not a full path is taken from the folder of the drawing where the drawing has a file.

The file runs in a new `python.exe` of the default venv (`envs\_default`, no packages of its own, made at the first PYRUN), as `python file.py`
runs it: fresh globals with `__name__` equal to `"__main__"` and `__file__` the full path, `sys.argv` equal to `[path]`, and the
file's folder first on `sys.path`, so a file beside it can be imported. The process ends with the file, about 0.3 s more than a
command of a tool that is running. The run is a command named `PYRUN`: one undo step, Esc takes everything back, and
`sys.exit()` and `sys.exit(0)` end it well.

PYRUN ends the command first, and the run starts when the command line is at the Command prompt again. A file that raises writes
`PYRUN: ValueError: boom` and the drawing is as it was.

## `@command`

A tool's `__init__.py` is imported when its process starts, as the package `<folder name>`, with the folder above it on
`sys.path`, and each function decorated with `command` is bound to the command of its name in `plugin.json`:

```python
@command(name, *, alias=None, description=None, uses_selection=False)
```

The command is typed by its name or alias (the ones `plugin.json` declares), Enter repeats it, AutoComplete offers it as it
offers a plugin's commands, and the function runs with no arguments as a run of its own whose undo step carries the command's
name; what it returns is ignored. An `alias` is taken when no command or alias has it. `uses_selection` makes `get_selection()`
return the objects selected before the command started, without asking, as MOVE does, and ask only when none were. The
module-level code of `__init__.py` runs when the process starts, before the first command, outside any run; a function marked
`@on_unload` is the twin of a plugin's `Terminate` and runs when the tool is unloaded or the app closes (an error in it is one
line and the unload goes on).

Loading a tool again replaces its commands, the old ones taken out first. A name that is a command already, and not this tool's,
is refused and the rest of the tool's commands are kept: `PYLOAD: LINE is a command already; Stairs did not define it.`

`command` is the Python twin of the `[CadCommand]` attribute of a [command plugin](plugins.md).

### A command with a selection

`selection_lengths` asks `Select objects:` as a command does, adds up the length and the closed area, and writes the total.
On a line from 0,0 to 300,400, a circle of radius 120, a closed polyline rectangle 100 by 50 and a text, all selected, it writes
`4 objects: length 1553.982, closed area 50238.934, 1 skipped`.

[!code-python[](../../src/python/examples/tools/selection_lengths/__init__.py)]

### A command that draws

`drawing_tools` defines RECTC (alias RC), a rectangle about a centre, and LAYERCOUNT. RECTC with the centre 500,300, a width
of 200 and a height of 100 draws one closed polyline of 20 000 square units, one undo step named RECTC; the width and the height
default to 1 000 and 500 on Enter.

[!code-python[](../../src/python/examples/tools/drawing_tools/__init__.py)]

### An edit that numbers

`number_points` defines NUMBERPOINTS: it asks for the first number and then for points until Enter, and adds a text of height
2.5 with the next number at each point. With a first number of 5 and the points 100,200, 300,200 and 500,200 it adds the texts
5, 6 and 7 and writes `3 points numbered, 5 to 7.`; one U takes all three away.

[!code-python[](../../src/python/examples/tools/number_points/__init__.py)]

The three run as they are in the repository, in tests that type the commands in a window's model (`PythonToolExampleTests`).

## The prompts

| Function | What it does |
|---|---|
| `get_point(message, base, keywords, default)` | a [`GeoPoint2`](xref:GeometryHelper.Geometry.GeoPoint2), picked with the object snaps or typed |
| `get_distance(message, base, keywords, default)` | a distance, typed or as two points |
| `get_angle(message, base, keywords, default)` | an angle in radians, typed in degrees or as a direction |
| `get_integer(message, keywords, default)` | a whole number |
| `get_real(message, keywords, default)` | a number |
| `get_string(message, default, spaces)` | a text; with `spaces` false it ends at the first space |
| `get_keyword(message, keywords, default)` | one of the keywords |
| `get_entity(message, keywords)` | one object, as an `Entity` of the run's drawing, or `None` on Enter |
| `get_selection()` | the objects selected at "Select objects:", until Enter, as a list of `Entity` |
| `write(text)` | writes one line or more on the command line |
| `Cancelled` | the exception of Esc at a prompt |

A prompt behaves as a command's own prompt does: the object snaps, typed coordinates, the rubber band from `base`, and Esc.
The message is written with a colon after it, `Specify centre:`, and a default is shown in angle brackets. The answer is a
point, a number or an `Entity` as it was given, or a keyword as the prompt writes it (a `str`). Enter with no default gives
`None`, as `getpoint` of AutoLISP gives `nil`, and Enter with a default gives the default. The default of an angle is in
degrees, as the prompt shows it, while the answer is in radians. A prompt is a request of the tool's run on the channel of the
tools: it waits for the user without holding up any other program connected to the app.

## `cmd` and `PAUSE`

`cmd(*answers)` runs a presenter command to its end, inside the run's undo step, at the Command prompt, as AutoLISP's `command`
does: it joins the run's step and writes no "Resuming" line. The first answer is the name of the command (`"CIRCLE"`,
`"_LINE"`, an alias), and each answer after it is fed in order:

| Answer | Fed as |
|---|---|
| `""` | Enter |
| a `str` | typed |
| an `int` or a `float` | typed, in the invariant culture |
| a `GeoPoint2` or `(x, y)` | a point picked |
| a `GeoPoint3` or `(x, y, z)` | typed as `x,y` |
| a `list` of handles or of entities | the answer to "Select objects:", then Enter |
| `PAUSE` | hands that one prompt to the user (the string `"\"`, as AutoLISP's `pause`); Esc there raises `Cancelled` |

`cmd("_LINE", (0, 100), (100, 100), "")` draws a line of 100 and ends; `cmd("CIRCLE", PAUSE, 50)` lets the user pick the centre
and gives the radius 50. After the last answer the command must have ended; one that still asks is cancelled and raises
`RuntimeError`: `CIRCLE still asked 'Specify radius of circle or [Diameter]:' after the last answer.` A name that is not a
command, an answer the prompt refuses and an answer given after the command ended raise `ValueError`, the command cancelled
first. A tool's own command is not run with `cmd`. A command that opens a window of its own shows it, modal, as typed by hand;
use the `-` form of such a command (`-LAYER`) in a tool.

## The drawing

`active_drawing()` is the drawing the command was typed in (not the one shown later) and gives a `Drawing`; `documents()` lists
the drawings of the window in its order, `open_drawing(path)` opens one as OPEN does and `new_drawing()` makes one. The same
`Drawing` works on a running app from outside (`connect()`) and on a file with no app (`open_file`, `new_file`), so a script
written for one runs on the others with only that line changed ([Python](../python.md#the-drawing-api)).

A drawing is read as copies and changed by queueing (on a drawing made by `new_file()` the commit gives `['101', '102']`):

```python
drawing = active_drawing()
drawing.layers.add("Steel", color=1)
drawing.add_line((0, 0), (100, 0), layer="Steel")
drawing.add_circle((50, 50), 25)
handles = drawing.commit()          # the handles of the circle and the line, in the order added
```

| Member | What it does |
|---|---|
| `name`, `path`, `is_active`, `is_modified` | what the tab shows; `path` is `None` for a drawing never saved |
| `entities(type, layer, layout)`, `entity(handle)`, `selection()`, `select(items)` | read objects as `Entity` copies; `type` is the DXF name, `"LINE"`, ignoring case |
| `add(dxf_entity, layout)` | queue a copy of any `GeometryHelper.Dxf` entity |
| `add_line`, `add_circle`, `add_arc`, `add_polyline`, `add_text`, `add_mtext` | queue the common objects; angles are **radians**; `**props` takes `layer`, `color`, `linetype` |
| `modify(entity)`, `erase(entity_or_handle)` | queue a change made to `entity.net`, or an erase |
| `commit()` | apply everything queued as one undo step; the handles of the objects added |
| `layers`, `current_layer`, `text_styles`, `dimension_styles`, `blocks`, `layouts`, `current_layout` | the tables; styles and blocks are read-only copies |
| `pick_point`, `pick_object`, `pick_objects` | ask the user (in a tool they are the `get_*` prompts; `RuntimeError` on a file) |
| `save()`, `save_as(path, version)`, `close(discard)` | as from outside; a modified drawing is refused by `close` unless `discard` is true |
| `net` | the `CadRemoteDocument` in a tool (a `CadDocument` for a file); its `Extents` gives the header's extents |

A point is a `GeoPoint2`, a `GeoPoint3` or a tuple of two or three numbers. An `Entity` has `handle`, `dxf_type` and a `net`, a
**copy** of the `DxfEntity` that is never the live object, so nothing bypasses undo; `layer`, `color` (256 ByLayer, 0 ByBlock)
and `linetype` are set on the `Entity` and committed with the drawing; after changing `net`, call `drawing.modify(entity)`. A
`Layer` has `name` (read only) and settable `color`, `linetype`, `description`, `is_on`, `is_frozen`, `is_locked` and
`is_plottable`. A dimension added or modified from Python is drawn by the app, so its picture holds the lines, arrows and words
of its measurement ([`CadDimensionPicture.Redraw`](xref:OpenCAD2D.CadDimensionPicture)): a rotated dimension from
0,0 to 100,0 comes with the words `100`, and one moved to 150 with `150`.

An object added from a tool has the handle `~1`, `~2`... until `commit()` returns the real ones. A command commits what is still
queued when it ends, inside its own step; `save()` commits first in every backend; anything left queued when a script ends
outside the app is lost, without a warning. A round trip to the app costs about 17 ms for one call and 0.15 s for 1 000
inserts queued and committed: a tool that makes many calls batches them (queue, then one `commit()`; the array API of
[Python](../python.md#numpy-in-blocks)).

### A drawing made and saved in code

`make_drawing.py OUT` makes a blank drawing with `new_file()`, adds a layer `Outline`, makes it current, adds a rectangle 4 000
by 3 000 and a circle of radius 500 on it, and saves the drawing as `OUT`, DXF or DWG by its extension. It prints `objects 2`,
`layers 0, Outline` and `saved plan.dwg`, and the file holds a polyline of area 12 000 000 and the circle on `Outline`.

[!code-python[](../../src/python/examples/library/make_drawing.py)]

## One undo step

A run is one undo step, whatever it draws, through the Drawing API, through `cmd` or both. The step is named `PYRUN` for a
file and the command's name for a command of a tool. A run that ends well ends every group it left open. A run that fails or is
cancelled is taken back whole, and what is still queued is dropped, so the drawing is as it was before the run and nothing is
left open that would make the next `SaveAs` throw.

A tool that draws a circle with `cmd("CIRCLE", (0, 0), 25)` and then divides by zero fails the run: the drawing has 0 objects,
not 1, and no undo step. A run that only asks and writes makes no step and does not mark the drawing modified. An
`undo_group` of the tool, from `opencad2d`, nests in the run's step. While the tool works the drawing is busy with the
command: `STAIRSPY is working... (Esc to stop)`, and no other command runs in it.

## Adding to the window

A tool adds to the window through its `plugin.json` (see [`plugin.json`](#pluginjson)): ribbon tabs, panels and buttons that
start its commands. The app shows them when the tool loads, with no Python running, and takes them away when the tool is
unloaded. What a command plugin written in C# can add and a tool cannot:

| C# | A Python tool |
|---|---|
| `[CadCommand(Name, Alias, Description, UsesSelection)]` | `@command(name, alias=, description=, uses_selection=)` and the same in `plugin.json` |
| `ICadPluginApplication.Terminate()` | `@on_unload` |
| `AddRibbonTab`, `AddRibbonPanel`, `CadRibbonButton` | the `"ribbon"` of `plugin.json`; a button starts a command and has no callback |
| `AddDrawingsButton`, `AddDrawingsMenu`, `AddCanvasMenu`, `AddProperties` | **not there**: they ran code of the tool at the window's thread |
| `AddObjectSnap`, `[CadSetting]` | **not there** |
| the side panel (`CadWorkspaceExtension.SidePanelContent`) | **not there**: a tool shows no WPF content; one that needs its own window is an outside app (`connect()`) |

A tool that needs one of those is a [command plugin](plugins.md) in C#.

## Esc

Esc ends a run in either of two places. At a prompt Esc is the command line's own: the question ends, the prompt function
raises `opencad2d.cad.Cancelled`, and every prompt the same run asks after it raises at once without asking, so a tool that
catches the first cannot go on asking. `Cancelled` is a `KeyboardInterrupt`, so `except Exception` does not swallow it, and
every change of the run is taken back: the command line writes `*Cancel*`. A tool that catches `BaseException` or a bare
`except` swallows each Esc once.

While the tool works Esc is read by the window, which tells the tool: its main thread gets `KeyboardInterrupt`, so a
`while True: pass` stops at once (Esc to the next command done: 0.03 s in the test). Code Python cannot interrupt
(`threading.Event().wait()`, a long call of a C library) is ended by the app after 5 seconds, with the line
`STAIRSPY: the tool Stairs did not stop within 5 s and was ended.`, and the process starts again at the next command.

## Errors and refusals

An error ends the run and never closes the app. The command line gets one line, `STAIRSPY: ValueError: no stairs here`, the
traceback goes to the log, and the changes are taken back. A tool that exits during a run writes
`STAIRSPY: the tool Stairs ended unexpectedly (exit code 3).` and the last five lines of its standard error, and the run is
cancelled. In both the drawing is as it was and the next command works. `sys.exit(3)` in a command is a failed run; `exit()`,
`quit()` and `sys.exit(0)` end it well.

These are written, and the run does not start:

| When | The line |
|---|---|
| a command runs in that drawing | `A command is running: finish it or press Esc first.` |
| a tool outside the trusted folders, refused ([Trusted folders](#trusted-folders)) | `File load canceled: <full path of __init__.py>` |
| a lone `.py` | the line of [A lone `.py`](#a-lone-py) |
| PYRUN of a file that is not there | `Cannot find Python file "<name>".` |
| a folder with no `__init__.py` | `Stairs has no __init__.py: a Python tool is a folder with requirements.txt and __init__.py.` |
| no usable Python | one of the sentences of [Finding Python](#finding-python) |
| packages not approved or not installed | the lines of [A venv for each tool](#a-venv-for-each-tool) |

The ways a tool can end badly were each run, and each leaves the app running, no undo group open, the drawing as it was where the
run failed, and the next command working: a tool that raises, one that exits (`os._exit(3)`), one that loops for ever (Esc, then
5 seconds at most), and one that `print`s a great deal. The process of a tool is put in a job object that ends it, and the
launcher of its venv, whenever the app lets it go, even when the app is killed.

## Trusted folders

PYLOAD, APPLOAD, PYRUN and the Startup Suite go through the gate AutoLISP's `load` goes through, with the same trusted folders
and the same answers: SECURELOAD, TRUSTEDPATHS and the files answered Always Load, set with SETVAR or on
[the Security page](app.md#the-security-page) of Global settings, as
[Trusted folders and SECURELOAD](lisp.md#trusted-folders-and-secureload) describes. The file asked about is the tool's
`__init__.py` (a PYRUN file is the file). At SECURELOAD 1, the app's default, one outside the trusted folders brings AutoCAD's
warning, **Security - Unsigned Executable File**: Load Once runs it and does not ask about that file again until the app
restarts, Always Load also keeps the answer for that file and its content, and Do Not Load writes
`File load canceled: <full path>` on the command line. At SECURELOAD 2 such a file is refused with no question. The folder of the
app, with the Pythons folder in it, is always trusted; a trusted path that names a folder and everything below it ends in `\...`.

The gate is in front of the files the app is asked to run. The modules a tool imports are Python's own business, found on
`sys.path`, and are not checked. A script outside the app, on the `opencad2d` package alone, meets no gate: a session made
by the library starts at SECURELOAD 0.

## `print` and the process

What a tool prints goes to the command line of its run in whole lines, in order with `write`. What the tool writes on its
standard error goes to the log. A line it writes on its standard output outside a run (or from a thread after one) goes to the
command line of its last run. `input(prompt)` is not a prompt of the app: ask with `get_string`.

The process is
`python -I -X utf8 -u -m opencad2d.tool <folder> <control in> <control out>`, with `OPENCAD2D_RUNTIME=coreclr`,
`OPENCAD2D_LIB` (the folder the app's own `OpenCAD2D.Core.dll`, `OpenCAD2D.Remote.dll` and `GeometryHelper.dll` are in, with
`Fonts`, `Hatches` and `Linetypes`), and the pipe and token of the private channel of the tools
([Driving the app from another program](remote.md#the-channel-of-the-tools-and-runs)). It stays until the tool is unloaded or
the app closes.

- **Cost.** The first command of a tool took 0.37 s (the process started, `opencad2d` imported, the connection made, one run;
  0.37 s again with numpy); the next, in the same process, 16 ms. A kept process is about 66 MB, and numpy commits about 810 MB
  of address space on its own (the library's reservation; the app sets nothing). Ten tools kept alive are about 0.7 GB.
- **One library, two versions.** A process holds one version of numpy, so each tool has its own process and venv: one tool with
  `numpy==1.26.4` and another with `numpy==2.3.5` both run.
- **Threads.** A tool may start threads; one that is left running when the tool is unloaded is ended with the process.

### A venv for each tool

A tool has a venv of its own, `%LocalAppData%\OpenCAD2D\python\envs\<name>-<8 hex>` (the hex is the start of the SHA-256 of the
tool folder's full path, so two tools of one name in two places have two), made from the Python of Global settings > Python, else
the newest 64-bit 3.11 to 3.15 the registry lists, and never from your own Python environment. It holds the packages of the
tool's `requirements.txt`, so a tool that pins `numpy==1.26.4` and another that pins `numpy==2.3.5` both install and both run.
The venv is made at the tool's first command, off the window's thread, while the drawing waits at `STAIRSPY is working...`.

- **Nothing is installed without a yes.** The app asks `pip install --dry-run --report` what would be installed and writes the
  list (the packages the file names first, then the ones they need, each with its version), then asks in a dialog, `Packages
  for Python tool Stairs` (Package, Version, Asked as: `shapely>=2.0`, or `for shapely`), or on the command line with FILEDIA 0
  (`Install with pip [Yes/No] <No>:`), before it downloads anything. While pip installs, the tool's row in Load/Unload
  Applications shows **installing…**; after a No it shows **needs packages** and the next command asks again; a failed install
  shows **install failed** and a red line with pip's first `ERROR:` line (a change that failed and left the tool on its old
  venv gives the red line and no badge). The venv is changed only after the Yes, and asked again when
  `requirements.txt` or the Python changes. Esc or No ends the run with `Stairs was not installed: its packages were not
  approved. To install them yourself, run: ...`. A tool with no packages asks nothing.
- **A lower bound is a version.** `numpy>=1.26` is installed as `numpy==1.26`: quick, and the same on every computer. Where pip
  has no wheel of `1.26` for the Python in use (it has only a source archive, or no such version), or `1.26` clashes with
  another line, the line is taken as written and pip picks. Neither says anything. The pinned try takes wheels only
  (`--only-binary` for the pinned names), so a version that would have to be built from source is not taken: `numpy>=2.0` on
  Python 3.14 has no `2.0.0` wheel, the pinned try fails in 1.2 s, and the line as written takes the `cp314` wheel (about 3 s
  for the list); pinned without that, pip built numpy 2.0.0 for 150 s and the import failed. Any other form (`~=`, `<`, `*`, `>=1.26,<2`) goes to pip as written. `requirements.txt`
  names packages only: a line that starts with `-` (`-r`, `-e`, `--index-url`) is refused, `requirements.txt line 3: options are not taken`.
- **Errors say what to do.** A version that does not exist, two lines that clash, or no network give pip's own `ERROR:` lines,
  then `Stairs was not installed. If this computer is offline, connect and try again, or run: "<python>" -m pip --python
  "<venv>\Scripts\python.exe" install -r "<folder>\requirements.txt"`; where the venv is not made yet, the command that makes it
  comes first: `"<python>" -m venv --without-pip "<venv>", then "<python>" -m pip ...`. The venv that was there is left as it was.
- **A change is built beside and swapped in.** When `requirements.txt` changes, the new venv is built next to the old one and
  replaces it once it works, with the tool's process stopped first; a failed install keeps the old venv working: the tool
  goes on running on it, ready as it was, and the failure lines (pip's own, a no to the question, an option line in the new
  file, a venv in use) come beside it, and the app writes them on the command line, after the first command of the tool, and in the log. A tool that has no venv yet is not started when its install fails: its run ends with the lines.
- **`opencad2d` is not installed in each venv.** A `.pth` file in the venv puts the app's own copy of the package and a base
  on the path, after the venv's own packages, so a tool may pin its own versions. The base, `python\base\cp314` for Python 3.14,
  holds pythonnet, clr_loader, cffi and pycparser, installed once for each Python version from the wheels the app ships in
  `python\wheels`, with no network and no question.

Measured on Python 3.14 (net48 test host, local wheels): the base 1.7 s once and 2.7 MB in 138 files; a venv with its `.pth`
75 to 98 ms and 0.54 MB in 19 files (`python -m venv` with pip: 4.0 s and 12 MB in 887 files); a tool with one package 2.1 s
(0.9 s for the list, the rest for the install); a tool already installed 0 ms; `import pythonnet, clr_loader` in a venv 0.1 s.

## What is not there yet

- `save()` inside a run makes two undo steps: the run's group ends at the save and opens again, so what was done before it is
  one step and what follows another.
- `Entity.dxf_type` is read from the text of the object (`str(net)`): a public DXF name on `DxfEntity` would touch the core
  library.
- Custom object snaps, plugin settings, a side panel, Properties rows and menus for a tool (the **not there** rows above).
- A script editor, completion (Ctrl+Space) and a debugger; a console for typing Python an entry at a time.
- A Python of the app's own: the app does not bundle one.
- Systems other than Windows: the app is Windows only.

## See also

- [Python](../python.md): the library and the presenter from outside the app, the stubs, numpy in blocks and undo groups.
- [Python examples](../python-examples.md): three tools for the app, with what each prints, and ten more scripts for outside it.
- [AutoLISP](lisp.md): the other language of the command line; its prompts and Esc as Python does them.
- [Command plugins](plugins.md): a command written in a .NET assembly.
- [Driving the app from another program](remote.md): the same `Drawing` from outside the app.
- [Settings, autosave and the window](app.md#python-in-the-window): the Python setting.
