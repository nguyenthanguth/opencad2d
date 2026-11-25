using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>A drawing and a layout in it, as the Expected layout row of the Sheet Properties asks for them.</summary>
    public sealed class SheetSetLayoutChoice
    {
        /// <summary>Makes a choice.</summary>
        /// <param name="drawingFile">The path of the drawing, a DWG.</param>
        /// <param name="layoutName">The name of the layout in it.</param>
        /// <exception cref="ArgumentNullException">Thrown when either is null.</exception>
        public SheetSetLayoutChoice(string drawingFile, string layoutName)
        {
            DrawingFile = drawingFile ?? throw new ArgumentNullException(nameof(drawingFile));
            LayoutName = layoutName ?? throw new ArgumentNullException(nameof(layoutName));
        }

        /// <summary>Gets the path of the drawing.</summary>
        public string DrawingFile { get; }

        /// <summary>Gets the name of the layout.</summary>
        public string LayoutName { get; }
    }

    /// <summary>
    /// Sheet Properties, as the board A12SheetProperties draws it: the rows of AutoCAD 2021's help page (Title, Number,
    /// Description, Include for publish, Category, Revision number, Revision date, Issue purpose, Expected layout, Found
    /// layout), the sheet's custom property values, and the Rename Options (the same boxes as Rename &amp; Renumber Sheet). A
    /// sheet whose layout is not found reads Not found in red with the reason under the grid, and Expected layout's ... button
    /// re-associates it with a drawing and layout. Changing the title or the number with the Rename Options on renames the
    /// layout, and the drawing file where that box is on, at OK. Not every row is written, only the ones edited.
    /// </summary>
    public sealed class SheetSetSheetPropertiesViewModel : SheetSetGridViewModel
    {
        private readonly string _sheetId;
        private readonly CadFoundLayout _found;
        private readonly List<KeyValuePair<SheetSetGridRow, CadSheetSetPropertyFlags>> _custom =
            new List<KeyValuePair<SheetSetGridRow, CadSheetSetPropertyFlags>>();
        private readonly string _expectedFile;
        private SheetSetLayoutChoice _picked;
        private bool _renameLayout = true;
        private bool _prefixLayout = true;
        private bool _renameFile;
        private bool _prefixFile = true;

        /// <summary>Makes the dialog over a sheet of the set.</summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="sheet">The sheet.</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <exception cref="ArgumentNullException">Thrown when the file or the sheet is null.</exception>
        public SheetSetSheetPropertiesViewModel(
            CadSheetSetFile file,
            CadSheetSetSheet sheet,
            SheetSetDialogOptions options = null
        )
            : base(file, options)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }

            _sheetId = sheet.Id;
            sheet = (CadSheetSetSheet)File.SheetSet.Find(_sheetId) ?? sheet;
            Number = sheet.Number;
            SheetTitle = sheet.Title;
            _found = sheet.FindLayout();
            CadDrawingReference reference = sheet.Layout;
            _expectedFile = reference?.File.Locate(File.Folder);
            AddSection("Sheet");
            AddText("Title", sheet.Title, "The sheet's title.");
            AddText("Number", sheet.Number, "The sheet's number, free text.");
            AddText("Description", sheet.Description, "Free text. A field reads it: Sheet > Description.");
            AddYesNo(
                "Include for publish",
                sheet.IncludeForPublish,
                "No leaves the sheet out of the publish of its sheet set."
            );
            AddText(
                "Category",
                sheet.Category,
                "Free text; the view category views placed on the sheet go under by default."
            );
            AddText("Revision number", sheet.RevisionNumber, "Free text.");
            AddText("Revision date", sheet.RevisionDate, "Free text.");
            AddText("Issue purpose", sheet.IssuePurpose, "Free text.");
            AddPath(
                "Expected layout",
                DrawingText(reference, File.Folder),
                "The layout the sheet should be: a drawing and a layout in it. The ... button picks another.",
                PickExpected,
                true
            );
            SheetSetGridRow found = AddText(
                "Found layout",
                _found.IsMissing ? "Not found" : _found.LayoutName + " (" + _found.DrawingFile + ")",
                "The layout that was found for the sheet, by its handle and then its name.",
                true
            );
            found.IsRed = _found.IsMissing;
            if (_found.IsMissing)
            {
                string where = reference == null ? null : reference.File.Locate(File.Folder) ?? reference.File.FileName;
                Problem =
                    where == null
                        ? "The sheet has no layout yet. Pick the drawing and layout under Expected layout (...), or Cancel."
                    : _found.DrawingFile == null
                        ? "The drawing "
                            + where
                            + " does not exist. Pick the drawing and layout under Expected layout (...), or Cancel."
                    : "The drawing "
                        + where
                        + " has no layout \""
                        + reference.Name
                        + "\". Pick the drawing and layout under Expected layout (...), or Cancel.";
            }

            AddSection("Sheet Custom Properties");
            foreach (CadSheetSetProperty property in sheet.CustomProperties)
            {
                SheetSetGridRow row = AddText(
                    property.Name,
                    property.Value,
                    "This sheet's value of a custom property. A field reads it: Sheet > " + property.Name + "."
                );
                _custom.Add(new KeyValuePair<SheetSetGridRow, CadSheetSetPropertyFlags>(row, property.Flags));
            }
        }

        /// <summary>Gets the dialog's title: "Sheet Properties - A-101 Ground Floor Plan".</summary>
        public override string Title => Titled("Sheet Properties - " + CadSheetSetDrawings.Joined(Number, SheetTitle));

        /// <inheritdoc />
        public override bool HasDescriptionPane => false;

        /// <summary>Gets the sheet's number as the file has it, for the title.</summary>
        private string Number { get; }

        /// <summary>Gets the sheet's title as the file has it, for the title.</summary>
        private string SheetTitle { get; }

        /// <summary>Gets the red reason a sheet's layout is not found, with how to mend it; null where it is found. It does not stop OK.</summary>
        public string Problem { get; }

        /// <summary>Gets whether the red reason shows.</summary>
        public bool HasProblem => Problem != null;

        /// <summary>Gets or sets what the ... button of Expected layout asks: given the path of the sheet's drawing now (or null), it returns a drawing and a layout or null. The path is the one the sheet's drawing is at, or was expected at. The window sets it to a file dialog and a list of the layouts.</summary>
        public Func<string, SheetSetLayoutChoice> PickLayout { get; set; }

        /// <summary>Gets or sets whether the layout is renamed to match the sheet title when the title or the number is changed.</summary>
        public bool RenameLayout
        {
            get => _renameLayout;
            set
            {
                if (Set(ref _renameLayout, value))
                {
                    Raise(nameof(CanEditPrefixLayout));
                }
            }
        }

        /// <summary>Gets or sets whether the layout's new name starts with the sheet number.</summary>
        public bool PrefixLayoutWithNumber
        {
            get => _prefixLayout;
            set => Set(ref _prefixLayout, value);
        }

        /// <summary>Gets or sets whether the drawing file is renamed to match the sheet title when the title or the number is changed.</summary>
        public bool RenameFile
        {
            get => _renameFile;
            set
            {
                if (Set(ref _renameFile, value))
                {
                    Raise(nameof(CanEditPrefixFile));
                }
            }
        }

        /// <summary>Gets or sets whether the file's new name starts with the sheet number.</summary>
        public bool PrefixFileWithNumber
        {
            get => _prefixFile;
            set => Set(ref _prefixFile, value);
        }

        /// <summary>Gets whether the Rename Options can be changed: not on a locked set.</summary>
        public bool CanEditRenameOptions => !IsReadOnly;

        /// <summary>Gets whether the layout's Prefix with sheet number box is live: its parent box is on.</summary>
        public bool CanEditPrefixLayout => !IsReadOnly && _renameLayout;

        /// <summary>Gets whether the file's Prefix with sheet number box is live: its parent box is on.</summary>
        public bool CanEditPrefixFile => !IsReadOnly && _renameFile;

        /// <inheritdoc />
        protected override string Validate() => null;

        /// <inheritdoc />
        protected override void Save()
        {
            SheetSetGridRow title = Row("Title");
            SheetSetGridRow number = Row("Number");
            bool renamed = title.IsEdited || number.IsEdited;
            bool canRename = renamed && (!_found.IsMissing || _picked != null);
            string layoutName =
                canRename && _renameLayout
                    ? CadSheetSetSheet.DefaultLayoutName(_prefixLayout ? number.Text : string.Empty, title.Text)
                    : null;
            string fileName =
                canRename && _renameFile
                    ? CadSheetSetSheet.DefaultFileName(_prefixFile ? number.Text : string.Empty, title.Text)
                    : null;
            var drawings = new List<string>();
            if (_picked != null)
            {
                drawings.Add(_picked.DrawingFile);
            }
            else if ((layoutName != null || fileName != null) && _found.DrawingFile != null)
            {
                drawings.Add(_found.DrawingFile);
            }

            Commit(
                set =>
                {
                    var sheet = (CadSheetSetSheet)set.Find(_sheetId);
                    if (_picked != null)
                    {
                        sheet.Associate(_picked.DrawingFile, _picked.LayoutName);
                    }

                    if (layoutName != null || fileName != null)
                    {
                        sheet.RenameAndRenumber(number.Text, title.Text, layoutName, fileName);
                    }
                    else
                    {
                        if (title.IsEdited)
                        {
                            sheet.Title = title.Text;
                        }

                        if (number.IsEdited)
                        {
                            sheet.Number = number.Text;
                        }
                    }

                    SheetSetGridRow publish = Row("Include for publish");
                    if (publish.IsEdited)
                    {
                        sheet.IncludeForPublish = publish.Text == "Yes";
                    }

                    if (Row("Description").IsEdited)
                    {
                        sheet.Description = Row("Description").Text;
                    }

                    if (Row("Category").IsEdited)
                    {
                        sheet.Category = Row("Category").Text;
                    }

                    if (Row("Revision number").IsEdited)
                    {
                        sheet.RevisionNumber = Row("Revision number").Text;
                    }

                    if (Row("Revision date").IsEdited)
                    {
                        sheet.RevisionDate = Row("Revision date").Text;
                    }

                    if (Row("Issue purpose").IsEdited)
                    {
                        sheet.IssuePurpose = Row("Issue purpose").Text;
                    }

                    foreach (KeyValuePair<SheetSetGridRow, CadSheetSetPropertyFlags> custom in _custom)
                    {
                        if (custom.Key.IsEdited)
                        {
                            sheet.SetCustomProperty(custom.Key.Label, custom.Key.Text, custom.Value);
                        }
                    }
                },
                drawings,
                fileName != null ? _sheetId : null
            );
        }

        private bool PickExpected()
        {
            SheetSetLayoutChoice chosen = PickLayout?.Invoke(_found.DrawingFile ?? _expectedFile);
            if (chosen == null)
            {
                return false;
            }

            _picked = chosen;
            Row("Expected layout").Text = chosen.LayoutName + " (" + chosen.DrawingFile + ")";
            return true;
        }
    }
}
