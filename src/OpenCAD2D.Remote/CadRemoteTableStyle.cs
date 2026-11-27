namespace OpenCAD2D.Remote
{
    /// <summary>
    /// A table style of a drawing of a running OpenCAD2D, as <see cref="CadRemoteDocument.GetTableStyles"/> lists it, in
    /// millimetres as drawn. It is a copy; changing the drawing's style does not change it.
    /// </summary>
    /// <remarks>A style whose data rows have words 4.5 high and margins of 1.5 draws a row 7.5 high at the least.</remarks>
    public sealed class CadRemoteTableStyle
    {
        internal static CadRemoteTableStyle FromJson(JsonObject json) =>
            new CadRemoteTableStyle
            {
                Name = json.GetString("name") ?? string.Empty,
                IsCurrent = json.GetBool("current"),
                Description = json.GetString("description") ?? string.Empty,
                HorizontalMargin = json.GetDouble("horizontalMargin"),
                VerticalMargin = json.GetDouble("verticalMargin"),
                IsTitleShown = json.GetBool("titleShown", true),
                IsHeaderShown = json.GetBool("headerShown", true),
                Title = CadRemoteTableCellStyle.FromJson(json.GetObject("title")),
                Header = CadRemoteTableCellStyle.FromJson(json.GetObject("header")),
                Data = CadRemoteTableCellStyle.FromJson(json.GetObject("data")),
            };

        /// <summary>Gets the name of the style.</summary>
        public string Name { get; private set; }

        /// <summary>Gets whether it is the current style (CTABLESTYLE), the one TABLE draws in.</summary>
        public bool IsCurrent { get; private set; }

        /// <summary>Gets the description of the style.</summary>
        public string Description { get; private set; }

        /// <summary>Gets the margin at the left and right of the words in a cell, in millimetres.</summary>
        public double HorizontalMargin { get; private set; }

        /// <summary>Gets the margin above and below the words in a cell, in millimetres.</summary>
        public double VerticalMargin { get; private set; }

        /// <summary>Gets whether a table of the style shows its title row.</summary>
        public bool IsTitleShown { get; private set; }

        /// <summary>Gets whether a table of the style shows its header row.</summary>
        public bool IsHeaderShown { get; private set; }

        /// <summary>Gets the cell style of the title row.</summary>
        public CadRemoteTableCellStyle Title { get; private set; }

        /// <summary>Gets the cell style of the header row.</summary>
        public CadRemoteTableCellStyle Header { get; private set; }

        /// <summary>Gets the cell style of the data rows.</summary>
        public CadRemoteTableCellStyle Data { get; private set; }
    }
}
