using System;
using GeometryHelper.Dxf;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>The multileader styles and table styles of a drawing, read from outside.</summary>
    /// <remarks>
    /// The answer holds the styles' settings as plain members, not the style objects, so that a program that only wants to list
    /// them or to read the scale of one does not take a copy of the drawing. The styles come in the order of their names, without
    /// regard to case, then in ordinal order.
    /// </remarks>
    public sealed partial class CadRemoteServer
    {
        private static JsonObject AnnotationStyles(CadDocument document, string kind)
        {
            if (kind != null && kind != "multileader" && kind != "table")
            {
                throw new Refusal("The member kind has to be multileader or table.");
            }

            DxfDatabase database = document.Database;
            var answer = new JsonObject();

            if (kind != "table")
            {
                var styles = new JsonArray();
                string current = CadMLeaderStyleEditor.Current(database);

                foreach (string name in CadMLeaderStyleEditor.Styles(database))
                {
                    DxfMLeaderStyle style = CadMLeaderStyleEditor.Find(database, name);
                    CadMLeaderStyleSettings s = CadMLeaderStyleSettings.Of(style);
                    styles.Add(
                        new JsonObject()
                            .Set("name", name)
                            .Set("current", string.Equals(name, current, StringComparison.OrdinalIgnoreCase))
                            .Set(
                                "leaderType",
                                style.LeaderLineType == DxfLeaderType.StraightLeader ? "straight"
                                    : style.LeaderLineType == DxfLeaderType.SplineLeader ? "spline"
                                    : "none"
                            )
                            .Set("arrow", s.Arrowhead)
                            .Set("arrowSize", s.ArrowSize)
                            .Set("landing", s.IncludeLanding)
                            .Set("landingLength", s.LandingDistance)
                            .Set("landingGap", s.LandingGap)
                            .Set("textStyle", s.TextStyle)
                            .Set("textHeight", s.TextHeight)
                            .Set("scale", s.Scale)
                            .Set("annotative", style.Annotative == DxfAnnotativeStates.True)
                            .Set(
                                "content",
                                s.ContentType == DxfContentType.MTextContent ? "text"
                                    : s.ContentType == DxfContentType.BlockContent ? "block"
                                    : "none"
                            )
                            .Set("defaultText", s.DefaultText)
                            .Set("maxLeaderPoints", (long)s.MaxLeaderPoints)
                    );
                }

                answer.Set("multileaderStyles", styles);
            }

            if (kind != "multileader")
            {
                var styles = new JsonArray();
                string current = CadTableStyleEditor.Current(database);

                foreach (string name in CadTableStyleEditor.Styles(database))
                {
                    DxfTableStyle style = CadTableStyleEditor.Find(database, name);
                    styles.Add(
                        new JsonObject()
                            .Set("name", name)
                            .Set("current", string.Equals(name, current, StringComparison.OrdinalIgnoreCase))
                            .Set("description", style.Description)
                            .Set("horizontalMargin", style.HorizontalCellMargin)
                            .Set("verticalMargin", style.VerticalCellMargin)
                            .Set("titleShown", !style.IsTitleSuppressed)
                            .Set("headerShown", !style.IsHeaderSuppressed)
                            .Set("title", TableCell(style, DxfRowType.TitleRow))
                            .Set("header", TableCell(style, DxfRowType.HeaderRow))
                            .Set("data", TableCell(style, DxfRowType.DataRow))
                    );
                }

                answer.Set("tableStyles", styles);
            }

            return answer;
        }

        private static JsonObject TableCell(DxfTableStyle style, DxfRowType row) =>
            new JsonObject()
                .Set("textStyle", style.TextStyleName(row))
                .Set("textHeight", style.TextHeight(row))
                .Set("alignment", style.Alignment(row).ToString());
    }
}
