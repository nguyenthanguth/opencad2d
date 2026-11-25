# Sheet sets

A sheet set is a list of sheets kept in a `.dst` file, AutoCAD 2021's own format: each sheet is one layout of a drawing, the
sheets sit in subsets, and the set keeps their order. A set made here opens in AutoCAD, and one made in AutoCAD opens here.
The engine is [`CadSheetSetFile`](xref:OpenCAD2D.SheetSets.CadSheetSetFile) (open, change and save a set, and the
operations on it); this page is the Sheet Set Manager, the palette that shows a set and runs those operations.

## The Sheet Set Manager

SHEETSET shows the palette, a floating window beside the drawing that stays open until SHEETSETHIDE (or its cross) closes it.
It is not docked, as DesignCenter and the tool palettes are not. SSMSTATE, AutoCAD's variable for it, is not built yet.
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
(the set) and Publish Dialog Box… (PUBLISH). Items in the menus that are not built yet say so on the command line ("Publish
to PDF is not built yet (A12 package 5a)."): the New Sheet, New Subset, Import Layouts, Rename & Renumber and Properties
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
work as before. The palette does not write a `.DBL` of its own yet.

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

## From a program

The palette is `SheetSetManagerViewModel` (the logic) and `SheetSetManagerWindow` (the window) in `OpenCAD2D.Wpf`; the model
keeps the sets open and the one current while the palette is closed (`MainViewModel.SheetSetManager`), and
`MainViewModel.IsSheetSetShown` says whether it is shown (AutoCAD's SSMSTATE). Its menus are lists of `CadMenuItemViewModel`
(`MenuFor`, `ControlMenu`), and its operations are methods (`NewSubset`, `NewSheet`, `ImportLayouts`, `RenameAndRenumber`,
`Remove`, `Move`, `ResaveAllSheets`, `OpenSheets`), each of which says on the command line what the core refused.
