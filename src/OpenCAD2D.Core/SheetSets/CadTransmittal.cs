using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using GeometryHelper;
using GeometryHelper.Dxf;
using IoFile = System.IO.File;
using IoPath = System.IO.Path;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// What a transmittal wrote: the zip, how many files went into it, and the report that went in with them.
    /// </summary>
    public sealed class CadTransmittalResult
    {
        internal CadTransmittalResult(
            string zipPath,
            int fileCount,
            long zipSize,
            IReadOnlyList<string> changes,
            string report
        )
        {
            ZipPath = zipPath;
            FileCount = fileCount;
            ZipSize = zipSize;
            Changes = changes;
            Report = report;
        }

        /// <summary>Gets the full path of the zip.</summary>
        public string ZipPath { get; }

        /// <summary>Gets how many files are in the zip, the report not counted.</summary>
        public int FileCount { get; }

        /// <summary>Gets the size of the zip in bytes.</summary>
        public long ZipSize { get; }

        /// <summary>Gets what was changed in the copies, a line each (<c>A-101 Ground Floor Plan.dwg: 2 fields updated.</c>); empty where every file went in as it is.</summary>
        public IReadOnlyList<string> Changes { get; }

        /// <summary>Gets the text of <c>transmittal.txt</c>, the report that is in the zip.</summary>
        public string Report { get; }
    }

    /// <summary>
    /// eTransmit of a sheet set (A12 D18): gathers what a set needs to be opened somewhere else, one zip of the set's data
    /// file, the drawings of its sheets, the files those drawings reference (external references, images, PDF underlays, the
    /// files of data links), the sheet creation templates and the page setup overrides file, the plot style tables the layouts
    /// of the sheets name, and the font files the text styles name that sit beside the drawings or in the support folders,
    /// with a report, <c>transmittal.txt</c>. It is AutoCAD's Create Transmittal with its <c>Use organized folder structure</c>
    /// and a package of the zip type, and nothing of its other types, setups or paths.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The folder of the .dst is the root of the zip. A file under it keeps its place: a drawing in
    /// <c>D:\Projects\Tower\Architectural</c> is <c>Architectural\A-101.dwg</c> of the zip, and an external reference
    /// <c>..\Model\Tower model.dwg</c> of it is still where it names. A file outside the root, such as a photograph on another
    /// drive, goes in <c>Files</c> (a second file of that name there is numbered, <c>site-photo (2).jpg</c>). Where a path
    /// would then be wrong, it is written again in the copy and the original is not touched: an absolute path, a path to a file
    /// that has gone to <c>Files</c>, becomes the path relative to the drawing, in the drawings (<c>PathName</c> of an
    /// external reference, the path of an image or a PDF underlay) and in the copy of the .dst (the drawing of each sheet, the
    /// templates, the overrides file), which then names its files by their relative path alone. A path of a data link is not
    /// written again: its file goes in the zip, and a link by a full path is left as it is. A file that is already right is
    /// copied byte for byte; a drawing is read and saved again only where a path or a field in it has to change, and not at all
    /// where the DWG writer would leave something of it out (the report says so).
    /// </para>
    /// <para>
    /// Plot style tables are looked for by name in the plot style folder given. A table that is not a file there and is one of
    /// the tables OpenCAD2D and AutoCAD make themselves (<c>acad.ctb</c>, <c>monochrome.ctb</c> and so on) is not sent: every
    /// installation has it. Fonts that Windows has or OpenCAD2D carries are not sent either: only a font file a text style
    /// names that is in the folder of the drawing, the folder of the .dst or a support folder given.
    /// </para>
    /// <para>
    /// Two runs over the same files give the same bytes: the entries are in the order of their paths, each with the time of its
    /// file, and the report carries the date of the clock of the sheet set file. A drawing saved again carries the time it was
    /// saved at in its own header, so the bytes of such a zip differ from run to run.
    /// </para>
    /// </remarks>
    public sealed class CadTransmittal
    {
        private const string ReportName = "transmittal.txt";

        private readonly CadSheetSetFile _file;
        private readonly string _plotStyleFolder;
        private readonly List<string> _fontFolders;

        // The scans and page setup tables read, by path, looked up only: a drawing is read once however often its sheets are ticked.
        private readonly Dictionary<string, ScanResult> _scans = new Dictionary<string, ScanResult>(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, IReadOnlyList<string>> _setupTables = new Dictionary<
            string,
            IReadOnlyList<string>
        >(StringComparer.OrdinalIgnoreCase);

        private List<string> _sheetNames = new List<string>();

        /// <summary>Makes the transmittal of a sheet set.</summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="plotStyleFolder">The folder the plot style tables are looked for in by name (<see cref="CadDocument.PlotStyleFolder"/>); null or empty for none.</param>
        /// <param name="fontFolders">The folders besides those of the drawings and of the .dst that font files are looked for in (the support folders); null for none.</param>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        public CadTransmittal(CadSheetSetFile file, string plotStyleFolder, IEnumerable<string> fontFolders)
        {
            _file = file ?? throw new ArgumentNullException(nameof(file));
            _plotStyleFolder = plotStyleFolder;
            _fontFolders = (fontFolders ?? Enumerable.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .ToList();
        }

        /// <summary>Gets the sheet set's file.</summary>
        public CadSheetSetFile SheetSetFile => _file;

        /// <summary>Gets or sets the notes that go in the report, what the box of the dialog holds; empty for none.</summary>
        public string Notes { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether the fields of the drawings are updated in the copies before they are zipped; true until set. A
        /// drawing with fields is opened and saved again for it, and each field is evaluated as that drawing's FIELDEVAL asks
        /// (bit 8 for eTransmit, and the bits for opening and saving that the program's own open and save use).
        /// </summary>
        public bool UpdateFields { get; set; } = true;

        /// <summary>Gets the zip the dialog offers: <c>Transmittals\&lt;set name&gt;.zip</c> in the folder of the .dst, the name with each character a file cannot hold made an underscore.</summary>
        public string DefaultZipPath
        {
            get
            {
                string name = CadSheetSetDrawings.AsFileName(_file.SheetSet.Name);
                return IoPath.Combine(_file.Folder, "Transmittals", (name.Length == 0 ? "Transmittal" : name) + ".zip");
            }
        }

        /// <summary>
        /// Lists the files the sheets under a selection need, the .dst and the set's templates and overrides file among them.
        /// A drawing is read once, however often a list is asked for. A sheet with no layout sends nothing; a sheet whose drawing
        /// is not found is listed by that drawing's path, not found.
        /// </summary>
        /// <param name="selection">The selected sheets, subsets and the set; null or empty for the whole set.</param>
        /// <returns>The files, those found first, in order of kind and then of path; the report's sheet list is that of this call.</returns>
        public IReadOnlyList<CadTransmittalFile> Gather(IEnumerable<CadSheetComponent> selection)
        {
            _file.ReloadIfChanged();
            CadSheetSet set = _file.SheetSet;
            string folder = _file.Folder;
            var chosen = new HashSet<string>(
                (selection ?? Enumerable.Empty<CadSheetComponent>()).Where(c => c != null).Select(c => c.Id),
                StringComparer.Ordinal
            );
            bool whole = chosen.Count == 0 || chosen.Contains(set.Id);
            List<CadSheetSetSheet> sheets = set.AllSheets().Where(s => whole || IsUnder(s, chosen)).ToList();

            var gathering = new Gathering();
            gathering.Add(CadTransmittalFileKind.SheetSet, _file.Path, null, null);
            var names = new List<string>();
            var templates = new List<CadSheetSubset>();

            foreach (CadSheetSetSheet sheet in sheets)
            {
                string user = Label(sheet);
                names.Add(user);

                for (CadSheetSubset up = sheet.Parent; up != null; up = up.Parent)
                {
                    if (!templates.Any(t => t.Id == up.Id))
                    {
                        templates.Add(up);
                    }
                }

                CadDrawingReference layout = sheet.Layout;
                if (layout == null)
                {
                    continue;
                }

                string drawing = layout.File.Resolve(folder);
                if (drawing == null || !IoFile.Exists(drawing))
                {
                    gathering.Add(
                        CadTransmittalFileKind.Sheet,
                        null,
                        layout.File.Locate(folder) ?? layout.File.RelativeFileName ?? layout.File.FileName,
                        user
                    );
                    continue;
                }

                CadTransmittalFile entry = gathering.Add(
                    CadTransmittalFileKind.Sheet,
                    drawing,
                    layout.File.RelativeFileName ?? drawing,
                    user
                );
                Visit(gathering, entry);
                string style = ScanOf(entry)?.StyleTableOf(layout.Handle, layout.Name);
                if (style != null)
                {
                    AddPlotStyle(gathering, style, user);
                }
            }

            foreach (CadSheetSubset subset in templates)
            {
                CadDrawingReference template = subset.Template;
                if (template != null)
                {
                    string user = "the sheet creation template of " + (subset.Id == set.Id ? set.Name : subset.Name);
                    string found = template.File.Resolve(folder);
                    if (found != null && IoFile.Exists(found))
                    {
                        gathering.Add(
                            CadTransmittalFileKind.Template,
                            found,
                            template.File.RelativeFileName ?? found,
                            user
                        );
                    }
                    else
                    {
                        gathering.Add(
                            CadTransmittalFileKind.Template,
                            null,
                            template.File.Locate(folder) ?? template.File.RelativeFileName,
                            user
                        );
                    }
                }
            }

            CadFileReference overrides = set.PageSetupOverrides;
            if (overrides != null)
            {
                string user = "the page setup overrides of " + set.Name;
                string found = overrides.Resolve(folder);
                if (found != null && IoFile.Exists(found))
                {
                    CadTransmittalFile entry = gathering.Add(
                        CadTransmittalFileKind.PageSetupOverrides,
                        found,
                        overrides.RelativeFileName ?? found,
                        user
                    );
                    foreach (string table in SetupTables(entry))
                    {
                        AddPlotStyle(gathering, table, entry.Name);
                    }
                }
                else
                {
                    gathering.Add(
                        CadTransmittalFileKind.PageSetupOverrides,
                        null,
                        overrides.Locate(folder) ?? overrides.RelativeFileName,
                        user
                    );
                }
            }

            _sheetNames = names;
            return gathering.Finish(IoPath.GetFullPath(folder));
        }

        /// <summary>
        /// Makes the report as it would be written now: the sheet set, the package, the notes, the sheets, the files that go, those
        /// left out and those not found, and the paths that are to be written again. The fields updated are not known until the
        /// zip is written.
        /// </summary>
        /// <param name="files">The files of <see cref="Gather"/>, with the boxes ticked or not.</param>
        /// <param name="zipPath">The path of the zip.</param>
        /// <returns>The text of <c>transmittal.txt</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the files or the path are null.</exception>
        public string Report(IReadOnlyList<CadTransmittalFile> files, string zipPath)
        {
            Check(files, zipPath);
            var changes = new List<string>();
            var byPath = ByPath(files);
            foreach (CadTransmittalFile file in files.Where(f => f.IsIncluded && IsDrawingToCopy(f)))
            {
                changes.AddRange(Repoints(file, byPath).Select(r => Describe(file, r)));
            }

            changes.AddRange(DstRewrites(byPath).Select(r => IoPath.GetFileName(_file.Path) + ": " + r.Say));
            return BuildReport(files, IoPath.GetFullPath(zipPath), changes, null);
        }

        /// <summary>
        /// Writes the zip: each file that is ticked, in the order of its path in the zip, then <c>transmittal.txt</c>. A file in the
        /// folder of the .dst keeps its place and one outside it goes in <c>Files</c>; the .dst, the drawings and the paths in them
        /// are written again as the class says, and the fields of the drawings are updated first where <see cref="UpdateFields"/> is
        /// on. The zip is written beside its name first and put in its place once written, replacing a zip that is there; its
        /// folder is made where it is not.
        /// </summary>
        /// <param name="files">The files of <see cref="Gather"/>, with the boxes ticked or not.</param>
        /// <param name="zipPath">The path of the zip.</param>
        /// <returns>What was written.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the files or the path are null.</exception>
        /// <exception cref="IOException">Thrown when a file cannot be read or the zip cannot be written.</exception>
        public CadTransmittalResult Write(IReadOnlyList<CadTransmittalFile> files, string zipPath)
        {
            Check(files, zipPath);
            string full = IoPath.GetFullPath(zipPath);
            string folder = IoPath.GetDirectoryName(full);
            Directory.CreateDirectory(folder);
            var byPath = ByPath(files);
            var included = files
                .Where(f => f.IsIncluded)
                .OrderBy(f => f.Entry, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Entry, StringComparer.Ordinal)
                .ToList();
            var changes = new List<string>();
            string work = IoPath.Combine(IoPath.GetTempPath(), "OpenCAD2D-transmittal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string temp = full + ".tmp";

            try
            {
                string report;
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    int copies = 0;
                    foreach (CadTransmittalFile file in included)
                    {
                        string from = Prepare(file, byPath, changes, work, ref copies);
                        ZipArchiveEntry entry = zip.CreateEntry(file.Entry, CompressionLevel.Optimal);
                        entry.LastWriteTime = EntryTime(IoFile.GetLastWriteTime(file.Source));
                        using (FileStream source = IoFile.OpenRead(from))
                        using (Stream target = entry.Open())
                        {
                            source.CopyTo(target);
                        }
                    }

                    report = BuildReport(files, full, changes, _file.Clock());
                    ZipArchiveEntry reportEntry = zip.CreateEntry(
                        UniqueName(ReportName, included),
                        CompressionLevel.Optimal
                    );
                    reportEntry.LastWriteTime = EntryTime(_file.Clock().ToLocalTime());
                    byte[] text = new UTF8Encoding(false).GetBytes(report);
                    using (Stream target = reportEntry.Open())
                    {
                        target.Write(text, 0, text.Length);
                    }
                }

                if (IoFile.Exists(full))
                {
                    IoFile.Replace(temp, full, null);
                }
                else
                {
                    IoFile.Move(temp, full);
                }

                return new CadTransmittalResult(full, included.Count, new FileInfo(full).Length, changes, report);
            }
            finally
            {
                TryDelete(temp);
                try
                {
                    Directory.Delete(work, true);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    // A temporary folder that cannot be deleted is left for the system to clear.
                }
            }
        }

        // ----- gathering -----

        /// <summary>The list under way: each file once, by its full path (a file not found by the path it is named by), looked up by key only.</summary>
        private sealed class Gathering
        {
            private readonly List<CadTransmittalFile> _files = new List<CadTransmittalFile>();
            private readonly Dictionary<string, CadTransmittalFile> _byKey = new Dictionary<string, CadTransmittalFile>(
                StringComparer.OrdinalIgnoreCase
            );

            /// <summary>The drawings whose references were listed, so that each is read once and a drawing that references itself ends.</summary>
            internal HashSet<string> Visited { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            internal CadTransmittalFile Add(CadTransmittalFileKind kind, string source, string reference, string user)
            {
                string key = source != null ? "F|" + source : "M|" + kind + "|" + reference;
                if (!_byKey.TryGetValue(key, out CadTransmittalFile file))
                {
                    file = new CadTransmittalFile(
                        kind,
                        source,
                        reference,
                        source == null ? 0 : new FileInfo(source).Length
                    );
                    if (source == null)
                    {
                        file.Note = "not found";
                    }

                    _byKey[key] = file;
                    _files.Add(file);
                }
                else if (kind < file.Kind)
                {
                    file.Kind = kind;
                }

                file.AddUse(user);
                return file;
            }

            /// <summary>Puts the files in order and gives each found one its place in the zip.</summary>
            internal IReadOnlyList<CadTransmittalFile> Finish(string root)
            {
                List<CadTransmittalFile> found = _files
                    .Where(f => f.IsFound)
                    .OrderBy(f => f.Source, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.Source, StringComparer.Ordinal)
                    .ToList();
                var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string prefix = root.TrimEnd('\\', '/') + "\\";

                foreach (CadTransmittalFile file in found)
                {
                    string full = IoPath.GetFullPath(file.Source);
                    if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        file.PathInZip = full.Substring(prefix.Length);
                        taken.Add(file.PathInZip);
                    }
                }

                foreach (CadTransmittalFile file in found.Where(f => f.PathInZip == null))
                {
                    file.PathInZip = Numbered("Files\\", file.Name, taken);
                    taken.Add(file.PathInZip);
                }

                return _files
                    .OrderBy(f => f.IsFound ? 0 : 1)
                    .ThenBy(f => f.Kind)
                    .ThenBy(f => f.IsFound ? f.PathInZip : f.Reference, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.IsFound ? f.PathInZip : f.Reference, StringComparer.Ordinal)
                    .ToList();
            }

            /// <summary>The path of a name in a folder of the zip that no file there has: the name, else <c>name (2).ext</c>, <c>name (3).ext</c> and on.</summary>
            private static string Numbered(string folder, string name, HashSet<string> taken)
            {
                string candidate = folder + name;
                string stem = IoPath.GetFileNameWithoutExtension(name);
                string extension = IoPath.GetExtension(name);
                for (int n = 2; taken.Contains(candidate); n++)
                {
                    candidate = folder + stem + " (" + n.ToString(CultureInfo.InvariantCulture) + ")" + extension;
                }

                return candidate;
            }
        }

        /// <summary>The result of reading a drawing: what it names, or why it could not be read.</summary>
        private sealed class ScanResult
        {
            internal CadTransmittalScan Scan { get; set; }

            internal string Error { get; set; }
        }

        private static bool IsUnder(CadSheetComponent part, HashSet<string> chosen)
        {
            for (CadSheetComponent at = part; at != null; at = at.Parent)
            {
                if (chosen.Contains(at.Id))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The sheet as the palette writes it: <c>A-101 - Ground Floor Plan</c>, the title alone for a sheet with no number, the layout's name for a sheet with neither.</summary>
        private static string Label(CadSheetSetSheet sheet)
        {
            string number = sheet.Number;
            string title = sheet.Title;
            if (string.IsNullOrEmpty(number) && string.IsNullOrEmpty(title))
            {
                return sheet.Layout?.Name ?? string.Empty;
            }

            return string.IsNullOrEmpty(number) ? title
                : string.IsNullOrEmpty(title) ? number
                : number + " - " + title;
        }

        /// <summary>Whether a drawing's own references are listed and written: the drawing of a sheet or an external reference, as a DWG or a DXF.</summary>
        private static bool IsDrawingToCopy(CadTransmittalFile file) =>
            file.IsFound
            && (file.Kind == CadTransmittalFileKind.Sheet || file.Kind == CadTransmittalFileKind.ExternalReference)
            && CadDrawingFile.IsDrawingName(file.Source);

        /// <summary>Reads a drawing once; null (and the file told) where it cannot be read.</summary>
        private CadTransmittalScan ScanOf(CadTransmittalFile file)
        {
            if (!IsDrawingToCopy(file))
            {
                return null;
            }

            if (!_scans.TryGetValue(file.Source, out ScanResult result))
            {
                result = new ScanResult();
                try
                {
                    result.Scan = CadTransmittalScan.Read(file.Source);
                }
                catch (Exception e) when (CadTransmittalScan.IsReadFailure(e))
                {
                    GeometryHelperLog.Warn("CadTransmittal: " + file.Source + " was not read.", e);
                    result.Error = e.Message;
                }

                _scans[file.Source] = result;
            }

            if (result.Error != null)
            {
                file.Note = "its references could not be listed: " + result.Error;
            }

            return result.Scan;
        }

        /// <summary>Lists what a drawing references, and what those drawings reference in turn, once each.</summary>
        private void Visit(Gathering gathering, CadTransmittalFile drawing)
        {
            if (!IsDrawingToCopy(drawing) || !gathering.Visited.Add(drawing.Source))
            {
                return;
            }

            CadTransmittalScan scan = ScanOf(drawing);
            if (scan == null)
            {
                return;
            }

            string folder = IoPath.GetDirectoryName(drawing.Source);
            string user = drawing.Name;

            foreach (CadTransmittalScan.Reference reference in scan.References)
            {
                string found = Locate(reference.Text, folder);
                if (found == null)
                {
                    gathering.Add(reference.Kind, null, reference.Text, user);
                    continue;
                }

                CadTransmittalFile file = gathering.Add(reference.Kind, found, reference.Text, user);
                if (reference.Kind == CadTransmittalFileKind.ExternalReference)
                {
                    Visit(gathering, file);
                }
            }

            foreach (string font in scan.Fonts)
            {
                string found = FindFont(font, folder);
                if (found != null)
                {
                    gathering.Add(CadTransmittalFileKind.Font, found, font, user);
                }
            }
        }

        /// <summary>
        /// Finds the file a drawing's path names as AutoCAD looks for it: the path as written (from the drawing's folder where it
        /// is not full), then its file name in the drawing's folder, then in the folder of the .dst; null where none is there.
        /// </summary>
        private string Locate(string text, string drawingFolder)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                string name = IoPath.GetFileName(text);
                var candidates = new List<string>
                {
                    IoPath.IsPathRooted(text) ? text : IoPath.Combine(drawingFolder, text),
                    IoPath.Combine(drawingFolder, name),
                    IoPath.Combine(_file.Folder, name),
                };

                foreach (string candidate in candidates)
                {
                    if (IoFile.Exists(candidate))
                    {
                        return IoPath.GetFullPath(candidate);
                    }
                }
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                // A text that is no path names no file.
            }

            return null;
        }

        /// <summary>Finds a font file by its name beside the drawing, beside the .dst or in a support folder; null where it is in none (Windows or the program has it).</summary>
        private string FindFont(string name, string drawingFolder)
        {
            var folders = new List<string> { drawingFolder, _file.Folder };
            folders.AddRange(_fontFolders);
            foreach (string folder in folders)
            {
                try
                {
                    string candidate = IoPath.Combine(folder, name);
                    if (IoFile.Exists(candidate))
                    {
                        return IoPath.GetFullPath(candidate);
                    }
                }
                catch (Exception e)
                    when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
                {
                    // A folder that is no path holds no font.
                }
            }

            return null;
        }

        /// <summary>Adds the plot style table of a name: the file in the plot style folder; none where it is a table every installation has; else a table not found.</summary>
        private void AddPlotStyle(Gathering gathering, string name, string user)
        {
            string path = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(_plotStyleFolder))
                {
                    string candidate = IoPath.Combine(_plotStyleFolder, name);
                    path = IoFile.Exists(candidate) ? IoPath.GetFullPath(candidate) : null;
                }
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                // A name that is no file name names no table.
            }

            if (path != null)
            {
                gathering.Add(CadTransmittalFileKind.PlotStyleTable, path, name, user);
            }
            else if (!CadPlotStyleFolder.BuiltInNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                gathering.Add(CadTransmittalFileKind.PlotStyleTable, null, name, user);
            }
        }

        /// <summary>The plot style tables the page setups of the overrides file name; none where the file cannot be read.</summary>
        private IReadOnlyList<string> SetupTables(CadTransmittalFile overrides)
        {
            if (!_setupTables.TryGetValue(overrides.Source, out IReadOnlyList<string> tables))
            {
                try
                {
                    tables = CadTransmittalScan.PageSetupStyleTables(CadDrawingFile.Open(overrides.Source));
                }
                catch (Exception e) when (CadTransmittalScan.IsReadFailure(e))
                {
                    GeometryHelperLog.Warn("CadTransmittal: " + overrides.Source + " was not read.", e);
                    tables = new List<string>();
                    overrides.Note = "its page setups could not be listed: " + e.Message;
                }

                _setupTables[overrides.Source] = tables;
            }

            return tables;
        }

        // ----- writing -----

        private static void Check(IReadOnlyList<CadTransmittalFile> files, string zipPath)
        {
            if (files == null)
            {
                throw new ArgumentNullException(nameof(files));
            }

            if (zipPath == null)
            {
                throw new ArgumentNullException(nameof(zipPath));
            }

            Guard.Argument(zipPath.Trim().Length > 0, nameof(zipPath), "A transmittal needs a zip to write.");
        }

        private static Dictionary<string, CadTransmittalFile> ByPath(IReadOnlyList<CadTransmittalFile> files)
        {
            var byPath = new Dictionary<string, CadTransmittalFile>(StringComparer.OrdinalIgnoreCase);
            foreach (CadTransmittalFile file in files.Where(f => f.IsFound))
            {
                byPath[file.Source] = file;
            }

            return byPath;
        }

        /// <summary>A time a zip entry can hold: the zip format starts in 1980 and ends in 2107.</summary>
        private static DateTimeOffset EntryTime(DateTime time)
        {
            if (time.Year < 1980)
            {
                return new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            }

            if (time.Year > 2107)
            {
                return new DateTimeOffset(2107, 12, 31, 23, 59, 58, TimeSpan.Zero);
            }

            return new DateTimeOffset(
                time.Year,
                time.Month,
                time.Day,
                time.Hour,
                time.Minute,
                time.Second,
                TimeSpan.Zero
            );
        }

        private static string UniqueName(string name, IReadOnlyList<CadTransmittalFile> files)
        {
            string candidate = name;
            string stem = IoPath.GetFileNameWithoutExtension(name);
            string extension = IoPath.GetExtension(name);
            for (int n = 2; files.Any(f => string.Equals(f.Entry, candidate, StringComparison.OrdinalIgnoreCase)); n++)
            {
                candidate = stem + " (" + n.ToString(CultureInfo.InvariantCulture) + ")" + extension;
            }

            return candidate;
        }

        private static void TryDelete(string path)
        {
            try
            {
                IoFile.Delete(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // A temporary file that cannot be deleted is left for the system to clear.
            }
        }

        /// <summary>The file to read into the zip for a file: itself, or a copy with its paths and fields written again.</summary>
        private string Prepare(
            CadTransmittalFile file,
            Dictionary<string, CadTransmittalFile> byPath,
            List<string> changes,
            string work,
            ref int copies
        )
        {
            if (file.Kind == CadTransmittalFileKind.SheetSet)
            {
                return PrepareSet(file, byPath, changes, work, ref copies);
            }

            return IsDrawingToCopy(file) ? PrepareDrawing(file, byPath, changes, work, ref copies) : file.Source;
        }

        private string PrepareDrawing(
            CadTransmittalFile file,
            Dictionary<string, CadTransmittalFile> byPath,
            List<string> changes,
            string work,
            ref int copies
        )
        {
            List<Repoint> repoints = Repoints(file, byPath);
            if (!UpdateFields && repoints.Count == 0)
            {
                return file.Source;
            }

            copies++;
            string copy = IoPath.Combine(
                work,
                copies.ToString(CultureInfo.InvariantCulture) + IoPath.GetExtension(file.Source)
            );
            string left;
            bool fields = false;

            try
            {
                if (UpdateFields)
                {
                    // Opened and saved as the program does, so that the fields are evaluated as FIELDEVAL asks for each of them.
                    CadDocument document = CadDocument.Open(file.Source);
                    fields = CadFields.TextsWithFields(document).Count > 0;
                    if (!fields && repoints.Count == 0)
                    {
                        return file.Source;
                    }

                    if (fields)
                    {
                        document.UpdateFields(CadFieldContexts.Etransmit);
                    }

                    foreach (Repoint repoint in repoints)
                    {
                        Apply(document.Database, repoint);
                    }

                    document.SaveAs(copy);
                    left = document.LeftOutOfDwg;
                }
                else
                {
                    DxfDatabase database = CadDrawingFile.Open(file.Source);
                    foreach (Repoint repoint in repoints)
                    {
                        Apply(database, repoint);
                    }

                    left = CadDrawingFile.Save(database, copy);
                }
            }
            catch (Exception e) when (CadTransmittalScan.IsReadFailure(e))
            {
                GeometryHelperLog.Warn("CadTransmittal: " + file.Source + " was not written again.", e);
                changes.Add(file.Name + ": left as it is, it could not be changed (" + e.Message + ").");
                return file.Source;
            }

            if (!string.IsNullOrEmpty(left))
            {
                changes.Add(
                    file.Name + ": left as it is, a copy with its paths or fields changed would have lost " + left + "."
                );
                return file.Source;
            }

            if (fields)
            {
                changes.Add(file.Name + ": its fields were updated.");
            }

            changes.AddRange(repoints.Select(r => Describe(file, r)));
            return copy;
        }

        private string PrepareSet(
            CadTransmittalFile file,
            Dictionary<string, CadTransmittalFile> byPath,
            List<string> changes,
            string work,
            ref int copies
        )
        {
            // The files are told as the data file's own: the drawings of its sheets, its templates and its overrides file.
            List<SetRewrite> rewrites = DstRewrites(byPath);
            if (rewrites.Count == 0)
            {
                return file.Source;
            }

            copies++;
            string copy = IoPath.Combine(work, copies.ToString(CultureInfo.InvariantCulture) + ".dst");
            IoFile.Copy(file.Source, copy, true);
            CadSheetSetFile data = CadSheetSetFile.Open(copy);
            data.Change(set =>
            {
                foreach (SetRewrite rewrite in rewrites)
                {
                    rewrite.Apply(set);
                }
            });
            changes.AddRange(rewrites.Select(r => file.Name + ": " + r.Say));
            return copy;
        }

        /// <summary>The paths of a drawing's references that are to be written again in its copy: those to a file that goes in the zip and that would not be found by the path the drawing has.</summary>
        private List<Repoint> Repoints(CadTransmittalFile file, Dictionary<string, CadTransmittalFile> byPath)
        {
            var repoints = new List<Repoint>();
            if (
                file.PathInZip == null
                || !_scans.TryGetValue(file.Source, out ScanResult result)
                || result.Scan == null
            )
            {
                return repoints;
            }

            string folder = IoPath.GetDirectoryName(file.Source);
            string zipFolder = IoPath.GetDirectoryName(Virtual(file.PathInZip));
            foreach (CadTransmittalScan.Reference reference in result.Scan.References)
            {
                if (reference.Kind == CadTransmittalFileKind.DataLink)
                {
                    continue;
                }

                string target = Locate(reference.Text, folder);
                if (target == null || !byPath.TryGetValue(target, out CadTransmittalFile sent) || !sent.IsIncluded)
                {
                    continue;
                }

                string want = CadXrefManager.PathFor(Virtual(sent.PathInZip), CadPathType.Relative, zipFolder);
                string have = CadXrefManager.PathFor(target, CadPathType.Relative, folder);
                bool right =
                    !IoPath.IsPathRooted(reference.Text)
                    && CadSheetSetDrawings.Same(IoPath.Combine(folder, reference.Text), target)
                    && string.Equals(want, have, StringComparison.OrdinalIgnoreCase);
                if (!right)
                {
                    repoints.Add(new Repoint(reference, want));
                }
            }

            return repoints;
        }

        /// <summary>A place in the zip as a path under the folder of the .dst, so that the relative path between two places is worked out as it is on disk.</summary>
        private string Virtual(string pathInZip) => IoPath.Combine(IoPath.GetFullPath(_file.Folder), pathInZip);

        private static void Apply(DxfDatabase database, Repoint repoint)
        {
            switch (repoint.Reference.Kind)
            {
                case CadTransmittalFileKind.ExternalReference:
                    foreach (DxfBlockTableRecord block in database.BlockTable)
                    {
                        if (
                            block.IsFromExternalReference
                            && !block.IsLayout
                            && string.Equals(block.PathName, repoint.Reference.Text, StringComparison.Ordinal)
                        )
                        {
                            block.PathName = repoint.Path;
                        }
                    }

                    break;
                case CadTransmittalFileKind.Image:
                    DxfDBDictionary images = CadXrefManager.Images(database);
                    if (images != null)
                    {
                        foreach (DxfDBDictionaryEntry entry in images)
                        {
                            if (
                                entry.Value is DxfRasterImageDef image
                                && string.Equals(image.SourceFileName, repoint.Reference.Text, StringComparison.Ordinal)
                            )
                            {
                                image.SourceFileName = repoint.Path;
                            }
                        }
                    }

                    break;
                case CadTransmittalFileKind.PdfUnderlay:
                    DxfDBDictionary pdfs = CadXrefManager.Pdfs(database);
                    if (pdfs != null)
                    {
                        foreach (DxfDBDictionaryEntry entry in pdfs)
                        {
                            if (
                                entry.Value is DxfPdfDefinition pdf
                                && string.Equals(pdf.SourceFileName, repoint.Reference.Text, StringComparison.Ordinal)
                            )
                            {
                                pdf.SourceFileName = repoint.Path;
                            }
                        }
                    }

                    break;
            }
        }

        private static string Describe(CadTransmittalFile file, Repoint repoint)
        {
            string what =
                repoint.Reference.Kind == CadTransmittalFileKind.Image ? "image"
                : repoint.Reference.Kind == CadTransmittalFileKind.PdfUnderlay ? "PDF underlay"
                : "external reference";
            return file.Name
                + ": the path of the "
                + what
                + " "
                + repoint.Reference.Text
                + " is now "
                + repoint.Path
                + ".";
        }

        /// <summary>
        /// What the copy of the .dst is to say differently: the relative path of the drawing of each sheet, of each sheet
        /// creation template and of the overrides file, where the file goes in the zip to another place than the path names.
        /// </summary>
        private List<SetRewrite> DstRewrites(Dictionary<string, CadTransmittalFile> byPath)
        {
            var rewrites = new List<SetRewrite>();
            _file.ReloadIfChanged();
            CadSheetSet set = _file.SheetSet;
            string folder = _file.Folder;

            CadTransmittalFile Sent(CadFileReference reference)
            {
                string at = reference?.Resolve(folder);
                return
                    at != null
                    && byPath.TryGetValue(IoPath.GetFullPath(at), out CadTransmittalFile sent)
                    && sent.IsIncluded
                    ? sent
                    : null;
            }

            string Want(CadTransmittalFile sent) => ".\\" + sent.PathInZip;

            foreach (CadSheetSetSheet sheet in set.AllSheets())
            {
                CadDrawingReference layout = sheet.Layout;
                CadTransmittalFile sent = Sent(layout?.File);
                if (
                    sent != null
                    && !string.Equals(layout.File.RelativeFileName, Want(sent), StringComparison.OrdinalIgnoreCase)
                )
                {
                    string id = sheet.Id;
                    string want = Want(sent);
                    rewrites.Add(
                        new SetRewrite("the drawing of the sheet " + Label(sheet) + " is now " + want + ".", s =>
                            ((CadSheetSetSheet)s.Find(id)).Layout = new CadDrawingReference(
                                new CadFileReference(null, want, null, null),
                                layout.Name,
                                layout.Handle,
                                layout.OwnerHandle
                            )
                        )
                    );
                }
            }

            var groups = new List<CadSheetSubset> { set };
            groups.AddRange(AllSubsets(set));
            foreach (CadSheetSubset group in groups)
            {
                CadDrawingReference template = group.Template;
                CadTransmittalFile sent = Sent(template?.File);
                if (
                    sent != null
                    && !string.Equals(template.File.RelativeFileName, Want(sent), StringComparison.OrdinalIgnoreCase)
                )
                {
                    string id = group.Id;
                    string want = Want(sent);
                    rewrites.Add(
                        new SetRewrite(
                            "the sheet creation template of "
                                + (group.Id == set.Id ? set.Name : group.Name)
                                + " is now "
                                + want
                                + ".",
                            s =>
                                ((CadSheetSubset)s.Find(id)).Template = new CadDrawingReference(
                                    new CadFileReference(null, want, null, null),
                                    template.Name,
                                    template.Handle,
                                    template.OwnerHandle
                                )
                        )
                    );
                }
            }

            CadFileReference overrides = set.PageSetupOverrides;
            CadTransmittalFile sentOverrides = Sent(overrides);
            if (
                sentOverrides != null
                && !string.Equals(overrides.RelativeFileName, Want(sentOverrides), StringComparison.OrdinalIgnoreCase)
            )
            {
                string want = Want(sentOverrides);
                rewrites.Add(
                    new SetRewrite("the page setup overrides file is now " + want + ".", s =>
                        s.PageSetupOverrides = new CadFileReference(null, want, null, null)
                    )
                );
            }

            return rewrites;
        }

        private static IEnumerable<CadSheetSubset> AllSubsets(CadSheetSubset group)
        {
            foreach (CadSheetSubset subset in group.Subsets)
            {
                yield return subset;
                foreach (CadSheetSubset inner in AllSubsets(subset))
                {
                    yield return inner;
                }
            }
        }

        private sealed class Repoint
        {
            internal Repoint(CadTransmittalScan.Reference reference, string path)
            {
                Reference = reference;
                Path = path;
            }

            internal CadTransmittalScan.Reference Reference { get; }

            internal string Path { get; }
        }

        private sealed class SetRewrite
        {
            internal SetRewrite(string say, Action<CadSheetSet> apply)
            {
                Say = say;
                Apply = apply;
            }

            internal string Say { get; }

            internal Action<CadSheetSet> Apply { get; }
        }

        // ----- the report -----

        /// <summary>Makes the text of <c>transmittal.txt</c>; <paramref name="written"/> is the time the zip is written at, null for a report seen before it is.</summary>
        private string BuildReport(
            IReadOnlyList<CadTransmittalFile> files,
            string zipPath,
            IReadOnlyList<string> changes,
            DateTime? written
        )
        {
            var text = new StringBuilder();
            void Line(string line = "") => text.Append(line).Append("\r\n");

            CadSheetSet set = _file.SheetSet;
            List<CadTransmittalFile> sent = files.Where(f => f.IsIncluded).ToList();
            long size = sent.Sum(f => f.Size);

            Line("Transmittal report");
            Line("==================");
            Line();
            Line("Sheet set:   " + set.Name);
            Line("Data file:   " + _file.Path);
            Line("Package:     " + zipPath);
            Line(
                "Date:        "
                    + (
                        written.HasValue
                            ? written.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
                            : "(not written yet)"
                    )
            );
            Line("Written by:  OpenCAD2D");
            Line();
            Line("Notes");
            Line("-----");
            Line(
                string.IsNullOrWhiteSpace(Notes) ? "(none)" : Notes.Trim().Replace("\r\n", "\n").Replace("\n", "\r\n")
            );
            Line();
            Line("Sheets");
            Line("------");
            if (_sheetNames.Count == 0)
            {
                Line("(none)");
            }

            foreach (string name in _sheetNames)
            {
                Line(name);
            }

            Line();
            Line(
                "Files in the package ("
                    + sent.Count.ToString(CultureInfo.InvariantCulture)
                    + (sent.Count == 1 ? " file, " : " files, ")
                    + CadTransmittalFile.SizeOf(size)
                    + ")"
            );
            Line("--------------------");
            foreach (
                CadTransmittalFile file in sent.OrderBy(f => f.Entry, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.Entry, StringComparer.Ordinal)
            )
            {
                Line(file.PathInZip + "  (" + KindWords(file.Kind) + ", " + file.SizeText + ")");
                Line("    from " + file.Source);
            }

            Line();
            List<CadTransmittalFile> left = files.Where(f => f.IsFound && !f.IsIncluded).ToList();
            if (left.Count > 0)
            {
                Line("Files left out by the sender");
                Line("----------------------------");
                foreach (CadTransmittalFile file in left)
                {
                    Line(file.Source + "  (" + KindWords(file.Kind) + ")");
                }

                Line();
            }

            List<CadTransmittalFile> missing = files.Where(f => !f.IsFound).ToList();
            if (missing.Count > 0)
            {
                Line("Files not found");
                Line("---------------");
                foreach (CadTransmittalFile file in missing)
                {
                    Line(
                        file.Reference
                            + "  ("
                            + KindWords(file.Kind)
                            + "; needed by "
                            + string.Join(", ", file.UsedBy)
                            + ")"
                    );
                }

                Line();
            }

            Line("Changes made in the copies");
            Line("--------------------------");
            if (changes.Count == 0)
            {
                Line("(none)");
            }

            foreach (string change in changes)
            {
                Line(change);
            }

            Line();
            Line(
                "Fonts: only font files found beside the drawings, beside the data file or in the support folders are sent;"
            );
            Line("the fonts Windows has and the ones OpenCAD2D carries are not.");
            Line(
                "Plot style tables are found by name: put the tables of this package in the plot style folder of the program."
            );
            return text.ToString();
        }

        private static string KindWords(CadTransmittalFileKind kind)
        {
            switch (kind)
            {
                case CadTransmittalFileKind.SheetSet:
                    return "sheet set data file";
                case CadTransmittalFileKind.Sheet:
                    return "drawing of a sheet";
                case CadTransmittalFileKind.ExternalReference:
                    return "external reference";
                case CadTransmittalFileKind.Template:
                    return "sheet creation template";
                case CadTransmittalFileKind.PageSetupOverrides:
                    return "page setup overrides";
                case CadTransmittalFileKind.Image:
                    return "image";
                case CadTransmittalFileKind.PdfUnderlay:
                    return "PDF underlay";
                case CadTransmittalFileKind.DataLink:
                    return "data link file";
                case CadTransmittalFileKind.PlotStyleTable:
                    return "plot style table";
                default:
                    return "font";
            }
        }
    }
}
