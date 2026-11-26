using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;

namespace OpenCAD2D.SheetSets
{
    /// <summary>What a drawing names outside itself: the files it references, the font files its text styles name and the plot style table of each of its layouts, read once from its file.</summary>
    internal sealed class CadTransmittalScan
    {
        private CadTransmittalScan(
            IReadOnlyList<Reference> references,
            IReadOnlyList<string> fonts,
            IReadOnlyList<ScannedLayout> layouts
        )
        {
            References = references;
            Fonts = fonts;
            Layouts = layouts;
        }

        /// <summary>The files the drawing references, an external reference, an image, a PDF underlay or a data link each, in order of kind and then of path.</summary>
        internal IReadOnlyList<Reference> References { get; }

        /// <summary>The file names of the fonts its text styles and big fonts name, in ordinal order, letters of either case alike first.</summary>
        internal IReadOnlyList<string> Fonts { get; }

        /// <summary>The layouts but Model, in the order of their tabs, each with its plot style table.</summary>
        internal IReadOnlyList<ScannedLayout> Layouts { get; }

        /// <summary>Whether a read of a drawing failed for a reason the transmittal says (and goes on) rather than stops at.</summary>
        internal static bool IsReadFailure(Exception exception) =>
            exception is IOException
            || exception is UnauthorizedAccessException
            || exception is FormatException
            || exception is NotSupportedException
            || exception is InvalidOperationException
            || exception is ArgumentException;

        /// <summary>Reads a drawing and lists what it names outside itself.</summary>
        /// <param name="path">The path of the DWG or DXF.</param>
        /// <returns>The scan.</returns>
        internal static CadTransmittalScan Read(string path) => Of(CadDrawingFile.Open(path));

        /// <summary>Lists what a drawing in memory names outside itself.</summary>
        internal static CadTransmittalScan Of(DxfDatabase database)
        {
            var references = new List<Reference>();

            foreach (DxfBlockTableRecord block in database.BlockTable)
            {
                if (block.IsFromExternalReference && !block.IsLayout && !string.IsNullOrWhiteSpace(block.PathName))
                {
                    references.Add(new Reference(CadTransmittalFileKind.ExternalReference, block.PathName));
                }
            }

            DxfDBDictionary images = CadXrefManager.Images(database);
            if (images != null)
            {
                foreach (DxfDBDictionaryEntry entry in images)
                {
                    if (entry.Value is DxfRasterImageDef image && !string.IsNullOrWhiteSpace(image.SourceFileName))
                    {
                        references.Add(new Reference(CadTransmittalFileKind.Image, image.SourceFileName));
                    }
                }
            }

            DxfDBDictionary pdfs = CadXrefManager.Pdfs(database);
            if (pdfs != null)
            {
                foreach (DxfDBDictionaryEntry entry in pdfs)
                {
                    if (entry.Value is DxfPdfDefinition pdf && !string.IsNullOrWhiteSpace(pdf.SourceFileName))
                    {
                        references.Add(new Reference(CadTransmittalFileKind.PdfUnderlay, pdf.SourceFileName));
                    }
                }
            }

            DxfDBDictionary links = database.DataLinkDictionary;
            if (links != null)
            {
                foreach (DxfDBDictionaryEntry entry in links)
                {
                    if (
                        entry.Value is DxfDataLink link
                        && !link.IsErased
                        && CadDataSource.TryParse(
                            DxfReader.Carets(link.ConnectionString),
                            out string file,
                            out _,
                            out _
                        )
                    )
                    {
                        references.Add(new Reference(CadTransmittalFileKind.DataLink, file));
                    }
                }
            }

            references.Sort(
                (a, b) =>
                {
                    int byKind = a.Kind.CompareTo(b.Kind);
                    if (byKind != 0)
                    {
                        return byKind;
                    }

                    int byText = string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase);
                    return byText != 0 ? byText : string.CompareOrdinal(a.Text, b.Text);
                }
            );

            var fonts = new List<string>();
            foreach (DxfTextStyleTableRecord style in database.TextStyleTable)
            {
                AddFont(fonts, style.FileName);
                AddFont(fonts, style.BigFontFileName);
            }

            fonts.Sort(
                (a, b) =>
                {
                    int by = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                    return by != 0 ? by : string.CompareOrdinal(a, b);
                }
            );

            var layouts = new List<ScannedLayout>();
            foreach (DxfLayout layout in CadSheetSetDrawings.SheetLayouts(database))
            {
                layouts.Add(new ScannedLayout(layout.LayoutName, layout.Handle, layout.CurrentStyleSheet));
            }

            return new CadTransmittalScan(references, fonts, layouts);
        }

        /// <summary>The plot style tables the page setups of a drawing name, in ordinal order, letters of either case alike first, each once.</summary>
        internal static IReadOnlyList<string> PageSetupStyleTables(DxfDatabase database)
        {
            var names = new List<string>();
            DxfDBDictionary setups = database.PlotSettingsDictionary;
            if (setups != null)
            {
                foreach (DxfDBDictionaryEntry entry in setups)
                {
                    if (
                        entry.Value is DxfPlotSettings settings
                        && !string.IsNullOrWhiteSpace(settings.CurrentStyleSheet)
                        && !names.Contains(settings.CurrentStyleSheet, StringComparer.OrdinalIgnoreCase)
                    )
                    {
                        names.Add(settings.CurrentStyleSheet);
                    }
                }
            }

            names.Sort(
                (a, b) =>
                {
                    int by = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                    return by != 0 ? by : string.CompareOrdinal(a, b);
                }
            );
            return names;
        }

        /// <summary>The plot style table of the layout of a handle, else of a name (letter case ignored); null where the drawing has neither.</summary>
        internal string StyleTableOf(string handle, string name)
        {
            ScannedLayout found = null;
            if (!string.IsNullOrEmpty(handle))
            {
                found = Layouts.FirstOrDefault(l =>
                    string.Equals(l.Handle, handle, StringComparison.OrdinalIgnoreCase)
                );
            }

            if (found == null && !string.IsNullOrEmpty(name))
            {
                found = Layouts.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            }

            return string.IsNullOrWhiteSpace(found?.StyleTable) ? null : found.StyleTable.Trim();
        }

        private static void AddFont(List<string> fonts, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            string name;
            try
            {
                name = Path.GetFileName(fileName.Trim());
            }
            catch (ArgumentException)
            {
                return;
            }

            string extension = Path.GetExtension(name).ToLowerInvariant();
            bool isFont = extension == ".shx" || extension == ".ttf" || extension == ".ttc" || extension == ".otf";
            if (isFont && !fonts.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                fonts.Add(name);
            }
        }

        /// <summary>A file a drawing references, as the drawing writes its path.</summary>
        internal sealed class Reference
        {
            internal Reference(CadTransmittalFileKind kind, string text)
            {
                Kind = kind;
                Text = text;
            }

            /// <summary>An external reference, an image, a PDF underlay or a data link.</summary>
            internal CadTransmittalFileKind Kind { get; }

            /// <summary>The path as written, full or relative to the drawing.</summary>
            internal string Text { get; }
        }

        /// <summary>A layout of a drawing and the plot style table it names.</summary>
        internal sealed class ScannedLayout
        {
            internal ScannedLayout(string name, string handle, string styleTable)
            {
                Name = name;
                Handle = handle;
                StyleTable = styleTable;
            }

            internal string Name { get; }

            internal string Handle { get; }

            internal string StyleTable { get; }
        }
    }
}
