# Sheet sets

A sheet set is a list of sheets kept in a `.dst` file, AutoCAD 2021's own format: each sheet is one layout of a drawing, the
sheets sit in subsets, and the set keeps their order. A set made here opens in AutoCAD, and one made in AutoCAD opens here.
The engine is [`CadSheetSetFile`](xref:OpenCAD2D.SheetSets.CadSheetSetFile) (open, change and save a set, and the
operations on it); this page is the Sheet Set Manager, the palette that shows a set and runs those operations.

## The Sheet Set Manager

SHEETSET shows the palette, a floating window beside the drawing that stays open until SHEETSETHIDE (or its cross) closes it.
It is not docked, as DesignCenter and the tool palettes are not. SSMSTATE, AutoCAD's variable for it, is 1 while the palette is
shown and 0 otherwise (it is read only).
From top to bottom the palette holds:

- **The sheet set control.** It reads **Open…** while no set is open, and the name of the current set afterwards. Its list
  holds the sets open (a tick by the current one), **Recent** (the last ten `.dst` files opened, newest first, shortened in
  the middle as `D:\...\Example Tower.dst` with the whole path as the tooltip, and greyed where the file is gone),
  **New Sheet Set…** (NEWSHEETSET) and **Open…** (the Open dialog for a `.dst`). Opening a set that is open already makes
  it current; closing the current one (**Close Sheet Set** on its menu) makes the one opened before it current. A lock before
  the name means the set is read-only (see below).
- **The tabs** Sheet List, Sheet Views and Model Views. Only Sheet List is built; the other two say so.
- **The toolbar** of the tab: Publish to PDF, Publish to Plotter, and the lists Publish Using Page Setup Override and
  Publish. They are grey while no set is open.
- **The tree** of the current set. With no set open it says "No sheet set is open. Pick New Sheet Set… or Open… in the
  list above."
- **Details and Preview** under the tree, with a splitter between: the switch picks one of the two.

## The Sheet List tab

The tree is the set, then its subsets and sheets in the order the set has, subsets nested as deep as the set goes. A sheet
reads "Number - Title" (the title alone when it has no number): a set of Example Tower with the cover sheet G-001 and, in a
subset Architectural, A-101 and A-102 lists as

```text
Example Tower
  G-001 - Cover Sheet
  Architectural
    A-101 - Ground Floor Plan
    A-102 - First Floor Plan
```

The icon of a sheet says what is known of its drawing: a green dot where the drawing is open in the app, a grey struck
icon and grey words where the sheet is left out of publishing, a dashed icon and red words where its drawing or its layout is
not found (the drawing is looked for by the layout's handle, then by its name, in the file the set names, whose relative path
is tried first, so a set folder that moved still finds its sheets). The arrow of a row, a double click or the Left and Right keys
close and open it. Ctrl and Shift select several rows. Where a set has 200 sheets the tree is made in about 0.2 seconds, and
looking at the sheets again (the palette does it each time it is activated) takes about 12 milliseconds, because a drawing is
read again only when its file's time stamp or size moved.

