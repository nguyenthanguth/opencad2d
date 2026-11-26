using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// What Place on Sheet places: a named view of model space of a resource drawing, or the whole of the drawing's model
    /// space, with the box of it in the drawing's own units. Made by <see cref="CadSheetSetPlacement.Plan"/>.
    /// </summary>
    public sealed class CadPlaceOnSheetPlan
    {
        internal CadPlaceOnSheetPlan(string drawingFile, CadModelView view, string title, DisplayBox box)
        {
            DrawingFile = drawingFile;
            View = view;
            Title = title;
            Box = box;
        }

        /// <summary>Gets the full path of the resource drawing.</summary>
        public string DrawingFile { get; }

        /// <summary>Gets the named view placed; null where the whole of model space is.</summary>
        public CadModelView View { get; }

        /// <summary>Gets the title of the sheet view the placing makes: the view's name, or the drawing's file name without its extension.</summary>
        public string Title { get; }

        /// <summary>Gets the part of the resource drawing's model space that is placed, in that drawing's units.</summary>
        public DisplayBox Box { get; }
    }

    /// <summary>What <see cref="CadSheetSetPlacement.Place"/> made on the sheet, which the caller records in the sheet set.</summary>
    public sealed class CadPlaceOnSheetResult
    {
        internal CadPlaceOnSheetResult(
            string xrefName,
            string viewportHandle,
            string namedViewName,
            string namedViewHandle,
            string namedViewOwnerHandle,
            string title,
            string scale
        )
        {
            XrefName = xrefName;
            ViewportHandle = viewportHandle;
            NamedViewName = namedViewName;
            NamedViewHandle = namedViewHandle;
            NamedViewOwnerHandle = namedViewOwnerHandle;
            Title = title;
            Scale = scale;
        }

        /// <summary>Gets the name of the external reference the resource drawing is attached under in the sheet's drawing.</summary>
        public string XrefName { get; }

        /// <summary>Gets the handle of the viewport made on the layout.</summary>
        public string ViewportHandle { get; }

        /// <summary>Gets the name of the named view saved in the layout, which the sheet view points at (the view's name, a number added where the drawing has one of that name).</summary>
        public string NamedViewName { get; }

        /// <summary>Gets the handle of the record of that named view.</summary>
        public string NamedViewHandle { get; }

        /// <summary>Gets the handle of the drawing's view table.</summary>
        public string NamedViewOwnerHandle { get; }

        /// <summary>Gets the title for the sheet view: the name of the view placed.</summary>
        public string Title { get; }

        /// <summary>Gets the scale the viewport was made at, as its name: 1:100.</summary>
        public string Scale { get; }
    }

    /// <summary>
    /// What the Model Views tab does for Place on Sheet, as AutoCAD 2021's Sheet Set Manager does it: the resource drawing is
    /// attached as an attached external reference in model space of the sheet's drawing (once, whatever the number of its views
    /// placed), a viewport is made in the sheet's layout at a scale of the drawing's list, showing the view, and a named view is
    /// saved in the layout around the viewport, which the sheet view of the set points at (AutoCAD's sheet views name a view of
    /// the sheet's own drawing). The whole is one undo step.
    /// </summary>
    public static class CadSheetSetPlacement
    {
        /// <summary>The scale Place on Sheet offers when the view has not been placed before.</summary>
        public const string DefaultScale = "1:100";

        /// <summary>
        /// Works out what to place: the named view of a resource drawing, or where no view is named the box of everything in the
        /// drawing's model space.
        /// </summary>
        /// <param name="drawingFile">The resource drawing.</param>
        /// <param name="viewName">The name of the view; null for the whole of model space.</param>
        /// <returns>What is to be placed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        /// <exception cref="IOException">Thrown when the drawing cannot be read.</exception>
        /// <exception cref="FormatException">Thrown when the drawing is a file that cannot be read.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the drawing has no such view, or nothing in model space to place.</exception>
        public static CadPlaceOnSheetPlan Plan(string drawingFile, string viewName)
        {
            if (drawingFile == null)
            {
                throw new ArgumentNullException(nameof(drawingFile));
            }

            string full = Path.GetFullPath(drawingFile);

            if (viewName != null)
            {
                CadModelView view = CadSheetSetResources
                    .ModelViewsOf(full)
                    .FirstOrDefault(v => string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase));

                if (view == null)
                {
                    throw new InvalidOperationException(
                        "The drawing " + Path.GetFileName(full) + " has no view named \"" + viewName + "\"."
                    );
                }

                GeoPoint2 c = view.Center;
                var around = new DisplayBox(
                    c.X - view.Width / 2.0,
                    c.Y - view.Height / 2.0,
                    c.X + view.Width / 2.0,
                    c.Y + view.Height / 2.0
                );
                return new CadPlaceOnSheetPlan(full, view, view.Name, around);
            }

            CadDocument document = CadDocument.Open(full);
            DisplayBox bounds = document.GetDisplay(document.ModelSpace).Bounds;

            if (bounds.IsEmpty || !(bounds.Width > 0.0) || !(bounds.Height > 0.0))
            {
                throw new InvalidOperationException(
                    "The drawing " + Path.GetFileName(full) + " has nothing in model space to place."
                );
            }

            return new CadPlaceOnSheetPlan(full, null, Path.GetFileNameWithoutExtension(full), bounds);
        }

        /// <summary>The scales of a drawing's list (SCALELISTEDIT), in the order of the list: the names Place on Sheet offers, as 1:100; the metric defaults for a drawing that keeps no list.</summary>
        /// <param name="document">The sheet's drawing.</param>
        /// <returns>The names.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document is null.</exception>
        public static IReadOnlyList<string> ScaleNames(CadDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            List<CadScaleListRow> rows = CadCommands.ScaleRows(document.Database);

            // A drawing that keeps no list of scales (one made in code) is offered the metric ones, as the sheets of a set are drawn in millimetres.
            return (rows.Count > 0 ? rows : CadCommands.MetricScales()).Select(r => r.Name).ToList();
        }

        /// <summary>
        /// Places a view on a layout of the sheet's drawing, as one undo step: the resource drawing attached as an external
        /// reference in model space at the origin (reused where this drawing has it already), a viewport made on the layout with
        /// its middle at a point, showing the view at the scale, and a named view saved in the layout around the viewport.
        /// </summary>
        /// <param name="document">The sheet's drawing.</param>
        /// <param name="layout">The layout of the sheet; not Model.</param>
        /// <param name="plan">What is placed.</param>
        /// <param name="at">The middle of the viewport, on the layout's paper.</param>
        /// <param name="scale">The scale, as 1:100 or 2:1.</param>
        /// <returns>What was made.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document, the layout, the plan or the scale is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the scale is not a ratio above nought.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the layout is Model, the resource drawing cannot be attached, or the edit is refused.</exception>
        public static CadPlaceOnSheetResult Place(
            CadDocument document,
            CadSpace layout,
            CadPlaceOnSheetPlan plan,
            GeoPoint2 at,
            string scale
        )
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (scale == null)
            {
                throw new ArgumentNullException(nameof(scale));
            }

            Prepared prepared = Prepare(document, layout, plan, scale);
            CadPlaceOnSheetResult result = null;
            CadEditResult edit = document.ApplyEdit("PLACEONSHEET", prepared.Planned, s =>
                result = Write(s, layout, plan, prepared, at)
            );

            if (!edit.Succeeded)
            {
                throw new InvalidOperationException(edit.Message);
            }

            return result;
        }

        /// <summary>
        /// Reads the scale a view was placed at on a sheet: the scale of the viewport that the named view saved in the layout
        /// surrounds.
        /// </summary>
        /// <param name="database">The sheet's drawing.</param>
        /// <param name="namedViewName">The name of the named view the sheet view points at.</param>
        /// <returns>The scale as 1:100; empty where the drawing has no such view or no viewport inside it.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the drawing or the name is null.</exception>
        public static string ScaleOf(DxfDatabase database, string namedViewName)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            if (namedViewName == null)
            {
                throw new ArgumentNullException(nameof(namedViewName));
            }

            if (!database.ViewTable.Has(namedViewName))
            {
                return string.Empty;
            }

            DxfViewTableRecord view = database.ViewTable[namedViewName];
            DxfLayout layout = view.Layout;

            if (layout?.BlockTableRecord == null)
            {
                return string.Empty;
            }

            // The viewport the view surrounds: the same middle, and no larger than the view (AutoCAD's own sheet views are a little
            // larger than their viewport, ours are as large).
            var tolerance = new Tolerance(1E-6 * Math.Max(1.0, Math.Max(view.Width, view.Height)), 1E-9);
            DxfViewport found = layout
                .BlockTableRecord.OfType<DxfViewport>()
                .Where(v => v.Number > 1)
                .OrderBy(v => v.Handle, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(v =>
                    Near(v.CenterPoint.X, view.CenterPoint.X, tolerance)
                    && Near(v.CenterPoint.Y, view.CenterPoint.Y, tolerance)
                    && v.Width <= view.Width + tolerance.EqualPoint
                    && v.Height <= view.Height + tolerance.EqualPoint
                );
            return found == null ? string.Empty : CadViewports.ScaleName(found.CustomScale);
        }

        private static bool Near(double a, double b, Tolerance tolerance) => Math.Abs(a - b) <= tolerance.EqualPoint;

        /// <summary>
        /// The command Place on Sheet runs on the command line: "Specify insertion point or [Scale]:" with the outline of the
        /// view at the scale following the cursor, the Scale option listing the drawing's scales, the click placing the view
        /// as <see cref="Place"/> does. Run it with <see cref="CadCommandSession.Start(CadCommand)"/>.
        /// </summary>
        /// <param name="plan">What is placed.</param>
        /// <param name="scale">The scale it starts at, as 1:100.</param>
        /// <param name="placed">Told what was made once the view is placed, with the scale it was placed at; the caller records it in the set.</param>
        /// <returns>The command.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the plan, the scale or the callback is null.</exception>
        public static CadCommand Command(CadPlaceOnSheetPlan plan, string scale, Action<CadPlaceOnSheetResult> placed)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (scale == null)
            {
                throw new ArgumentNullException(nameof(scale));
            }

            if (placed == null)
            {
                throw new ArgumentNullException(nameof(placed));
            }

            return new CadCommand("PLACEONSHEET", c => Body(c, plan, scale, placed));
        }

        private static IEnumerable<CadPrompt> Body(
            CadCommandContext c,
            CadPlaceOnSheetPlan plan,
            string scale,
            Action<CadPlaceOnSheetResult> placed
        )
        {
            if (c.Space.IsModel)
            {
                c.Write("** Command not allowed in Model Tab **");
                yield break;
            }

            CadSpace layout = c.Space;
            string[] scales = ScaleNames(c.Document).ToArray();
            double factor = UnitFactor(c.Document, plan);

            while (true)
            {
                string now = scale;
                yield return CadPrompt
                    .ForPoint("Specify insertion point", null, new[] { "Scale" })
                    .Previewing(CadPreview.MakingOne(c, input => Outline(plan, now, factor, input.Point)));

                if (c.Input.IsPoint)
                {
                    GeoPoint2 at = c.Input.Point;
                    CadPlaceOnSheetResult result = null;

                    try
                    {
                        Prepared prepared = Prepare(c.Document, layout, plan, scale);
                        CadEditResult edit = c.Apply(prepared.Planned, s =>
                            result = Write(s, layout, plan, prepared, at)
                        );

                        if (!edit.Succeeded)
                        {
                            c.Write(edit.Message);
                        }
                    }
                    catch (Exception e) when (e is InvalidOperationException || e is ArgumentException)
                    {
                        c.Write(e.Message);
                    }

                    if (result != null)
                    {
                        c.Write("View \"" + plan.Title + "\" placed at " + result.Scale + ".");
                        placed(result);
                    }

                    yield break;
                }

                if (!c.Input.Is("Scale"))
                {
                    yield break;
                }

                yield return CadPrompt.ForKeywordOrText("Enter scale", scales, scale);
                string typed =
                    c.Input.Kind == CadInputKind.Keyword || c.Input.Kind == CadInputKind.Text
                        ? c.Input.Text.Trim()
                        : string.Empty;

                if (typed.Length == 0)
                {
                    continue;
                }

                if (CadViewports.TryReadScale(typed, out double ratio))
                {
                    scale = CadViewports.ScaleName(ratio);
                }
                else
                {
                    c.Write("\"" + typed + "\" is not a scale: write it as 1:100.");
                }
            }
        }

        /// <summary>The outline of a view at a scale, centred on a point of the paper, as the rectangle that follows the cursor.</summary>
        private static DxfEntity Outline(CadPlaceOnSheetPlan plan, string scale, double factor, GeoPoint2 at)
        {
            if (!CadViewports.TryReadScale(scale, out double ratio))
            {
                return null;
            }

            double width = plan.Box.Width * factor * ratio;
            double height = plan.Box.Height * factor * ratio;
            var outline = new DxfPolyline();
            outline.AddVertexAt(0, new GeoPoint2(at.X - width / 2.0, at.Y - height / 2.0), 0.0, 0.0, 0.0);
            outline.AddVertexAt(1, new GeoPoint2(at.X + width / 2.0, at.Y - height / 2.0), 0.0, 0.0, 0.0);
            outline.AddVertexAt(2, new GeoPoint2(at.X + width / 2.0, at.Y + height / 2.0), 0.0, 0.0, 0.0);
            outline.AddVertexAt(3, new GeoPoint2(at.X - width / 2.0, at.Y + height / 2.0), 0.0, 0.0, 0.0);
            outline.Closed = true;
            return outline;
        }

        /// <summary>What from the resource drawing and the sheet's drawing is worked out before the edit is made.</summary>
        private sealed class Prepared
        {
            internal CadAttachRequest Request { get; set; }

            internal string XrefName { get; set; }

            internal bool Attached { get; set; }

            internal double Scale { get; set; }

            internal string ScaleName { get; set; }

            internal double Factor { get; set; }

            internal IReadOnlyList<CadEditRecord> Planned { get; set; }

            internal DxfDatabase Referenced { get; set; }
        }

        /// <summary>The factor from the resource drawing's unit to the sheet drawing's, 1 where the drawings agree or one has no unit.</summary>
        private static double UnitFactor(CadDocument document, CadPlaceOnSheetPlan plan) =>
            new CadAttachRequest(document, CadAttachKind.Xref) { FileName = plan.DrawingFile }.UnitFactor;

        private static Prepared Prepare(CadDocument document, CadSpace layout, CadPlaceOnSheetPlan plan, string scale)
        {
            if (layout.IsModel)
            {
                throw new InvalidOperationException("** Command not allowed in Model Tab **");
            }

            if (!CadViewports.TryReadScale(scale, out double ratio))
            {
                throw new ArgumentException("\"" + scale + "\" is not a scale: write it as 1:100.", nameof(scale));
            }

            var request = new CadAttachRequest(document, CadAttachKind.Xref) { FileName = plan.DrawingFile };

            if (request.Error != null)
            {
                throw new InvalidOperationException(request.Error);
            }

            DxfDatabase database = document.Database;
            string name = request.Name;
            bool attached = false;

            // The drawing is attached once: its xref is reused where the sheet's drawing has it, else named by the file, a number
            // added where another block or xref has the name.
            for (int n = 1; ; n++)
            {
                string candidate = n == 1 ? request.Name : request.Name + n.ToString(CultureInfo.InvariantCulture);

                if (!CadCommands.IsBlockName(candidate) || candidate.IndexOf('|') >= 0)
                {
                    throw new InvalidOperationException("Invalid xref name \"" + candidate + "\".");
                }

                if (!database.BlockTable.Has(candidate))
                {
                    name = candidate;
                    attached = true;
                    break;
                }

                DxfBlockTableRecord existing = database.BlockTable[candidate];

                if (
                    existing.IsFromExternalReference
                    && string.Equals(
                        document.Xrefs.Locate(existing.PathName),
                        request.FileName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    name = existing.Name;
                    break;
                }
            }

            var prepared = new Prepared
            {
                Request = request,
                XrefName = name,
                Attached = attached,
                Scale = ratio,
                ScaleName = CadViewports.ScaleName(ratio),
                Factor = request.UnitFactor,
                Referenced = request.Drawing,
            };
            var planned = new List<CadEditRecord>();

            if (attached)
            {
                planned.Add(new CadEditRecord(CadEditRecordKind.TableChanged, null, "Attach xref", null, name));
            }

            planned.Add(new CadEditRecord(CadEditRecordKind.TableChanged, null, "View", null, plan.Title));
            prepared.Planned = planned;
            return prepared;
        }

        private static CadPlaceOnSheetResult Write(
            CadEditScope s,
            CadSpace layout,
            CadPlaceOnSheetPlan plan,
            Prepared prepared,
            GeoPoint2 at
        )
        {
            DxfDatabase database = s.Database;
            CadSpace model = s.Document.ModelSpace;
            DxfBlockTableRecord block;

            if (prepared.Attached)
            {
                block = database.AttachXref(prepared.Request.SavedPath, prepared.XrefName);

                if (prepared.Referenced != null)
                {
                    CadXrefLayers.Bring(database, prepared.XrefName, prepared.Referenced, true);
                }
            }
            else
            {
                block = database.BlockTable[prepared.XrefName];
            }

            if (!model.Block.OfType<DxfBlockReference>().Any(r => ReferenceEquals(r.BlockTableRecord, block)))
            {
                var insert = new DxfBlockReference(new GeoPoint3(0.0, 0.0, 0.0), block);
                insert.SetDatabaseDefaults(database);
                s.Append(model, insert);
                insert.ScaleFactors = new GeoVector3(prepared.Factor, prepared.Factor, prepared.Factor);
            }

            // The viewport is as large as the view is at the scale, and shows the view's own middle of model space.
            double width = plan.Box.Width * prepared.Factor * prepared.Scale;
            double height = plan.Box.Height * prepared.Factor * prepared.Scale;
            GeoPoint2 view = new GeoPoint2(
                (plan.Box.MinX + plan.Box.MaxX) / 2.0 * prepared.Factor,
                (plan.Box.MinY + plan.Box.MaxY) / 2.0 * prepared.Factor
            );
            int before = layout.Layout.GetViewports().Count;
            DxfViewport sheet = layout.Layout.Initialize();

            if (layout.Layout.GetViewports().Count > before)
            {
                s.Added(layout, sheet);
            }

            var viewport = new DxfViewport();
            viewport.SetDatabaseDefaults(database);
            s.Append(layout, viewport);
            viewport.CenterPoint = new GeoPoint3(at.X, at.Y, 0.0);
            viewport.Width = width;
            viewport.Height = height;
            viewport.ViewCenter = view;
            viewport.ViewHeight = plan.Box.Height * prepared.Factor;
            viewport.On = true;

            // The named view of the layout the sheet view points at: the frame of the viewport, a number added to the name where the
            // drawing has the name.
            string title = plan.Title;
            string named = title;

            for (int n = 2; database.ViewTable.Has(named); n++)
            {
                named = title + n.ToString(CultureInfo.InvariantCulture);
            }

            var record = new DxfViewTableRecord
            {
                Name = named,
                IsPaperspaceView = true,
                CenterPoint = at,
                Width = width,
                Height = height,
            };
            database.ViewTable.Add(record);
            record.Layout = layout.Layout;
            return new CadPlaceOnSheetResult(
                prepared.XrefName,
                viewport.Handle,
                named,
                record.Handle,
                database.ViewTable.Handle,
                title,
                prepared.ScaleName
            );
        }
    }
}
