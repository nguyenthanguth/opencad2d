using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using IoFile = System.IO.File;

namespace OpenCAD2D.SheetSets
{
    public sealed partial class CadSheetSet
    {
        /// <summary>
        /// Adds a custom property as Add Custom Property does (D10). The name must not be empty, must not hold a dot or a
        /// double quote (a field could not carry it) and must not be one of the set's properties already, letter case
        /// ignored. A property of the sheet set's own is held by the set alone. A property of every sheet is held by the set
        /// as its definition, with the default value, and given at that value to every sheet in the set, in subsets and out of
        /// them; a sheet made later takes it at the default.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="defaultValue">The value, or the default value of a sheet property; null or empty for none.</param>
        /// <param name="owner">Who holds it: <see cref="CadSheetSetPropertyFlags.Set"/> or <see cref="CadSheetSetPropertyFlags.Sheet"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the name is empty, holds a dot or a double quote, or is a property's already, or the owner is neither Set nor Sheet.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public void AddCustomProperty(string name, string defaultValue, CadSheetSetPropertyFlags owner)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Edit();
            Guard.Argument(name.Trim().Length > 0, nameof(name), "A custom property needs a name.");
            Guard.Argument(
                name.IndexOf('.') < 0 && name.IndexOf('"') < 0,
                nameof(name),
                "The name \"" + name + "\" holds a dot or a double quote, which a field cannot carry."
            );
            Guard.Argument(
                owner == CadSheetSetPropertyFlags.Set || owner == CadSheetSetPropertyFlags.Sheet,
                nameof(owner),
                "A custom property is held by the sheet set or by every sheet."
            );
            Guard.Argument(
                FindCustomProperty(name) == null,
                nameof(name),
                "There is a custom property named \"" + name + "\" already."
            );
            SetCustomProperty(name, defaultValue, owner);
            if (owner == CadSheetSetPropertyFlags.Sheet)
            {
                foreach (CadSheetSetSheet sheet in AllSheets())
                {
                    sheet.SetCustomProperty(name, defaultValue, CadSheetSetPropertyFlags.Sheet);
                }
            }
        }

        /// <summary>
        /// Deletes a custom property from the set and, where it is one of every sheet, from every sheet (D10); letter case of
        /// the name ignored.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>True when the set had such a property.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public bool DeleteCustomProperty(string name)
        {
            Edit();
            bool had = RemoveCustomProperty(name);
            foreach (CadSheetSetSheet sheet in AllSheets())
            {
                sheet.RemoveCustomProperty(name);
            }

            return had;
        }

        /// <summary>
        /// Checks the layouts of a drawing for Import Layouts as Sheets and for the wizard's Choose Layouts page: each layout
        /// but Model, in the order of the tabs, with the reason it cannot become a sheet where it cannot. A layout is refused
        /// when its name holds a # (a sheet set cannot use it), when it is a sheet of this set already, or when it is a sheet
        /// of another sheet set that is there: the set the drawing's mark names, if it is not this one and still lists the
        /// layout. A drawing that is not a DWG has no layout that can be a sheet.
        /// </summary>
        /// <param name="drawingFile">The path of the drawing.</param>
        /// <returns>The layouts; empty where the drawing is not a DWG.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the drawing is not there.</exception>
        public IReadOnlyList<CadSheetImportLayout> CheckLayouts(string drawingFile) =>
            CadSheetSetDrawings.CheckFile(drawingFile, this);

        /// <summary>
        /// Opens every sheet's drawing and saves it again, as Resave All Sheets does, so that the drawings and the set agree:
        /// the sheet takes the name, handle and file the layout has now (found as <see cref="CadSheetSetSheet.FindLayout"/>
        /// finds it), the drawing is given the sheet set mark and saved as a DWG of AutoCAD 2018, once for all the sheets that
        /// are layouts of it. A sheet that is missing is left as it is.
        /// </summary>
        /// <returns>The sheets that are missing, in set order; empty where all were resaved.</returns>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        /// <exception cref="NotSupportedException">Thrown when the DWG writer would leave something of a drawing out; that drawing is not replaced and the sheets after it are not resaved.</exception>
        public IReadOnlyList<CadSheetSetSheet> ResaveAllSheets()
        {
            Edit();
            List<CadSheetSetSheet> all = AllSheets().ToList();
            var drawings = new List<string>();
            var byDrawing = new List<List<CadSheetSetSheet>>();
            var missing = new List<XElement>();
            foreach (CadSheetSetSheet sheet in all)
            {
                string drawing = sheet.Layout?.File.Resolve(File.Folder);
                if (drawing == null || !IoFile.Exists(drawing))
                {
                    missing.Add(sheet.Element);
                    continue;
                }

                int at = drawings.FindIndex(d => CadSheetSetDrawings.Same(d, drawing));
                if (at < 0)
                {
                    drawings.Add(drawing);
                    byDrawing.Add(new List<CadSheetSetSheet>());
                    at = drawings.Count - 1;
                }

                byDrawing[at].Add(sheet);
            }

            for (int i = 0; i < drawings.Count; i++)
            {
                DxfDatabase database = CadDrawingFile.Open(drawings[i]);
                var found = new List<KeyValuePair<CadSheetSetSheet, DxfLayout>>();
                foreach (CadSheetSetSheet sheet in byDrawing[i])
                {
                    CadDrawingReference reference = sheet.Layout;
                    DxfLayout layout = CadSheetSetDrawings.Find(database, reference.Handle, reference.Name, out _);
                    if (layout == null)
                    {
                        missing.Add(sheet.Element);
                    }
                    else
                    {
                        found.Add(new KeyValuePair<CadSheetSetSheet, DxfLayout>(sheet, layout));
                    }
                }

                if (found.Count == 0)
                {
                    continue;
                }

                DxfLayout last = found[found.Count - 1].Value;
                CadSheetSetHint.Write(database, File.Path, drawings[i], last.LayoutName, last.Handle, File.Clock());
                CadSheetSetDrawings.Save(database, drawings[i], true);
                foreach (KeyValuePair<CadSheetSetSheet, DxfLayout> pair in found)
                {
                    pair.Key.Layout = CadSheetSetDrawings.Reference(drawings[i], File.Folder, pair.Value);
                }
            }

            return all.Where(s => missing.Contains(s.Element)).ToList();
        }

        /// <summary>Gets the names of the page setups in the page setup overrides file, in the order the file has them.</summary>
        /// <returns>The names; empty where the set names no overrides file, or the file is not there.</returns>
        public IReadOnlyList<string> PageSetupOverrideNames()
        {
            string file = PageSetupOverrides?.Resolve(File.Folder);
            return file == null || !IoFile.Exists(file)
                ? new List<string>()
                : CadPlotSettings.PageSetupNames(CadDrawingFile.Open(file));
        }
    }
}
