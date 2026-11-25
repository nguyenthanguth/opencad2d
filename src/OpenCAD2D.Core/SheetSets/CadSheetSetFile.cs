using System;
using System.IO;
using System.Xml.Linq;
using GeometryHelper;
using IoPath = System.IO.Path;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// A sheet set data file (.dst), AutoCAD 2021's own format, opened, changed and saved. The file is UTF-8 XML under a
    /// one-byte cipher; its XML tree is kept whole in memory and the model (<see cref="SheetSet"/> and what hangs from it)
    /// reads and writes through that tree, so every element and property this program does not know (the sheet selections,
    /// the transmittal setups, the callout block lists, the DWF options) is saved as it was read. A set made here opens in
    /// AutoCAD and one made in AutoCAD opens here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every change is saved at once, as in AutoCAD: <see cref="Change"/> applies an edit to the set, writes the file to
    /// <c>name.dst.tmp</c> and replaces the file with it, and the <see cref="FileRevision"/> goes up by one. If the file's
    /// time stamp or length has moved since it was read (another copy of the program, or AutoCAD, saved it), the set is
    /// reloaded first and the edit applied to the fresh copy. An edit that throws saves nothing and leaves the tree as it was.
    /// The parts of the model can be changed only inside <see cref="Change"/>; the objects handed out belong to the copy that
    /// was loaded and are stale after a reload, so read them again from <see cref="SheetSet"/>.
    /// </para>
    /// <para>
    /// New objects get an <c>ID</c> of a <c>g</c> and an upper-case GUID, from <see cref="Guid.NewGuid"/> or, where a test
    /// gives <c>Open</c> or <c>Create</c> a source of its own, a counter, so that the bytes of a set are the same on every run.
    /// The order of the set is the order of the XML, and nothing here follows the order of a dictionary.
    /// </para>
    /// </remarks>
    public sealed partial class CadSheetSetFile
    {
        private readonly Func<Guid> _newGuid;
        private XDocument _document;
        private DateTime _stamp;
        private long _length;
        private bool _editing;

        private CadSheetSetFile(string path, Func<Guid> newGuid)
        {
            Path = path;
            _newGuid = newGuid ?? Guid.NewGuid;
        }

        /// <summary>Gets the full path of the .dst.</summary>
        public string Path { get; }

        /// <summary>Gets the folder of the .dst, which the relative form of every file reference in it is relative to.</summary>
        public string Folder => IoPath.GetDirectoryName(Path);

        /// <summary>Gets the version of the format the file says it is (<c>DbVersion</c>); "1.1" for a set made here, as AutoCAD 2021 writes a new one.</summary>
        public string DbVersion => CadDstTree.GetString(_document.Root, "DbVersion");

        /// <summary>Gets the fingerprint of the database (<c>DbFingerPrint</c>, a <c>g</c> and a GUID), which AutoCAD keeps from the day the set was made.</summary>
        public string DbFingerPrint => CadDstTree.GetString(_document.Root, "DbFingerPrint");

        /// <summary>Gets how many times the file has been saved (<c>FileRevision</c>).</summary>
        public int FileRevision => CadDstTree.GetInt(_document.Root, "FileRevision", 0);

        /// <summary>Gets the sheet set, the root of the model. It is new after every <see cref="Change"/> that reloads.</summary>
        public CadSheetSet SheetSet => new CadSheetSet(this, _document.Root.Element(CadDstTree.SheetSetClass));

        /// <summary>Gets whether the file's time stamp or length is not what it was when it was read or last saved here. False where the file is gone.</summary>
        public bool HasChangedOnDisk
        {
            get
            {
                var info = new FileInfo(Path);
                return info.Exists && (info.LastWriteTimeUtc != _stamp || info.Length != _length);
            }
        }

        internal XDocument Document => _document;

        /// <summary>Reads a sheet set data file.</summary>
        /// <param name="path">The path of the .dst.</param>
        /// <returns>The file.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when there is no such file.</exception>
        /// <exception cref="FormatException">Thrown when the file is not a sheet set data file (the message begins "the file is not a sheet set data file").</exception>
        public static CadSheetSetFile Open(string path) => Open(path, null);

        /// <summary>
        /// Makes an empty sheet set and saves it, as AutoCAD's New Sheet Set.dst is: no template (so a new sheet asks for
        /// one), new sheets going to the folder of the .dst, one default view category, PDF publish options asking for a
        /// name. <see cref="DbVersion"/> is "1.1" and <see cref="FileRevision"/> 1.
        /// </summary>
        /// <param name="path">The path of the .dst to make; the file must not be there, and its folder must.</param>
        /// <param name="name">The name of the sheet set.</param>
        /// <param name="description">The description, or null for none.</param>
        /// <returns>The file, as saved.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path or the name is null.</exception>
        /// <exception cref="IOException">Thrown when a file is already at the path.</exception>
        /// <exception cref="ArgumentException">Thrown when the name or description holds a character a sheet set data file cannot hold.</exception>
        public static CadSheetSetFile Create(string path, string name, string description = null) =>
            Create(path, name, description, null);

        /// <summary>Reads a sheet set data file, new objects getting their GUIDs from a source of the caller's (a test passes a counter).</summary>
        internal static CadSheetSetFile Open(string path, Func<Guid> newGuid)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            Guard.Argument(path.Trim().Length > 0, nameof(path), "Open needs the path of a sheet set data file.");
            var file = new CadSheetSetFile(IoPath.GetFullPath(path), newGuid);
            file.Load();
            return file;
        }

        /// <summary>Makes a sheet set as <see cref="Create(string, string, string)"/> does, new objects getting their GUIDs from a source of the caller's.</summary>
        internal static CadSheetSetFile Create(string path, string name, string description, Func<Guid> newGuid)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Guard.Argument(
                path.Trim().Length > 0,
                nameof(path),
                "Create needs the path of the sheet set data file to make."
            );
            var file = new CadSheetSetFile(IoPath.GetFullPath(path), newGuid);
            if (File.Exists(file.Path))
            {
                throw new IOException("A file is already at " + file.Path + ".");
            }

            file._document = file.Blank(name, description);
            file.Save();
            return file;
        }

        /// <summary>Reloads the file where its time stamp or length has moved since it was read or last saved here.</summary>
        /// <returns>True when it was reloaded (the objects handed out before are stale); false when the file is as it was.</returns>
        /// <exception cref="FormatException">Thrown when the file has become something that is not a sheet set data file.</exception>
        public bool ReloadIfChanged()
        {
            if (!HasChangedOnDisk)
            {
                return false;
            }

            Load();
            return true;
        }

        /// <summary>
        /// Changes the sheet set and saves it: reloads the file first where it has moved on disk, runs the edit on the
        /// (fresh) <see cref="SheetSet"/>, and where the edit changed anything writes the file at once and raises the
        /// <see cref="FileRevision"/> by one. An edit that throws, or a save that fails, leaves the file and the tree as
        /// they were and the exception goes on.
        /// </summary>
        /// <param name="edit">The edit, given the sheet set to change.</param>
        /// <exception cref="ArgumentNullException">Thrown when the edit is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when called from inside an edit.</exception>
        /// <exception cref="ArgumentException">Thrown when the saved tree would hold a character a sheet set data file cannot hold (a control character other than tab, line feed and carriage return).</exception>
        public void Change(Action<CadSheetSet> edit)
        {
            if (edit == null)
            {
                throw new ArgumentNullException(nameof(edit));
            }

            if (_editing)
            {
                throw new InvalidOperationException(
                    "A change of the sheet set is under way; a change cannot start another."
                );
            }

            ReloadIfChanged();
            var backup = new XDocument(_document);
            _editing = true;
            try
            {
                edit(SheetSet);
                if (!XNode.DeepEquals(_document, backup))
                {
                    Save();
                }
            }
            catch
            {
                _document = backup;
                throw;
            }
            finally
            {
                _editing = false;
            }
        }

        internal void RequireEditing(XElement element)
        {
            if (!_editing)
            {
                throw new InvalidOperationException(
                    "A sheet set is changed inside CadSheetSetFile.Change, which saves it."
                );
            }

            if (element.Document != _document)
            {
                throw new InvalidOperationException(
                    "This part of the set belongs to a copy of the file that has been reloaded; take it again from SheetSet."
                );
            }
        }

        /// <summary>Makes a new object element of a class: its <c>clsid</c>, a new <c>ID</c> and, for an object a property holds, its <c>propname</c> and <c>vt</c> 13.</summary>
        internal XElement NewObject(string className, string propName)
        {
            var element = new XElement(
                className,
                new XAttribute("clsid", "g" + CadDstTree.ClassId(className)),
                new XAttribute("ID", NewId())
            );
            if (propName != null)
            {
                element.Add(new XAttribute("propname", propName), new XAttribute("vt", "13"));
            }

            return element;
        }

        internal string NewId() => "g" + _newGuid().ToString("D").ToUpperInvariant();

        private void Load()
        {
            var info = new FileInfo(Path);
            byte[] bytes = File.ReadAllBytes(Path);
            XDocument document = CadDstXml.Parse(CadDstCodec.Decode(bytes));
            if (
                document.Root == null
                || document.Root.Name.LocalName != CadDstTree.Database
                || document.Root.Element(CadDstTree.SheetSetClass) == null
            )
            {
                throw new FormatException("the file is not a sheet set data file (it holds no sheet set)");
            }

            _document = document;
            _stamp = info.LastWriteTimeUtc;
            _length = info.Length;
        }

        private void Save()
        {
            CadDstTree.SetInt(_document.Root, "FileRevision", FileRevision + 1);
            byte[] bytes = CadDstCodec.Encode(CadDstXml.Write(_document));
            string temp = Path + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(Path))
                {
                    File.Replace(temp, Path, null);
                }
                else
                {
                    File.Move(temp, Path);
                }
            }
            catch
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // A temporary file that cannot be deleted is left; the save failed for the reason that goes on.
                }

                throw;
            }

            var info = new FileInfo(Path);
            _stamp = info.LastWriteTimeUtc;
            _length = info.Length;
        }

        private XDocument Blank(string name, string description)
        {
            XElement database = NewObject(CadDstTree.Database, null);
            CadDstTree.SetString(database, "DbFingerPrint", NewId());
            CadDstTree.SetString(database, "DbVersion", "1.1");
            CadDstTree.SetInt(database, "FileRevision", 0);
            XElement set = NewObject(CadDstTree.SheetSetClass, "SheetSet");
            CadDstTree.InsertNamed(database, set);
            CadDstTree.InsertNamed(set, NewObject("AcSmCalloutBlocks", "CalloutBlocks"));
            CadDstTree.InsertNamed(set, NewObject(CadDstTree.LayoutReferenceClass, "DefDwtLayout"));
            CadDstTree.InsertNamed(set, NewObject(CadDstTree.BlockReferenceClass, "DefLabelBlk"));
            CadDstTree.SetString(set, "Desc", description);
            CadDstTree.SetString(set, "Name", name);
            XElement location = NewObject(CadDstTree.FileReferenceClass, "NewSheetLocation");
            CadDstTree.WriteFile(location, CadFileReference.FromPath(Folder, Folder));
            CadDstTree.InsertNamed(set, location);
            CadDstTree.InsertNamed(set, NewObject("AcSmProjectPointLocations", "ProjectPointLocations"));
            CadDstTree.SetBool(set, "PromptForDwt", true);
            XElement publish = NewObject("AcSmPublishOptions", "PublishOptions");
            CadDstTree.SetBool(publish, "DwfType", true);
            CadDstTree.SetBool(publish, "PromptForName", true);
            CadDstTree.InsertNamed(set, publish);
            CadDstTree.InsertNamed(set, NewObject(CadDstTree.ResourcesClass, "Resources"));
            CadDstTree.InsertNamed(set, NewObject("AcSmSheetSelSets", "SheetSelSets"));
            XElement categories = NewObject(CadDstTree.ViewCategoriesClass, "ViewCategories");
            XElement category = NewObject(CadDstTree.ViewCategoryClass, null);
            category.Add(NewObject(CadDstTree.CalloutBlockReferencesClass, "CalloutBlocks"));
            categories.Add(category);
            CadDstTree.InsertNamed(set, categories);
            return new XDocument(database);
        }
    }
}
