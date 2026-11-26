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
        // ---- Sheet Set Example

        /// <summary>Gets the example sheet sets listed: the .dst files of the template folder, sorted by name.</summary>
        public ObservableCollection<NewSheetSetExampleRow> Examples { get; } =
            new ObservableCollection<NewSheetSetExampleRow>();

        /// <summary>Gets the folder the examples are listed from: the one the wizard was made over, or the folder of the examples shipped with the program.</summary>
        public string ExampleFolder => _exampleFolder ?? CadSheetSetExamples.ShippedFolder;

        /// <summary>Gets or sets the example chosen in the list.</summary>
        public NewSheetSetExampleRow SelectedExample
        {
            get => _selectedExample;
            set
            {
                if (Set(ref _selectedExample, value) && !_browsedExample)
                {
                    LoadExample(value?.Path);
                }
            }
        }

        /// <summary>Gets or sets whether the example is the one chosen in the list ("Select a sheet set to use as an example").</summary>
        public bool IsListedExample
        {
            get => !_browsedExample;
            set
            {
                if (value)
                {
                    UseBrowsed(false);
                }
            }
        }

        /// <summary>Gets or sets whether the example is the one browsed to ("Browse to another sheet set to use as an example").</summary>
        public bool IsBrowsedExample
        {
            get => _browsedExample;
            set
            {
                if (value)
                {
                    UseBrowsed(true);
                }
            }
        }

        /// <summary>Gets or sets the path of the .dst browsed to.</summary>
        public string BrowsedExamplePath
        {
            get => _browsedPath;
            set
            {
                if (Set(ref _browsedPath, value ?? string.Empty) && _browsedExample)
                {
                    LoadExample(_browsedPath.Trim());
                }
            }
        }

        /// <summary>Gets the name of the example the box under the list is headed with: its title; empty where none is chosen.</summary>
        public string ExampleTitle => _exampleTitle;

        /// <summary>Gets the title, description and path of the example chosen, as the box under the list shows them.</summary>
        public string ExampleDetails => _exampleDetails;

        /// <summary>The .dst files the list offers: those of <see cref="ExampleFolder"/>; none where the folder is not there.</summary>
        private List<string> ExampleFiles()
        {
            var found = new List<string>();

            try
            {
                if (Directory.Exists(ExampleFolder))
                {
                    found.AddRange(
                        Directory
                            .GetFiles(ExampleFolder)
                            .Where(f => string.Equals(Path.GetExtension(f), ".dst", StringComparison.OrdinalIgnoreCase))
                    );
                }
            }
            catch (Exception ex) when (IsProblem(ex))
            {
                found.Clear();
            }

            return found;
        }

        private void LoadExamples()
        {
            List<string> found = ExampleFiles();

            foreach (
                string path in found
                    .OrderBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f, StringComparer.Ordinal)
            )
            {
                Examples.Add(new NewSheetSetExampleRow(path));
            }

            _browsedExample = Examples.Count == 0;
            _selectedExample = Examples.FirstOrDefault();
            LoadExample(_browsedExample ? _browsedPath : _selectedExample.Path);
        }

        private void UseBrowsed(bool browsed)
        {
            if (_browsedExample == browsed)
            {
                return;
            }

            _browsedExample = browsed;
            Raise(nameof(IsListedExample));
            Raise(nameof(IsBrowsedExample));
            LoadExample(browsed ? _browsedPath.Trim() : _selectedExample?.Path);
        }

        /// <summary>Browse of the Sheet Set Example page: asks for a .dst and makes it the example.</summary>
        public void BrowseExample()
        {
            string chosen = AskExample?.Invoke(_browsedPath);

            if (!string.IsNullOrEmpty(chosen))
            {
                _browsedPath = chosen;
                Raise(nameof(BrowsedExamplePath));
                UseBrowsed(true);
                LoadExample(chosen);
            }
        }

        /// <summary>Reads the example, drops the draft of the properties, and sets the box under the list and the line of the page.</summary>
        private void LoadExample(string path)
        {
            DiscardDraft();
            _example = null;
            _exampleProblem = null;
            _exampleTitle = string.Empty;
            _exampleDetails = string.Empty;

            if (string.IsNullOrEmpty(path))
            {
                _exampleProblem = _browsedExample
                    ? "Browse to the sheet set data file to use as an example."
                    : "There is no example sheet set in " + ExampleFolder + ". Browse to one.";
            }
            else if (!File.Exists(path))
            {
                _exampleProblem = "The sheet set data file " + path + " is not there.";
            }
            else
            {
                try
                {
                    _example = CadSheetSetFile.Open(path);
                    CadSheetSet set = _example.SheetSet;
                    _exampleTitle = set.Name;
                    _exampleDetails =
                        "Title: "
                        + set.Name
                        + Environment.NewLine
                        + "Description: "
                        + set.Description
                        + Environment.NewLine
                        + "Path: "
                        + _example.Path;
                }
                catch (Exception ex) when (IsProblem(ex))
                {
                    _exampleProblem = path + " is not a sheet set data file. " + ex.Message;
                }
            }

            Raise(nameof(ExampleTitle));
            Raise(nameof(ExampleDetails));
            Changed();
        }

        // ---- Sheet Set Details

        /// <summary>Gets or sets the name of the new sheet set, which is also the name of its .dst.</summary>
        public string Name
        {
            get => _name;
            set
            {
                if (Set(ref _name, value ?? string.Empty))
                {
                    Raise(nameof(DstPath));
                    Changed();
                }
            }
        }

        /// <summary>Gets or sets the description; may be empty.</summary>
        public string Description
        {
            get => _description;
            set => Set(ref _description, value ?? string.Empty);
        }

        /// <summary>Gets or sets the folder the .dst is stored in ("Store sheet set data file (.dst) here").</summary>
        public string Folder
        {
            get => _folder;
            set
            {
                if (Set(ref _folder, value ?? string.Empty))
                {
                    Raise(nameof(DstPath));
                    Changed();
                }
            }
        }

        /// <summary>Gets or sets whether each subset gets a folder, as "Create a folder hierarchy based on subsets" says; the example route only.</summary>
        public bool CreateFolderHierarchy
        {
            get => _createFolderHierarchy;
            set => Set(ref _createFolderHierarchy, value);
        }

        /// <summary>Gets whether the folder hierarchy box is shown: on the example route only, where the subsets come from the example.</summary>
        public bool ShowFolderHierarchy => !_fromDrawings;

        /// <summary>Gets the full path of the .dst that is to be made; null where the name or the folder is not fit to name one.</summary>
        public string DstPath
        {
            get
            {
                string name = (_name ?? string.Empty).Trim();
                string folder = (_folder ?? string.Empty).Trim();

                if (
                    name.Length == 0
                    || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    || folder.Length == 0
                    || !Path.IsPathRooted(folder)
                )
                {
                    return null;
                }

                try
                {
                    return Path.Combine(Path.GetFullPath(folder), name + ".dst");
                }
                catch (Exception ex) when (IsProblem(ex))
                {
                    return null;
                }
            }
        }

        /// <summary>Browse of the Sheet Set Details page: asks for the folder the .dst is stored in.</summary>
        public void BrowseStorage()
        {
            string chosen = AskStorageFolder?.Invoke(_folder);

            if (!string.IsNullOrEmpty(chosen))
            {
                Folder = chosen;
            }
        }

        /// <summary>"New Sheet Set", or "New Sheet Set 1", "New Sheet Set 2" and so on where that .dst is already in the folder.</summary>
        private string FreeName()
        {
            const string stem = "New Sheet Set";
            string name = stem;

            for (int n = 1; n < 1000 && TakenIn(_folder, name); n++)
            {
                name = stem + " " + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return name;
        }

        private static bool TakenIn(string folder, string name)
        {
            try
            {
                return File.Exists(Path.Combine(folder, name + ".dst"));
            }
            catch (Exception ex) when (IsProblem(ex))
            {
                return false;
            }
        }

        /// <summary>What is wrong with the name and the folder, in the wizard's words; null where the .dst can be made.</summary>
        private string DetailsProblem()
        {
            string name = (_name ?? string.Empty).Trim();
            string folder = (_folder ?? string.Empty).Trim();

            if (name.Length == 0)
            {
                return "Name the sheet set.";
            }

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return "The name of a sheet set is the name of its file, so it cannot hold any of \\ / : * ? \" < > |.";
            }

            if (folder.Length == 0)
            {
                return "Say where to store the sheet set data file.";
            }

            if (!Path.IsPathRooted(folder))
            {
                return "Give the whole path of the folder, as D:\\Projects\\Tower.";
            }

            string full;

            try
            {
                full = Path.GetFullPath(folder);
            }
            catch (Exception ex) when (IsProblem(ex))
            {
                return "\"" + folder + "\" is not the path of a folder.";
            }

            if (File.Exists(full))
            {
                return full + " is a file, not a folder.";
            }

            return TakenIn(full, name)
                ? name + ".dst already exists in " + full + ". Choose another name or folder."
                : null;
        }

        // ---- Properties

        /// <summary>
        /// Sheet Set Properties… of the Sheet Set Details page: makes the draft of the set being made where there is none, gives
        /// it to <see cref="AskProperties"/>, and takes the description back from it. Says on the line of the page that there
        /// is no such dialog where <see cref="AskProperties"/> is not set. Nothing is written beside the draft.
        /// </summary>
        public void OpenProperties()
        {
            if (AskProperties == null)
            {
                _notice = "There is no Sheet Set Properties dialog to show here.";
                RaiseMessage();
                return;
            }

            try
            {
                CadSheetSetFile draft = EnsureDraft();
                draft.Change(set => set.Description = _description.Trim());

                if (AskProperties(draft))
                {
                    Description = draft.SheetSet.Description;
                }
            }
            catch (Exception ex) when (IsProblem(ex))
            {
                _notice = "The properties of the sheet set could not be opened. " + ex.Message;
                RaiseMessage();
            }
        }

        /// <summary>Gets the draft of the set being made, where Sheet Set Properties… made one; null before.</summary>
        public CadSheetSetFile Draft => _draft;

        private CadSheetSetFile EnsureDraft()
        {
            if (_draft != null)
            {
                return _draft;
            }

            string folder = Path.Combine(
                Path.GetTempPath(),
                "OpenCAD2D",
                "sheet-set-wizard-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "draft.dst");
            string name = _name.Trim().Length > 0 ? _name.Trim() : "New Sheet Set";
            string description = _description.Trim().Length > 0 ? _description.Trim() : null;
            _draft =
                _fromDrawings || _example == null
                    ? CadSheetSetFile.Create(path, name, description)
                    : CadSheetSetFile.CreateFromExample(path, name, description, _example.Path, false);
            return _draft;
        }

        private void DiscardDraft()
        {
            if (_draft == null)
            {
                return;
            }

            string folder = _draft.Folder;
            _draft = null;

            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException)
            {
                // A folder still held is left for the next clean of the temporary folder.
            }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Writes the properties of the draft into the set that was made: see the remarks of the class.</summary>
        private void ApplyDraft(CadSheetSetFile file)
        {
            if (_draft == null)
            {
                return;
            }

            CadSheetSet from = _draft.SheetSet;
            string draftFolder = _draft.Folder;
            string folder = file.Folder;
            file.Change(set =>
            {
                set.ProjectNumber = from.ProjectNumber;
                set.ProjectName = from.ProjectName;
                set.ProjectPhase = from.ProjectPhase;
                set.ProjectMilestone = from.ProjectMilestone;
                set.PromptForTemplate = from.PromptForTemplate;
                set.Template = Moved(from.Template, draftFolder, folder);
                set.LabelBlock = Moved(from.LabelBlock, draftFolder, folder);
                set.PageSetupOverrides = Moved(from.PageSetupOverrides, draftFolder, folder);
                CadFileReference location = from.NewSheetLocation;
                string where = location?.Resolve(draftFolder);

                if (location != null && !(where != null && SamePath(where, draftFolder)))
                {
                    set.NewSheetLocation = Moved(location, draftFolder, folder);
                }

                foreach (CadSheetSetProperty property in from.CustomProperties)
                {
                    CadSheetSetProperty have = set.FindCustomProperty(property.Name);

                    if (have == null)
                    {
                        set.AddCustomProperty(
                            property.Name,
                            property.Value,
                            property.IsSheetProperty ? CadSheetSetPropertyFlags.Sheet : CadSheetSetPropertyFlags.Set
                        );
                    }
                    else if (!string.Equals(have.Value, property.Value, StringComparison.Ordinal))
                    {
                        set.SetCustomProperty(property.Name, property.Value, have.Flags);
                    }
                }

                foreach (
                    string gone in set
                        .CustomProperties.Where(p => from.FindCustomProperty(p.Name) == null)
                        .Select(p => p.Name)
                        .ToList()
                )
                {
                    set.DeleteCustomProperty(gone);
                }
            });
        }

        private static CadFileReference Moved(CadFileReference reference, string fromFolder, string toFolder)
        {
            if (reference == null)
            {
                return null;
            }

            string path = reference.Resolve(fromFolder) ?? reference.FileName;
            return path == null ? reference : CadFileReference.FromPath(path, toFolder);
        }

        private static CadDrawingReference Moved(CadDrawingReference reference, string fromFolder, string toFolder) =>
            reference == null
                ? null
                : new CadDrawingReference(
                    Moved(reference.File, fromFolder, toFolder),
                    reference.Name,
                    reference.Handle,
                    reference.OwnerHandle
                );

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd('\\', '/'),
                    Path.GetFullPath(b).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase
                );
            }
            catch (Exception ex) when (IsProblem(ex))
            {
                return false;
            }
        }

        // ---- Finish

        /// <summary>
        /// Finish: makes the folder and the .dst through the engine, writes the properties of the draft into it, keeps the route,
        /// the folder and the import options in the settings, opens the set in the Sheet Set Manager (which puts it first in
        /// Recent) and closes the wizard. A set that cannot be made is refused on the line of the Confirm page, in the engine's
        /// words, and nothing is left behind.
        /// </summary>
        /// <returns>Whether the set was made.</returns>
        public bool Finish()
        {
            if (!CanFinish)
            {
                return false;
            }

            string problem = DetailsProblem();
            string dst = DstPath;

            if (problem != null || dst == null)
            {
                _notice = problem ?? "The sheet set data file cannot be named.";
                RaiseMessage();
                return false;
            }

            string name = _name.Trim();
            string description = _description.Trim().Length > 0 ? _description.Trim() : null;
            CadSheetSetFile file;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                file = _fromDrawings
                    ? CadSheetSetFile.CreateFromDrawings(dst, name, description, Sources(), _importOptions)
                    : CadSheetSetFile.CreateFromExample(dst, name, description, _example.Path, _createFolderHierarchy);
            }
            catch (Exception ex) when (IsProblem(ex))
            {
                _notice = "The sheet set was not made. " + ex.Message;
                RaiseMessage();
                return false;
            }

            try
            {
                ApplyDraft(file);
            }
            catch (Exception ex) when (IsProblem(ex))
            {
                _main.Say("The sheet set " + dst + " was made, but its properties could not be set: " + ex.Message);
            }

            _finished = true;
            CreatedFile = file.Path;
            string kept = Path.GetDirectoryName(dst);
            bool fromDrawings = _fromDrawings;
            CadSheetImportOptions options = _importOptions;
            _main.SheetSetStoreFolder = kept;
            _main.KeepSettings(s =>
            {
                s.NewSheetSetFromDrawings = fromDrawings;
                s.SheetImportPrefixTitles = options.PrefixTitleWithFileName;
                s.SheetImportSubsetsFromFolders = options.CreateSubsetsFromFolders;
                s.SheetImportIgnoreTopFolder = options.IgnoreTopLevelFolder;
            });
            DiscardDraft();
            _main.SheetSetManager.OpenSet(dst);
            Update();
            CloseRequested?.Invoke(this, true);
            return true;
        }

        /// <summary>The words the engine's file name is spelt as in a folder: each character a file cannot hold made an underscore, the ends trimmed of spaces and dots.</summary>
        private static string AsFolderName(string name)
        {
            var text = new StringBuilder(name.Length);

            foreach (char c in name)
            {
                text.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            }

            return text.ToString().Trim(' ', '.');
        }
    }
}
