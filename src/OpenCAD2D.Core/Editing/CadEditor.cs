using System;
using System.Collections.Generic;
using System.Globalization;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Lisp;

namespace OpenCAD2D
{
    /// <summary>
    /// What a plugin, a program driving the app through Remote and a Python script do to a drawing's session with a call
    /// instead of a typed command, as AutoCAD's <c>Editor</c> does: move the view (zoom, pan, regenerate), keep named views,
    /// select by an area, and read and set system variables. Every call does what the command of its name does, through the
    /// code the command runs: <see cref="ZoomWindow"/> fits the window as ZOOM Window does and is the same step of undo, so
    /// that U after it puts the view back, and a drawing's view moved by a call is the view a typed ZOOM moves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Units are those of the drawing, in millimetres where the drawing is. Points are of the world, not of the UCS, as
    /// AutoCAD's .NET API takes them. A session with no window (a test, a plugin's own test, the Remote test server) has a
    /// view of its own, 942 by 534 pixels showing 10 units of height about the origin as AutoCAD 2021's console starts a
    /// drawing, which the view calls work on; <see cref="HasWindow"/> says false there.
    /// </para>
    /// <para>
    /// A call made while a command runs in the session (a plugin's command, the run of a Python tool) joins the command's
    /// undo step; one made at rest is a step of its own, named as the command is: ZOOM, PAN, VIEW or SETVAR.
    /// </para>
    /// <para>
    /// Through a viewport (MSPACE on a layout) zoom Extents, Window, Scaled and Previous and Pan change the viewport's view,
    /// as ZOOM and -PAN do there; zoom Center and Objects are refused, as ZOOM does not offer them there. Where the viewport
    /// is locked the paper is zoomed and panned.
    /// </para>
    /// </remarks>
    public sealed partial class CadEditor
    {
        /// <summary>The command whose name the steps of the named views take, as AutoCAD's U says VIEW for -VIEW.</summary>
        private static readonly CadCommand ViewStep = new CadCommand("VIEW", c => Array.Empty<CadPrompt>());

        private readonly CadCommandSession _session;

        internal CadEditor(CadCommandSession session)
        {
            _session = session;
        }

        /// <summary>Gets the drawing the editor works on.</summary>
        public CadDocument Document => _session.Document;

        /// <summary>
        /// Gets whether the session has a window's view (<see cref="CadCommandSession.View"/>): false in a test and in the
        /// Remote test server, where the view calls work on the view the session keeps of its own.
        /// </summary>
        public bool HasWindow => _session.View != null;

        // ---- The view ----

        /// <summary>
        /// Zooms to the extents, as ZOOM Extents: what the space shown draws and, on a layout, its sheet, fitted with a margin
        /// of 5 %. The reference drawing of a line from (0, 0) to (100, 0) and a circle about (50, 50) of radius 25, in a view
        /// 942 by 534 pixels, shows (-23.502, -4.167) to (123.502, 79.167). One undo step named ZOOM.
        /// </summary>
        public void ZoomExtents()
        {
            DxfViewport through = Document.ActiveViewport;

            if (through != null && !through.Locked)
            {
                GeoPoint2 center = through.ViewCenter;
                double height = through.ViewHeight;
                CadZoom.FitViewport(through, CadZoom.ModelExtents(Document), ref center, ref height);
                MoveViewport(through, "ZOOM", center, height);
                return;
            }

            MoveSpace("ZOOM", (view, shown) =>
            {
                view.ZoomTo(CadZoom.Extents(Document, shown));
                return true;
            });
        }

        /// <summary>
        /// Zooms to a window, as ZOOM Window: the window fitted with no margin, the view as wide or as high as the canvas
        /// shape makes it. In the reference view the window (0, 0) to (10, 10) shows (-3.820, 0) to (13.820, 10). Through a
        /// locked viewport the corners are points of model space, shown on the paper where it zooms. One undo step named ZOOM.
        /// </summary>
        /// <param name="first">One corner, in the world.</param>
        /// <param name="second">The opposite corner.</param>
        /// <exception cref="ArgumentException">Thrown when a corner is not a finite point, or the window has no width or no height.</exception>
        public void ZoomWindow(GeoPoint2 first, GeoPoint2 second)
        {
            Guard.Argument(IsFinite(first), nameof(first), "A corner of the window is not a finite point.");
            Guard.Argument(IsFinite(second), nameof(second), "A corner of the window is not a finite point.");
            DxfViewport through = Document.ActiveViewport;

            if (through != null && !through.Locked)
            {
                DisplayBox box = CadZoom.Window(first, second);
                Guard.Argument(!CadZoom.IsFlat(box), nameof(second), WindowHasNoSize);
                CadViewports.Fit(through, box, out GeoPoint2 center, out double height);
                MoveViewport(through, "ZOOM", center, height);
                return;
            }

            if (through != null)
            {
                first = CadViewports.ToPaper(through, first);
                second = CadViewports.ToPaper(through, second);
            }

            DisplayBox window = CadZoom.Window(first, second);
            Guard.Argument(!CadZoom.IsFlat(window), nameof(second), WindowHasNoSize);
            MoveSpace("ZOOM", (view, shown) =>
            {
                view.ZoomTo(window, 0.0);
                return true;
            });
        }

