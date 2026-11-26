using System;
using System.Collections.Generic;
using System.Linq;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Sheet Set Properties, as the board A12SheetSetProperties draws it and AutoCAD 2021's help names its rows: a property grid
    /// of Sheet Set (Name, the data file, Description, Model view, Label block for views, Callout blocks, Page setup overrides
    /// file), Project Control, Sheet Creation (Sheet storage location, Sheet creation template, Prompt for template) and the
    /// custom properties of the set, with Edit Custom Properties… under it. OK writes every value that was edited through
    /// <see cref="CadSheetSetFile.Change"/> in one change; a value not edited is not written, and a set left as it was is not
    /// saved at all.
    /// </summary>
    public sealed class SheetSetPropertiesViewModel : SheetSetGridViewModel
    {
        private const string TemplateFilter =
            "Drawing templates (*.dwt)|*.dwt|Drawings (*.dwg)|*.dwg|All files (*.*)|*.*";

        private readonly List<KeyValuePair<SheetSetGridRow, CadSheetSetPropertyFlags>> _custom =
            new List<KeyValuePair<SheetSetGridRow, CadSheetSetPropertyFlags>>();
        private CadDrawingReference _template;
        private CadDrawingReference _labelBlock;
        private bool _templateChanged;
        private bool _labelChanged;
        private SheetSetGridRow _firstCustom;

        /// <summary>Makes the dialog over a set, the grid read from the set as the file has it.</summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        public SheetSetPropertiesViewModel(CadSheetSetFile file, SheetSetDialogOptions options = null)
            : base(file, options)
        {
            CadSheetSet set = File.SheetSet;
            _template = set.Template;
            _labelBlock = set.LabelBlock;
            AddSection("Sheet Set");
            AddText(
                "Name",
                set.Name,
                "The name of the sheet set, shown at the top of the Sheet List and in the title of its dialogs.",
                options != null && options.NameReadOnly
            );
            AddText(
                "Sheet set data file",
                File.Path,
                "The .dst file that holds the sheet set. It is shown here and cannot be changed.",
                true
            );
            AddText(
                "Description",
                set.Description,
                "Free text kept in the .dst. A field reads it: SheetSet > Description."
            );
            AddPath(
                "Model view",
                string.Join("; ", set.ResourceLocations.Select(l => PathText(l, File.Folder))),
                "The folders the Model Views tab lists drawings from. Several folders are separated by a semicolon; the ... button adds one.",
                BrowseModelFolder
            );
            AddPath(
                "Label block for views",
                DrawingText(_labelBlock, File.Folder),
                "The block placed with a view that is put on a sheet from the Model Views tab: a drawing, then a block in it.",
                PickLabel,
                true
            );
            AddText(
                "Callout blocks",
                "(kept; not used by OpenCAD2D)",
                "Callout blocks are not used by OpenCAD2D. The list is kept as it is when the sheet set is saved, so AutoCAD still has it.",
                true
            );
            AddPath(
                "Page setup overrides file",
                PathText(set.PageSetupOverrides, File.Folder),
                "A drawing template whose page setups Publish Using Page Setup Override can apply to every sheet in place of its own.",
                BrowseOverrides
            );
            AddSection("Project Control");
            AddText("Project number", set.ProjectNumber, Project("ProjectNumber", "Project number"));
            AddText("Project name", set.ProjectName, Project("ProjectName", "Project name"));
            AddText("Project phase", set.ProjectPhase, Project("ProjectPhase", "Project phase"));
            AddText("Project milestone", set.ProjectMilestone, Project("ProjectMilestone", "Project milestone"));
            AddSection("Sheet Creation");
            AddPath(
                "Sheet storage location",
                PathText(set.NewSheetLocation, File.Folder),
                "The folder a new sheet's drawing is saved in. A subset can name a folder of its own.",
                BrowseLocation
            );
            AddPath(
                "Sheet creation template",
                DrawingText(_template, File.Folder),
                "The template layout new sheets are made from: a layout name and its DWT or DWG. The ... button opens Select Layout as Sheet Template.",
                PickSheetTemplate,
                true
            );
            AddYesNo(
                "Prompt for template",
                set.PromptForTemplate,
                "Yes asks which template layout to use each time a sheet is made. No always uses the sheet creation template."
            );
            BuildCustomRows();
            SelectedRow = Row("Name");
        }

        /// <summary>Gets the dialog's title: "Sheet Set Properties - Tower".</summary>
        public override string Title => Titled("Sheet Set Properties - " + File.SheetSet.Name);

        /// <summary>Gets or sets what the "..." button of Label block for views asks: given the block now named (or null), it returns the block chosen or null. The window sets it to a block picker.</summary>
        public Func<CadDrawingReference, CadDrawingReference> PickLabelBlock { get; set; }

        /// <summary>Gets or sets what Edit Custom Properties… opens: the Custom Properties dialog over the same file, which returns whether it changed the set. The window sets it.</summary>
        public Func<CadSheetSetFile, bool> ShowCustomProperties { get; set; }

        /// <summary>Gets whether Edit Custom Properties… is live: not on a locked set.</summary>
        public bool CanEditCustomProperties => !IsReadOnly && ShowCustomProperties != null;

        /// <summary>Opens the Custom Properties dialog, and reads the custom property rows again where it changed the set.</summary>
        public void EditCustomProperties()
        {
            if (IsReadOnly || ShowCustomProperties == null || !ShowCustomProperties(File))
            {
                return;
            }

            BuildCustomRows();
        }

        /// <inheritdoc />
        protected override string Validate()
        {
            SheetSetGridRow name = Row("Name");
            bool empty = name.Text.Trim().Length == 0;
            name.HasError = empty;
            return empty ? "A sheet set needs a name." : null;
        }

        /// <inheritdoc />
        protected override void Save()
        {
            string name = Row("Name").Text.Trim();
            SheetSetGridRow description = Row("Description");
            SheetSetGridRow model = Row("Model view");
            SheetSetGridRow overrides = Row("Page setup overrides file");
            SheetSetGridRow location = Row("Sheet storage location");
            SheetSetGridRow prompt = Row("Prompt for template");
            List<string> folders = model.Text.Split(';').Select(f => f.Trim()).Where(f => f.Length > 0).ToList();
            List<KeyValuePair<string, string>> projects = new[]
            {
                "Project number",
                "Project name",
                "Project phase",
                "Project milestone",
            }
                .Where(label => Row(label).IsEdited)
                .Select(label => new KeyValuePair<string, string>(label, Row(label).Text))
                .ToList();
            Commit(set =>
            {
                string folder = File.Folder;
                if (!string.Equals(name, set.Name, StringComparison.Ordinal))
                {
                    set.Name = name;
                }

                if (description.IsEdited)
                {
                    set.Description = description.Text;
                }

                if (model.IsEdited)
                {
                    for (int i = set.ResourceLocations.Count - 1; i >= 0; i--)
                    {
                        set.RemoveResourceLocationAt(i);
                    }

                    foreach (string path in folders)
                    {
                        set.AddResourceLocation(ReferenceOf(path, folder));
                    }
                }

                if (_labelChanged)
                {
                    set.LabelBlock = _labelBlock;
                }

                if (overrides.IsEdited)
                {
                    set.PageSetupOverrides = ReferenceOf(overrides.Text, folder);
                }

                foreach (KeyValuePair<string, string> project in projects)
                {
                    switch (project.Key)
                    {
                        case "Project number":
                            set.ProjectNumber = project.Value;
                            break;
                        case "Project name":
                            set.ProjectName = project.Value;
                            break;
                        case "Project phase":
                            set.ProjectPhase = project.Value;
                            break;
                        default:
                            set.ProjectMilestone = project.Value;
                            break;
                    }
                }

                if (location.IsEdited)
                {
                    set.NewSheetLocation = ReferenceOf(location.Text, folder);
                }

                if (_templateChanged)
                {
                    set.Template = _template;
                }

                if (prompt.IsEdited)
                {
                    set.PromptForTemplate = prompt.Text == "Yes";
                }

                foreach (KeyValuePair<SheetSetGridRow, CadSheetSetPropertyFlags> custom in _custom)
                {
                    if (custom.Key.IsEdited)
                    {
                        set.SetCustomProperty(custom.Key.Label, custom.Key.Text, custom.Value);
                    }
                }
            });
        }

        private static string Project(string field, string label) =>
            "A free text kept in the .dst ("
            + field
            + "). A field reads it: SheetSet > "
            + label
            + ", or CurrentSheetSet"
            + field
            + ".";

        /// <summary>Makes the rows of the custom properties again from the set, keeping what was typed in a value that is still there.</summary>
        private void BuildCustomRows()
        {
            var typed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<SheetSetGridRow, CadSheetSetPropertyFlags> custom in _custom)
            {
                if (custom.Key.IsEdited)
                {
                    typed[custom.Key.Label] = custom.Key.Text;
                }
            }

            if (_firstCustom != null)
            {
                RemoveRowsFrom(_firstCustom);
            }

            _custom.Clear();
            IReadOnlyList<CadSheetSetProperty> properties = File.SheetSet.CustomProperties;
            AddSection("Sheet Set Custom Properties");
            _firstCustom = Rows[Rows.Count - 1];
            foreach (CadSheetSetProperty property in properties.Where(p => p.IsSetProperty))
            {
                SheetSetGridRow row = AddText(
                    property.Name,
                    property.Value,
                    "A custom property of the sheet set. A field reads it: SheetSet > " + property.Name + "."
                );
                _custom.Add(new KeyValuePair<SheetSetGridRow, CadSheetSetPropertyFlags>(row, property.Flags));
                if (typed.TryGetValue(property.Name, out string text))
                {
                    row.Text = text;
                }
            }

            AddSection("Sheet Custom Properties");
            foreach (CadSheetSetProperty property in properties.Where(p => p.IsSheetProperty && !p.IsSetProperty))
            {
                AddText(
                    property.Name,
                    property.Value,
                    "A custom property of every sheet; this is its default value. A sheet's own value is set in its Sheet Properties.",
                    true
                );
            }

            Revalidate();
        }

        private bool BrowseModelFolder()
        {
            SheetSetGridRow row = Row("Model view");
            string last = row.Text.Split(';').Select(f => f.Trim()).LastOrDefault(f => f.Length > 0);
            string folder = AskFolder(last);
            if (folder == null)
            {
                return false;
            }

            row.Text = row.Text.Trim().Length == 0 ? folder : row.Text.Trim().TrimEnd(';').TrimEnd() + "; " + folder;
            return true;
        }

        private bool BrowseOverrides()
        {
            SheetSetGridRow row = Row("Page setup overrides file");
            string file = AskFile("Page setup overrides file", TemplateFilter, row.Text);
            if (file == null)
            {
                return false;
            }

            row.Text = file;
            return true;
        }

        private bool BrowseLocation()
        {
            SheetSetGridRow row = Row("Sheet storage location");
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
            Row("Sheet creation template").Text = DrawingText(chosen, File.Folder);
            return true;
        }

        private bool PickLabel()
        {
            CadDrawingReference chosen = PickLabelBlock?.Invoke(_labelBlock);
            if (chosen == null)
            {
                return false;
            }

            _labelBlock = chosen;
            _labelChanged = true;
            Row("Label block for views").Text = DrawingText(chosen, File.Folder);
            return true;
        }
    }
}
