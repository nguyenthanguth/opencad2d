using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// The layouts, linetypes and block summaries of a drawing as the list tools give them, read from a drawing file or from the
    /// answer of an app (<see cref="CadRemoteDocument.GetLayouts"/>, <see cref="CadRemoteDocument.GetLinetypes"/>,
    /// <see cref="CadRemoteDocument.GetBlocks"/>), so that both give the same rows without copying the app's drawing.
    /// </summary>
    internal static class TableReads
    {
        /// <summary>The layouts of a document in tab order, Model first.</summary>
        public static IReadOnlyList<LayoutInfo> Layouts(CadDocument document, string current) =>
            document
                .Spaces.Where(s => !s.IsBlockEditor)
                .Select(s => LayoutOf(s, current))
                .OrderBy(l => l.TabOrder)
                .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.Name, StringComparer.Ordinal)
                .ToList();

        /// <summary>The layouts an app answers, in tab order.</summary>
        public static IReadOnlyList<LayoutInfo> Layouts(IReadOnlyList<CadRemoteLayout> layouts) =>
            layouts
                .Select(l =>
                    l.IsModel
                        ? new LayoutInfo
                        {
                            Name = l.Name,
                            TabOrder = l.TabOrder,
                            Current = l.IsCurrent,
                            Entities = l.Entities,
                            Viewports = new List<ViewportInfo>(),
                        }
                        : new LayoutInfo
                        {
                            Name = l.Name,
                            TabOrder = l.TabOrder,
                            Current = l.IsCurrent,
                            PaperSize = new[] { EntityDescriber.R(l.PaperWidth), EntityDescriber.R(l.PaperHeight) },
                            PaperUnits = l.PaperUnits,
                            Paper = l.Paper,
                            Rotation = l.Rotation,
                            Entities = l.Entities,
                            Viewports = l.Viewports.Select(ViewportOf).ToList(),
                        }
                )
                .ToList();

        /// <summary>The linetypes of a database, by name.</summary>
        public static IReadOnlyList<LinetypeInfo> Linetypes(DxfDatabase database) =>
            database
                .LinetypeTable.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.Name, StringComparer.Ordinal)
                .Select(l => new LinetypeInfo
                {
                    Name = l.Name,
                    Description = l.AsciiDescription,
                    PatternLength = EntityDescriber.R(l.PatternLength),
                    Dashes = Enumerable
                        .Range(0, l.NumDashes)
                        .Select(i => EntityDescriber.R(l.DashLengthAt(i)))
                        .ToList(),
                })
                .ToList();

        /// <summary>The linetypes an app answers, by name.</summary>
        public static IReadOnlyList<LinetypeInfo> Linetypes(IReadOnlyList<CadRemoteLinetype> linetypes) =>
            linetypes
                .Select(l => new LinetypeInfo
                {
                    Name = l.Name,
                    Description = l.Description,
                    PatternLength = EntityDescriber.R(l.PatternLength),
                    Dashes = l.Dashes.Select(EntityDescriber.R).ToList(),
                })
                .ToList();

        /// <summary>The blocks of a database that an INSERT can place, by name.</summary>
        public static IReadOnlyList<BlockInfo> Blocks(DxfDatabase database) =>
            database
                .BlockTable.Where(b =>
                    !b.IsLayout
                    && !b.IsAnonymous
                    && !b.Name.StartsWith("*", StringComparison.Ordinal)
                    && !b.IsDependent
                    && !b.IsFromExternalReference
                    && !b.IsFromOverlayReference
                )
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.Name, StringComparer.Ordinal)
                .Select(b => new BlockInfo { Name = b.Name, Entities = b.Count(e => !e.IsErased) })
                .ToList();

        /// <summary>The blocks an app answers, by name.</summary>
        public static IReadOnlyList<BlockInfo> Blocks(IReadOnlyList<CadRemoteBlock> blocks) =>
            blocks.Select(b => new BlockInfo { Name = b.Name, Entities = b.Entities }).ToList();

        private static LayoutInfo LayoutOf(CadSpace space, string current)
        {
            var info = new LayoutInfo
            {
                Name = space.Name,
                Current = string.Equals(space.Name, current, StringComparison.OrdinalIgnoreCase),
                Entities = space.Block.Count(e => !e.IsErased && !(e is DxfViewport v && v.Number == 1)),
                Viewports = new List<ViewportInfo>(),
            };

            if (space.IsModel || space.Layout == null)
            {
                return info;
            }

            DxfLayout layout = space.Layout;
            info.TabOrder = layout.TabOrder;
            info.PaperSize = new[]
            {
                EntityDescriber.R(layout.PlotPaperSize.X),
                EntityDescriber.R(layout.PlotPaperSize.Y),
            };
            info.PaperUnits =
                layout.PlotPaperUnits == DxfPlotPaperUnit.Millimeters ? "millimetres"
                : layout.PlotPaperUnits == DxfPlotPaperUnit.Inches ? "inches"
                : "pixels";
            info.Paper = layout.CanonicalMediaName;
            info.Rotation = (int)layout.PlotRotation * 90;
            info.Viewports = space
                .Block.OfType<DxfViewport>()
                .Where(v => !v.IsErased && v.Number != 1)
                .OrderBy(v => v.Number)
                .ThenBy(v => v.Handle, StringComparer.Ordinal)
                .Select(ViewportOf)
                .ToList();
            return info;
        }

        private static ViewportInfo ViewportOf(DxfViewport v)
        {
            double scale = v.CustomScale;
            return new ViewportInfo
            {
                Handle = v.Handle,
                Number = v.Number,
                On = v.On,
                Locked = v.Locked,
                Center = new[] { EntityDescriber.R(v.CenterPoint.X), EntityDescriber.R(v.CenterPoint.Y) },
                Width = EntityDescriber.R(v.Width),
                Height = EntityDescriber.R(v.Height),
                ViewCenter = new[] { EntityDescriber.R(v.ViewCenter.X), EntityDescriber.R(v.ViewCenter.Y) },
                ViewHeight = EntityDescriber.R(v.ViewHeight),
                CustomScale = Math.Round(scale, 8),
                Scale = CadViewports.ScaleName(scale) is string name && name.Length > 0 ? name : null,
                FrozenLayers = v.GetFrozenLayers()
                    .Select(l => l.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n => n, StringComparer.Ordinal)
                    .ToList(),
            };
        }

        private static ViewportInfo ViewportOf(CadRemoteViewport v) =>
            new ViewportInfo
            {
                Handle = v.Handle,
                Number = v.Number,
                On = v.IsOn,
                Locked = v.IsLocked,
                Center = new[] { EntityDescriber.R(v.CenterX), EntityDescriber.R(v.CenterY) },
                Width = EntityDescriber.R(v.Width),
                Height = EntityDescriber.R(v.Height),
                ViewCenter = new[] { EntityDescriber.R(v.ViewCenterX), EntityDescriber.R(v.ViewCenterY) },
                ViewHeight = EntityDescriber.R(v.ViewHeight),
                CustomScale = Math.Round(v.CustomScale, 8),
                Scale = v.Scale,
                FrozenLayers = v.FrozenLayers,
            };
    }
}
