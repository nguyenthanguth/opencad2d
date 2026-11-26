namespace OpenCAD2D
{
    public sealed partial class GlobalSettings
    {
        /// <summary>
        /// Gets or sets whether the Create Sheet Set wizard (NEWSHEETSET) starts at "Existing drawings" and not at "An example
        /// sheet set", the choice of its Begin page as the user last made it. False by default.
        /// </summary>
        public bool NewSheetSetFromDrawings { get; set; }

        /// <summary>Gets or sets the Import Options "Prefix sheet titles with file name" of the wizard's Choose Layouts page; true by default.</summary>
        public bool SheetImportPrefixTitles { get; set; } = true;

        /// <summary>Gets or sets the Import Options "Create subsets based on folder structure"; true by default.</summary>
        public bool SheetImportSubsetsFromFolders { get; set; } = true;

        /// <summary>Gets or sets the Import Options "Ignore top level folder", which counts where subsets are made from folders; true by default.</summary>
        public bool SheetImportIgnoreTopFolder { get; set; } = true;
    }
}
