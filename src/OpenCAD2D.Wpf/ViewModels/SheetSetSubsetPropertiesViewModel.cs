using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Subset Properties, as the board A12SubsetProperties draws it, which New Subset…, Rename Subset… and Properties… of a
    /// subset all open: a grid of the six rows of AutoCAD 2021's help page (Subset name, Create Folder Hierarchy, Publish
    /// Sheets in Subset, New Sheet Location, Sheet Creation Template, Prompt for Template). A new subset starts with the
    /// parent's folder, template and prompt, and writes only what the user changed, so what it left alone is still the
    /// parent's. A name is refused where a sibling subset has it (letter case ignored), in red, with OK greyed. Create Folder
    /// Hierarchy Yes makes the folder at OK, named after the subset inside the parent's sheet location, and points New Sheet
    /// Location to it.
    /// </summary>
    public sealed class SheetSetSubsetPropertiesViewModel : SheetSetGridViewModel
    {
        private readonly string _parentId;
        private readonly string _subsetId;
        private readonly string _parentName;
        private readonly string _parentLocation;
        private CadDrawingReference _template;
        private bool _templateChanged;

        private SheetSetSubsetPropertiesViewModel(
            CadSheetSetFile file,
            SheetSetDialogOptions options,
            CadSheetSubset parent,
            CadSheetSubset subset
        )
            : base(file, options)
        {
            _parentId = parent.Id;
            _subsetId = subset?.Id;
            _parentName = parent is CadSheetSet set ? set.Name : parent.Name;
            _parentLocation = Location(parent, File.Folder);
            CadSheetSubset source = subset ?? parent;
            _template = Chain(source).Select(s => s.Template).FirstOrDefault(t => t != null);
            string location = Chain(source)
                .Select(s => s.NewSheetLocation)
                .Where(l => l != null)
                .Select(l => PathText(l, File.Folder))
                .FirstOrDefault();
            AddSection("Subset");
            AddText(
                "Subset name",
                subset?.Name ?? string.Empty,
                "The subset's name in the tree. With Create Folder Hierarchy Yes it is also the name of its folder under the parent's New Sheet Location."
            );
            AddYesNo(
                "Create Folder Hierarchy",
                false,
                "Yes makes a folder for the subset at OK, named after it inside the parent's New Sheet Location, and points New Sheet Location to it."
            );
            AddYesNo(
                "Publish Sheets in Subset",
                subset == null || subset.PublishSheets,
                "No leaves the sheets of this subset out of every publish."
            );
            AddPath(
                "New Sheet Location",
                location ?? string.Empty,
                "The folder a new sheet of this subset is saved in. It starts as the parent's.",
                BrowseLocation
            );
            AddPath(
                "Sheet Creation Template",
                DrawingText(_template, File.Folder),
                "The template layout a new sheet of this subset is made from. It starts as the parent's. The ... button opens Select Layout as Sheet Template.",
                PickSheetTemplate,
                true
            );
            AddYesNo(
                "Prompt for Template",
                (subset ?? parent).PromptForTemplate,
                "Yes asks which template layout to use each time a sheet is made in this subset."
            );
            SelectedRow = Row("Subset name");
        }

        /// <summary>Gets the dialog's title: "Subset Properties".</summary>
        public override string Title => Titled("Subset Properties");

        /// <summary>Gets the ID of the subset the dialog made or changed; null for a new subset that OK has not made yet.</summary>
        public string SubsetId { get; private set; }

        /// <summary>Gets whether the dialog is for a subset that is made at OK.</summary>
        public bool IsNew => _subsetId == null;

        /// <summary>Makes the dialog of New Subset…: a subset to be made inside a group.</summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="parent">The group the subset goes into (the sheet set itself or a subset of it).</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <returns>The dialog.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file or the parent is null.</exception>
        public static SheetSetSubsetPropertiesViewModel New(
            CadSheetSetFile file,
            CadSheetSubset parent,
            SheetSetDialogOptions options = null
        )
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            return new SheetSetSubsetPropertiesViewModel(file, options, parent, null);
        }

        /// <summary>Makes the dialog of Rename Subset… and Properties…: a subset that is in the set.</summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="subset">The subset (not the sheet set itself).</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <returns>The dialog.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file or the subset is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the subset is the sheet set, which has Sheet Set Properties.</exception>
        public static SheetSetSubsetPropertiesViewModel Existing(
            CadSheetSetFile file,
            CadSheetSubset subset,
            SheetSetDialogOptions options = null
        )
        {
            if (subset == null)
            {
                throw new ArgumentNullException(nameof(subset));
            }

            if (subset.Parent == null)
            {
                throw new ArgumentException(
                    "The sheet set has Sheet Set Properties, not Subset Properties.",
                    nameof(subset)
                );
            }

            return new SheetSetSubsetPropertiesViewModel(file, options, subset.Parent, subset);
        }

        /// <inheritdoc />
        protected override bool IsComplete => Row("Subset name").Text.Trim().Length > 0;

        /// <inheritdoc />
        protected override bool IsLocked(SheetSetGridRow row) =>
            row.Label == "New Sheet Location" && Rows.Count > 0 && Row("Create Folder Hierarchy").Text == "Yes";

        /// <inheritdoc />
        protected override void OnRowEdited(SheetSetGridRow row)
        {
            if (row.Label != "Subset name" && row.Label != "Create Folder Hierarchy")
            {
                return;
            }

            if (Row("Create Folder Hierarchy").Text == "Yes")
            {
                string name = Row("Subset name").Text.Trim();
                Row("New Sheet Location").Text = name.Length == 0 ? _parentLocation : FolderOf(name);
            }

            RaiseRowsReadOnly();
        }

        /// <inheritdoc />
        protected override string Validate()
        {
            SheetSetGridRow name = Row("Subset name");
            string text = name.Text.Trim();
            string refusal = null;
            if (text.Length > 0)
            {
                CadSheetSubset parent = File.SheetSet.Find(_parentId) as CadSheetSubset;
                CadSheetSubset sibling = parent?.Subsets.FirstOrDefault(s =>
                    s.Id != _subsetId && string.Equals(s.Name, text, StringComparison.OrdinalIgnoreCase)
                );
                if (sibling != null)
                {
                    refusal = _parentName + " already holds a subset named " + sibling.Name + ".";
                }
                else if (
                    Row("Create Folder Hierarchy").Text == "Yes"
                    && (text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || text.Trim('.', ' ').Length == 0)
                )
                {
                    refusal = "The name \"" + text + "\" cannot be the name of a folder.";
                }
            }

            name.HasError = refusal != null;
            return refusal;
        }

        /// <inheritdoc />
        protected override void Save()
        {
            string name = Row("Subset name").Text.Trim();
            bool folder = Row("Create Folder Hierarchy").Text == "Yes";
            bool publish = Row("Publish Sheets in Subset").Text == "Yes";
            SheetSetGridRow location = Row("New Sheet Location");
            SheetSetGridRow prompt = Row("Prompt for Template");
            Commit(set =>
            {
                CadSheetSubset parent = (CadSheetSubset)set.Find(_parentId);
                CadSheetSubset subset;
                if (_subsetId == null)
                {
                    subset = parent.NewSubset(name, null, folder);
                }
                else
                {
                    subset = (CadSheetSubset)set.Find(_subsetId);
                    if (!string.Equals(subset.Name, name, StringComparison.Ordinal))
                    {
                        subset.Name = name;
                    }

                    if (folder)
                    {
                        Directory.CreateDirectory(FolderOf(name));
                        subset.NewSheetLocation = CadFileReference.FromPath(FolderOf(name), File.Folder);
                    }
                }

                if (subset.PublishSheets != publish)
                {
                    subset.PublishSheets = publish;
                }

                if (!folder && location.IsEdited)
                {
                    subset.NewSheetLocation = ReferenceOf(location.Text, File.Folder);
                }

                if (_templateChanged)
                {
                    subset.Template = _template;
                }

                if (prompt.IsEdited)
                {
                    subset.PromptForTemplate = prompt.Text == "Yes";
                }

                SubsetId = subset.Id;
            });
        }

        private string FolderOf(string name) => Path.Combine(_parentLocation, name);

        /// <summary>The group and the groups it is in, up to the sheet set, nearest first.</summary>
        private static IEnumerable<CadSheetSubset> Chain(CadSheetSubset group)
        {
            for (CadSheetSubset at = group; at != null; at = at.Parent)
            {
                yield return at;
            }
        }

        /// <summary>The folder new sheets of a group are saved in: the nearest sheet location of the group or the groups above it; the folder of the .dst where none is set.</summary>
        private static string Location(CadSheetSubset group, string setFolder) =>
            Chain(group)
                .Select(s => s.NewSheetLocation)
                .Where(l => l != null)
                .Select(l => PathText(l, setFolder))
                .FirstOrDefault(p => p.Length > 0)
            ?? setFolder;

        private bool BrowseLocation()
        {
            SheetSetGridRow row = Row("New Sheet Location");
            string folder = AskFolder(row.Text);
            if (folder == null)
            {
                return false;
            }

            row.Text = folder;
            return true;
        }

        private bool PickSheetTemplate()
        {
            CadDrawingReference chosen = PickTemplate?.Invoke(_template);
            if (chosen == null)
            {
                return false;
            }

            _template = chosen;
            _templateChanged = true;
            Row("Sheet Creation Template").Text = DrawingText(chosen, File.Folder);
            return true;
        }
    }
}