        private const string WindowHasNoSize = "The window has no width or no height.";

        /// <summary>
        /// Zooms to objects, as ZOOM Object: the box of the objects as the space shown draws them, fitted with a margin of 5 %.
        /// Objects that are not drawn in the space shown (of another layout, erased, not in the drawing) leave the view as it
        /// was, and so does a list with none. This is not the Find dialog's zoom, which switches space. One undo step named
        /// ZOOM, none where the view stays.
        /// </summary>
        /// <param name="handles">The handles of the objects.</param>
        /// <exception cref="ArgumentNullException">Thrown when the handles are null.</exception>
        /// <exception cref="InvalidOperationException">Thrown through a viewport, where ZOOM offers no Object.</exception>
        public void ZoomObjects(IEnumerable<string> handles)
        {
            if (handles == null)
            {
                throw new ArgumentNullException(nameof(handles));
            }

            RefuseThroughViewport("Object");
            MoveSpace("ZOOM", (view, shown) =>
            {
                DisplayBox box = CadZoom.ObjectsBox(Document, shown, handles);

                if (box.IsEmpty)
                {
                    return false;
                }

                view.ZoomTo(box);
                return true;
            });
        }

        /// <summary>
        /// Zooms to a height about a centre, as ZOOM Center: the view shows the height given and as much width as the canvas
        /// has. In the reference view the centre (50, 50) and the height 20 show (32.360, 40) to (67.640, 60). One undo step
        /// named ZOOM.
        /// </summary>
        /// <param name="center">The centre, in the world.</param>
        /// <param name="height">The height the view shows, above nought.</param>
        /// <exception cref="ArgumentException">Thrown when the centre is not a finite point.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the height is not a finite number above nought.</exception>
        /// <exception cref="InvalidOperationException">Thrown through a viewport, where ZOOM offers no Center.</exception>
        public void ZoomCenter(GeoPoint2 center, double height)
        {
            Guard.Argument(IsFinite(center), nameof(center), "The centre is not a finite point.");
            Guard.Positive(height, nameof(height), "The height of a view is a finite number above nought.");
            RefuseThroughViewport("Center");
            MoveSpace("ZOOM", (view, shown) =>
            {
                view.ZoomToHeight(center.X, center.Y, height);
                return true;
            });
        }

        /// <summary>
        /// Scales the view as it is about its centre, as ZOOM nX: 2 shows half the width and half the height, 0.5 twice as
        /// much. One undo step named ZOOM.
        /// </summary>
        /// <param name="factor">The factor, above nought.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the factor is not a finite number above nought.</exception>
        public void ZoomScaled(double factor)
        {
            Guard.Positive(factor, nameof(factor), "A scale factor is a finite number above nought.");
            DxfViewport through = Document.ActiveViewport;

            if (through != null && !through.Locked)
            {
                GeoPoint2 center = through.ViewCenter;
                double height = through.ViewHeight;
                CadZoom.ScaleViewport(through, DisplayBox.Empty, factor, false, true, ref center, ref height);
                MoveViewport(through, "ZOOM", center, height);
                return;
            }

            MoveSpace("ZOOM", (view, shown) =>
            {
                CadZoom.Scaled(view, DisplayBox.Empty, factor, true);
                return true;
            });
        }

        /// <summary>
        /// Goes back to the view before the last zoom or pan of the space shown (of the viewport worked through), as ZOOM
        /// Previous; 20 are kept. One undo step named ZOOM.
        /// </summary>
        /// <returns>False where none is saved ("No previous view saved."): the view stays and nothing is written.</returns>
        public bool ZoomPrevious()
        {
            DxfViewport through = Document.ActiveViewport;

            if (through != null && !through.Locked)
            {
                bool took = InStep("ZOOM", () => CadZoom.PreviousOfViewport(_session, "ZOOM", through.Handle));

                if (took)
                {
                    _session.RaiseViewChanged();
                }

                return took;
            }

            CadView view = _session.CommandView;
            DisplayBox before = view.VisibleBox;
            JoinRun();

            if (!CadZoom.PreviousOfSpace(_session, Document.CurrentSpace, view, before))
            {
                return false;
            }

            _session.RaiseViewChanged();
            return true;
        }

