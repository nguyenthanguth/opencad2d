namespace OpenCAD2D.SheetSets
{
    public partial class CadSheetSubset
    {
        /// <summary>
        /// Gets or sets whether the sheets of this group are published with the set (the Publish Sheets in Subset row of the
        /// Subset Properties); true for a group the file says nothing of. It is kept as the opposite of AutoCAD's
        /// <c>OverrideSheetPublish</c>, which a file leaves out while it is false, so that a group that is published writes
        /// nothing.
        /// </summary>
        public bool PublishSheets
        {
            get => !CadDstTree.GetBool(Element, "OverrideSheetPublish");
            set
            {
                Edit();
                CadDstTree.SetBool(Element, "OverrideSheetPublish", !value);
            }
        }
    }
}
