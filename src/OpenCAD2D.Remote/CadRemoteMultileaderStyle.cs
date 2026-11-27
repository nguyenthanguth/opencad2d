namespace OpenCAD2D.Remote
{
    /// <summary>
    /// A multileader style of a drawing of a running OpenCAD2D, as <see cref="CadRemoteDocument.GetMultileaderStyles"/> lists it:
    /// the settings of the Multileader Style Manager, in millimetres as drawn. It is a copy; changing the drawing's style
    /// does not change it.
    /// </summary>
    /// <remarks>A style of text height 2.5 and scale 10 draws its words 25 high on the sheet of a drawing.</remarks>
    public sealed class CadRemoteMultileaderStyle
    {
        internal static CadRemoteMultileaderStyle FromJson(JsonObject json) =>
            new CadRemoteMultileaderStyle
            {
                Name = json.GetString("name") ?? string.Empty,
                IsCurrent = json.GetBool("current"),
                LeaderType = json.GetString("leaderType") ?? "none",
                Arrowhead = json.GetString("arrow") ?? string.Empty,
                ArrowSize = json.GetDouble("arrowSize"),
                HasLanding = json.GetBool("landing"),
                LandingLength = json.GetDouble("landingLength"),
                LandingGap = json.GetDouble("landingGap"),
                TextStyle = json.GetString("textStyle") ?? string.Empty,
                TextHeight = json.GetDouble("textHeight"),
                Scale = json.GetDouble("scale", 1.0),
                IsAnnotative = json.GetBool("annotative"),
                Content = json.GetString("content") ?? "none",
                DefaultText = json.GetString("defaultText") ?? string.Empty,
                MaxLeaderPoints = (int)json.GetLong("maxLeaderPoints"),
            };

        /// <summary>Gets the name of the style.</summary>
        public string Name { get; private set; }

        /// <summary>Gets whether it is the current style (CMLEADERSTYLE), the one MLEADER draws in.</summary>
        public bool IsCurrent { get; private set; }

        /// <summary>Gets the type of the leader line: <c>straight</c>, <c>spline</c> or <c>none</c>.</summary>
        public string LeaderType { get; private set; }

        /// <summary>Gets the arrowhead by the name the Multileader Style Manager shows, such as <c>Closed filled</c> or <c>Dot</c>.</summary>
        public string Arrowhead { get; private set; }

        /// <summary>Gets the size of the arrowhead in millimetres.</summary>
        public double ArrowSize { get; private set; }

        /// <summary>Gets whether the leader has a landing (a dogleg).</summary>
        public bool HasLanding { get; private set; }

        /// <summary>Gets the length of the landing in millimetres.</summary>
        public double LandingLength { get; private set; }

        /// <summary>Gets the gap between the landing and the words in millimetres.</summary>
        public double LandingGap { get; private set; }

        /// <summary>Gets the name of the text style of the words.</summary>
        public string TextStyle { get; private set; }

        /// <summary>Gets the height of the words in millimetres.</summary>
        public double TextHeight { get; private set; }

        /// <summary>Gets the overall scale of the style; an annotative style takes its scale from the annotation scale instead.</summary>
        public double Scale { get; private set; }

        /// <summary>Gets whether the style is annotative.</summary>
        public bool IsAnnotative { get; private set; }

        /// <summary>Gets what the leader holds: <c>text</c>, <c>block</c> or <c>none</c>.</summary>
        public string Content { get; private set; }

        /// <summary>Gets the words a new multileader of the style starts with.</summary>
        public string DefaultText { get; private set; }

        /// <summary>Gets the most points a leader line takes; 0 for no limit.</summary>
        public int MaxLeaderPoints { get; private set; }
    }
}
