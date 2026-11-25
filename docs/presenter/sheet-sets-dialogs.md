# Sheet set dialogs

The dialogs the Sheet Set Manager opens from its menus (A12). Each is a window of the same make as the other dialogs of
the app: a header with the title and the close cross, the form, and a footer with OK, Cancel and Help. Each takes the
sheet set's file and the part of the set it is for, saves the set itself through
[`CadSheetSetFile.Change`](xref:OpenCAD2D.SheetSets.CadSheetSetFile.Change(System.Action{OpenCAD2D.SheetSets.CadSheetSet})),
and writes the `.dst` once at OK.

A value the set cannot take is shown in red under the form, in the dialog's own words, and OK is greyed until it is
mended. Where the core refuses a change that the form could not know of (a drawing that cannot be saved without losing
something, a layout of the drawing that is named already), the same red line says why and the dialog stays open.

A locked set (another user holds it) opens a dialog read-only: every value is grey, the yellow line at the top says
"The sheet set is locked by h.le on WS-07. Nothing here can be changed.", and the footer has one Close and Help.

## Sheet Set Properties

Right-click the set, Properties… A grid in four sections, then the custom properties of the set and the defaults of the
custom properties of every sheet.

| Section | Rows |
|---|---|
| Sheet Set | Name, Sheet set data file (read-only), Description, Model view, Label block for views, Callout blocks (read-only), Page setup overrides file |
| Project Control | Project number, Project name, Project phase, Project milestone |
| Sheet Creation | Sheet storage location, Sheet creation template, Prompt for template |
| Sheet Set Custom Properties | the properties of the set, each with its value |
| Sheet Custom Properties | the properties of every sheet, with their default values (read-only here; a sheet's own value is set in its Sheet Properties) |

- A row clicked shows its description under the grid.
- Model view is one row of folders separated by semicolons; its ... button adds a folder.
- A row with a ... button picks its value: Sheet storage location a folder, Page setup overrides file a drawing template,
  Sheet creation template a layout of a drawing template (Select Layout as Sheet Template), Label block for views a block
  of a drawing.
- Callout blocks is shown as "(kept; not used by OpenCAD2D)": the list is saved as it was read, so AutoCAD still has it.
- Edit Custom Properties… opens Custom Properties over the dialog; the rows are read again when it saves.
- Only the rows that were edited are written; a set left as it was is not saved.

## Subset Properties

New Subset…, Rename Subset… and Properties… of a subset open one dialog, a grid of six rows: Subset name, Create Folder
Hierarchy, Publish Sheets in Subset, New Sheet Location, Sheet Creation Template and Prompt for Template.

- A new subset starts with its parent's folder, template and prompt, and writes only what the user changed, so what it
  left alone is still the parent's.
- A name that a sibling subset has (letter case ignored) is refused: "Architectural already holds a subset named
  Details."
- Create Folder Hierarchy Yes makes a folder at OK, named after the subset inside the parent's sheet location, and points
  New Sheet Location to it; the row then shows that folder and cannot be typed in. A name a folder cannot have is refused.
- Publish Sheets in Subset No leaves the sheets of the subset out of every publish (`CadSheetSubset.PublishSheets`).

## Sheet Properties

Right-click a sheet, Properties… Title, Number, Description, Include for publish, Category, Revision number, Revision
date, Issue purpose, Expected layout and Found layout, then the sheet's custom property values, then the Rename Options.

- Found layout reads "Not found" in red where the sheet's layout is gone, and the reason (the drawing does not exist, or
  has no such layout) is said under the grid. OK is not stopped by it.
- Expected layout's ... button picks a drawing and a layout in it; OK points the sheet to them, writes the sheet set mark
  into the drawing and saves it. A layout the set refuses (a `#` in its name, a sheet of another set) is refused in red.
- Rename Options: Rename layout to match sheet title (with Prefix with sheet number) and Rename drawing file to match
  sheet title. Changing the title or the number with a box on renames the layout, and the file, at OK, as Rename &
  Renumber Sheet does. A sheet whose layout is missing is not renamed in its drawing.

