using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// The multileader styles and table styles of a drawing as the list tools give them, read from a drawing file's database or from
    /// the answer of an app (<see cref="CadRemoteDocument.GetMultileaderStyles"/>), so that both give the same rows.
    /// </summary>
    internal static class AnnotationStyleReads
    {
        /// <summary>The multileader styles of a database, by name.</summary>
        public static IReadOnlyList<MLeaderStyleInfo> MLeaderStyles(DxfDatabase database)
        {
            string current = CadMLeaderStyleEditor.Current(database);
            return CadMLeaderStyleEditor
                .Styles(database)
                .Select(name =>
                {
                    DxfMLeaderStyle style = CadMLeaderStyleEditor.Find(database, name);
                    CadMLeaderStyleSettings s = CadMLeaderStyleSettings.Of(style);
                    return new MLeaderStyleInfo
                    {
                        Name = name,
                        Current = string.Equals(name, current, StringComparison.OrdinalIgnoreCase),
                        LeaderType =
                            style.LeaderLineType == DxfLeaderType.StraightLeader ? "straight"
                            : style.LeaderLineType == DxfLeaderType.SplineLeader ? "spline"
                            : "none",
                        Arrowhead = s.Arrowhead,
                        ArrowSize = EntityDescriber.R(s.ArrowSize),
                        Landing = s.IncludeLanding,
                        LandingDistance = EntityDescriber.R(s.LandingDistance),
                        LandingGap = EntityDescriber.R(s.LandingGap),
                        TextStyle = s.TextStyle,
                        TextHeight = EntityDescriber.R(s.TextHeight),
                        Scale = EntityDescriber.R(s.Scale),
                        Annotative = style.Annotative == DxfAnnotativeStates.True,
                        Content =
                            s.ContentType == DxfContentType.MTextContent ? "text"
                            : s.ContentType == DxfContentType.BlockContent ? "block"
                            : "none",
                        DefaultText = s.DefaultText,
                        MaxLeaderPoints = s.MaxLeaderPoints,
                        DrawnScale = s.Scale,
                    };
                })
                .ToList();
        }

        /// <summary>The multileader styles an app answers, by name.</summary>
        public static IReadOnlyList<MLeaderStyleInfo> MLeaderStyles(IReadOnlyList<CadRemoteMultileaderStyle> styles) =>
            styles
                .Select(s => new MLeaderStyleInfo
                {
                    Name = s.Name,
                    Current = s.IsCurrent,
                    LeaderType = s.LeaderType,
                    Arrowhead = s.Arrowhead,
                    ArrowSize = EntityDescriber.R(s.ArrowSize),
                    Landing = s.HasLanding,
                    LandingDistance = EntityDescriber.R(s.LandingLength),
                    LandingGap = EntityDescriber.R(s.LandingGap),
                    TextStyle = s.TextStyle,
                    TextHeight = EntityDescriber.R(s.TextHeight),
                    Scale = EntityDescriber.R(s.Scale),
                    Annotative = s.IsAnnotative,
                    Content = s.Content,
                    DefaultText = s.DefaultText,
                    MaxLeaderPoints = s.MaxLeaderPoints,
                    DrawnScale = s.Scale,
                })
                .ToList();

        /// <summary>The table styles of a database, by name.</summary>
        public static IReadOnlyList<TableStyleInfo> TableStyles(DxfDatabase database)
        {
            string current = CadTableStyleEditor.Current(database);
            return CadTableStyleEditor
                .Styles(database)
                .Select(name =>
                {
                    DxfTableStyle style = CadTableStyleEditor.Find(database, name);
                    return new TableStyleInfo
                    {
                        Name = name,
                        Current = string.Equals(name, current, StringComparison.OrdinalIgnoreCase),
                        Description = style.Description,
                        HorizontalMargin = EntityDescriber.R(style.HorizontalCellMargin),
                        VerticalMargin = EntityDescriber.R(style.VerticalCellMargin),
                        TitleShown = !style.IsTitleSuppressed,
                        HeaderShown = !style.IsHeaderSuppressed,
                        Title = CellStyle(style, DxfRowType.TitleRow),
                        Header = CellStyle(style, DxfRowType.HeaderRow),
                        Data = CellStyle(style, DxfRowType.DataRow),
                    };
                })
                .ToList();
        }

        /// <summary>The table styles an app answers, by name.</summary>
        public static IReadOnlyList<TableStyleInfo> TableStyles(IReadOnlyList<CadRemoteTableStyle> styles) =>
            styles
                .Select(s => new TableStyleInfo
                {
                    Name = s.Name,
                    Current = s.IsCurrent,
                    Description = s.Description,
                    HorizontalMargin = EntityDescriber.R(s.HorizontalMargin),
                    VerticalMargin = EntityDescriber.R(s.VerticalMargin),
                    TitleShown = s.IsTitleShown,
                    HeaderShown = s.IsHeaderShown,
                    Title = CellStyle(s.Title),
                    Header = CellStyle(s.Header),
                    Data = CellStyle(s.Data),
                })
                .ToList();

        private static TableCellStyleInfo CellStyle(DxfTableStyle style, DxfRowType row) =>
            new TableCellStyleInfo
            {
                TextStyle = style.TextStyleName(row),
                TextHeight = EntityDescriber.R(style.TextHeight(row)),
                Alignment = style.Alignment(row).ToString(),
            };

        private static TableCellStyleInfo CellStyle(CadRemoteTableCellStyle cell) =>
            new TableCellStyleInfo
            {
                TextStyle = cell.TextStyle,
                TextHeight = EntityDescriber.R(cell.TextHeight),
                Alignment = cell.Alignment,
            };
    }
}
