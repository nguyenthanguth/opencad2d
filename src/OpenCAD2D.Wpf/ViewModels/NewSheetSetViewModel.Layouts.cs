using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    public sealed partial class NewSheetSetViewModel
    {
        private readonly List<NewSheetSetLayoutNode> _roots = new List<NewSheetSetLayoutNode>();

        // ---- Choose Layouts

        /// <summary>Gets the rows of the Choose Layouts tree that are shown, in order: a folder that was picked, then its drawings and folders, a collapsed row's rows left out.</summary>
        public ObservableCollection<NewSheetSetLayoutNode> LayoutRows { get; } =
            new ObservableCollection<NewSheetSetLayoutNode>();

        /// <summary>Gets the import options of the Import Options dialog: the settings' to begin with.</summary>
        public CadSheetImportOptions ImportOptions => _importOptions;

        /// <summary>Gets how many layouts are ticked, which Finish makes sheets.</summary>
        public int TickedLayoutCount => _roots.SelectMany(r => r.TickedLayouts()).Count();

        /// <summary>Browse… of the Choose Layouts page: asks for a folder of drawings and adds it to the tree.</summary>
        public void AddFolder()
        {
            string folder = AskDrawingFolder?.Invoke();

            if (!string.IsNullOrEmpty(folder))
            {
                AddDrawingFolder(folder);
            }
        }

        /// <summary>
        /// Adds a folder of drawings to the Choose Layouts tree, as Browse… does: the drawings (DWG) in it and in the folders
        /// below it are read, closed, and listed with their layouts, the layouts that can be sheets ticked. A folder is
        /// listed once, and a drawing already listed under another folder is not listed again. A folder with no drawing is
        /// left out, as is a drawing that cannot be read (the line of the page names it).
        /// </summary>
        /// <param name="folder">The path of the folder.</param>
        /// <returns>Whether the folder is in the tree now.</returns>
        public bool AddDrawingFolder(string folder)
        {
            string full;

            try
            {
                full = Path.GetFullPath(folder).TrimEnd('\\', '/');
            }
            catch (Exception ex) when (IsProblem(ex))
            {
                return Say("\"" + folder + "\" is not the path of a folder.");
            }

            if (!Directory.Exists(full))
            {
                return Say("The folder " + full + " is not there.");
            }

            if (_roots.Any(r => SamePath(r.Path, full)))
            {
                return true;
            }

            var seen = new HashSet<string>(
                _roots.SelectMany(r => Drawings(r)).Select(d => d.Path),
                StringComparer.OrdinalIgnoreCase
            );
            var unreadable = new List<string>();
            NewSheetSetLayoutNode root = Scan(full, null, full, seen, unreadable);

            if (root == null)
            {
                return Say(
                    unreadable.Count > 0
                        ? "No drawing in " + full + " could be read. " + unreadable[0]
                        : "There is no drawing (DWG) in " + full + " or the folders below it."
                );
            }

            _roots.Add(root);
            root.IsChecked = true;
            RebuildRows();
            Raise(nameof(TickedLayoutCount));
            Changed();

            if (unreadable.Count > 0)
            {
                _notice =
                    unreadable.Count == 1
                        ? "A drawing could not be read and is left out. " + unreadable[0]
                        : unreadable.Count + " drawings could not be read and are left out. " + unreadable[0];
                RaiseMessage();
            }

            return true;
        }

        private bool Say(string line)
        {
            _notice = line;
            RaiseMessage();
            return false;
        }

        /// <summary>The drawings of a tree, in the order of the tree.</summary>
        private static IEnumerable<NewSheetSetLayoutNode> Drawings(NewSheetSetLayoutNode node)
        {
            if (node.Kind == NewSheetSetNodeKind.Drawing)
            {
                yield return node;
                yield break;
            }

            foreach (NewSheetSetLayoutNode drawing in node.Children.SelectMany(Drawings))
            {
                yield return drawing;
            }
        }

        /// <summary>Reads a folder into rows: its drawings first, then its folders, each sorted by name; null where there is no drawing in it or below it.</summary>
        private NewSheetSetLayoutNode Scan(
            string folder,
            NewSheetSetLayoutNode parent,
            string text,
            HashSet<string> seen,
            List<string> unreadable
        )
        {
            var node = new NewSheetSetLayoutNode(
                NewSheetSetNodeKind.Folder,
                text,
                folder,
                null,
                parent,
                null,
                OnTicked,
                RebuildRows
            );
            string[] files;
            string[] folders;

            try
            {
                files = Directory.GetFiles(folder);
                folders = Directory.GetDirectories(folder);
            }
            catch (Exception ex) when (IsProblem(ex))
            {
                unreadable.Add(folder + ": " + ex.Message);
                Detach(node);
                return null;
            }

            foreach (
                string file in files
                    .Where(f => string.Equals(Path.GetExtension(f), ".dwg", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f, StringComparer.Ordinal)
            )
            {
                if (seen.Add(file))
                {
                    ScanDrawing(file, node, unreadable);
                }
            }

            foreach (
                string below in folders
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f, StringComparer.Ordinal)
            )
            {
                Scan(below, node, Path.GetFileName(below), seen, unreadable);
            }

            if (node.Children.Count == 0)
            {
                Detach(node);
                return null;
            }

            return node;
        }

        private void ScanDrawing(string file, NewSheetSetLayoutNode folder, List<string> unreadable)
        {
            IReadOnlyList<CadSheetImportLayout> layouts;

            try
            {
                layouts = CadSheetSetFile.CheckLayouts(file);
            }
            catch (Exception ex) when (IsProblem(ex))
            {
                unreadable.Add(Path.GetFileName(file) + ": " + ex.Message);
                return;
            }

            if (layouts.Count == 0)
            {
                return;
            }

            var drawing = new NewSheetSetLayoutNode(
                NewSheetSetNodeKind.Drawing,
                Path.GetFileName(file),
                file,
                null,
                folder,
                null,
                OnTicked,
                RebuildRows
            );

            foreach (CadSheetImportLayout layout in layouts)
            {
                new NewSheetSetLayoutNode(
                    NewSheetSetNodeKind.Layout,
                    layout.Name,
                    file,
                    layout.Name,
                    drawing,
                    layout.Refusal,
                    OnTicked,
                    RebuildRows
                );
            }
        }

        private static void Detach(NewSheetSetLayoutNode node) => node.Parent?.Children.Remove(node);

        private void OnTicked()
        {
            Raise(nameof(TickedLayoutCount));
            Changed();
        }

        private void RebuildRows()
        {
            LayoutRows.Clear();

            foreach (NewSheetSetLayoutNode root in _roots)
            {
                Show(root);
            }
        }

        private void Show(NewSheetSetLayoutNode node)
        {
            LayoutRows.Add(node);

            if (node.IsExpanded)
            {
                foreach (NewSheetSetLayoutNode child in node.Children)
                {
                    Show(child);
                }
            }
        }

        /// <summary>Import Options… of the Choose Layouts page: shows the dialog over the options, and where OK is pressed keeps them in the settings and builds the preview again.</summary>
        public void OpenImportOptions()
        {
            if (AskImportOptions == null)
            {
                return;
            }

            var dialog = new SheetImportOptionsViewModel(_importOptions);

            if (AskImportOptions(dialog))
            {
                CadSheetImportOptions options = _importOptions;
                _main.KeepSettings(s =>
                {
                    s.SheetImportPrefixTitles = options.PrefixTitleWithFileName;
                    s.SheetImportSubsetsFromFolders = options.CreateSubsetsFromFolders;
                    s.SheetImportIgnoreTopFolder = options.IgnoreTopLevelFolder;
                });
                Changed();
            }
        }

        private string LayoutsProblem()
        {
            if (_roots.Count == 0)
            {
                return "Pick a folder of drawings with Browse….";
            }

            return TickedLayoutCount == 0 ? "Check the layouts that are to become sheets." : null;
        }

        /// <summary>The drawings ticked, each with its ticked layouts in tab order, as <see cref="CadSheetSetFile.CreateFromDrawings(string, string, string, IEnumerable{CadSheetImportSource}, CadSheetImportOptions)"/> takes them.</summary>
        internal IReadOnlyList<CadSheetImportSource> Sources() =>
            _roots
                .SelectMany(root =>
                    Drawings(root)
                        .Select(d => new
                        {
                            Root = root,
                            Drawing = d,
                            Layouts = d.TickedLayouts().ToList(),
                        })
                        .Where(x => x.Layouts.Count > 0)
                        .Select(x => new CadSheetImportSource(
                            x.Root.Path,
                            x.Drawing.Path,
                            x.Layouts.Select(l => l.LayoutName)
                        ))
                )
                .ToList();

        // ---- Confirm

        /// <summary>Gets the first line of the Sheet Set Preview, in bold: the name of the new sheet set.</summary>
        public string PreviewTitle => _previewTitle;

        /// <summary>Gets the Sheet Set Preview under its name: where the .dst goes, the description, and by the route what the set will hold.</summary>
        public string PreviewBody => _previewBody;

        private void BuildPreview()
        {
            _previewTitle = _name.Trim();
            _previewBody = _fromDrawings ? PreviewOfDrawings() : PreviewOfExample();
            Raise(nameof(PreviewTitle));
            Raise(nameof(PreviewBody));
        }

        private string PreviewOfExample()
        {
            CadSheetSet from = _example.SheetSet;
            string dst = DstPath;
            string folder = Path.GetDirectoryName(dst);
            var text = new StringBuilder();
            text.AppendLine("Sheet set data file: " + dst);
            text.AppendLine("Example: " + from.Name);

            if (_description.Trim().Length > 0)
            {
                text.AppendLine("Description: " + _description.Trim());
            }

            CadFileReference location = from.NewSheetLocation;
            string where = location?.Resolve(_example.Folder) ?? location?.FileName;
            text.AppendLine(
                "Sheet storage location: " + (where == null || SamePath(where, _example.Folder) ? folder : where)
            );
            CadDrawingReference template = from.Template;
            text.AppendLine(
                "Sheet creation template: "
                    + (
                        template == null
                            ? "none"
                            : (template.Name ?? "(first layout)")
                                + " ("
                                + (template.File.Resolve(_example.Folder) ?? template.File.FileName)
                                + ")"
                    )
            );
            text.AppendLine("Prompt for template: " + (from.PromptForTemplate ? "Yes" : "No"));

            if (_createFolderHierarchy)
            {
                List<string> made = FolderPaths(from, string.Empty).ToList();
                text.AppendLine("Folders made: " + (made.Count == 0 ? "none" : string.Join(", ", made)));
            }

            text.AppendLine();
            text.AppendLine("Subsets:");
            int subsets = 0;

            foreach (string line in SubsetLines(from, 1))
            {
                text.AppendLine(line);
                subsets++;
            }

            if (subsets == 0)
            {
                text.AppendLine("  none");
            }

            AppendProperties(text, "Sheet set custom properties:", from.CustomProperties.Where(p => p.IsSetProperty));
            AppendProperties(text, "Sheet custom properties:", from.CustomProperties.Where(p => p.IsSheetProperty));
            return text.ToString().TrimEnd();
        }

        private static void AppendProperties(
            StringBuilder text,
            string heading,
            IEnumerable<CadSheetSetProperty> properties
        )
        {
            List<string> names = properties.Select(p => p.Name).ToList();

            if (names.Count == 0)
            {
                return;
            }

            text.AppendLine();
            text.AppendLine(heading);

            foreach (string name in names)
            {
                text.AppendLine("  " + name);
            }
        }

        private static IEnumerable<string> SubsetLines(CadSheetSubset group, int depth)
        {
            foreach (CadSheetSubset subset in group.Subsets)
            {
                yield return new string(' ', depth * 2) + subset.Name;

                foreach (string line in SubsetLines(subset, depth + 1))
                {
                    yield return line;
                }
            }
        }

        /// <summary>The folder each subset gets under Create a folder hierarchy: its name as a folder name, below its parent's ("Architectural\Details").</summary>
        private static IEnumerable<string> FolderPaths(CadSheetSubset group, string above)
        {
            foreach (CadSheetSubset subset in group.Subsets)
            {
                string name = AsFolderName(subset.Name);
                string path = above.Length == 0 ? (name.Length == 0 ? "Subset" : name) : above + "\\" + name;
                yield return path;

                foreach (string below in FolderPaths(subset, path))
                {
                    yield return below;
                }
            }
        }

        /// <summary>A subset or a sheet of the preview of the existing drawings route.</summary>
        private sealed class PreviewGroup
        {
            public PreviewGroup(string name) => Name = name;

            public string Name { get; }

            /// <summary>The sheets (strings) and subsets (groups) in the order they are made.</summary>
            public List<object> Items { get; } = new List<object>();
        }

        private string PreviewOfDrawings()
        {
            var top = new PreviewGroup(null);

            foreach (CadSheetImportSource source in Sources())
            {
                PreviewGroup into = GroupOf(top, source);

                foreach (string layout in source.LayoutNames)
                {
                    into.Items.Add(
                        (
                            _importOptions.PrefixTitleWithFileName
                                ? Path.GetFileNameWithoutExtension(source.DrawingFile) + " - "
                                : string.Empty
                        ) + layout
                    );
                }
            }

            var text = new StringBuilder();
            text.AppendLine("Sheet set data file: " + DstPath);

            if (_description.Trim().Length > 0)
            {
                text.AppendLine("Description: " + _description.Trim());
            }

            text.AppendLine();
            int sheets = 0;
            int subsets = 0;
            Write(top, 0, text, ref sheets, ref subsets);
            text.AppendLine();
            text.Append(
                sheets
                    + (sheets == 1 ? " sheet" : " sheets")
                    + (subsets > 0 ? " in " + subsets + (subsets == 1 ? " subset." : " subsets.") : ".")
            );
            List<string> refused = _roots
                .SelectMany(r => Drawings(r))
                .SelectMany(d => d.Children)
                .Where(l => l.IsRefused)
                .Select(l => l.Text + " (" + RefusedBecause(l) + ")")
                .ToList();

            if (refused.Count > 0)
            {
                text.Append(" Not imported: " + string.Join(", ", refused) + ".");
            }

            return text.ToString().TrimEnd();
        }

        private static string RefusedBecause(NewSheetSetLayoutNode layout) =>
            layout.LayoutName.IndexOf('#') >= 0 ? "its name holds #"
            : layout.Note.StartsWith("in ", StringComparison.Ordinal) ? "a sheet of " + layout.Note.Substring(3)
            : "not a sheet";

        private static void Write(PreviewGroup group, int depth, StringBuilder text, ref int sheets, ref int subsets)
        {
            foreach (object item in group.Items)
            {
                if (item is PreviewGroup subset)
                {
                    subsets++;
                    text.AppendLine(new string(' ', depth * 2) + subset.Name);
                    Write(subset, depth + 1, text, ref sheets, ref subsets);
                }
                else
                {
                    sheets++;
                    text.AppendLine(new string(' ', depth * 2) + (string)item);
                }
            }
        }

        /// <summary>The group the sheets of a drawing go into, made in the preview where it is not there: as <see cref="CadSheetSetFile.CreateFromDrawings(string, string, string, IEnumerable{CadSheetImportSource}, CadSheetImportOptions)"/> places them, by the folders between the picked folder and the drawing.</summary>
        private PreviewGroup GroupOf(PreviewGroup top, CadSheetImportSource source)
        {
            if (!_importOptions.CreateSubsetsFromFolders || source.RootFolder == null)
            {
                return top;
            }

            string root = Path.GetFullPath(source.RootFolder).TrimEnd('\\', '/');
            string folder = Path.GetFullPath(Path.GetDirectoryName(Path.GetFullPath(source.DrawingFile)))
                .TrimEnd('\\', '/');
            bool inside =
                string.Equals(folder, root, StringComparison.OrdinalIgnoreCase)
                || folder.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);

            if (!inside)
            {
                return top;
            }

            var parts = new List<string>();

            if (!_importOptions.IgnoreTopLevelFolder)
            {
                string name = Path.GetFileName(root);
                parts.Add(name.Length == 0 ? root : name);
            }

            parts.AddRange(
                folder.Substring(root.Length).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
            );
            PreviewGroup current = top;

            foreach (string part in parts)
            {
                PreviewGroup existing = current
                    .Items.OfType<PreviewGroup>()
                    .FirstOrDefault(g => string.Equals(g.Name, part, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    existing = new PreviewGroup(part);
                    current.Items.Add(existing);
                }

                current = existing;
            }

            return current;
        }
    }
}