        /// <summary>
        /// Pans the view so that the drawing moves by the displacement, as -PAN does: (10, 0) moves what is drawn 10 to the right,
        /// so that the view shows 10 less at the left. One undo step named PAN.
        /// </summary>
        /// <param name="displacement">The displacement, in drawing units, in the world.</param>
        /// <exception cref="ArgumentException">Thrown when the displacement is not finite.</exception>
        public void Pan(GeoVector2 displacement)
        {
            Guard.Argument(
                Guard.IsFinite(displacement.X) && Guard.IsFinite(displacement.Y),
                nameof(displacement),
                "The displacement is not finite."
            );
            DxfViewport through = Document.ActiveViewport;

            if (through != null && !through.Locked)
            {
                MoveViewport(through, "PAN", CadZoom.PannedCenter(through, displacement), through.ViewHeight);
                return;
            }

            GeoVector2 move = through != null ? CadZoom.OnPaper(through, displacement) : displacement;
            MoveSpace("PAN", (view, shown) =>
            {
                CadZoom.Pan(view, move);
                return true;
            });
        }

        /// <summary>
        /// Regenerates the drawing, as REGEN does without writing its line: the fields of a regeneration are evaluated again, the
        /// temporary graphics cleared, the display lists made again, and the size of a point of relative size taken from the
        /// view. It is no step of undo.
        /// </summary>
        public void Regen() => CadZoom.Regen(_session);

        /// <summary>Gets the part of the space shown that the canvas shows: the whole canvas, so wider or taller than a box set where the shape differs.</summary>
        /// <returns>The box, in drawing units.</returns>
        public DisplayBox GetCurrentView() => _session.CommandView.VisibleBox;

        /// <summary>
        /// Sets the view to show a box, fitted with no margin as ZOOM Window fits one: the canvas shows the whole box and, along
        /// one axis, more. One undo step named ZOOM.
        /// </summary>
        /// <param name="box">The box, in the world.</param>
        /// <exception cref="ArgumentException">Thrown when the box is empty, not finite, or has no width or no height.</exception>
        public void SetCurrentView(DisplayBox box)
        {
            Guard.Argument(
                !box.IsEmpty
                    && Guard.IsFinite(box.MinX)
                    && Guard.IsFinite(box.MinY)
                    && Guard.IsFinite(box.MaxX)
                    && Guard.IsFinite(box.MaxY),
                nameof(box),
                "The view is not a finite box."
            );
            ZoomWindow(new GeoPoint2(box.MinX, box.MinY), new GeoPoint2(box.MaxX, box.MaxY));
        }

        // ---- Named views ----

        /// <summary>
        /// Gets the names of the named views of the drawing, as -VIEW ? lists them: the views of model space and of the layouts
        /// together, in ordinal order, letters of either case alike.
        /// </summary>
        public IReadOnlyList<string> NamedViews => CadNamedViews.Matching(ViewContext(), "*");

        /// <summary>
        /// Saves the view on screen under a name, as -VIEW Save does, with no layer snapshot; a name that is a view's already
        /// redefines it. The view is saved in the space shown, a layout's with the layout. One undo step named VIEW.
        /// </summary>
        /// <param name="name">The name: not empty, 255 letters at most, no space at either end and none of &lt;&gt;/\":;?*|=`.</param>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the name is not one a view can have.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the view on screen has no size to save.</exception>
        public void SaveView(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Guard.Argument(CadNamedViews.IsName(name), nameof(name), "Invalid view name.");
            DisplayBox box = _session.CommandView.VisibleBox;

            if (box.IsEmpty || CadZoom.IsFlat(box))
            {
                throw new InvalidOperationException("There is no view to save.");
            }

            CadCommandContext c = ViewContext();
            InStep("VIEW", () =>
            {
                CadNamedViews.Save(c, name, box, false);
                return true;
            });
        }

        /// <summary>
        /// Shows a named view, as -VIEW Restore does: the space it was saved in made the one shown where another is, the canvas
        /// zoomed to it, and its layer snapshot restored where it has one. Restoring on screen is no step of undo of its own.
        /// </summary>
        /// <param name="name">The name, letters of either case alike.</param>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the drawing has no view of that name.</exception>
        public void RestoreView(string name)
        {
            CadCommandContext c = ViewContext();
            DxfViewTableRecord view = Named(c, name);
            InStep("VIEW", () =>
            {
                CadNamedViews.Restore(c, view.Name);
                return true;
            });
            _session.RaiseViewChanged();
        }

