using System.Collections.Generic;

namespace OpenCAD2D.Remote
{
    /// <summary>
    /// A viewport of a paper layout of a running OpenCAD2D, as <see cref="CadRemoteLayout.Viewports"/> lists it: where it
    /// stands on the sheet, the part of model space it shows, its scale and the layers frozen in it alone.
    /// </summary>
    public sealed class CadRemoteViewport
    {
        internal static CadRemoteViewport FromJson(JsonObject json)
        {
            var frozen = new List<string>();

            foreach (object item in json.GetArray("frozenLayers"))
            {
                if (item is string name)
                {
                    frozen.Add(name);
                }
            }

            return new CadRemoteViewport
            {
                Handle = json.GetString("handle") ?? string.Empty,
                Number = (int)json.GetLong("number"),
                IsOn = json.GetBool("on"),
                IsLocked = json.GetBool("locked"),
                CenterX = json.GetDouble("centerX"),
                CenterY = json.GetDouble("centerY"),
                Width = json.GetDouble("width"),
                Height = json.GetDouble("height"),
                ViewCenterX = json.GetDouble("viewCenterX"),
                ViewCenterY = json.GetDouble("viewCenterY"),
                ViewHeight = json.GetDouble("viewHeight"),
                CustomScale = json.GetDouble("customScale"),
                Scale = json.GetString("scale"),
                FrozenLayers = frozen,
            };
        }

        /// <summary>Gets the handle of the viewport in the drawing.</summary>
        public string Handle { get; private set; }

        /// <summary>Gets the viewport's number; the viewport of the sheet itself (number 1) is left out of the list.</summary>
        public int Number { get; private set; }

        /// <summary>Gets whether the viewport shows model space.</summary>
        public bool IsOn { get; private set; }

        /// <summary>Gets whether the viewport's display is locked against zooming and panning.</summary>
        public bool IsLocked { get; private set; }

        /// <summary>Gets the x coordinate of the viewport's centre on the sheet, in paper units.</summary>
        public double CenterX { get; private set; }

        /// <summary>Gets the y coordinate of the viewport's centre on the sheet, in paper units.</summary>
        public double CenterY { get; private set; }

        /// <summary>Gets the width of the viewport on the sheet, in paper units.</summary>
        public double Width { get; private set; }

        /// <summary>Gets the height of the viewport on the sheet, in paper units.</summary>
        public double Height { get; private set; }

        /// <summary>Gets the x coordinate of the centre of the model-space view.</summary>
        public double ViewCenterX { get; private set; }

        /// <summary>Gets the y coordinate of the centre of the model-space view.</summary>
        public double ViewCenterY { get; private set; }

        /// <summary>Gets the height of the model-space view.</summary>
        public double ViewHeight { get; private set; }

        /// <summary>Gets the paper length of one model unit, such as 0.1 for 1:10.</summary>
        public double CustomScale { get; private set; }

        /// <summary>Gets the scale written as <c>1:10</c> or <c>2:1</c>, or null where the viewport has no named ratio.</summary>
        public string Scale { get; private set; }

        /// <summary>Gets the names of the layers frozen in this viewport only, sorted by name ignoring case.</summary>
        public IReadOnlyList<string> FrozenLayers { get; private set; }
    }
}
