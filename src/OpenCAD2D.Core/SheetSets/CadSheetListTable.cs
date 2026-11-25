using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.SheetSets
{
    /// <summary>What a column of a sheet list table shows of each sheet, the Data type list of the Insert Sheet List Table dialog.</summary>
    public enum CadSheetListData
    {
        /// <summary>The sheet's number (<c>A-101</c>), a hyperlink to the sheet.</summary>
        SheetNumber,

        /// <summary>The sheet's title, a hyperlink to the sheet.</summary>
        SheetTitle,

        /// <summary>The sheet's description.</summary>
        SheetDescription,

        /// <summary>The name of the subset the sheet is in; empty for a sheet in the set itself.</summary>
        SubsetName,

        /// <summary>The sheet's revision number.</summary>
        RevisionNumber,

        /// <summary>The sheet's revision date.</summary>
        RevisionDate,

        /// <summary>The sheet's issue purpose.</summary>
        IssuePurpose,

        /// <summary>The sheet's category.</summary>
        Category,

        /// <summary>The name of the sheet's drawing file (<c>A-101 Ground floor.dwg</c>); written as text, as no field of AutoCAD gives it.</summary>
        DrawingFileName,

        /// <summary>The name of the sheet's layout in its drawing; written as text, as no field of AutoCAD gives it.</summary>
        LayoutName,

        /// <summary>A custom property of the sheets, the column's <see cref="CadSheetListColumn.CustomName"/>.</summary>
        CustomProperty,
    }

    /// <summary>A column of a sheet list table: what it shows and the heading above it.</summary>
    public sealed class CadSheetListColumn
    {
        private CadSheetListColumn(CadSheetListData data, string heading, string customName)
        {
            Data = data;
            Heading = heading;
            CustomName = customName;
        }

        /// <summary>Gets what the column shows.</summary>
        public CadSheetListData Data { get; }

        /// <summary>Gets the heading text above the column.</summary>
        public string Heading { get; }

        /// <summary>Gets the name of the custom property of a <see cref="CadSheetListData.CustomProperty"/> column; empty for any other.</summary>
        public string CustomName { get; }

        /// <summary>Makes a column of a data type, its heading the type's own words ("Sheet Number").</summary>
        /// <param name="data">What it shows; not <see cref="CadSheetListData.CustomProperty"/>, which <see cref="Custom"/> makes.</param>
        /// <returns>The column.</returns>
        /// <exception cref="ArgumentException">Thrown when the data type is a custom property.</exception>
        public static CadSheetListColumn Of(CadSheetListData data)
        {
            Guard.Argument(
                data != CadSheetListData.CustomProperty,
                nameof(data),
                "A column of a custom property is made with Custom."
            );
            return new CadSheetListColumn(data, HeadingOf(data), string.Empty);
        }

        /// <summary>Makes a column of a data type under a heading of the caller's.</summary>
        /// <param name="data">What it shows; not <see cref="CadSheetListData.CustomProperty"/>.</param>
        /// <param name="heading">The heading text.</param>
        /// <returns>The column.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the heading is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the data type is a custom property.</exception>
        public static CadSheetListColumn Of(CadSheetListData data, string heading)
        {
            if (heading == null)
            {
                throw new ArgumentNullException(nameof(heading));
            }

            Guard.Argument(
                data != CadSheetListData.CustomProperty,
                nameof(data),
                "A column of a custom property is made with Custom."
            );
            return new CadSheetListColumn(data, heading, string.Empty);
        }

        /// <summary>Makes a column of a custom property of the sheets.</summary>
        /// <param name="name">The property's name.</param>
        /// <param name="heading">The heading text; null for the property's name.</param>
        /// <returns>The column.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the name is empty.</exception>
        public static CadSheetListColumn Custom(string name, string heading = null)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Guard.Argument(name.Trim().Length > 0, nameof(name), "A custom property column needs the property's name.");
            return new CadSheetListColumn(CadSheetListData.CustomProperty, heading ?? name, name);
        }

        private static string HeadingOf(CadSheetListData data)
        {
            switch (data)
            {
                case CadSheetListData.SheetNumber:
                    return "Sheet Number";
                case CadSheetListData.SheetTitle:
                    return "Sheet Title";
                case CadSheetListData.SheetDescription:
                    return "Sheet Description";
                case CadSheetListData.SubsetName:
                    return "Subset";
                case CadSheetListData.RevisionNumber:
                    return "Revision Number";
                case CadSheetListData.RevisionDate:
                    return "Revision Date";
                case CadSheetListData.IssuePurpose:
                    return "Issue Purpose";
                case CadSheetListData.Category:
                    return "Category";
                case CadSheetListData.DrawingFileName:
                    return "Drawing File";
                default:
                    return "Layout";
            }
        }
    }

    /// <summary>
    /// The settings of a sheet list table, what the Insert Sheet List Table dialog sets: the title text, whether the subsets
    /// head their sheets, the columns, and which subsets and sheets are listed. A table keeps them (<see cref="CadSheetListTable"/>)
    /// so that Update Sheet List Table lists the set again as it was set up.
    /// </summary>
    public sealed class CadSheetListTableSettings
    {
        /// <summary>Gets or sets the title text, the first row of the table: "Sheet List Table" by default.</summary>
        public string Title { get; set; } = "Sheet List Table";

        /// <summary>Gets or sets whether a row with the subset's name heads the sheets of each subset (Include subsets as headers in output).</summary>
        public bool IncludeSubsetHeaders { get; set; }

        /// <summary>Gets the columns, left to right: Sheet Number and Sheet Title to begin with.</summary>
        public IList<CadSheetListColumn> Columns { get; } =
            new List<CadSheetListColumn>
            {
                CadSheetListColumn.Of(CadSheetListData.SheetNumber),
                CadSheetListColumn.Of(CadSheetListData.SheetTitle),
            };

        /// <summary>
        /// Gets or sets the IDs of the subsets and sheets that are checked in the Subsets and Sheets tab; null for the whole set. A
        /// checked sheet is listed, and so is every sheet of a checked subset, those added later too ("a checked subset picks up
        /// new sheets").
        /// </summary>
        public IList<string> CheckedIds { get; set; }
    }

    /// <summary>
    /// The sheet list table of a sheet set (Insert Sheet List Table): a table of the current style with a title row, a row of
    /// headings, then a row for each sheet in set order, a merged row with the subset's name ahead of its sheets where the subsets
    /// are headers. The number and title cells are fields of the set bound to the sheet
    /// (<c>\AcSm Database("…").SheetSet("g…").Component("g…").Number</c>) carrying AutoCAD's hyperlink switch
    /// (<c>\href ".\A-101.dwg#,Ground floor#A-101#1"</c>), so that they follow a renumbering and open the sheet; the other cells are
    /// bound fields too, but the drawing file and layout names, which are text.
    /// </summary>
    /// <remarks>
    /// The settings are kept in the table's extension dictionary under the xrecord <see cref="RecordName"/>, our own: to AutoCAD
    /// the table is a plain one whose fields still update. <see cref="Update"/> lists the set again from them, in a new table of
    /// the same place, style and column widths in the same undo step, the words typed in its cells by hand gone.
    /// </remarks>
    public static class CadSheetListTable
    {
        /// <summary>The name of the xrecord in the table's extension dictionary that keeps the settings.</summary>
        public const string RecordName = "OPENCAD2D_SHEETLISTTABLE";

        private const string Version = "OPENCAD2D-SHEETLISTTABLE 1";

        /// <summary>
        /// Makes the table at a corner of the current layout, in the table style named (the drawing's current one where null; Standard
        /// where it has none of that name), as one undo step.
        /// </summary>
        /// <param name="document">The drawing; its current space is a layout.</param>
        /// <param name="file">The sheet set to list.</param>
        /// <param name="settings">The settings; copied, so that changing them later changes no table.</param>
        /// <param name="corner">The top left corner of the table.</param>
        /// <param name="tableStyle">The name of the table style; null for the current one.</param>
        /// <returns>The handle of the table.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document, the set or the settings are null.</exception>
        /// <exception cref="ArgumentException">Thrown when the settings have no column.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the current space is model space, or the table could not be made (the message says why).</exception>
        public static string Insert(
            CadDocument document,
            CadSheetSetFile file,
            CadSheetListTableSettings settings,
            GeoPoint2 corner,
            string tableStyle = null
        )
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Guard.Argument(settings.Columns.Count > 0, nameof(settings), "A sheet list table needs a column.");

            if (document.CurrentSpace == null || document.CurrentSpace.IsModel)
            {
                throw new InvalidOperationException("A sheet list table is put in a layout, not in model space.");
            }

            document.EnsureNamedObjectsDictionary();
            CadTableStyleEditor.EnsureStandard(document.Database);
            DxfTableStyle style = CadTableStyleEditor.Find(
                document.Database,
                tableStyle ?? CadTableStyleEditor.Current(document.Database)
            ) ?? CadTableStyleEditor.Find(document.Database, "Standard");
            double[] widths = settings.Columns.Select(c => WidthOf(c.Data)).ToArray();
            return Make(document, file, Copy(settings), corner, style, widths, null);
        }

        /// <summary>
        /// Lists the set again in a table made by <see cref="Insert"/>, as Update Sheet List Table does: the rows are made anew from
        /// the settings the table keeps and the set as it is now, in a table of the same corner, style and column widths that takes
        /// the old one's place (one undo step), so that a sheet added since is listed and one removed is not.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="handle">The handle of the table.</param>
        /// <param name="file">The sheet set; null to open the one the table names.</param>
        /// <returns>The handle of the new table.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document or the handle is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the handle is not that of a sheet list table of the drawing.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the table names a set whose file is no longer there and none is given.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the table could not be made again (the message says why).</exception>
        public static string Update(CadDocument document, string handle, CadSheetSetFile file = null)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (handle == null)
            {
                throw new ArgumentNullException(nameof(handle));
            }

            DxfTable old = document.FindEntity(handle) as DxfTable;
            Kept kept = old == null ? null : Read(old);
            Guard.Argument(kept != null, nameof(handle), "The object " + handle + " is not a sheet list table.");

            if (file == null)
            {
                if (!File.Exists(kept.SetFile))
                {
                    throw new FileNotFoundException(
                        "The sheet set " + kept.SetFile + " of the sheet list table is not there.",
                        kept.SetFile
                    );
                }

                file = CadSheetSetFile.Open(kept.SetFile);
            }

            CadTableStyleEditor.EnsureStandard(document.Database);
            DxfTableStyle style = CadTableStyleEditor.Find(document.Database, old.TableStyleName ?? "Standard")
                ?? CadTableStyleEditor.Find(document.Database, "Standard");
            double[] widths = Enumerable.Range(0, old.NumColumns).Select(old.ColumnWidth).ToArray();
            GeoPoint3 position = old.Position;
            return Make(document, file, kept.Settings, new GeoPoint2(position.X, position.Y), style, widths, old);
        }

        /// <summary>Reads the settings a sheet list table keeps.</summary>
        /// <param name="document">The drawing.</param>
        /// <param name="handle">The handle of the table.</param>
        /// <returns>The settings; null where the object is not a sheet list table of this program.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document or the handle is null.</exception>
        public static CadSheetListTableSettings ReadSettings(CadDocument document, string handle)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (handle == null)
            {
                throw new ArgumentNullException(nameof(handle));
            }

            DxfTable table = document.FindEntity(handle) as DxfTable;
            return table == null ? null : Read(table)?.Settings;
        }

        private static string Make(
            CadDocument document,
            CadSheetSetFile file,
            CadSheetListTableSettings settings,
            GeoPoint2 corner,
            DxfTableStyle style,
            double[] widths,
            DxfTable replaced
        )
        {
            List<Row> rows = Rows(file.SheetSet, settings);
            int columns = settings.Columns.Count;
            double margin = style.VerticalCellMargin;
            double Height(DxfRowType kind) => style.TextHeight(kind) * 4.0 / 3.0 + 2.0 * margin;
            string made = null;
            IReadOnlyList<CadEditRecord> planned =
                replaced == null ? null : new[] { new CadEditRecord(CadEditRecordKind.Erased, replaced.Handle) };
            string drawingFolder = HasFolder(document) ? Path.GetDirectoryName(document.FileName) : null;
            CadEditResult result = document.ApplyEdit("Sheet list table", planned, s =>
            {
                var table = new DxfTable(
                    new GeoPoint3(corner.X, corner.Y, 0.0),
                    rows.Count + 2,
                    columns,
                    Height(DxfRowType.DataRow),
                    widths[0]
                );
                table.SetDatabaseDefaults(s.Database);
                s.Append(document.CurrentSpace, table);
                table.TableStyle = style;
                table.SetRowHeight(0, Height(DxfRowType.TitleRow));
                table.SetRowHeight(1, Height(DxfRowType.HeaderRow));

                for (int c = 0; c < columns; c++)
                {
                    table.SetColumnWidth(c, widths[c]);
                }

                Fill(table, file, settings, rows, drawingFolder);
                Keep(table, file, settings);
                CadTableEditor.UpdateFields(document, table);
                CadTableEditor.Redraw(table, document);

                if (replaced != null)
                {
                    DxfEntity gone = s.Document.FindEntity(replaced.Handle);
                    gone?.Erase();
                }

                made = table.Handle;
            });

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Message);
            }

            return made;
        }

        /// <summary>The rows of the table below the headings, in set order.</summary>
        private sealed class Row
        {
            internal CadSheetSubset Subset;
            internal CadSheetSetSheet Sheet;
        }

        private static List<Row> Rows(CadSheetSet set, CadSheetListTableSettings settings)
        {
            var rows = new List<Row>();
            Walk(set, settings, false, rows);
            return rows;
        }

        private static void Walk(
            CadSheetSubset group,
            CadSheetListTableSettings settings,
            bool checkedAbove,
            List<Row> rows
        )
        {
            foreach (CadSheetComponent child in group.Children)
            {
                bool on = checkedAbove || settings.CheckedIds == null || settings.CheckedIds.Contains(child.Id);

                if (child is CadSheetSetSheet sheet)
                {
                    if (on)
                    {
                        rows.Add(new Row { Sheet = sheet });
                    }

                    continue;
                }

                var subset = (CadSheetSubset)child;
                var inside = new List<Row>();
                Walk(subset, settings, on, inside);

                if (inside.Count == 0)
                {
                    continue;
                }

                if (settings.IncludeSubsetHeaders)
                {
                    rows.Add(new Row { Subset = subset });
                }

                rows.AddRange(inside);
            }
        }

        private static void Fill(
            DxfTable table,
            CadSheetSetFile file,
            CadSheetListTableSettings settings,
            List<Row> rows,
            string drawingFolder
        )
        {
            int columns = settings.Columns.Count;
            table.SetTextString(0, 0, settings.Title);

            if (columns > 1)
            {
                table.MergeCells(new DxfCellRange(0, 0, 0, columns - 1));
            }

            for (int c = 0; c < columns; c++)
            {
                table.SetTextString(1, c, settings.Columns[c].Heading);
            }

            for (int r = 0; r < rows.Count; r++)
            {
                int row = r + 2;
                Row one = rows[r];

                if (one.Subset != null)
                {
                    table.SetTextString(row, 0, one.Subset.Name);

                    if (columns > 1)
                    {
                        table.MergeCells(new DxfCellRange(row, 0, row, columns - 1));
                    }

                    continue;
                }

                for (int c = 0; c < columns; c++)
                {
                    FillCell(table, row, c, file, one.Sheet, settings.Columns[c], drawingFolder);
                }
            }
        }

        private static void FillCell(
            DxfTable table,
            int row,
            int column,
            CadSheetSetFile file,
            CadSheetSetSheet sheet,
            CadSheetListColumn data,
            string drawingFolder
        )
        {
            CadDrawingReference layout = sheet.Layout;
            string drawing = layout?.File.Resolve(file.Folder);

            switch (data.Data)
            {
                case CadSheetListData.DrawingFileName:
                    table.SetTextString(row, column, drawing == null ? string.Empty : Path.GetFileName(drawing));
                    return;
                case CadSheetListData.LayoutName:
                    table.SetTextString(row, column, layout == null ? string.Empty : layout.Name);
                    return;
                case CadSheetListData.SubsetName:
                    if (sheet.Parent == null || sheet.Parent is CadSheetSet)
                    {
                        return;
                    }

                    table.SetFieldCode(row, column, 0, Field(file, sheet.Parent.Id, "Name", null));
                    return;
            }

            string property = PropertyOf(data);
            string link = null;

            if (
                (data.Data == CadSheetListData.SheetNumber || data.Data == CadSheetListData.SheetTitle)
                && drawing != null
            )
            {
                string path =
                    drawingFolder == null
                        ? drawing
                        : CadFileReference.FromPath(drawing, drawingFolder).RelativeFileName ?? drawing;
                string shown = data.Data == CadSheetListData.SheetNumber ? sheet.Number : sheet.Title;
                link = path + "#," + layout.Name + "#" + shown + "#1";
            }

            table.SetFieldCode(row, column, 0, Field(file, sheet.Id, property, link));
        }

        private static string PropertyOf(CadSheetListColumn column)
        {
            switch (column.Data)
            {
                case CadSheetListData.SheetNumber:
                    return "Number";
                case CadSheetListData.SheetTitle:
                    return "Title";
                case CadSheetListData.SheetDescription:
                    return "Description";
                case CadSheetListData.RevisionNumber:
                    return "RevisionNumber";
                case CadSheetListData.RevisionDate:
                    return "RevisionDate";
                case CadSheetListData.IssuePurpose:
                    return "IssuePurpose";
                case CadSheetListData.Category:
                    return "Category";
                default:
                    return column.CustomName;
            }
        }

        /// <summary>The field of a property of a part of the set, bound as AutoCAD binds one, with the hyperlink switch where there is a link.</summary>
        private static string Field(CadSheetSetFile file, string componentId, string property, string link) =>
            "%<\\AcSm Database(\""
            + file.Path
            + "\").SheetSet(\""
            + file.SheetSet.Id
            + "\").Component(\""
            + componentId
            + "\")."
            + property
            + (link == null ? string.Empty : " \\href \"" + link + "\"")
            + ">%";

        private static double WidthOf(CadSheetListData data)
        {
            switch (data)
            {
                case CadSheetListData.SheetNumber:
                    return 30.0;
                case CadSheetListData.SheetTitle:
                case CadSheetListData.SheetDescription:
                    return 90.0;
                default:
                    return 45.0;
            }
        }

        private static bool HasFolder(CadDocument document) =>
            !string.IsNullOrEmpty(document.FileName) && Path.IsPathRooted(document.FileName);

        // ---- The settings the table keeps ----

        private sealed class Kept
        {
            internal string SetFile;
            internal CadSheetListTableSettings Settings;
        }

        private static CadSheetListTableSettings Copy(CadSheetListTableSettings settings)
        {
            var copy = new CadSheetListTableSettings
            {
                Title = settings.Title,
                IncludeSubsetHeaders = settings.IncludeSubsetHeaders,
                CheckedIds = settings.CheckedIds == null ? null : new List<string>(settings.CheckedIds),
            };
            copy.Columns.Clear();

            foreach (CadSheetListColumn column in settings.Columns)
            {
                copy.Columns.Add(column);
            }

            return copy;
        }

        private static void Keep(DxfTable table, CadSheetSetFile file, CadSheetListTableSettings settings)
        {
            var data = new List<DxfTypedValue>
            {
                new DxfTypedValue(1, Version),
                new DxfTypedValue(1, file.Path),
                new DxfTypedValue(1, settings.Title ?? string.Empty),
                new DxfTypedValue(90, settings.IncludeSubsetHeaders ? "1" : "0"),
                new DxfTypedValue(90, settings.Columns.Count.ToString(CultureInfo.InvariantCulture)),
            };

            foreach (CadSheetListColumn column in settings.Columns)
            {
                data.Add(new DxfTypedValue(1, column.Data.ToString()));
                data.Add(new DxfTypedValue(1, column.Heading));
                data.Add(new DxfTypedValue(1, column.CustomName));
            }

            data.Add(
                new DxfTypedValue(
                    90,
                    settings.CheckedIds == null
                        ? "-1"
                        : settings.CheckedIds.Count.ToString(CultureInfo.InvariantCulture)
                )
            );

            if (settings.CheckedIds != null)
            {
                data.AddRange(settings.CheckedIds.Select(id => new DxfTypedValue(1, id)));
            }

            table.CreateExtensionDictionary();
            table.ExtensionDictionary.SetAt(RecordName, new DxfXrecord { Data = data });
        }

        private static Kept Read(DxfTable table)
        {
            DxfDBDictionary extension = table.ExtensionDictionary;

            if (
                extension == null
                || !extension.Contains(RecordName)
                || !(extension.GetAt(RecordName) is DxfXrecord record)
            )
            {
                return null;
            }

            IReadOnlyList<DxfTypedValue> data = record.Data;
            int at = 0;

            string Next() => at < data.Count ? data[at++].Value : null;

            bool Count(out int count) =>
                int.TryParse(Next(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count);

            if (Next() != Version)
            {
                return null;
            }

            var kept = new Kept
            {
                SetFile = Next(),
                Settings = new CadSheetListTableSettings { Title = Next() },
            };
            kept.Settings.IncludeSubsetHeaders = Next() == "1";
            kept.Settings.Columns.Clear();

            if (kept.SetFile == null || !Count(out int columns))
            {
                return null;
            }

            for (int i = 0; i < columns; i++)
            {
                string type = Next();
                string heading = Next();
                string custom = Next();

                if (heading == null || custom == null || !Enum.TryParse(type, out CadSheetListData what))
                {
                    return null;
                }

                kept.Settings.Columns.Add(
                    what == CadSheetListData.CustomProperty
                        ? CadSheetListColumn.Custom(custom, heading)
                        : CadSheetListColumn.Of(what, heading)
                );
            }

            if (!Count(out int checkedCount))
            {
                return null;
            }

            if (checkedCount >= 0)
            {
                kept.Settings.CheckedIds = new List<string>();

                for (int i = 0; i < checkedCount; i++)
                {
                    kept.Settings.CheckedIds.Add(Next() ?? string.Empty);
                }
            }

            return kept;
        }
    }
}
