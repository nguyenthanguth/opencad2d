namespace OpenCAD2D.Remote
{
    /// <summary>The cell style of one kind of row (title, header or data) of a <see cref="CadRemoteTableStyle"/>.</summary>
    public sealed class CadRemoteTableCellStyle
    {
        internal static CadRemoteTableCellStyle FromJson(JsonObject json) =>
            new CadRemoteTableCellStyle
            {
                TextStyle = json?.GetString("textStyle") ?? string.Empty,
                TextHeight = json?.GetDouble("textHeight") ?? 0.0,
                Alignment = json?.GetString("alignment") ?? string.Empty,
            };

        /// <summary>Gets the name of the text style of the words.</summary>
        public string TextStyle { get; private set; }

        /// <summary>Gets the height of the words in millimetres.</summary>
        public double TextHeight { get; private set; }

        /// <summary>Gets where the words stand in the cell, such as <c>MiddleCenter</c>.</summary>
        public string Alignment { get; private set; }
    }
}
