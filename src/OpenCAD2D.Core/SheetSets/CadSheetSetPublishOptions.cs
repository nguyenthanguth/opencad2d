using System.Xml.Linq;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// What a sheet set keeps for publishing (the <c>PublishOptions</c> object of the .dst, AutoCAD's Sheet Set PDF Publish
    /// Options): the folder the files go to, whether the sheets are one multi-sheet file or a file each, whether the file's
    /// name is asked for, and whether the sheets are published in reverse order. Taken from
    /// <see cref="CadSheetSet.PublishOptions"/>; it reads and writes the set's tree, and can be changed only inside
    /// <see cref="CadSheetSetFile.Change"/>.
    /// </summary>
    /// <remarks>
    /// The first three are AutoCAD's own (<c>DefaultOutputdir</c>, <c>DwfType</c> and <c>PromptForName</c>); a set made in
    /// AutoCAD or here opens with them in the other. Publish in Reverse Order is this program's: AutoCAD keeps it outside the
    /// file, so it is saved as <c>PublishInReverseOrder</c> in the same object, which AutoCAD 2021 reads past (checked by
    /// publishing a set that carries it).
    /// </remarks>
    public sealed class CadSheetSetPublishOptions
    {
        private readonly CadSheetSet _set;

        internal CadSheetSetPublishOptions(CadSheetSet set)
        {
            _set = set;
        }

        /// <summary>Gets or sets the folder the published files are saved in (<c>DefaultOutputdir</c>); null for none, which publishes to the folder of the .dst.</summary>
        /// <exception cref="System.InvalidOperationException">Thrown, on set, outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public CadFileReference OutputFolder
        {
            get => CadDstTree.ReadFile(Find("DefaultOutputdir"));
            set
            {
                XElement options = Writable();
                XElement folder = CadDstTree.FindObject(options, "DefaultOutputdir");
                if (value == null)
                {
                    folder?.Remove();
                    return;
                }

                if (folder == null)
                {
                    folder = _set.File.NewObject(CadDstTree.SimpleFileReferenceClass, "DefaultOutputdir");
                    CadDstTree.InsertNamed(options, folder);
                }

                CadDstTree.WriteFile(folder, value);
            }
        }

        /// <summary>Gets or sets whether the sheets go to one multi-sheet file (<c>DwfType</c>); false for a file each. A set made here starts with true, as AutoCAD's New Sheet Set does.</summary>
        /// <exception cref="System.InvalidOperationException">Thrown, on set, outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public bool MultiSheet
        {
            get => Get("DwfType");
            set => Set("DwfType", value);
        }

        /// <summary>Gets or sets whether publishing to one file asks for the file's name (<c>PromptForName</c>); a file for each sheet is named by the sheet and never asks.</summary>
        /// <exception cref="System.InvalidOperationException">Thrown, on set, outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public bool PromptForName
        {
            get => Get("PromptForName");
            set => Set("PromptForName", value);
        }

        /// <summary>Gets or sets whether the sheets are published in the reverse of set order (<c>PublishInReverseOrder</c>), the Publish in Reverse Order check of the palette.</summary>
        /// <exception cref="System.InvalidOperationException">Thrown, on set, outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public bool ReverseOrder
        {
            get => Get("PublishInReverseOrder");
            set => Set("PublishInReverseOrder", value);
        }

        private XElement Find(string name)
        {
            XElement options = CadDstTree.FindObject(_set.Element, "PublishOptions");
            return options == null ? null : CadDstTree.FindObject(options, name);
        }

        private bool Get(string name)
        {
            XElement options = CadDstTree.FindObject(_set.Element, "PublishOptions");
            return options != null && CadDstTree.GetBool(options, name);
        }

        private void Set(string name, bool value) => CadDstTree.SetBool(Writable(), name, value);

        /// <summary>The set's publish options object, made where a set from an older file has none.</summary>
        private XElement Writable()
        {
            _set.Edit();
            XElement options = CadDstTree.FindObject(_set.Element, "PublishOptions");
            if (options == null)
            {
                options = _set.File.NewObject(CadDstTree.PublishOptionsClass, "PublishOptions");
                CadDstTree.InsertNamed(_set.Element, options);
            }

            return options;
        }
    }
}