        /// <summary>Erases a named view with its layer snapshot, as -VIEW Delete does. One undo step named VIEW.</summary>
        /// <param name="name">The name, letters of either case alike.</param>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the drawing has no view of that name.</exception>
        public void DeleteView(string name)
        {
            CadCommandContext c = ViewContext();
            DxfViewTableRecord view = Named(c, name);
            InStep("VIEW", () =>
            {
                CadNamedViews.Delete(c, new[] { view.Name });
                return true;
            });
        }

        private CadCommandContext ViewContext() => new CadCommandContext(_session, ViewStep, Array.Empty<string>());

        private static DxfViewTableRecord Named(CadCommandContext c, string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            return CadNamedViews.Find(c, name)
                ?? throw new ArgumentException("The drawing has no view named " + name + ".");
        }

        // ---- Selection ----

        /// <summary>
        /// Gives the objects of the working space (model space while a viewport is worked through, else the space shown), as
        /// the All option of "Select objects:" takes them: the first viewport of a layout left out, objects on locked layers
        /// in. The pickfirst selection of the drawing is not changed: set it with <see cref="CadDocument.Selection"/>.
        /// </summary>
        /// <param name="filter">Keeps the objects it says true of; null keeps all.</param>
        /// <returns>The handles, in the order the space lists its objects.</returns>
        public IReadOnlyList<string> SelectAll(Func<DxfEntity, bool> filter = null)
        {
            var found = new List<string>();

            foreach (string handle in _session.WorkingSpaceHandles())
            {
                DxfEntity entity = Document.FindEntity(handle);

                if (entity != null && (filter == null || filter(entity)))
                {
                    found.Add(handle);
                }
            }

            return found;
        }

        /// <summary>
        /// Gives the objects that lie wholly inside a window, as the Window option of "Select objects:" takes them. Where the
        /// session has a window, only objects the view shows are taken, as AutoCAD's does; with none, every object. Objects on
        /// locked layers are in. The pickfirst selection is not changed.
        /// </summary>
        /// <param name="first">One corner, in the world.</param>
        /// <param name="second">The opposite corner.</param>
        /// <returns>The handles, in the order the space lists its objects.</returns>
        /// <exception cref="ArgumentException">Thrown when a corner is not a finite point.</exception>
        public IReadOnlyList<string> SelectWindow(GeoPoint2 first, GeoPoint2 second) => Area(first, second, false);

        /// <summary>
        /// Gives the objects a window lies across or contains, as the Crossing option of "Select objects:" takes them, held to
        /// the view as <see cref="SelectWindow"/> is. The pickfirst selection is not changed.
        /// </summary>
        /// <param name="first">One corner, in the world.</param>
        /// <param name="second">The opposite corner.</param>
        /// <returns>The handles, in the order the space lists its objects.</returns>
        /// <exception cref="ArgumentException">Thrown when a corner is not a finite point.</exception>
        public IReadOnlyList<string> SelectCrossingWindow(GeoPoint2 first, GeoPoint2 second) =>
            Area(first, second, true);

        private IReadOnlyList<string> Area(GeoPoint2 first, GeoPoint2 second, bool crossing)
        {
            Guard.Argument(IsFinite(first), nameof(first), "A corner of the window is not a finite point.");
            Guard.Argument(IsFinite(second), nameof(second), "A corner of the window is not a finite point.");
            DisplayBox box = CadZoom.Window(first, second);
            PickIndex index = Document.GetPickIndex(Document.WorkingSpace);
            List<string> taken = _session.TakenByWindow(crossing ? index.Crossing(box) : index.Window(box), true);
            var wanted = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
            var found = new List<string>(taken.Count);

            foreach (string handle in _session.WorkingSpaceHandles())
            {
                if (wanted.Contains(handle))
                {
                    found.Add(handle);
                }
            }

            return found;
        }

        // ---- System variables ----

        /// <summary>
        /// Gets the value of a system variable as AutoLISP's <c>getvar</c> gives it: an <see cref="int"/> for a whole number or a
        /// switch, a <see cref="double"/> for a real (an angle in radians), a <see cref="string"/> for a text, and a
        /// <c>double[]</c> of three coordinates for a point, of two for a variable AutoCAD keeps as a 2D point (SNAPBASE).
        /// A variable is the drawing's own: another drawing has its own value.
        /// </summary>
        /// <param name="name">The name, letters of either case alike.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the presenter has no variable of that name.</exception>
        public object GetVariable(string name) => Variable(name).ValueForLisp(_session);