## Custom Properties and Add Custom Property

Edit Custom Properties… of Sheet Set Properties opens a list of Name, Default Value and Owner (Sheet Set or Sheet), with
Add… and Delete.

- A property is never edited, only deleted and added again.
- Add Custom Property takes a name, a default value and the owner. A name is refused where it is empty, holds a dot or a
  double quote (a field could not carry it) or a property has it already, letter case ignored: "A custom property named
  Client already exists in this sheet set."
- Delete asks first.
- Add and Delete are kept in the dialog until OK writes the set once: the deletes first (a property is taken out of the
  set and out of every sheet), then the adds (a property of every sheet is given, at its default, to every sheet).

## New Sheet

Right-click the set, a subset or a sheet, New Sheet… The sheet goes after the sheet it was asked from, else at the end of
the subset.

- Number (free text; empty is allowed), Sheet title, File name (it follows "Number Title" until it is typed in), Folder
  path and Sheet template (both read-only; the subset's, else the set's) and Open in drawing editor.
- An empty title is not accepted, a file name a file cannot have is refused, and a drawing that is there already is never
  overwritten: "A-103 Roof Plan.dwg already exists in this folder. Choose another file name, or Import Layout as Sheet to
  use that drawing."
- Where the subset's Prompt for template is Yes, Select Layout as Sheet Template comes first: a drawing template file
  name with a ... button and the list of that drawing's layouts in tab order.
- OK makes the drawing from the template layout, adds the sheet and tells the caller what it made, so that the palette can
  open the drawing where Open in drawing editor is on.

## Rename & Renumber Sheet

Number, Sheet title, Layout name, File name and Folder path (read-only), Rename layout to match sheet title and Rename
drawing file to match sheet title, each with Prefix with sheet number.

- While a Rename box is on, its name follows the number and the title as they are typed (the number first where Prefix is
  on). A name typed directly is kept until the number or the title changes again.
- A layout name or file name the drawing cannot have is refused: a `#`, a character a file cannot hold, a file that is
  there already.
- Previous and Next apply the changes first and show the neighbouring sheet in set order, so that a run of sheets can be
  renumbered in a row.
- A drawing that is open in the session is said so in yellow ("A-102 First Floor Plan.dwg is open in this session. It is
  saved, renamed and reopened under the new name at OK."); the saving and reopening is done by the palette's change.

## Import Layouts as Sheets

Right-click the set or a subset, Import Layout as Sheet… Browse for Drawings… chooses one drawing; its layouts are listed
in tab order with a check box each, all checked but the ones the set refuses (a `#` in the name, a sheet of this set
already, a sheet of another sheet set that is there). The reason of a refused layout is shown in yellow. Prefix sheet
titles with file name puts the drawing's name before the layout's name in the title. Import Checked adds a sheet for each
layout checked at the end of the subset, with no number, and writes the sheet set mark into the drawing.

## For the program that opens them

[`SheetSetDialogs`](xref:OpenCAD2D.Wpf.SheetSetDialogs) has one static method for each dialog. Each takes the window to show
over, the sheet set's file, the part of the set and a [`SheetSetDialogOptions`](xref:OpenCAD2D.Wpf.SheetSetDialogOptions):

- `ReadOnlyReason`, a sentence, where the set is locked;
- `Changer`, how a change is made: the palette gives its own, which saves and closes the open drawings a change rewrites
  and opens them again after, and the dialog hands it the edit, the drawings and the sheet that follows its drawing;
- `IsOpenInSession`, which says whether a drawing is open, for the yellow line of Rename & Renumber.

The screenshot tool draws them: `--dialog sheetset`, `sheetset-locked`, `subset`, `subset-refused`, `sheet`,
`sheet-missing`, `customprops`, `addprop`, `addprop-refused`, `newsheet`, `newsheet-exists`, `sheettemplate`, `renumber`,
`renumber-open`, `importlayouts` and `importlayouts-listed`, over a sample sheet set built in the temporary files.
