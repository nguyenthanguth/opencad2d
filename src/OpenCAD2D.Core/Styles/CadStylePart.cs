namespace OpenCAD2D
{
    /// <summary>
    /// The parts of a sample dimension, leader or multileader that the style previews tell apart: the preview lights the parts
    /// a field sets. A dimension's parts are those of its picture (<see cref="CadStyleSample"/>); the multileader uses
    /// <see cref="LeaderLine"/>, <see cref="Landing"/>, <see cref="Text"/>, <see cref="Block"/> and <see cref="Arrow"/>.
    /// </summary>
    public enum CadStylePart
    {
        /// <summary>No part: the outline of the sample, or an entity that no field sets.</summary>
        None,

        /// <summary>A definition point (a point on the Defpoints layer); the sample keeps them but the preview does not draw them.</summary>
        DefinitionPoint,

        /// <summary>The first half of the dimension line, from the first extension line to the middle (DIMSD1 leaves it out).</summary>
        DimensionLine1,

        /// <summary>The second half of the dimension line, from the middle to the second extension line (DIMSD2 leaves it out).</summary>
        DimensionLine2,

        /// <summary>The first extension line (DIMSE1 leaves it out).</summary>
        ExtensionLine1,

        /// <summary>The second extension line (DIMSE2 leaves it out).</summary>
        ExtensionLine2,

        /// <summary>The first arrowhead (DIMBLK1).</summary>
        Arrow1,

        /// <summary>The second arrowhead (DIMBLK2).</summary>
        Arrow2,

        /// <summary>The arrowhead of a leader drawn by the dimension style (DIMLDRBLK).</summary>
        LeaderArrow,

        /// <summary>The line of a leader or of a multileader.</summary>
        LeaderLine,

        /// <summary>The words of a dimension or of a multileader.</summary>
        Text,

        /// <summary>The box drawn round the words of a dimension (a DIMGAP below nought).</summary>
        TextFrame,

        /// <summary>The fill behind the words of a dimension (DIMTFILL); it is the words' own background, so it shares their entity.</summary>
        TextFill,

        /// <summary>The leader or the landing the words of a dimension are put out on (DIMTMOVE).</summary>
        TextLeader,

        /// <summary>The centre mark or the centre lines of a radius or a diameter (DIMCEN).</summary>
        CentreMark,

        /// <summary>The arc symbol of an arc length dimension (DIMARCSYM).</summary>
        ArcSymbol,

        /// <summary>The jog of a jogged radius, or of a linear dimension (DIMJOGANG).</summary>
        Jog,

        /// <summary>The landing (the dogleg) of a multileader.</summary>
        Landing,

        /// <summary>The block a multileader points to.</summary>
        Block,

        /// <summary>The arrowhead of a multileader.</summary>
        Arrow,
    }

    /// <summary>
    /// The sample a tab of the dimension style dialogs previews, each made of the dimensions that show the variables of its tab.
    /// Every sample is drawn as many times bigger as the style's overall scale (DIMSCALE) says, nought taken for 1.
    /// </summary>
    public enum CadDimSampleKind
    {
        /// <summary>The sample of the Dimension Style Manager and of Purge: a part 32 by 20 with a linear dimension across its top, a circle with its radius and an angle of 60 degrees.</summary>
        Overview,

        /// <summary>The Lines tab: the part with two linear dimensions across its top, 20 and 32 long, DIMDLI apart.</summary>
        Lines,

        /// <summary>The Symbols and arrows tab: a linear dimension, a circle with its radius and centre mark, an arc length dimension, a jogged radius and a leader.</summary>
        SymbolsAndArrows,

        /// <summary>The Text tab: the part with a linear dimension across its top and a vertical one on its left.</summary>
        Text,

        /// <summary>The Fit tab: a wide dimension, a narrow one the arrowheads and words do not fit, and one whose words were moved by hand.</summary>
        Fit,

        /// <summary>The Primary units tab: a linear dimension of 28.375 and an angle of 37.5 degrees.</summary>
        PrimaryUnits,

        /// <summary>The Alternate units tab: the sample of the Primary units tab.</summary>
        AlternateUnits,

        /// <summary>The Tolerances tab: the sample of the Primary units tab.</summary>
        Tolerances,
    }
}