        /// <summary>
        /// Sets a system variable as AutoLISP's <c>setvar</c> does, through the variable's own rules: a whole number may be given
        /// as a real that is whole, a real as a whole number, an angle in radians. A variable the drawing keeps is one undo step
        /// named SETVAR (none where the value is the one it has); one the app keeps is set at once.
        /// </summary>
        /// <param name="name">The name, letters of either case alike.</param>
        /// <param name="value">An <see cref="int"/>, a <see cref="double"/>, a <see cref="string"/>, or a <c>double[]</c> of two or three coordinates.</param>
        /// <exception cref="ArgumentNullException">Thrown when the name or the value is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the presenter has no variable of that name, it is read only, or it refuses the value.</exception>
        public void SetVariable(string name, object value)
        {
            CadSystemVariable variable = Variable(name);

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (variable.IsReadOnly)
            {
                throw new ArgumentException("The system variable " + variable.Name + " is read only.");
            }

            bool taken = variable.IsInDrawing
                ? InStep("SETVAR", () => variable.SetFromLisp(_session, value))
                : variable.SetFromLisp(_session, value);

            if (!taken)
            {
                throw new ArgumentException("The value " + Shown(value) + " is refused for " + variable.Name + ".");
            }
        }

        private CadSystemVariable Variable(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            return LispDrawing.Named(_session, name)
                ?? throw new ArgumentException("The system variable " + name + " is not known.");
        }

        private static string Shown(object value)
        {
            if (value is double real)
            {
                return real.ToString("R", CultureInfo.InvariantCulture);
            }

            if (value is double[] point)
            {
                return string.Join(",", Array.ConvertAll(point, p => p.ToString("R", CultureInfo.InvariantCulture)));
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        // ---- The steps ----

        private static bool IsFinite(GeoPoint2 point) => Guard.IsFinite(point.X) && Guard.IsFinite(point.Y);

        /// <summary>Refuses what ZOOM does not offer through a viewport that is not locked.</summary>
        private void RefuseThroughViewport(string option)
        {
            DxfViewport through = Document.ActiveViewport;

            if (through != null && !through.Locked)
            {
                throw new InvalidOperationException(
                    "ZOOM "
                        + option
                        + " works in model space or on the paper: through a viewport ZOOM offers All, Extents, Previous and Window."
                );
            }
        }

        /// <summary>Joins the undo step of the command running in the session, where one runs.</summary>
        private void JoinRun()
        {
            if (_session.IsRunning)
            {
                _session.EnsureGroup(_session.Current.Name);
            }
        }

        /// <summary>
        /// Runs work that makes edits in the step they are to be in: the command's where one runs, else a step of its own named
        /// <paramref name="name"/>, which is closed as the work ends; one the session holds open already (a routine's) is
        /// joined and left open.
        /// </summary>
        private T InStep<T>(string name, Func<T> work)
        {
            bool own = false;

            if (_session.IsRunning)
            {
                _session.EnsureGroup(_session.Current.Name);
            }
            else if (!_session.IsGrouped)
            {
                _session.EnsureGroup(name);
                own = true;
            }

            _session.LastRefusal = null;

            try
            {
                T done = work();

                if (_session.LastRefusal != null)
                {
                    throw new InvalidOperationException(_session.LastRefusal);
                }

                return done;
            }
            finally
            {
                if (own)
                {
                    _session.CloseGroup();
                }
            }
        }

        /// <summary>
        /// Moves the view of the space shown by <paramref name="move"/>, which says whether it moved it, and records the move as
        /// the typed command does: one undo step and an entry of ZOOM Previous, the command's step where one runs.
        /// </summary>
        private void MoveSpace(string name, Func<CadView, CadSpace, bool> move)
        {
            CadView view = _session.CommandView;
            CadSpace shown = Document.CurrentSpace;
            DisplayBox before = view.VisibleBox;

            if (!move(view, shown))
            {
                return;
            }

            JoinRun();
            Document.RecordSpaceView(shown, view, before, name, false);
            _session.RaiseViewChanged();
        }

        /// <summary>Gives a viewport a view, as ZOOM and -PAN through it do, and tells the window.</summary>
        private void MoveViewport(DxfViewport viewport, string step, GeoPoint2 center, double height)
        {
            InStep(step, () =>
                CadZoom.SetViewport(
                    _session,
                    step,
                    viewport.Handle,
                    center,
                    height,
                    viewport.ViewCenter,
                    viewport.ViewHeight
                )
            );
            _session.RaiseViewChanged();
        }
    }
}
