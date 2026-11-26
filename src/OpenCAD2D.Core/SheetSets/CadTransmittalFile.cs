using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IoPath = System.IO.Path;

namespace OpenCAD2D.SheetSets
{
    /// <summary>What a file of a transmittal is for: why a sheet set sends it. A file two reasons ask for has the first of these in this order.</summary>
    public enum CadTransmittalFileKind
    {
        /// <summary>The sheet set data file (.dst).</summary>
        SheetSet,

        /// <summary>The drawing of a sheet.</summary>
        Sheet,

        /// <summary>A drawing a sheet attaches, or one that drawing attaches in turn, as an external reference.</summary>
        ExternalReference,

        /// <summary>A drawing template a new sheet is made from (the sheet creation template of the set or of a subset).</summary>
        Template,

        /// <summary>The drawing template that holds the page setups a sheet can be published with in place of its own.</summary>
        PageSetupOverrides,

        /// <summary>A picture an image of a drawing shows.</summary>
        Image,

        /// <summary>A PDF a drawing underlays.</summary>
        PdfUnderlay,

        /// <summary>A workbook or CSV file a data link of a drawing reads.</summary>
        DataLink,

        /// <summary>A plot style table (.ctb or .stb) a layout of a sheet is plotted with.</summary>
        PlotStyleTable,

        /// <summary>A font file (.shx, .ttf, .ttc or .otf) a text style names, found beside the drawing or in the support folders.</summary>
        Font,
    }

    /// <summary>
    /// A file of a transmittal, a row of the Create Transmittal dialog's Files Table: what a sheet set needs to be opened
    /// elsewhere, where it is, where it goes in the zip, how big it is and why it is there. Made by
    /// <see cref="CadTransmittal.Gather"/>. A file that is not found is listed, unchecked, so that the report says so.
    /// </summary>
    public sealed class CadTransmittalFile
    {
        private readonly List<string> _usedBy = new List<string>();
        private bool _isIncluded;

        internal CadTransmittalFile(CadTransmittalFileKind kind, string source, string reference, long size)
        {
            Kind = kind;
            Source = source;
            Reference = reference ?? source;
            Size = size;
            _isIncluded = source != null;
        }

        /// <summary>Gets why the file is sent.</summary>
        public CadTransmittalFileKind Kind { get; internal set; }

        /// <summary>Gets the full path of the file; null where it was not found.</summary>
        public string Source { get; }

        /// <summary>Gets the path as the file that names it writes it (<c>..\Survey\Survey 2025.dwg</c>); the full path for a file found that nothing names (the .dst).</summary>
        public string Reference { get; }

        /// <summary>Gets whether the file was found.</summary>
        public bool IsFound => Source != null;

        /// <summary>Gets the name of the file, with its extension.</summary>
        public string Name => IoPath.GetFileName(Source ?? Reference);

        /// <summary>Gets the size of the file in bytes; 0 where it was not found.</summary>
        public long Size { get; }

        /// <summary>Gets the size as the Files Table writes it: <c>412 KB</c>, <c>2.4 MB</c>, <c>96 B</c>; empty where the file was not found.</summary>
        public string SizeText => IsFound ? SizeOf(Size) : string.Empty;

        /// <summary>Gets the path of the file in the zip, with backslashes (<c>Architectural\A-101.dwg</c>, <c>Files\site-photo.jpg</c>); null where the file was not found.</summary>
        public string PathInZip { get; internal set; }

        /// <summary>Gets the folder of the file in the zip as the Files Table writes it: <c>.\</c> for the folder of the .dst, <c>.\Architectural</c>, <c>.\Files</c>; empty where the file was not found.</summary>
        public string FolderInZip
        {
            get
            {
                if (PathInZip == null)
                {
                    return string.Empty;
                }

                string folder = IoPath.GetDirectoryName(PathInZip);
                return string.IsNullOrEmpty(folder) ? ".\\" : ".\\" + folder;
            }
        }

        /// <summary>Gets or sets whether the file goes in the zip: true for a file found until unticked; a file that was not found is never included.</summary>
        public bool IsIncluded
        {
            get => _isIncluded;
            set => _isIncluded = value && IsFound;
        }

        /// <summary>Gets what the Files Table says besides: <c>not found</c>, or why the drawing's own references could not be listed; empty for none.</summary>
        public string Note { get; internal set; } = string.Empty;

        /// <summary>Gets the names of the sheets and drawings that need the file, in ordinal order, letters of either case alike first.</summary>
        public IReadOnlyList<string> UsedBy => _usedBy;

        /// <summary>Gets the name of the file in the zip with forward slashes, as the zip holds it; null where the file was not found.</summary>
        internal string Entry => PathInZip?.Replace('\\', '/');

        internal void AddUse(string user)
        {
            if (string.IsNullOrEmpty(user) || _usedBy.Contains(user, StringComparer.Ordinal))
            {
                return;
            }

            _usedBy.Add(user);
            _usedBy.Sort(
                (a, b) =>
                {
                    int by = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                    return by != 0 ? by : string.CompareOrdinal(a, b);
                }
            );
        }

        /// <summary>A size in bytes as the dialog writes it: bytes below 1 KB, then KB with no decimals up to 1 MB, then MB, and GB, with one decimal.</summary>
        /// <param name="bytes">The size.</param>
        /// <returns>The words.</returns>
        internal static string SizeOf(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            }

            if (bytes < 1024 * 1024)
            {
                return Math.Round(bytes / 1024.0, MidpointRounding.AwayFromZero)
                        .ToString("0", CultureInfo.InvariantCulture) + " KB";
            }

            if (bytes < 1024L * 1024 * 1024)
            {
                return (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            }

            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }
    }
}
