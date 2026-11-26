using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// What a thing would take on the page, found before it is added, for a program that lays a drawing out and wants to try
    /// a place: the box of everything it draws and the frame of each word (<see cref="CadMeasurement"/>). The thing is drawn
    /// by the code that draws it once it is in the drawing, and measured by the code that measures it there
    /// (<see cref="CadSpatial.Bounds(CadDocument, CadSpace)"/>), so the box is the same to the last digit: a TEXT or an MTEXT is
    /// drawn by the document's own builder in its own text styles and fonts, a TrueType word measured by the font's letters; a
    /// dimension's picture is made as DIM makes it, in a scratch drawing that holds the dimension style and text styles the
    /// document has. Nothing is added to the drawing, no handle taken, no undo step made, and no display list made stale, so
    /// a program may measure a thousand places between two edits.
    /// </summary>
    public static class CadMeasure
    {
        private static readonly PropertyInfo[] DimensionVariables = typeof(DxfDimStyleTableRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p =>
                p.DeclaringType == typeof(DxfDimStyleTableRecord)
                && p.CanRead
                && p.CanWrite
                && p.GetIndexParameters().Length == 0
            )
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

        /// <summary>
        /// Measures an entity as it would be once added to a space of the document: a copy of it is drawn and the box and the
        /// frames of the words found; the entity itself is not changed and the document is not. A TEXT 5 high reading "Plate"
        /// has the box its letters take; an MTEXT 40 wide wrapping at 20 has the box of its lines; a dimension has the box of its
        /// lines, arrows and words. The text style and the dimension style the entity names are the document's: an entity
        /// that names one the document has not is refused, as a commit refuses it.
        /// </summary>
        /// <param name="document">The drawing the entity is to be added to, whose styles and fonts it is drawn in.</param>
        /// <param name="entity">The entity, in no space or in one.</param>
        /// <returns>The box and the frames.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document or the entity is null.</exception>
        /// <exception cref="ArgumentException">Thrown when a text or a dimension names a text style or a dimension style the document has not.</exception>
        public static CadMeasurement Of(CadDocument document, DxfEntity entity)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            DxfEntity copy = entity.Clone();

            if (copy is DxfDimension dimension)
            {
                return OfDimension(document, dimension);
            }

            string words =
                copy is DxfDBText single ? single.TextStyleName
                : copy is DxfMText multi ? multi.TextStyleName
                : null;
            Guard.Argument(
                string.IsNullOrEmpty(words) || document.Database.TextStyleTable.Has(words),
                nameof(entity),
                "The drawing has no text style " + words + "."
            );
            return Measured(CadSpatialIndex.OfLoose(document.BuildLoose(copy)));
        }

        /// <summary>
        /// Measures a multileader of words as MLEADER would make it, its arrow at one point and its landing at another, in a
        /// multileader style of the document: the leader line, its arrowhead, the landing and the words, laid out as the command
        /// lays them and measured in the style's text style. An arrow at (0, 0) and a landing at (30, 20) in a style of text 2.5
        /// high and a landing of 0.36 gives a box from (0, 0) to a little past (30.45, 20) by the width of the words.
        /// </summary>
        /// <param name="document">The drawing, whose style and fonts are used.</param>
        /// <param name="style">The name of the multileader style; null for the current one, which a drawing with none is given as Standard, as MLEADER gives it.</param>
        /// <param name="arrow">Where the arrowhead points.</param>
        /// <param name="landing">Where the leader lands.</param>
        /// <param name="words">The words, not empty.</param>
        /// <param name="scale">The scale of the multileader; null for the one MLEADER gives a new one.</param>
        /// <returns>The box and the frames.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document is null.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when the words are empty, the arrow and the landing are the same point, the scale is not above nought, the
        /// document has no such style, or the style is annotative or of no words (a block or none).
        /// </exception>
        public static CadMeasurement Leader(
            CadDocument document,
            string style,
            GeoPoint2 arrow,
            GeoPoint2 landing,
            string words,
            double? scale
        ) =>
            Leader(
                document,
                style,
                arrow,
                landing,
                words,
                scale,
                new Tolerance(Tolerance.DefaultEqualPoint, Tolerance.DefaultEqualVector)
            );

        /// <summary>
        /// Measures a multileader as <see cref="Leader(CadDocument, string, GeoPoint2, GeoPoint2, string, double?)"/> does, the
        /// landing refused where it is within the tolerance of the arrow: a landing 1E-12 away from the arrow draws no leader
        /// line, as one exactly at it does not.
        /// </summary>
        /// <param name="document">The drawing, whose style and fonts are used.</param>
        /// <param name="style">The name of the multileader style; null for the current one, which a drawing with none is given as Standard, as MLEADER gives it.</param>
        /// <param name="arrow">Where the arrowhead points.</param>
        /// <param name="landing">Where the leader lands.</param>
        /// <param name="words">The words, not empty.</param>
        /// <param name="scale">The scale of the multileader; null for the one MLEADER gives a new one.</param>
        /// <param name="tolerance">How near the landing may be to the arrow, in millimetres, before it is taken for the same point.</param>
        /// <returns>The box and the frames.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document is null.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when the words are empty, the arrow and the landing are within the tolerance of each other, the scale is not
        /// above nought, the document has no such style, or the style is annotative or of no words (a block or none).
        /// </exception>
        public static CadMeasurement Leader(
            CadDocument document,
            string style,
            GeoPoint2 arrow,
            GeoPoint2 landing,
            string words,
            double? scale,
            Tolerance tolerance
        )
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            Guard.Argument(!string.IsNullOrEmpty(words), nameof(words), "A multileader to measure has words.");
            Guard.Argument(
                Guard.IsFinite(arrow.X)
                    && Guard.IsFinite(arrow.Y)
                    && Guard.IsFinite(landing.X)
                    && Guard.IsFinite(landing.Y),
                nameof(arrow),
                "The arrow and the landing of a multileader are finite points."
            );
            Guard.Argument(
                Math.Sqrt(
                    ((arrow.X - landing.X) * (arrow.X - landing.X)) + ((arrow.Y - landing.Y) * (arrow.Y - landing.Y))
                ) > tolerance.EqualPoint,
                nameof(landing),
                "The landing of a multileader is not at its arrow."
            );
            DxfDatabase real = document.Database;
            DxfDatabase scratchDatabase = NewScratch(real);
            string name = string.IsNullOrWhiteSpace(style) ? CadMLeaderStyleEditor.Current(real) : style.Trim();
            DxfMLeaderStyle found = CadMLeaderStyleEditor.Find(real, name);
            double scaled;

            if (
                found == null
                && string.IsNullOrWhiteSpace(style)
                && string.CompareOrdinal(real.Version ?? string.Empty, "AC1021") >= 0
            )
            {
                // A drawing with no current multileader style is given Standard by MLEADER before it draws: in the scratch.
                scratchDatabase.Measurement = real.Measurement;
                CadMLeaderStyleEditor.MakeStandard(scratchDatabase);
                name = "Standard";
                CopyTextStyle(real, scratchDatabase, "Standard");
                found = CadMLeaderStyleEditor.Find(scratchDatabase, name);
                scaled = scale ?? CadMLeaderStyleEditor.NewScale(real, found, null);
            }
            else
            {
                Guard.Argument(found != null, nameof(style), "The drawing has no multileader style " + name + ".");
                Guard.Argument(
                    found.ContentType == DxfContentType.MTextContent,
                    nameof(style),
                    "The multileader style " + name + " is not of words: only words are measured."
                );
                Guard.Argument(
                    found.Annotative != DxfAnnotativeStates.True,
                    nameof(style),
                    "The multileader style "
                        + name
                        + " is annotative: its size follows the annotation scale of the space."
                );
                scaled = scale ?? CadMLeaderStyleEditor.NewScale(real, found, null);
                CadMLeaderStyleSettings settings = CadMLeaderStyleSettings.Of(found);
                CopyTextStyle(real, scratchDatabase, settings.TextStyle);
                CadMLeaderStyleEditor.WriteNew(scratchDatabase, name, settings);
            }

            Guard.Argument(
                scaled > 0.0 && Guard.IsFinite(scaled),
                nameof(scale),
                "The scale of a multileader is a number above nought."
            );
            CadDocument scratch = document.MakeScratch(scratchDatabase);

            var from = new GeoPoint3(arrow.X, arrow.Y, 0.0);
            var to = new GeoPoint3(landing.X, landing.Y, 0.0);
            DxfMLeaderStyle written = CadMLeaderStyleEditor.Find(scratchDatabase, name);
            DxfMLeader mleader = CadMLeaderStyleEditor.Make(written, scaled, from, to, words);

            // As the MLEADER command makes it once its words are asked.
            scratchDatabase.ModelSpace.AppendEntity(mleader);
            mleader.MLeaderStyle = name;
            mleader.SetFromStyle();

            if (!scaled.Equals(written.Scale))
            {
                mleader.Scale = scaled;
            }

            CadMLeaderStyleEditor.LandWords(mleader, from, to, scratchDatabase);
            mleader.AsTheCommandMakesIt(written.Scale, false);
            return Measured(CadSpatialIndex.Of(scratch.GetDisplay(scratch.ModelSpace)));
        }

        private static CadMeasurement OfDimension(CadDocument document, DxfDimension dimension)
        {
            DxfDatabase real = document.Database;
            DxfDatabase scratchDatabase = NewScratch(real);
            string styleName = string.IsNullOrEmpty(dimension.DimensionStyleName)
                ? "Standard"
                : dimension.DimensionStyleName;
            CopyDimensionStyle(real, scratchDatabase, styleName);
            CopyLayer(real, scratchDatabase, dimension.Layer);
            CadDocument scratch = document.MakeScratch(scratchDatabase);

            // A dimension made in code comes with a picture that is empty, a block of its own: drawn as DIM draws it.
            var picture = new DxfBlockTableRecord { Name = "*D" };
            scratchDatabase.BlockTable.Add(picture);
            dimension.DimBlock = picture;
            scratchDatabase.ModelSpace.AppendEntity(dimension);
            CadDimensionPicture.Redraw(dimension);
            return Measured(CadSpatialIndex.Of(scratch.GetDisplay(scratch.ModelSpace)));
        }

        private static CadMeasurement Measured(CadSpatialIndex index)
        {
            DisplayBox box = DisplayBox.Empty;

            if (index.HandleCount > 0)
            {
                double[] drawn = index.HandleBox;

                if (drawn[0] <= drawn[2] && drawn[1] <= drawn[3])
                {
                    box = new DisplayBox(drawn[0], drawn[1], drawn[2], drawn[3]);
                }
            }

            var frames = new List<CadFrame>(index.Frames.Length);

            foreach (CadTextFrame frame in index.Frames)
            {
                frames.Add(CadFrame.Of(frame));
            }

            return new CadMeasurement(box, frames);
        }

        /// <summary>A drawing of no entities of the version of the real one, which a measure fills with the records of the real one it draws in.</summary>
        private static DxfDatabase NewScratch(DxfDatabase real)
        {
            try
            {
                return new DxfDatabase(real.Version);
            }
            catch (ArgumentException)
            {
                // A drawing of R12 to R14 is not made new: the scratch is of R2013.
                return new DxfDatabase("AC1027");
            }
        }

        /// <summary>
        /// Makes the text style of a name in the scratch as the real drawing has it: the scratch's own Standard is given the real
        /// one's font, size, width, slant and flags, any other is added.
        /// </summary>
        private static void CopyTextStyle(DxfDatabase real, DxfDatabase scratch, string name)
        {
            string wanted = string.IsNullOrEmpty(name) ? "Standard" : name;
            Guard.Argument(
                real.TextStyleTable.Has(wanted),
                nameof(name),
                "The drawing has no text style " + wanted + "."
            );
            DxfTextStyleTableRecord from = real.TextStyleTable[wanted];

            if (!scratch.TextStyleTable.Has(from.Name))
            {
                scratch.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = from.Name });
            }

            DxfTextStyleTableRecord to = scratch.TextStyleTable[from.Name];
            to.FileName = from.FileName;
            to.BigFontFileName = from.BigFontFileName;
            to.TextSize = from.TextSize;
            to.XScale = from.XScale;
            to.ObliquingAngle = from.ObliquingAngle;
            to.FlagBits = from.FlagBits;
            to.IsVertical = from.IsVertical;
            to.Font = from.Font;
        }

        /// <summary>Makes the dimension style of a name in the scratch with every variable it has in the real drawing, and the records the variables point at.</summary>
        private static void CopyDimensionStyle(DxfDatabase real, DxfDatabase scratch, string name)
        {
            Guard.Argument(
                real.DimStyleTable.Has(name),
                nameof(name),
                "The drawing has no dimension style " + name + "."
            );
            DxfDimStyleTableRecord from = real.DimStyleTable[name];

            if (!scratch.DimStyleTable.Has(from.Name))
            {
                scratch.DimStyleTable.Add(new DxfDimStyleTableRecord { Name = from.Name });
            }

            DxfDimStyleTableRecord to = scratch.DimStyleTable[from.Name];

            foreach (PropertyInfo variable in DimensionVariables)
            {
                object value;

                try
                {
                    value = variable.GetValue(from, null);
                }
                catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
                {
                    // A variable the drawing's version has not.
                    continue;
                }

                if (value is DxfTextStyleTableRecord textStyle)
                {
                    CopyTextStyle(real, scratch, textStyle.Name);
                    value = scratch.TextStyleTable[textStyle.Name];
                }
                else if (value is DxfBlockTableRecord block)
                {
                    value = CopyBlock(real, scratch, block);
                }
                else if (value is DxfLinetypeTableRecord linetype)
                {
                    if (!scratch.LinetypeTable.Has(linetype.Name))
                    {
                        scratch.File.CopyTableRecords(real.File, "LTYPE", new List<string> { linetype.Name });
                    }

                    value = scratch.LinetypeTable[linetype.Name];
                }

                try
                {
                    variable.SetValue(to, value, null);
                }
                catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
                {
                    // A variable the drawing's version has not.
                }
            }
        }

        private static DxfBlockTableRecord CopyBlock(DxfDatabase real, DxfDatabase scratch, DxfBlockTableRecord block)
        {
            if (scratch.BlockTable.Has(block.Name))
            {
                return scratch.BlockTable[block.Name];
            }

            var copy = new DxfBlockTableRecord { Name = block.Name, Origin = block.Origin };
            scratch.BlockTable.Add(copy);
            real.WblockCloneObjects(new List<DxfEntity>(block), copy);
            return copy;
        }

        /// <summary>
        /// Brings the layer of a name into the scratch as the real drawing has it, so that a layer that is off or frozen draws
        /// nothing there as it draws nothing here; layer 0 the scratch has.
        /// </summary>
        private static void CopyLayer(DxfDatabase real, DxfDatabase scratch, string name)
        {
            if (!string.IsNullOrEmpty(name) && real.LayerTable.Has(name) && !scratch.LayerTable.Has(name))
            {
                scratch.File.CopyTableRecords(real.File, "LAYER", new List<string> { name });
            }
        }
    }
}
