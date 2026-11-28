using System.Collections.Generic;

namespace OpenCAD2D.Remote
{
    /// <summary>
    /// A layout (or model space) of a drawing of a running OpenCAD2D, as <see cref="CadRemoteDocument.GetLayouts"/> lists it:
    /// its tab order, whether it is shown, its sheet settings, how many entities it holds and its viewports.
    /// </summary>
    public sealed class CadRemoteLayout
    {
        internal static CadRemoteLayout FromJson(JsonObject json)
        {
            var viewports = new List<CadRemoteViewport>();

            foreach (object item in json.GetArray("viewports"))
            {
                if (item is JsonObject viewport)
                {
                    viewports.Add(CadRemoteViewport.FromJson(viewport));
                }
            }

            return new CadRemoteLayout
            {
                Name = json.GetString("name") ?? string.Empty,
                TabOrder = (int)json.GetLong("tabOrder"),
                IsCurrent = json.GetBool("current"),
                IsModel = json.GetBool("model"),
                PaperWidth = json.GetDouble("paperWidth"),
                PaperHeight = json.GetDouble("paperHeight"),
                PaperUnits = json.GetString("paperUnits"),
                Paper = json.GetString("paper"),
                Rotation = (int)json.GetLong("rotation"),
                Entities = (int)json.GetLong("entities"),
                Viewports = viewports,
            };
        }

        /// <summary>Gets the name of the tab, <c>Model</c> or the layout's name.</summary>
        public string Name { get; private set; }

        /// <summary>Gets the place of the tab; <c>Model</c> is 0.</summary>
        public int TabOrder { get; private set; }

        /// <summary>Gets whether this tab is the one shown in the drawing.</summary>
        public bool IsCurrent { get; private set; }

        /// <summary>Gets whether this space is model space, which has no sheet of its own.</summary>
        public bool IsModel { get; private set; }

        /// <summary>Gets the width of the sheet in <see cref="PaperUnits"/>; 0 for model space.</summary>
        public double PaperWidth { get; private set; }

        /// <summary>Gets the height of the sheet in <see cref="PaperUnits"/>; 0 for model space.</summary>
        public double PaperHeight { get; private set; }

        /// <summary>Gets the sheet's units (<c>millimetres</c>, <c>inches</c> or <c>pixels</c>), or null for model space.</summary>
        public string PaperUnits { get; private set; }

        /// <summary>Gets the canonical media name of the sheet, or null for model space.</summary>
        public string Paper { get; private set; }

        /// <summary>Gets the plot rotation in degrees: 0, 90, 180 or 270.</summary>
        public int Rotation { get; private set; }

        /// <summary>Gets the number of entities in the space, excluding the sheet's own viewport (number 1).</summary>
        public int Entities { get; private set; }

        /// <summary>Gets the viewports of the layout in number order; empty for model space.</summary>
        public IReadOnlyList<CadRemoteViewport> Viewports { get; private set; }
    }
}
