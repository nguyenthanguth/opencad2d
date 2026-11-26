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
- **The tabs** Sheet List, Sheet Views and Model Views, each with a toolbar of its own and a tree.
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
(the set) and Publish Dialog Box… (the Publish dialog over the set's sheets; see [Publish](#publish)). eTransmit… says on the command line that it is not built yet;
the dialogs are shown by the window, and with none (a program driving the palette) the command line says so.

- **Remove Subset** is grey while the subset holds sheets or subsets: move or remove them first.
- **Remove Sheet** asks first and never deletes the sheet's drawing: the sheet leaves the set and the drawing stays on disk.
- **Include for Publish** puts a sheet in or out of publishing (the sheet's words go grey, struck, where it is out).
- **Insert Sheet List Table…** is grey on Model and on a layout that is not a sheet of the current set; it opens the dialog described in [The sheet list table](#the-sheet-list-table).
- **Resave All Sheets** opens every sheet's drawing and saves it again so that the drawings and the set agree.

### A set that is read-only

A `.dst` whose file is read-only, or whose `.DBL` lock file (`Example Tower.DBL`) another program holds, opens read-only: a
lock shows before the name in the control, its tooltip says why, and the Status of the set says "Read-only: …". Every item that
changes the set is grey (New Sheet, New Subset, Import Layout as Sheet, Resave All Sheets, Insert Sheet List Table, and on a
sheet New Sheet, Rename & Renumber, Remove Sheet and Include for Publish), the rows cannot be dragged, and a change asked of
the model is refused with a line on the command line. Close Sheet Set, Publish, eTransmit, Manage Page Setups and Properties
work as before. The palette writes the `.DBL` of a set it opens for editing (see below), and deletes it when the set is closed.

## The Sheet Views tab

The tab lists the views placed on the sheets of the set: the **sheet views** the set keeps for each sheet (a plain viewport of a
layout is not one). The toolbar has **New View Category**, and two buttons that are a switch, **View by Category** and **View by
Sheet**.

By category the tree is the set, its view categories in the order the set has them, and the views in each in the order of the
sheets, with the number of the sheet a view is on in grey at the right; views with no category come last, directly under the
set. By sheet it is the sheets that hold views, in set order, and under each its views with the scale each was placed at in grey
("1:100"). A view reads "Number - Title". A category with no views has no arrow; the blank category a new set holds is not shown.

```text
Example Tower
  Plans
    1 - Ground Floor Plan       A-101
    1 - First Floor Plan        A-102
  Elevations
    1 - North Elevation         A-201
  2 - Entrance Detail           A-101
```

**Display** (the bold item of a view's menu, a double click, or Enter) opens the sheet's drawing on its layout and shows the
named view the sheet view points at. **New View Category…** asks the View Category dialog for a name, which must not be empty or
another category's (letter case ignored); the callout block list in it is greyed, as callout blocks are left out. **Rename…**
and **Properties…** of a category open the same dialog, **Remove Category** is grey while the category holds views. **Set
Category** on a view's menu lists the categories (the view's own ticked); it acts on every view selected, and a view dragged onto a
category (in View by Category) does the same. **Rename & Renumber…** shows the number and the title of the view, and **< Previous**
and **Next >** make the change first and go to the view before or after it in the tree as shown. A title must not be empty. Renaming a view changes the set only: the named view in the sheet's drawing keeps its name (not built yet). A set
that is read-only greys every change and keeps Display. **Place View Label Block** and **Properties…** of a view are grey: label
blocks are left out, and a view has no properties dialog yet.

## The Model Views tab

The tab lists the places a set takes drawings from: the **resource locations**, folders the set keeps (AutoCAD's Model view
list in the Sheet Set Properties). The toolbar has **Refresh** and **Add New Location**. The tree is the locations (a folder
that is not there is red), then the DWG files in each (not those in the folders under it), then the named views of model space of
each drawing, read from the closed file when the drawing is opened in the tree (a view saved in a layout is not listed). The
Details pane gives a view's drawing, folder, size in the drawing's unit ("32 000 × 18 500 mm") and when the file was saved.

**Add New Location…** asks for a folder and adds it with its relative path, so a set folder that moves keeps its locations;
a folder that is not there or is listed already is refused. **Remove Location** takes the folder out of the set and touches no
file. **Open** opens the drawing in the app; the presenter has no read-only drawings, so Open Read-Only opens it for editing and
says so. **See Model Space Views** of a drawing opens it in the tree, and **Expand**, **Collapse** and **Preview/Details Pane**
(the tick shows or hides the pane under the tree) are on every menu.

### Place on Sheet

**Place on Sheet** is on for a view or a drawing when the set can be changed and the layout current in the app is a sheet of
the set. It asks "Specify insertion point or [Scale]:" on the command line with the outline of the view at the scale following
the cursor; **Scale** lists the scales of the drawing's list (SCALELISTEDIT; the metric ones where the drawing keeps no list)
and takes a typed ratio. The default is the scale the view was last placed at, else 1:100. Dragging a view or a drawing from the
tab onto the drawing area places it at the point dropped, at the default scale. One undo step of the sheet's drawing does:

- **the xref:** the resource drawing is attached (Attach, not Overlay: 22 of the 25 xrefs in AutoCAD's own sample sheets are) as an external reference in model space of the sheet's drawing, with one INSERT at
  the origin; a drawing placed again reuses it. The path is relative to the sheet's drawing.
- **the viewport:** on the current layout, the size of the view at the scale (a view 16 000 by 8 000 at 1:100 is a viewport 160 by
  80), centred on the point, showing the view.
- **the named view:** a view saved in the layout round the viewport, named after the model view (a number is added where the
  drawing has the name), which is what the sheet view points at; AutoCAD's own sample sheets keep their sheet views the same way.
- **the sheet view:** recorded in the set on that sheet, with the next number of the sheet and the model view's name for a title.
  A drawing placed with no view shows all of model space and takes the file's name. The set is saved at once; the sheet's drawing
  is changed and not saved, as any edit is.

AutoCAD frames the named view a little wider than the viewport; ours is the viewport's own frame. The label block of the set is
not inserted.

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
table has a new handle, and what was typed in the old table's cells by hand is gone).

### Insert Sheet List Table

Right-click the set (or a sheet) in the Sheet List and pick **Insert Sheet List Table…** while the layout current is a sheet of the
set. The dialog has the table style at the left (its name, **...** for the Table Style dialog, a preview) with **Include Subsets as
Headers in Output**, and two tabs at the right.

- **Table Data.** **Title Text** is the first row of the table ("Sheet List Table" to begin with). The list has one line for each
  column, the top line being the left column: its **Data type** and its **Heading text**. A new table starts with Sheet Number and
  Sheet Title. Click a line to change them in place. The Data type list offers Sheet Number, Sheet Title, Sheet Description, Subset
  name, Revision number, Revision date, Issue purpose, Category, Drawing file name and Layout name, then the custom properties the
  sheets hold (Drawn By, Checked By); a heading follows the data type until you type one. **Add** puts a Sheet Number column at the
  end; **Remove**, **Move Up** and **Move Down** act on the selected line.
- **Subsets and Sheets.** The tree of the set, each part with a check box, all checked to begin with. Checking a subset checks
  everything in it; a subset with only some parts checked shows a square. A checked subset takes in the sheets added to it later
  when the table is updated; a sheet checked alone stays alone. **Populate From Sheet Selection** is shown grey: sheet selections
  are left out.

A table with no column, or with no sheet checked, is refused in the dialog's words and OK is grey. **OK** asks "Specify insertion
point:" on the command line and draws the table there in the layout current, as one undo step.

Right-click a cell of a sheet list table and its two items come first:

- **Update Sheet List Table** lists the set again as it is now, by the settings the table keeps: a sheet added since is listed, one
  removed is not. The table is made anew in the same place, style and column widths (one undo step, and a new handle). A cell typed
  in by hand is lost, and the command line says so: "Sheet list table updated from Example Tower: cells edited by hand were
  replaced." The set is read from the one open in the palette, else from the `.dst` the table names; where that file is gone the
  command line says so and the table stays.
- **Edit Sheet List Table Settings…** opens the same dialog, titled so, on the settings the table keeps. The table style is fixed
  (it is the table's own). OK lists the set by the new settings and keeps them in the table; a column left where it was keeps its
  width, a new one takes the width of its data type.

## From a program

The palette is `SheetSetManagerViewModel` (the logic) and `SheetSetManagerWindow` (the window) in `OpenCAD2D.Wpf`; the model
keeps the sets open and the one current while the palette is closed (`MainViewModel.SheetSetManager`), and
`MainViewModel.IsSheetSetShown` says whether it is shown (AutoCAD's SSMSTATE). Its menus are lists of `CadMenuItemViewModel`
(`MenuFor`, `ControlMenu`), and its operations are methods (`NewSubset`, `NewSheet`, `ImportLayouts`, `RenameAndRenumber`,
`Remove`, `Move`, `ResaveAllSheets`, `OpenSheets`; for the other tabs `NewViewCategory`, `SetCategory`,
`RenameAndRenumberView`, `DisplayView`, `AddLocation`, `RemoveLocation`, `PlaceOnSheet`), each of which says on the command line what the core refused.

The commands and the variables are in `CadCommands.SheetSetFiles.cs`; a command asks the window with the requests OPENSHEETSET (a
`CadSheetSetRequest` whose `Path` is the `.dst` to open, or null for the Open Sheet Set dialog), NEWSHEETSET (null: the wizard) and
SSLOCATE (the set found, and whether to show the palette), and SSMSTATE (answered "1" in `CadUiRequestEventArgs.Answer` while the palette
is shown). `CadCommandSession.SheetSetOptions` holds the variables (the app gives every session one), `CadCommandSession.LocateSheetSet`
is SSLOCATE and `CadCommandSession.SheetSetFound` SSFOUND. `CadSheetSetFile.OpenForEditing` opens a set the way the palette does, taking the
lock (`CadSheetSetLock`) or opening read-only (`IsReadOnly`, `ReadOnlyReason`, `LockedBy`), and `ReleaseLock` lets it go; `Change` refuses a
read-only set. `CadSheetSetExamples` lists the examples and `CadSheetListTable` makes and updates the table (`Update` takes new settings, as Edit Sheet List Table Settings gives them, and `ReadSetFile` says which set a table was made from); the window shows the table's dialog through `MainViewModel.ShowSheetListTable`.

The dialogs (New Sheet, Import, Rename & Renumber, the Properties and the Create
Sheet Set wizard), publishing and eTransmit are added by the packages that build them.
