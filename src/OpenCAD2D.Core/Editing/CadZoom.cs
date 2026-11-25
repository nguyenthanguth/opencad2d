using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// What ZOOM, PAN and REGEN do to a view, in the pieces the commands and <see cref="CadEditor"/> both call: the extents of
    /// a space, the box of some objects, the move of a view or of the view of a viewport, and the REGEN itself. The commands
    /// keep their prompts and the editor its checks; the arithmetic is here once, so that a zoom made through the editor shows
    /// what the typed one shows.
    /// </summary>
    internal static class CadZoom
    {
        /// <summary>What a space draws and, in a layout, its sheet, as a double click of the middle button shows them: what ZOOM Extents fits.</summary>
        internal static DisplayBox Extents(CadDocument document, CadSpace shown) =>
            CadBlockAuthoring.WithGlyphs(document, shown, document.GetDisplay(shown).Bounds.Union(shown.PaperBounds));

        /// <summary>What model space draws: what ZOOM Extents fits through a viewport.</summary>
        internal static DisplayBox ModelExtents(CadDocument document) =>
            document.GetDisplay(document.ModelSpace).Bounds;

        /// <summary>The box of the objects as the space draws them, the box <see cref="DisplayBox.Empty"/> where none of them is drawn in it.</summary>
        internal static DisplayBox ObjectsBox(CadDocument document, CadSpace shown, IEnumerable<string> handles)
        {
            PickIndex index = document.GetPickIndex(shown);
            DisplayBox box = DisplayBox.Empty;

            foreach (string handle in handles)
            {
                foreach (int i in index.IndicesOf(handle))
                {
                    box = box.Union(index.List.Items[i].Bounds);
                }
            }

            return box;
        }

        /// <summary>
        /// Scales the view: nX scales it as it is about its centre; n alone scales the view of the extents, as AutoCAD's n scales
        /// that of the limits.
        /// </summary>
        internal static void Scaled(CadView view, DisplayBox extents, double factor, bool relative)
        {
            if (!relative)
            {
                view.ZoomTo(extents);
            }

            view.ZoomAt(view.Width / 2.0, view.Height / 2.0, factor);
        }

        /// <summary>The box two corners make, whichever corner comes first.</summary>
        internal static DisplayBox Window(GeoPoint2 first, GeoPoint2 second) =>
            new DisplayBox(
                Math.Min(first.X, second.X),
                Math.Min(first.Y, second.Y),
                Math.Max(first.X, second.X),
                Math.Max(first.Y, second.Y)
            );

        /// <summary>Whether a window has no width or no height: ZOOM Window's "*Invalid window*".</summary>
        internal static bool IsFlat(DisplayBox box) => box.Width <= 0.0 || box.Height <= 0.0;

        /// <summary>Takes the view a space showed last off its ZOOM Previous list and shows it, one undo step; false where the list is empty.</summary>
        internal static bool PreviousOfSpace(CadCommandSession session, CadSpace shown, CadView view, DisplayBox before)
        {
            if (!session.Views.TryTake(shown, out DisplayBox previous))
            {
                return false;
            }

            view.ZoomTo(previous, 0.0);
            session.Document.RecordSpaceView(shown, view, before, "ZOOM", false, previous);
            return true;
        }

        /// <summary>Pans a view so that the drawing moves by the displacement.</summary>
        internal static void Pan(CadView view, GeoVector2 move) =>
            view.Pan(move.X * view.PixelsPerUnit, -move.Y * view.PixelsPerUnit);

        /// <summary>A displacement of model space through a locked viewport, the paper moving by it at the viewport's scale.</summary>
        internal static GeoVector2 OnPaper(DxfViewport through, GeoVector2 move) =>
            move * (through.Height / through.ViewHeight);

        // ---- through a viewport ----

        /// <summary>ZOOM All and Extents through a viewport: its view fitted to model space's extents, as they are where there are some.</summary>
        internal static void FitViewport(
            DxfViewport viewport,
            DisplayBox extents,
            ref GeoPoint2 center,
            ref double height
        )
        {
            if (!extents.IsEmpty)
            {
                CadViewports.Fit(viewport, extents, out center, out height);
            }
        }

        /// <summary>
        /// ZOOM through a viewport by a scale factor: nXP sets the viewport's scale, n paper units to the model unit; nX scales
        /// its view as it is; n alone scales the view of the extents.
        /// </summary>
        internal static void ScaleViewport(
            DxfViewport viewport,
            DisplayBox extents,
            double factor,
            bool paper,
            bool relative,
            ref GeoPoint2 center,
            ref double height
        )
        {
            if (paper)
            {
                height = viewport.Height / factor;
            }
            else
            {
                if (!relative && !extents.IsEmpty)
                {
                    CadViewports.Fit(viewport, extents, out center, out height);
                }

                height /= factor;
            }
        }

        /// <summary>The view centre a viewport has once model space is panned by a shift, kept turned by its twist.</summary>
        internal static GeoPoint2 PannedCenter(DxfViewport viewport, GeoVector2 shift)
        {
            if (viewport.TwistAngle != 0.0)
            {
                double cos = Math.Cos(viewport.TwistAngle);
                double sin = Math.Sin(viewport.TwistAngle);
                shift = new GeoVector2(cos * shift.X - sin * shift.Y, sin * shift.X + cos * shift.Y);
            }

            return viewport.ViewCenter - shift;
        }

        /// <summary>
        /// Gives a viewport a view, one undo step named <paramref name="step"/>, and keeps the view it showed on its ZOOM
        /// Previous list.
        /// </summary>
        internal static CadEditResult SetViewport(
            CadCommandSession session,
            string step,
            string handle,
            GeoPoint2 center,
            double height,
            GeoPoint2 wasCenter,
            double wasHeight
        )
        {
            CadDocument document = session.Document;
            CadEditResult moved = session.Apply(
                step,
                document.ViewRecord(handle, center, height),
                CadDocument.ViewWriter(handle, center, height)
            );

            if (moved.Succeeded)
            {
                document.KeepViewportView(moved, handle, wasCenter, wasHeight);
            }

            return moved;
        }

        /// <summary>ZOOM Previous through a viewport: each viewport goes back through views of its own, as AutoCAD's; false where its list is empty.</summary>
        internal static bool PreviousOfViewport(CadCommandSession session, string step, string handle)
        {
            if (!session.Views.TryTake(handle, out GeoPoint2 center, out double height))
            {
                return false;
            }

            CadDocument document = session.Document;
            CadEditResult taken = session.Apply(
                step,
                document.ViewRecord(handle, center, height),
                CadDocument.ViewWriter(handle, center, height)
            );

            if (taken.Succeeded)
            {
                document.TookViewportView(taken, handle, center, height);
            }

            return true;
        }

        // ---- REGEN ----

        /// <summary>What REGEN does, without its line: the size of a point taken from the view, the fields of a regeneration evaluated again, the temporary graphics cleared, the display lists made again.</summary>
        internal static void Regen(CadCommandSession session)
        {
            // Points of a relative size take the size of the view regenerated in, as AutoCAD's.
            if (session.View != null)
            {
                session.Document.PointViewHeight = session.WorkView.VisibleBox.Height;
            }

            // The fields are evaluated again where FIELDEVAL has the regen bit.
            session.Document.UpdateFields(CadFieldContexts.Regen);
            session.ClearTemporaryGraphics();
            session.Document.Regen();
        }
    }
}
