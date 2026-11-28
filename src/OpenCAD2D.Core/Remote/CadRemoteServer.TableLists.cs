using System;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>The layouts, linetypes and block summaries of a drawing, read from outside without copying the drawing.</summary>
    public sealed partial class CadRemoteServer
    {
        private static JsonObject LayoutDetails(CadDocument document)
        {
            string current = (document.CurrentSpace ?? document.ModelSpace).Name;
            var layouts = new JsonArray();

            foreach (
                CadSpace space in document
                    .Spaces.Where(s => !s.IsBlockEditor)
                    .OrderBy(s => s.IsModel || s.Layout == null ? 0 : s.Layout.TabOrder)
                    .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(s => s.Name, StringComparer.Ordinal)
            )
            {
                bool isModel = space.IsModel || space.Layout == null;
                int entities = space.Block.Count(e => !e.IsErased && !(e is DxfViewport v && v.Number == 1));
                var row = new JsonObject()
                    .Set("name", space.Name)
                    .Set("tabOrder", isModel ? 0L : (long)space.Layout.TabOrder)
                    .Set("current", string.Equals(space.Name, current, StringComparison.OrdinalIgnoreCase))
                    .Set("model", isModel)
                    .Set("entities", (long)entities);

                if (!isModel)
                {
                    DxfLayout layout = space.Layout;
                    var viewports = new JsonArray();

                    foreach (
                        DxfViewport v in space
                            .Block.OfType<DxfViewport>()
                            .Where(vp => !vp.IsErased && vp.Number != 1)
                            .OrderBy(vp => vp.Number)
                            .ThenBy(vp => vp.Handle ?? string.Empty, StringComparer.Ordinal)
                    )
                    {
                        double scale = v.CustomScale;
                        var frozen = new JsonArray(
                            v.GetFrozenLayers()
                                .Select(l => l.Name)
                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                .ThenBy(n => n, StringComparer.Ordinal)
                                .Select(n => (object)n)
                        );
                        viewports.Add(
                            new JsonObject()
                                .Set("handle", v.Handle ?? string.Empty)
                                .Set("number", (long)v.Number)
                                .Set("on", v.On)
                                .Set("locked", v.Locked)
                                .Set("centerX", v.CenterPoint.X)
                                .Set("centerY", v.CenterPoint.Y)
                                .Set("width", v.Width)
                                .Set("height", v.Height)
                                .Set("viewCenterX", v.ViewCenter.X)
                                .Set("viewCenterY", v.ViewCenter.Y)
                                .Set("viewHeight", v.ViewHeight)
                                .Set("customScale", scale)
                                .Set("scale", CadViewports.ScaleName(scale) is string s && s.Length > 0 ? s : null)
                                .Set("frozenLayers", frozen)
                        );
                    }

                    row.Set("paperWidth", layout.PlotPaperSize.X)
                        .Set("paperHeight", layout.PlotPaperSize.Y)
                        .Set(
                            "paperUnits",
                            layout.PlotPaperUnits == DxfPlotPaperUnit.Millimeters ? "millimetres"
                                : layout.PlotPaperUnits == DxfPlotPaperUnit.Inches ? "inches"
                                : "pixels"
                        )
                        .Set("paper", layout.CanonicalMediaName)
                        .Set("rotation", (long)((int)layout.PlotRotation * 90))
                        .Set("viewports", viewports);
                }

                layouts.Add(row);
            }

            return new JsonObject().Set("layouts", layouts);
        }

        private static JsonObject LinetypeDetails(CadDocument document)
        {
            var linetypes = new JsonArray();

            foreach (
                DxfLinetypeTableRecord linetype in document
                    .Database.LinetypeTable.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(l => l.Name, StringComparer.Ordinal)
            )
            {
                var dashes = new JsonArray(
                    Enumerable.Range(0, linetype.NumDashes).Select(i => (object)linetype.DashLengthAt(i))
                );
                linetypes.Add(
                    new JsonObject()
                        .Set("name", linetype.Name)
                        .Set("description", linetype.AsciiDescription)
                        .Set("patternLength", linetype.PatternLength)
                        .Set("dashes", dashes)
                );
            }

            return new JsonObject().Set("linetypes", linetypes);
        }

        private static JsonObject BlockSummaries(CadDocument document)
        {
            var blocks = new JsonArray();

            foreach (
                DxfBlockTableRecord block in document
                    .Database.BlockTable.Where(b =>
                        !b.IsLayout
                        && !b.IsAnonymous
                        && !b.Name.StartsWith("*", StringComparison.Ordinal)
                        && !b.IsDependent
                        && !b.IsFromExternalReference
                        && !b.IsFromOverlayReference
                    )
                    .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(b => b.Name, StringComparer.Ordinal)
            )
            {
                blocks.Add(
                    new JsonObject().Set("name", block.Name).Set("entities", (long)block.Count(e => !e.IsErased))
                );
            }

            return new JsonObject().Set("blocks", blocks);
        }
    }
}
