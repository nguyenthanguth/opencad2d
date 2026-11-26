using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;

namespace OpenCAD2D.Remote
{
    /// <summary>Measuring before placing: what an object would take, the empty places of a frame, and whether a candidate fits.</summary>
    public sealed partial class CadRemoteDocument
    {
        /// <summary>
        /// Asks the app what an object would take on the page once added, without adding it: the box of everything it draws and
        /// the frame of each word, drawn by the app's own code in the app's own text styles, fonts and dimension styles. A
        /// dimension is drawn in the style it names. A text or MTEXT that names a text style names one the drawing has.
        /// </summary>
        /// <param name="entity">The object, made in code or read from the app; it is copied now and not changed.</param>
        /// <returns>The box and the frames.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the entity is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or the app refuses the object (a style it has not).</exception>
        public CadRemoteMeasurement Measure(DxfEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            var fragment = new DxfDatabase(RemoteFragment.Version);
            DxfEntity copy = entity.Clone();

            if (copy is DxfDimension dimension && dimension.DimBlock == null)
            {
                var picture = new DxfBlockTableRecord { Name = "*D" };
                fragment.BlockTable.Add(picture);
                dimension.DimBlock = picture;
            }

            Provide(copy, fragment);
            fragment.ModelSpace.AppendEntity(copy);
            return MeasurementOf(Ask(RemoteOps.Measure, request => request.Set("fragment", fragment.ToText())));
        }

        /// <summary>
        /// Asks the app what a multileader of words would take once MLEADER made it, its arrow at one point and its landing at
        /// another, in a multileader style of the drawing.
        /// </summary>
        /// <param name="style">The name of the multileader style; null for the current one.</param>
        /// <param name="arrowX">The X of the arrowhead's point.</param>
        /// <param name="arrowY">The Y of the arrowhead's point.</param>
        /// <param name="landingX">The X of the landing.</param>
        /// <param name="landingY">The Y of the landing.</param>
        /// <param name="words">The words.</param>
        /// <param name="scale">The scale; null for the one MLEADER gives a new multileader.</param>
        /// <returns>The box and the frames.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or the app refuses (no such style, no words).</exception>
        public CadRemoteMeasurement MeasureLeader(
            string style,
            double arrowX,
            double arrowY,
            double landingX,
            double landingY,
            string words,
            double? scale
        ) => MeasurementOf(Ask(RemoteOps.MeasureLeader, request =>
                {
                    request
                        .Set("arrow", new JsonArray(new object[] { arrowX, arrowY }))
                        .Set("landing", new JsonArray(new object[] { landingX, landingY }))
                        .Set("words", words);

                    if (style != null)
                    {
                        request.Set("style", style);
                    }

                    if (scale.HasValue)
                    {
                        request.Set("scale", scale.Value);
                    }
                }));

        /// <summary>
        /// Asks the app for the empty rectangles of a frame: the largest rectangles, not turned, that no line or word of the kinds
        /// asked about runs through or comes within the margin of, and that cannot be made larger, the largest first.
        /// </summary>
        /// <param name="layout">The name of a layout, or null or empty for model space.</param>
        /// <param name="minX">The least X of the frame.</param>
        /// <param name="minY">The least Y of the frame.</param>
        /// <param name="maxX">The greatest X of the frame.</param>
        /// <param name="maxY">The greatest Y of the frame.</param>
        /// <param name="kinds">The DXF names of the entities that take room; null for every kind.</param>
        /// <param name="margin">The clear room left round them, nought or more.</param>
        /// <param name="minWidth">The least width of a rectangle to give; 0 for any.</param>
        /// <param name="minHeight">The least height of a rectangle to give; 0 for any.</param>
        /// <param name="tolerance">How little still counts as touching, in drawing units; 0.001 is the library's.</param>
        /// <returns>The rectangles.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed, has no layout of that name, or the frame, margin or sizes are not usable.</exception>
        public CadRemoteFreeSpace GetFreeSpace(
            string layout,
            double minX,
            double minY,
            double maxX,
            double maxY,
            IEnumerable<string> kinds,
            double margin,
            double minWidth,
            double minHeight,
            double tolerance
        )
        {
            JsonObject answer = Ask(RemoteOps.FreeSpace, request =>
            {
                request
                    .Set("layout", layout)
                    .Set("frame", new JsonArray(new object[] { minX, minY, maxX, maxY }))
                    .Set("margin", margin)
                    .Set("minWidth", minWidth)
                    .Set("minHeight", minHeight)
                    .Set("tolerance", tolerance);

                if (kinds != null)
                {
                    request.Set("kinds", Strings(kinds));
                }
            });
            return new CadRemoteFreeSpace(NumbersOf(answer.GetArray("boxes")));
        }

        /// <summary>Asks the app whether a candidate place is free: nothing of the kinds asked about runs through it or comes within the margin of it.</summary>
        /// <param name="layout">The name of a layout, or null or empty for model space.</param>
        /// <param name="x">The X of the candidate's first corner.</param>
        /// <param name="y">The Y of the candidate's first corner.</param>
        /// <param name="width">The width, above nought.</param>
        /// <param name="height">The height, above nought.</param>
        /// <param name="rotation">The angle the width runs at, in radians.</param>
        /// <param name="kinds">The DXF names of the entities that take room; null for every kind.</param>
        /// <param name="margin">The clear room to leave, nought or more.</param>
        /// <param name="tolerance">How little still counts as touching, in drawing units; 0.001 is the library's.</param>
        /// <returns>True where nothing is in the way.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed, has no layout of that name, or the candidate or margin is not usable.</exception>
        public bool Fits(
            string layout,
            double x,
            double y,
            double width,
            double height,
            double rotation,
            IEnumerable<string> kinds,
            double margin,
            double tolerance
        )
        {
            JsonObject answer = Ask(RemoteOps.Fits, request =>
            {
                request
                    .Set("layout", layout)
                    .Set("candidate", new JsonArray(new object[] { x, y, width, height, rotation }))
                    .Set("margin", margin)
                    .Set("tolerance", tolerance);

                if (kinds != null)
                {
                    request.Set("kinds", Strings(kinds));
                }
            });
            return answer.GetBool("fits");
        }

        private static CadRemoteMeasurement MeasurementOf(JsonObject answer) =>
            new CadRemoteMeasurement(NumbersOf(answer.GetArray("box")), NumbersOf(answer.GetArray("frames")));
    }
}
