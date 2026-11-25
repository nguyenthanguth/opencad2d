using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// A layout of a drawing as the Import Layouts as Sheets dialog and the Choose Layouts page of the wizard list it: its
    /// name and handle, and the reason it cannot become a sheet where it cannot. Made by
    /// <see cref="CadSheetSet.CheckLayouts(string)"/> and <see cref="CadSheetSetFile.CheckLayouts(string)"/>.
    /// </summary>
    public sealed class CadSheetImportLayout
    {
        internal CadSheetImportLayout(string name, string handle, string refusal)
        {
            Name = name;
            Handle = handle;
            Refusal = refusal;
        }

        /// <summary>Gets the layout's name, as the drawing spells it.</summary>
        public string Name { get; }

        /// <summary>Gets the layout's handle, in upper-case hexadecimal.</summary>
        public string Handle { get; }

        /// <summary>
        /// Gets why the layout cannot become a sheet, in a sentence ("The layout "A-101" is already a sheet of the sheet set
        /// "Tower" (C:\Sheets\Tower.dst)."); null where it can.
        /// </summary>
        public string Refusal { get; }

        /// <summary>Gets whether the layout cannot become a sheet.</summary>
        public bool IsRefused => Refusal != null;
    }

    /// <summary>
    /// The options of the Import Options dialog of the wizard, for a sheet set made from existing drawings
    /// (<see cref="CadSheetSetFile.CreateFromDrawings(string, string, string, IEnumerable{CadSheetImportSource}, CadSheetImportOptions)"/>).
    /// Every one is off in a new object; the dialog turns them on.
    /// </summary>
    public sealed class CadSheetImportOptions
    {
        /// <summary>
        /// Gets or sets whether a sheet's title is the drawing's file name, a hyphen and the layout's name ("A-101 - Ground
        /// floor") and not the layout's name alone.
        /// </summary>
        public bool PrefixTitleWithFileName { get; set; }

        /// <summary>
        /// Gets or sets whether a subset is made for each folder the drawings lie in, nested as the folders are, the sheets
        /// of a drawing going into the subset of its folder.
        /// </summary>
        public bool CreateSubsetsFromFolders { get; set; }

        /// <summary>
        /// Gets or sets, where <see cref="CreateSubsetsFromFolders"/> is on, whether the folder that was picked makes no
        /// subset of its own: the drawings lying directly in it go to the top of the set, and its folders make the first
        /// subsets. With it off, the picked folder is the first subset, named as the folder is.
        /// </summary>
        public bool IgnoreTopLevelFolder { get; set; }
    }

    /// <summary>
    /// One drawing the wizard's Choose Layouts page ticked: the folder that was picked to find it, the drawing, and the
    /// names of its layouts that are ticked, in the order they become sheets.
    /// </summary>
    public sealed class CadSheetImportSource
    {
        /// <summary>Makes the source of one drawing.</summary>
        /// <param name="rootFolder">The folder that was picked, which the drawing lies in or under; null for none.</param>
        /// <param name="drawingFile">The path of the drawing, a DWG.</param>
        /// <param name="layoutNames">The names of the layouts to import, in the order of their sheets.</param>
        /// <exception cref="ArgumentNullException">Thrown when the drawing or the layouts are null.</exception>
        /// <exception cref="ArgumentException">Thrown when the drawing's path or a layout's name is empty.</exception>
        public CadSheetImportSource(string rootFolder, string drawingFile, IEnumerable<string> layoutNames)
        {
            if (drawingFile == null)
            {
                throw new ArgumentNullException(nameof(drawingFile));
            }

            if (layoutNames == null)
            {
                throw new ArgumentNullException(nameof(layoutNames));
            }

            GeometryHelper.Guard.Argument(
                drawingFile.Trim().Length > 0,
                nameof(drawingFile),
                "A drawing needs a path."
            );
            LayoutNames = layoutNames.ToList().AsReadOnly();
            GeometryHelper.Guard.Argument(
                LayoutNames.All(n => !string.IsNullOrWhiteSpace(n)),
                nameof(layoutNames),
                "A layout needs a name."
            );
            RootFolder = string.IsNullOrWhiteSpace(rootFolder) ? null : rootFolder;
            DrawingFile = drawingFile;
        }

        /// <summary>Gets the folder that was picked, or null.</summary>
        public string RootFolder { get; }

        /// <summary>Gets the path of the drawing.</summary>
        public string DrawingFile { get; }

        /// <summary>Gets the names of the layouts to import, in the order of their sheets.</summary>
        public IReadOnlyList<string> LayoutNames { get; }
    }

    /// <summary>How <see cref="CadSheetSetSheet.FindLayout"/> found the layout of a sheet in its drawing (D7).</summary>
    public enum CadSheetLayoutStatus
    {
        /// <summary>The sheet has no layout in a drawing that is there: the drawing is gone, has no layout of the sheet's handle and none of its name.</summary>
        Missing = 0,

        /// <summary>The layout whose handle the sheet holds was found, whatever its name now is.</summary>
        FoundByHandle = 1,

        /// <summary>The handle is gone from the drawing (or the sheet holds none); the layout of the sheet's name was found.</summary>
        FoundByName = 2,
    }

    /// <summary>
    /// What <see cref="CadSheetSetSheet.FindLayout"/> found: the drawing's file as it resolves, and the layout in it, by its
    /// handle first and its name next.
    /// </summary>
    public sealed class CadFoundLayout
    {
        internal CadFoundLayout(
            CadSheetLayoutStatus status,
            string drawingFile,
            string layoutName,
            string layoutHandle,
            string ownerHandle
        )
        {
            Status = status;
            DrawingFile = drawingFile;
            LayoutName = layoutName;
            LayoutHandle = layoutHandle;
            OwnerHandle = ownerHandle;
        }

        /// <summary>Gets how the layout was found.</summary>
        public CadSheetLayoutStatus Status { get; }

        /// <summary>Gets whether the sheet's layout was not found.</summary>
        public bool IsMissing => Status == CadSheetLayoutStatus.Missing;

        /// <summary>Gets the full path of the drawing; null where the sheet's file is gone or the sheet names none.</summary>
        public string DrawingFile { get; }

        /// <summary>Gets the layout's name as the drawing has it now; null where it is missing.</summary>
        public string LayoutName { get; }

        /// <summary>Gets the layout's handle in upper-case hexadecimal; null where it is missing.</summary>
        public string LayoutHandle { get; }

        /// <summary>Gets the handle of the layout's owner (the layout dictionary); null where it is missing.</summary>
        public string OwnerHandle { get; }
    }
}