**Details** gives, for the first row selected, a set: Sheet set, Status, Data file, Sheets ("11 in 3 subsets") and the
description; a subset: Subset, Sheets and the description; a sheet: Sheet, Status (Available, Open in this session), Subset,
the description, File, Folder, Layout, Size and Last saved; and a sheet that is missing: Sheet, Status in red ("Not found: the
drawing file does not exist."), the drawing Expected, Layout and how To fix it. **Preview** draws the layout of the sheet
selected, white on its paper, from the drawing as it is on disk; a set, a subset or a missing sheet has no picture.

### Open, and the layout current

**Open** (the bold item of a sheet's menu, a double click, or Enter) opens the sheet's drawing in the app and makes the
sheet's layout current; a drawing that is open already is shown. With several sheets selected it opens them all. A sheet that
is missing is not opened (the item is grey). The presenter has no read-only drawings, so **Open Read-Only** opens the drawing
for editing and says so on the command line.

### Drag and drop

A sheet or a subset moves by dragging its row (several rows move together, keeping the order they have in the tree). On a
sheet the top half is before it and the bottom half after it; on a subset or the set the top fourth is before, the bottom
fourth after, and the middle puts the rows at the end of that group. A subset cannot go into itself or into what it holds, and
the set itself cannot be moved. Dragging A-102 before A-101 in the example above gives A-102, A-101; dropping G-001 on
Architectural puts it after A-102. A move is one change of the `.dst`, saved at once.

### The menus

Right-click a row: a row outside the selection is selected first; a row inside it acts on every row selected of its kind
(Open, Remove Sheet, Include for Publish and the like) and on the first for Rename & Renumber and Properties.

| Row | Items |
|---|---|
| Sheet set | Close Sheet Set; New Sheet…, New Subset…, Import Layout as Sheet…; Resave All Sheets; Publish ▸, eTransmit…; Insert Sheet List Table…, Manage Page Setups…, Plot Stamp Settings…; Properties… |
| Subset | New Sheet…, New Subset…, Import Layout as Sheet…, Remove Subset, Rename Subset…; Publish ▸, eTransmit…; Properties… |
| Sheet | **Open**, Open Read-Only; New Sheet…, Rename & Renumber…, Remove Sheet; Publish ▸, eTransmit…; Insert Sheet List Table…; Properties… |

**Publish ▸** holds Publish to PDF, Publish to Plotter, Publish Using Page Setup Override ▸ (the page setups of the set's
overrides file, grey where the set names none), then the check of the row (Publish in Reverse Order on the set; Publish
Sheets in Subset and Publish in Reverse Order on a subset; Include for Publish on a sheet), Sheet Set PDF Publish Options…
(the set) and Publish Dialog Box… (the Publish dialog over the set's sheets; see [Publish](#publish)). Items in the menus
that are not built yet say so on the command line: the New Sheet, New Subset, Import Layouts, Rename & Renumber and Properties
dialogs, Insert Sheet List Table and the Sheet Views and Model Views tabs. The operations those dialogs run are built.

- **Remove Subset** is grey while the subset holds sheets or subsets: move or remove them first.
- **Remove Sheet** asks first and never deletes the sheet's drawing: the sheet leaves the set and the drawing stays on disk.
- **Include for Publish** puts a sheet in or out of publishing (the sheet's words go grey, struck, where it is out).
- **Insert Sheet List Table…** is grey on Model and on a layout that is not a sheet of the current set.
- **Resave All Sheets** opens every sheet's drawing and saves it again so that the drawings and the set agree.

### A set that is read-only

A `.dst` whose file is read-only, or whose `.DBL` lock file (`Example Tower.DBL`) another program holds, opens read-only: a
lock shows before the name in the control, its tooltip says why, and the Status of the set says "Read-only: …". Every item that
changes the set is grey (New Sheet, New Subset, Import Layout as Sheet, Resave All Sheets, Insert Sheet List Table, and on a
sheet New Sheet, Rename & Renumber, Remove Sheet and Include for Publish), the rows cannot be dragged, and a change asked of
the model is refused with a line on the command line. Close Sheet Set, Publish, eTransmit, Manage Page Setups and Properties
work as before. The palette writes the `.DBL` of a set it opens for editing (see below), and deletes it when the set is closed.

## Publish

The Publish items on the Sheet List (set, subset and sheet menus, and the toolbar) publish the sheets under the rows
chosen, or the whole set when the set or nothing is chosen, in set order. A sheet is left out, and said so on the command
line, when it is not included for publish, or when it sits (at any depth) in a subset whose Publish Sheets in Subset is
cleared. Publish to PDF writes one PDF named after the set (or the name asked for) or, with Multi-sheet file off, a PDF
for each sheet named "Number Title.pdf", into the Location of Sheet Set PDF Publish Options (the folder of the .dst until
set). A set of three drawings in two subsets, one sheet not included, gives a PDF of the other three sheets in set order,
each page the size of its own sheet (ISO A3, A1 and A4 in the test). With Create bookmarks the PDF has AutoCAD's
bookmarks: "Sheets and Views", then one for each page ("A-101 Ground Floor Plan"), not nested by subset. Two sheets of
the same name give "A-101 Plan.pdf" and "A-101 Plan (2).pdf" where AutoCAD overwrites the first, and a character a file
name cannot hold becomes "_" where AutoCAD writes nothing.

Publish Using Page Setup Override publishes every sheet by a page setup of the page setup overrides file. Publish in
Reverse Order writes the pages, and their bookmarks, in the reverse of set order. Sheet Set PDF Publish Options keeps
Location, Prompt for name and Multi-sheet file in the .dst; Include hyperlinks, Create bookmarks and File name last for
the session. While it runs a small window shows "Sheet 4 of 9: A-103 Roof Plan"; Cancel stops at the next sheet, and a
PDF of several sheets that was not finished is not written. Publish to Plotter runs with no progress window.

## eTransmit

eTransmit… on the set, a subset or a sheet in the Sheet List sends the sheets under it as one zip. It opens Create
Transmittal. The Sheets tab shows the set, its subsets and its sheets with a box each; the ones you right-clicked are
ticked, and a sheet whose drawing is not found is red and cannot be ticked. The Files Table lists everything the ticked
sheets need, with where each file goes in the zip and its size; untick a file to leave it out. A file that was not found
is red and unchecked, and the report names it. Notes typed in the box go into the report. The Zip file box starts at
`Transmittals\<set name>.zip` beside the .dst; an existing zip is replaced after asking. View Report… shows
`transmittal.txt` as it would be written.

The zip holds the .dst, the drawing of each ticked sheet, the external references those drawings attach (and theirs),
their images, PDF underlays and data-link files, the sheet creation templates, the page setup overrides file, the plot
style tables the sheets' layouts name, font files found beside the drawings, and `transmittal.txt`. The folder of the
.dst is the root of the zip. A file under it keeps its place, so that a drawing in `Architectural` with an external
reference `..\Model\Tower model.dwg` finds it after the zip is unpacked. A file outside it, a photograph on another
drive for example, goes in `Files`, and a path to it that would no longer be right is written again, relative, in the
copy of the drawing (an external reference, an image, a PDF underlay) or of the .dst (the drawing of each sheet, the
templates, the overrides file). Your own files are never changed.

With Update fields in the copies first on, a drawing that holds fields is opened and saved again, and its fields are
evaluated as its FIELDEVAL says; a drawing the DWG writer would have to leave something out of is copied as it is, and
the report says so. The fonts Windows has and the ones OpenCAD2D carries are not sent, nor is a plot style table that is
one of OpenCAD2D's own (`acad.ctb`, `monochrome.ctb` and so on). Two runs over the same files give the same zip, byte
for byte, unless a drawing had to be saved again.

## The Create Sheet Set wizard

New Sheet Set… in the sheet set control (NEWSHEETSET) opens the wizard, a window of 660 by 470 with the pages of the route
down the left and Back, Next (Finish on the last page) and Cancel under it. **Nothing is written before Finish**: Back,
Cancel and the cross leave no file and no folder behind. Finish makes the `.dst`, opens the set in the palette (which puts it
first in Recent and shows the palette) and keeps your choices for the next time: the route, the folder and the import options.

**Begin** asks "Create a sheet set using" **An example sheet set** or **Existing drawings**, and the pages that follow depend
on it:

| Route | Pages |
|---|---|
| An example sheet set | Begin, Sheet Set Example, Sheet Set Details, Confirm |
| Existing drawings | Begin, Sheet Set Details, Choose Layouts, Confirm |

- **Sheet Set Example** lists the example sheet sets (the `.dst` files shipped with the program), sorted by name, or lets you
  browse to any `.dst`, AutoCAD's included. The box under the list shows the title, description and path of the one chosen. A
  file that is not there, or is no sheet set data file, is refused in red and Next stays grey. The new set copies the example's
  subsets (nested as they are), custom properties, sheet creation template, publish options and the parts this program does not
  model; its sheets are left out.
- **Sheet Set Details** takes the name (also the name of the `.dst`), an optional description and the folder the `.dst` is
  stored in (the last folder used, else `OpenCAD2D Sheet Sets` in Documents). The name starts as "New Sheet Set", and
  "New Sheet Set 1", "New Sheet Set 2" where that file is there already. A name empty or holding any of `\ / : * ? " < > |`, a
  folder that is not a whole path or is a file, and a `.dst` that exists already are each refused in red under the folder box,
  and Next stays grey until the line is clear. On the example route **Create a folder hierarchy based on subsets** makes a folder
  per subset (nested as the subsets are) and sets it as the subset's sheet location. **Sheet Set Properties…** edits the
  properties of the set being made on a draft kept in the temporary folder; Finish writes them into the set, and Cancel deletes
  the draft. Changing the route or the example drops the draft.
- **Choose Layouts** (existing drawings only) takes folders with **Browse…**: the DWG drawings in them and in the folders below
  are read without being opened in the app and listed as a tree of folder, drawing and layout, each with a box. A tick on a
  folder or a drawing ticks everything below it, and a box above shows what is below (a small square where some are ticked).
  Everything that can be a sheet starts ticked. A layout whose name holds a `#`, or that is a sheet of a set already (the
  drawing's mark names it), is grey, clear and cannot be ticked; the reason is on its right ("# in the name", "in Riverside
  Clinic.dst"). A folder with no drawing is not added, and a drawing that cannot be read is left out with its name on the
  red line. A folder is listed once, and a drawing already listed under an earlier folder is not listed again. Next stays grey
  until a layout is ticked.
- **Import Options…** has three boxes, all on at first and kept in the settings: **Prefix sheet titles with file name** (a
  title reads "A-201 Elevations - North and South" and not "North and South"), **Create subsets based on folder structure**
  (a subset per folder, nested as the folders are) and, under it, **Ignore top level folder** (the folder picked makes no
  subset of its own, and its drawings go to the top of the set). The number of every sheet is left empty, to fill with Rename &
  Renumber.
- **Confirm** shows the Sheet Set Preview: for an example, where the `.dst` goes, the example, the sheet storage location, the
  template, the folders to be made, the subsets and the custom properties; for drawings, the subsets and sheets exactly as
  Finish will make them, and "4 sheets in 2 subsets. Not imported: Sketch #2 (its name holds #)." Finish is the only button
  that writes. If the set cannot be made (a `.dst` that appeared meanwhile, a layout that became a sheet of another set) the
  reason is on the red line, in the engine's words, and nothing is left behind.

The wizard is `NewSheetSetViewModel` (the logic) and `NewSheetSetWindow` in `OpenCAD2D.Wpf`, and its Import Options dialog
`SheetImportOptionsViewModel` and `SheetImportOptionsWindow`. It makes the set through
[`CadSheetSetFile.CreateFromExample`](xref:OpenCAD2D.SheetSets.CadSheetSetFile) or `CreateFromDrawings`.

## What the operations do to the drawings

- **New Sheet** makes a drawing from the layout of the sheet creation template (the subset's, else the set's): every other layout
  and everything in model space is left out, the layout is renamed "Number Title" (A-101 and Ground Floor Plan give
  "A-101 Ground Floor Plan"), and the drawing is saved as a DWG of AutoCAD 2018, named the same, where the subset or the set
  puts new sheets (the folder of the `.dst` where neither says). A file of that name already there is refused. The sheet goes
  at the end of the set or subset picked, or after the sheet picked. With Open in drawing editor the drawing opens on its
  layout.
- **The sheet set mark.** A drawing that is made, imported, renamed or resaved here carries AutoCAD's `AcSheetSetData` record
  (the set's file, the drawing, the layout's name and handle, the version, how many times it was updated, and when), which
  AutoCAD reads to find the set of a drawing. A drawing holds one mark: the last sheet written.
- **Import Layout as Sheet** makes sheets of layouts of a DWG at the end of the group: no number, the layout's name as the title
  (the file name and a hyphen before it with Prefix sheet titles with file name). A layout whose name holds a `#`, or that is a
  sheet of this set already, or of another set that is there, is refused with the reason, and then nothing changes.
- **Rename & Renumber** with a new number or title writes the set only. With a new layout name or file name it renames the
  layout in the drawing or the drawing's file on disk, and the other sheets that are layouts of that drawing follow the file. A
  drawing that is **open in the app** is saved first (what was changed and not saved stays), closed, renamed, and opened again
  under its new name with the layout current. A file of the new name there already, or a layout of that name in the drawing,
  is refused with a line on the command line and the drawing is opened again as it was.
- **Import**, **Resave All Sheets** and **Rename & Renumber** do the same dance for a drawing they rewrite.
- **Remove Sheet** takes the sheet out of the set; the drawing is kept.

## Opening and making sets from the command line

- **OPENSHEETSET** shows the palette's Open Sheet Set dialog. With FILEDIA 0, or where there is no window, it asks "Enter name of
  sheet set to open:" (**-OPENSHEETSET** asks it whatever FILEDIA is, as AutoCAD 2021's own does): a `.dst` typed with its path, or with
  none one in the folder of the drawing; the extension is added where there is none. A file that is not there is answered "File can not
  be opened." and one that is not a sheet set data file "Sheet Set file is not valid." (AutoCAD's own words, read from its resources).
  The set is opened in the Sheet Set Manager and made current, and the palette is shown.
- **NEWSHEETSET** shows the Create Sheet Set wizard. AutoCAD 2021 has no typed form of it (its console does not know the command), so
  with FILEDIA 0, or where there is no window, the questions are ours, in the words of the wizard's pages: "Create a sheet set using
  [Example/Drawings] <Example>:". For an example it lists the example sets (see below) and asks "Select a sheet set to use as an
  example" (a number of the list, a name in it or the path of a `.dst`); for drawings it asks the drawings, separated by commas, whose
  layouts become the sheets (a layout that cannot be a sheet is said, with its reason, and left out). Then "Name of new sheet set:",
  "Description (optional):", "Store sheet set data file (.dst) here <...>:" (a folder that is not there is made on a Yes) and, for an
  example, "Create a folder hierarchy based on subsets [Yes/No] <No>:". A file of that name already there is answered "This file already
  exists. Please enter a different filename." and the name asked again. The set is made as `CadSheetSetFile.CreateFromExample` or
  `CreateFromDrawings` make it and opened in the Sheet Set Manager. Until the wizard is built the palette's New Sheet Set... runs these
  questions.

## The variables

| Variable | Meaning | Default and range |
|---|---|---|
| SSLOCATE | open the sheet set of a drawing when the drawing opens | 1; 0 or 1 ("Requires 0 or 1 only.") |
| SSMAUTOOPEN | show the palette when a sheet's drawing opens (needs SSLOCATE 1) | 1; 0 or 1 |
| SSMPOLLTIME | seconds between looks at the sheets' status while the palette is shown and SSMSHEETSTATUS is 2 | 60; 20 to 600 ("Requires an integer between 20 and 600.") |
| SSMSHEETSTATUS | when the sheets' status is looked at: 0 never, 1 when a set is loaded or refreshed, 2 as well on the timer | 2; 0 to 2 |
| SSMSTATE | 1 while the palette is shown | 0; read only |
| SSFOUND | the file of the sheet set found for the drawing | ""; read only |

The defaults, the ranges and the messages are those AutoCAD 2021 gave in its console. SSLOCATE, SSMAUTOOPEN, SSMPOLLTIME and
SSMSHEETSTATUS are the same in every drawing of the session and are kept in the settings file (`sheetSets` of `settings.json`); the
folder the wizard offers for the `.dst` (`OpenCAD2D Sheet Sets` in Documents unless the settings name another) is kept there too.

### A drawing that is a sheet

When a drawing is opened with SSLOCATE 1, the sheet set mark it carries (see "What the operations do to the drawings") names its set. The
file is looked for where the mark says and, if it is not there, as far from the drawing as the mark says it was, so that a set moved
with its sheets is found. SSFOUND holds the file, the set is opened in the Sheet Set Manager and made current, and with SSMAUTOOPEN 1
the palette is shown. A drawing with no mark, or whose set is gone or is no sheet set, finds nothing and SSFOUND stays empty.

## Who has a set open: the lock file

A set the palette opens for editing gets a lock file beside it, as AutoCAD writes one: `Example Tower.DBL` for `Example Tower.dst`, 58
bytes of text for the user InuYasha on the machine INUYASHA-MEV, with the lines ended `\r\r\n`:

```text
[UserInfo]
UserName=InuYasha
Machine=INUYASHA-MEV
(an empty line)
```

The file is deleted when the set is closed. A set whose lock names another user, or whose `.dst` is read-only, opens read-only: the lock
before its name in the control says "Read-only: Locked for Edit by SOMEONE on OTHER-PC." and every change is refused. A lock of this user
on this machine is taken as one's own, so that a lock left by a program that was killed does not shut its user out.

## The example sheet sets

The first route of the wizard lists the sets in the `SheetSetExamples` folder beside `OpenCAD2D.Core.dll`, which are our own, made by
our writer, none of AutoCAD's: **New Sheet Set** (no subsets, no sheets, no template: a new sheet asks for one) and **OpenCAD2D
Metric Sheet Set** (the subsets General, Plans and Details; the set properties Client and Project Address; the sheet properties
Drawn By and Checked By; the layout "A1 sheet" of `OpenCAD2D-sheets-mm.dwt`, beside them, as the sheet creation template, with no
question). The template is a drawing in millimetres with one layout, "A1 sheet" (841 by 594), a frame 10 in from the paper's edge and a
title block 180 by 54 at its bottom right corner whose attributes hold sheet set fields: the sheet's number and title, the set's name,
client and address, who drew and who checked. A set made from the metric example and a sheet made in it ("A-101", "Ground floor", in
Plans) gives a drawing `A-101 Ground floor.dwg` whose layout is "A-101 Ground floor" and whose title block shows A-101, Ground floor
and the set's name, with the fields bound to that sheet, so that a later renumbering reaches it.

## The sheet list table

`CadSheetListTable.Insert` puts a table in the current layout (never in model space): a title row ("Sheet List Table" unless it is
given another), a row of headings, then a row for each sheet in set order. With **Include subsets as headers** a row, merged across
the table, with the subset's name comes ahead of its sheets. The default columns are Sheet Number and Sheet Title; the data types are
Sheet Number, Sheet Title, Sheet Description, Subset, Revision Number, Revision Date, Issue Purpose, Category, Drawing File and Layout,
and a custom property of the sheets. A set of G-001 Cover, then Plans with A-101 Ground floor and A-102 First floor, then Details with
D-001 Door lists as 6 rows of a table (the title, the headings, 4 sheets), or as 8 with the headers:

```text
Sheet List Table
Sheet Number | Sheet Title
G-001        | Cover
Plans
A-101        | Ground floor
A-102        | First floor
Details
D-001        | Door
```

The cells are fields of the set bound to their sheet; the number and title ones carry AutoCAD's hyperlink switch
(`\href ".\A-101.dwg#,A-101#A-101#1"`: the sheet's drawing relative to the drawing the table is in, the layout, the words), so that
they follow a renumbering when the fields are updated (the table's Update Fields) and open the sheet. Which subsets and sheets are
listed is a list of checked parts: a sheet checked is listed, and so is every sheet of a checked subset, the ones added later too.

The settings are kept in the table itself, in its extension dictionary under the xrecord `OPENCAD2D_SHEETLISTTABLE`, our own: to AutoCAD
the table is a plain one whose fields still update. `CadSheetListTable.Update` lists the set again from them, after a sheet was added or
removed: it makes the table anew in the same corner, style and column widths and takes the old one's place, as one undo step (the new
table has a new handle, and what was typed in the old table's cells by hand is gone). The Insert Sheet List Table dialog and the table's
right-click items follow in a later package.

## From a program

The palette is `SheetSetManagerViewModel` (the logic) and `SheetSetManagerWindow` (the window) in `OpenCAD2D.Wpf`; the model
keeps the sets open and the one current while the palette is closed (`MainViewModel.SheetSetManager`), and
`MainViewModel.IsSheetSetShown` says whether it is shown (AutoCAD's SSMSTATE). Its menus are lists of `CadMenuItemViewModel`
(`MenuFor`, `ControlMenu`), and its operations are methods (`NewSubset`, `NewSheet`, `ImportLayouts`, `RenameAndRenumber`,
`Remove`, `Move`, `ResaveAllSheets`, `OpenSheets`), each of which says on the command line what the core refused.

The commands and the variables are in `CadCommands.SheetSetFiles.cs`; a command asks the window with the requests OPENSHEETSET (a
`CadSheetSetRequest` whose `Path` is the `.dst` to open, or null for the Open Sheet Set dialog), NEWSHEETSET (null: the wizard) and
SSLOCATE (the set found, and whether to show the palette), and SSMSTATE (answered "1" in `CadUiRequestEventArgs.Answer` while the palette
is shown). `CadCommandSession.SheetSetOptions` holds the variables (the app gives every session one), `CadCommandSession.LocateSheetSet`
is SSLOCATE and `CadCommandSession.SheetSetFound` SSFOUND. `CadSheetSetFile.OpenForEditing` opens a set the way the palette does, taking the
lock (`CadSheetSetLock`) or opening read-only (`IsReadOnly`, `ReadOnlyReason`, `LockedBy`), and `ReleaseLock` lets it go; `Change` refuses a
read-only set. `CadSheetSetExamples` lists the examples and `CadSheetListTable` makes and updates the table.

The sections of the Sheet Views and Model Views tabs, the dialogs (New Sheet, Import, Rename & Renumber, the Properties and the Create
Sheet Set wizard), publishing and eTransmit are added by the packages that build them.
