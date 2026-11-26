using System;
using System.IO;
using System.Linq;
using System.Windows;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The sheet set dialogs of A12 4b that <c>--dialog</c> draws in screenshot mode (Sheet Set, Subset, Sheet and Custom
    /// Properties, Add Custom Property, New Sheet, Select Layout as Sheet Template, Rename &amp; Renumber and Import Layouts as
    /// Sheets), each in the state a board of the design draws. They are made over a sample sheet set "Example Tower" built in a
    /// folder of the temporary files (drawings made in code, never a drawing of anyone's), which is made again at each call, so
    /// that the paths a dialog shows are real ones and no dialog changes anything that matters.
    /// </summary>
    internal static class CadAppSheetSetDialogs
    {
        /// <summary>The names <c>--dialog</c> takes for these dialogs.</summary>
        internal static readonly string[] Names =
        {
            "sheetset",
            "sheetset-locked",
            "subset",
            "subset-refused",
            "sheet",
            "sheet-missing",
            "customprops",
            "addprop",
            "addprop-refused",
            "newsheet",
            "newsheet-exists",
            "sheettemplate",
            "renumber",
            "renumber-open",
            "importlayouts",
            "importlayouts-listed",
            "sheetlist",
            "sheetlist-sheets",
            "sheetlist-edit",
        };

        private const string Locked = "The sheet set is locked by h.le on WS-07.";

        /// <summary>Gets whether a <c>--dialog</c> names one of these dialogs.</summary>
        /// <param name="dialogName">The name given to <c>--dialog</c>.</param>
        /// <returns>True when it is one of <see cref="Names"/>, whatever its case.</returns>
        internal static bool Knows(string dialogName) =>
            dialogName != null && Names.Contains(dialogName, StringComparer.OrdinalIgnoreCase);

        /// <summary>Makes the window of a dialog over a sample sheet set built in a folder of the temporary files.</summary>
        /// <param name="dialogName">The name given to <c>--dialog</c>, one of <see cref="Names"/>.</param>
        /// <param name="window">The dialog's window, not shown.</param>
        /// <param name="root">The content of the window that is drawn.</param>
        /// <returns>True when the dialog was made.</returns>
        internal static bool TryCreate(string dialogName, out Window window, out FrameworkElement root)
        {
            string folder = Path.Combine(Path.GetTempPath(), "OpenCAD2D-sheetset-dialogs");
            return TryCreate(dialogName, folder, out window, out root);
        }

        /// <summary>Makes the window of a dialog over a sample sheet set built in a folder; the folder is emptied first.</summary>
        /// <param name="dialogName">The name given to <c>--dialog</c>, one of <see cref="Names"/>.</param>
        /// <param name="folder">The folder the sample is built in.</param>
        /// <param name="window">The dialog's window, not shown.</param>
        /// <param name="root">The content of the window that is drawn.</param>
        /// <returns>True when the dialog was made.</returns>
        internal static bool TryCreate(string dialogName, string folder, out Window window, out FrameworkElement root)
        {
            string kind = dialogName.ToLowerInvariant();
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);
            CadSheetSetFile file = Sample(folder, kind == "sheet-missing");
            SheetSetDialogOptions locked = new SheetSetDialogOptions { ReadOnlyReason = Locked };
            SheetSetDialogWindow dialog;
            switch (kind)
            {
                case "sheetset":
                    var set = new SheetSetPropertiesViewModel(file);
                    set.SelectedRow = set.Row("Sheet creation template");
                    dialog = new SheetSetPropertiesWindow(set);
                    break;
                case "sheetset-locked":
                    var lockedSet = new SheetSetPropertiesViewModel(file, locked);
                    lockedSet.SelectedRow = lockedSet.Row("Project number");
                    dialog = new SheetSetPropertiesWindow(lockedSet);
                    break;
                case "subset":
                    var fresh = SheetSetSubsetPropertiesViewModel.New(file, Subset(file, "Architectural"));
                    fresh.Row("Subset name").Text = "Interiors";
                    dialog = new SheetSetPropertiesWindow(fresh);
                    break;
                case "subset-refused":
                    var refused = SheetSetSubsetPropertiesViewModel.New(file, Subset(file, "Architectural"));
                    refused.Row("Subset name").Text = "Details";
                    dialog = new SheetSetPropertiesWindow(refused);
                    break;
                case "sheet":
                    dialog = new SheetSetPropertiesWindow(
                        new SheetSetSheetPropertiesViewModel(file, Sheet(file, "A-101"))
                    );
                    break;
                case "sheet-missing":
                    var missing = new SheetSetSheetPropertiesViewModel(file, Sheet(file, "A-202"));
                    missing.SelectedRow = missing.Row("Expected layout");
                    dialog = new SheetSetPropertiesWindow(missing);
                    break;
                case "customprops":
                    var list = new SheetSetCustomPropertiesViewModel(file);
                    list.SelectedItem = list.Items.Single(i => i.Name == "Drawn By");
                    dialog = new SheetSetCustomPropertiesWindow(list);
                    break;
                case "addprop":
                    var add = new SheetSetAddPropertyViewModel(new SheetSetCustomPropertiesViewModel(file));
                    add.Name = "Approved By";
                    add.DefaultValue = "T. Tran";
                    add.Owner = CadSheetSetPropertyFlags.Sheet;
                    dialog = new SheetSetAddPropertyWindow(add);
                    break;
                case "addprop-refused":
                    var used = new SheetSetAddPropertyViewModel(new SheetSetCustomPropertiesViewModel(file));
                    used.Name = "client";
                    dialog = new SheetSetAddPropertyWindow(used);
                    break;
                case "newsheet":
                    var sheet = new SheetSetNewSheetViewModel(file, Subset(file, "Architectural"), null);
                    sheet.Number = "A-104";
                    sheet.SheetTitle = "Roof Terrace Plan";
                    dialog = new SheetSetNewSheetWindow(sheet);
                    break;
                case "newsheet-exists":
                    var exists = new SheetSetNewSheetViewModel(file, Subset(file, "Architectural"), null);
                    exists.Number = "A-104";
                    exists.SheetTitle = "Roof Terrace Plan";
                    exists.FileName = "A-103 Roof Plan";
                    dialog = new SheetSetNewSheetWindow(exists);
                    break;
                case "sheettemplate":
                    dialog = new SheetSetTemplateWindow(
                        SheetSetTemplatePickViewModel.ForLayouts(file, file.SheetSet.Template)
                    );
                    break;
                case "renumber":
                    dialog = new SheetSetRenameWindow(new SheetSetRenameViewModel(file, Sheet(file, "A-202")));
                    break;
                case "renumber-open":
                    string open = Sheet(file, "A-102").Layout.File.Resolve(file.Folder);
                    var inSession = new SheetSetRenameViewModel(
                        file,
                        Sheet(file, "A-102"),
                        new SheetSetDialogOptions
                        {
                            IsOpenInSession = path => string.Equals(path, open, StringComparison.OrdinalIgnoreCase),
                        }
                    );
                    inSession.RenameFile = true;
                    dialog = new SheetSetRenameWindow(inSession);
                    break;
                case "importlayouts":
                    dialog = new SheetSetImportLayoutsWindow(
                        new SheetSetImportLayoutsViewModel(file, Subset(file, "Structural"))
                    );
                    break;
                case "sheetlist":
                case "sheetlist-sheets":
                case "sheetlist-edit":
                    dialog = new SheetListTableWindow(ListTable(file, kind));
                    break;
                default:
                    var listed = new SheetSetImportLayoutsViewModel(file, Subset(file, "Structural"));
                    listed.Load(Path.Combine(folder, "Drawings", "S-201 Slab.dwg"));
                    dialog = new SheetSetImportLayoutsWindow(listed);
                    break;
            }

            window = dialog;
            root = dialog.Body;
            return true;
        }

        /// <summary>
        /// Insert Sheet List Table as the board A12SheetListTable draws it: the sample set with the cover sheet G-001, the sheets A-501
        /// and S-501 added, the columns Sheet No., Sheet Title, Rev. and Drawn, the subsets as headers; the second picture is the
        /// Subsets and Sheets tab with S-501 unchecked, the third the dialog on a table's settings.
        /// </summary>
        private static SheetListTableViewModel ListTable(CadSheetSetFile file, string kind)
        {
            file.Change(set =>
            {
                set.NewSheet("G-001", "Cover Sheet");
                set.Subsets.Single(s => s.Name == "Architectural")
                    .Subsets.Single(s => s.Name == "Details")
                    .NewSheet("A-501", "Wall Sections");
                set.Subsets.Single(s => s.Name == "Structural").NewSheet("S-501", "Typical Details");
            });
            var settings = new CadSheetListTableSettings { Title = "Sheet List", IncludeSubsetHeaders = true };
            settings.Columns.Clear();
            settings.Columns.Add(CadSheetListColumn.Of(CadSheetListData.SheetNumber, "Sheet No."));
            settings.Columns.Add(CadSheetListColumn.Of(CadSheetListData.SheetTitle));
            settings.Columns.Add(CadSheetListColumn.Of(CadSheetListData.RevisionNumber, "Rev."));
            settings.Columns.Add(CadSheetListColumn.Custom("Drawn By", "Drawn"));
            var document = CadDocument.From(new DxfDatabase("AC1032"), "Example Tower sheets.dwg");
            var dialog = new SheetListTableViewModel(file, document, kind == "sheetlist-edit" ? settings : null);

            if (kind != "sheetlist-edit")
            {
                dialog.TitleText = settings.Title;
                dialog.IncludeSubsetHeaders = true;
                dialog.Columns.Clear();

                foreach (CadSheetListColumn column in settings.Columns)
                {
                    dialog.AddColumn();
                    SheetListColumnRow row = dialog.Columns.Last();
                    row.DataType = dialog.DataTypes.Single(d =>
                        d.Data == column.Data && d.CustomName == column.CustomName
                    );
                    row.Heading = column.Heading;
                }
            }

            dialog.SelectedColumn = dialog.Columns[2];

            if (kind == "sheetlist-sheets")
            {
                dialog.IsSubsetsTab = true;
                dialog.Nodes.Single(n => n.Text == "S-501 - Typical Details").IsChecked = false;
            }

            return dialog;
        }

        private static CadSheetSubset Subset(CadSheetSetFile file, string name) =>
            file.SheetSet.Subsets.Single(s => s.Name == name);

        private static CadSheetSetSheet Sheet(CadSheetSetFile file, string number) =>
            file.SheetSet.AllSheets().Single(s => s.Number == number);

        /// <summary>The sample set of the boards: Example Tower, with its template, its subsets, its sheets (A-202's drawing taken away for the board of a missing sheet) and a drawing of layouts to import, one of which belongs to another set.</summary>
        private static CadSheetSetFile Sample(string folder, bool dropElevations)
        {
            string setFolder = Path.Combine(folder, "Example Tower");
            Directory.CreateDirectory(setFolder);
            string template = Drawing(
                Path.Combine(folder, "Templates"),
                "OpenCAD2D-mm.dwt",
                "A1 sheet",
                "A3 sheet",
                "A1 sheet, no title block"
            );
            CadSheetSetFile file = CadSheetSetFile.Create(
                Path.Combine(setFolder, "Example Tower.dst"),
                "Example Tower",
                "Six-storey residential tower, 48 flats"
            );
            file.Change(set =>
            {
                set.Template = new CadDrawingReference(
                    CadFileReference.FromPath(template, setFolder),
                    "A1 sheet",
                    null,
                    null
                );
                set.PromptForTemplate = false;
                set.ProjectNumber = "ET-2026-014";
                set.ProjectName = "Example Tower";
                set.ProjectPhase = "Construction documents";
                set.ProjectMilestone = "Issued for tender";
                set.AddCustomProperty("Client", "Northbank Developments", CadSheetSetPropertyFlags.Set);
                set.AddCustomProperty("Project Address", "12 River Road, Riverside", CadSheetSetPropertyFlags.Set);
                set.AddCustomProperty("Drawn By", string.Empty, CadSheetSetPropertyFlags.Sheet);
                set.AddCustomProperty("Checked By", string.Empty, CadSheetSetPropertyFlags.Sheet);
                CadSheetSubset architectural = set.NewSubset("Architectural", null, true);
                architectural.NewSubset("Details", null, true);
                CadSheetSubset structural = set.NewSubset("Structural", null, true);
                architectural.NewSheet("A-101", "Ground Floor Plan");
                architectural.NewSheet("A-102", "First Floor Plan");
                architectural.NewSheet("A-103", "Roof Plan");
                architectural.NewSheet("A-202", "East and West Elevations");
                structural.NewSheet("S-101", "Foundation Plan");
            });
            if (dropElevations)
            {
                File.Delete(Sheet(file, "A-202").Layout.File.Resolve(setFolder));
            }

            string slab = Drawing(
                Path.Combine(folder, "Drawings"),
                "S-201 Slab.dwg",
                "S-201 Slab plan",
                "Pile layout",
                "Old issue"
            );
            CadSheetSetFile clinic = CadSheetSetFile.Create(
                Path.Combine(folder, "Riverside Clinic", "Riverside Clinic.dst").EnsureFolder(),
                "Riverside Clinic",
                null
            );
            clinic.Change(set => set.ImportLayouts(slab, new[] { "Old issue" }, false));
            return file;
        }

        private static string EnsureFolder(this string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return path;
        }

        /// <summary>A drawing made in code with the layouts named, a line in each and a circle in model space, written as a DWG.</summary>
        private static string Drawing(string folder, string file, params string[] layouts)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, file);
            var database = new DxfDatabase("AC1032");
            database.Layouts.Single(l => l.LayoutName == "Layout1").LayoutName = layouts[0];
            foreach (string name in layouts.Skip(1))
            {
                var layout = new DxfLayout { LayoutName = name };
                layout.AddToLayoutDictionary(database, null);
                layout.Initialize();
            }

            for (int i = 0; i < layouts.Length; i++)
            {
                database
                    .Layouts.Single(l => l.LayoutName == layouts[i])
                    .BlockTableRecord.AppendEntity(
                        new DxfLine(new GeoPoint3(10 * (i + 1), 0, 0), new GeoPoint3(10 * (i + 1), 50, 0))
                    );
            }

            database.ModelSpace.AppendEntity(new DxfCircle(new GeoPoint3(0, 0, 0), GeoVector3.ZAxis, 5));
            CadDocument.From(database, file).SaveAs(path);
            return path;
        }
    }
}
