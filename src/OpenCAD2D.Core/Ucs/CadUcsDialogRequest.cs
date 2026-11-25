using System;
using System.Collections.Generic;
using GeometryHelper;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// The UCS dialog of UCSMAN (UC), as data its command and the window share: AutoCAD 2021's three tabs, Named UCSs,
    /// Orthographic UCSs and Settings. It holds a working copy of the drawing's systems; nothing reaches the drawing until the
    /// command applies it on OK, and then everything the dialog changed is one undo step named UCSMAN. Cancel, Esc and the
    /// close button apply nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Named UCSs list is Unnamed (only while the current system has no name and is not the world), World, Previous, then
    /// the named systems A to Z, letters of either case the same. Set Current moves the black triangle and the "Current UCS"
    /// label at once, and the system becomes current at OK; a system not parallel to the view (the presenter shows the
    /// drawing from above) is refused. Rename edits a name in place: renaming Unnamed saves the current system under the
    /// name; World, Previous and Unnamed are refused as new names, as "already in use". Delete takes a row out with no
    /// question, and a current system whose name is deleted goes on as an unnamed one.
    /// </para>
    /// <para>
    /// The Orthographic UCSs tab lists Top, Bottom, Front, Back, Left and Right with their depth, relative to World or a named
    /// system. A system is the axes of the face in the axes of the system it is relative to, its origin moved along its Z axis
    /// by the depth: Front relative to World with depth 5 has its origin at (0, -5, 0). Only a system whose Z axis is the
    /// world's (Top, and the others relative to a system tilted to match) can be made current. The depth and Relative to were
    /// not measured in AutoCAD 2021; they follow its documentation.
    /// </para>
    /// <para>
    /// The Settings tab holds the UCS icon (On, Display at UCS origin point, Allow Selecting UCS icon, and Apply to all active
    /// viewports, which opens off every time and acts once at OK) and the UCS settings (Save UCS with viewport, Update view to
    /// Plan when UCS is changed). They are the UCSICON bits 1 and 2, UCSSELECTMODE, UCSVP and UCSFOLLOW of AutoCAD.
    /// </para>
    /// </remarks>
    public sealed class CadUcsDialogRequest
    {
        /// <summary>The characters AutoCAD 2021's UCS dialog does not take in a name.</summary>
        private const string UnsupportedCharacters = "\\<>/?\":;*|,=`";

        private const int LongestName = 255;

        private readonly CadUcsManager _manager;
        private readonly List<CadUcsDialogRow> _rows = new List<CadUcsDialogRow>();
        private readonly List<CadUcsDialogRow> _named = new List<CadUcsDialogRow>();
        private readonly List<CadUcsOrthoRow> _ortho;
        private readonly CadUcsDialogRow _world;
        private readonly CadUcsDialogRow _previous;
        private readonly bool _hasPrevious;
        private CadUcsDialogRow _unnamed;
        private CadUcsDialogRow _current;
        private bool _previousChosen;
        private CadUcsDialogRow _orthoBase;

        internal CadUcsDialogRequest(CadDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            _manager = document.Ucs;
            CadUcs current = _manager.Current;
            _world = new CadUcsDialogRow(CadUcsRowKind.World, "World", CadUcs.World, null);
            CadUcs back = _manager.PeekPrevious();
            _hasPrevious = back != null;
            _previous = new CadUcsDialogRow(CadUcsRowKind.Previous, "Previous", back ?? CadUcs.World, null);

            foreach (CadUcs ucs in _manager.Named)
            {
                var row = new CadUcsDialogRow(CadUcsRowKind.Named, ucs.Name, ucs, ucs.Name);
                _named.Add(row);

                if (string.Equals(ucs.Name, current.Name, StringComparison.OrdinalIgnoreCase))
                {
                    _current = row;
                }
            }

            if (_current == null && current.Name == null && current.IsWorld)
            {
                _current = _world;
            }
            else if (_current == null)
            {
                _unnamed = new CadUcsDialogRow(CadUcsRowKind.Unnamed, "Unnamed", current.WithName(null), null);
                _current = _unnamed;
            }

            _orthoBase = _world;
            _ortho = new List<CadUcsOrthoRow>
            {
                Face("Top", 1, 0, 0, 0, 1, 0),
                Face("Bottom", 1, 0, 0, 0, -1, 0),
                Face("Front", 1, 0, 0, 0, 0, 1),
                Face("Back", -1, 0, 0, 0, 0, 1),
                Face("Left", 0, -1, 0, 0, 0, 1),
                Face("Right", 0, 1, 0, 0, 0, 1),
            };
            IconOn = _manager.IconOn;
            IconAtOrigin = _manager.IconAtOrigin;
            IconSelectable = _manager.IconSelectable;
            SaveWithViewport = _manager.SaveWithViewport;
            FollowToPlan = _manager.FollowToPlan;
            Rebuild();
        }

        /// <summary>Gets or sets what the window did: OK, Cancel, or PickPoint (a new origin for the depth of an orthographic system, then the dialog is shown again).</summary>
        public CadDialogAction Action { get; set; }

        /// <summary>Gets or sets the tab shown: 0 for Named UCSs, 1 for Orthographic UCSs, 2 for Settings. The command keeps it for the next time, as AutoCAD's dialog opens on the tab used last.</summary>
        public int Tab { get; set; }

        /// <summary>
        /// Gets the rows of the Named UCSs list in the order AutoCAD shows them: Unnamed where there is one, World, Previous,
        /// then the named systems A to Z, letters of either case the same.
        /// </summary>
        public IReadOnlyList<CadUcsDialogRow> Rows => _rows;

        /// <summary>Gets the World row.</summary>
        public CadUcsDialogRow World => _world;

        /// <summary>Gets the row that is the current system as the dialog has it now, the one the black triangle marks.</summary>
        public CadUcsDialogRow CurrentRow => _current;

        /// <summary>Gets the name the "Current UCS" label shows: <c>Unnamed</c>, <c>World</c> or the name of the system.</summary>
        public string CurrentName => _current.Name;

        /// <summary>Gets the systems Details can be relative to: Unnamed where there is a row of it, World, then the named systems.</summary>
        public IReadOnlyList<CadUcsDialogRow> RelativeToRows
        {
            get
            {
                var rows = new List<CadUcsDialogRow>();

                if (_unnamed != null)
                {
                    rows.Add(_unnamed);
                }

                rows.Add(_world);
                rows.AddRange(_named);
                return rows;
            }
        }

        /// <summary>Gets the rows of the Orthographic UCSs tab: Top, Bottom, Front, Back, Left and Right.</summary>
        public IReadOnlyList<CadUcsOrthoRow> OrthoRows => _ortho;

        /// <summary>Gets the systems the Orthographic UCSs tab can be relative to: World, then the named systems.</summary>
        public IReadOnlyList<CadUcsDialogRow> OrthoBaseRows
        {
            get
            {
                var rows = new List<CadUcsDialogRow> { _world };
                rows.AddRange(_named);
                return rows;
            }
        }

        /// <summary>Gets or sets the system the Orthographic UCSs tab is relative to: World until set.</summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        public CadUcsDialogRow OrthoBase
        {
            get => _orthoBase;
            set => _orthoBase = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Gets or sets the orthographic row whose depth the window is picking a new origin for; null for none.</summary>
        public CadUcsOrthoRow PickRow { get; set; }

        /// <summary>Gets or sets whether the UCS icon is shown (UCSICON bit 1).</summary>
        public bool IconOn { get; set; }

        /// <summary>Gets or sets whether the icon is drawn at the origin of the system (UCSICON bit 2).</summary>
        public bool IconAtOrigin { get; set; }

        /// <summary>
        /// Gets or sets whether OK gives every viewport the icon settings, as UCSICON All. It is off every time the dialog
        /// opens and acts once. A space has one view and one icon here, so on or off, OK gives the same result.
        /// </summary>
        public bool ApplyToAllViewports { get; set; }

        /// <summary>Gets or sets whether the UCS icon can be selected (UCSSELECTMODE).</summary>
        public bool IconSelectable { get; set; }

        /// <summary>Gets or sets whether each viewport keeps its own system (UCSVP).</summary>
        public bool SaveWithViewport { get; set; }

        /// <summary>Gets or sets whether the view goes to the plan when the system changes (UCSFOLLOW).</summary>
        public bool FollowToPlan { get; set; }

        /// <summary>A face of the orthographic tab: its X and Y axes in the axes of the system it is relative to.</summary>
        private static CadUcsOrthoRow Face(
            string name,
            double xx,
            double xy,
            double xz,
            double yx,
            double yy,
            double yz
        ) => new CadUcsOrthoRow(name, new GeoVector3(xx, xy, xz), new GeoVector3(yx, yy, yz));

        /// <summary>
        /// Makes a system current in the dialog: the black triangle and the label move, and OK makes it current in the drawing.
        /// </summary>
        /// <param name="row">The row, one of <see cref="Rows"/>.</param>
        /// <returns>Null; or why it was refused, with nothing changed: the system is not parallel to the view, or there is no Previous.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        public string SetCurrent(CadUcsDialogRow row)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            if (row.Kind == CadUcsRowKind.Previous)
            {
                if (!_hasPrevious)
                {
                    return "No previous coordinate system saved.";
                }

                _previousChosen = true;
                Place(row.System);
                return null;
            }

            if (!row.System.IsPlanar)
            {
                return CadCommands.NotInPlan;
            }

            _previousChosen = false;
            _current = row;
            Rebuild();
            return null;
        }

        /// <summary>
        /// Renames a row in place. A named system takes the name; Unnamed is saved under it, the current system as it stands,
        /// and becomes a named row.
        /// </summary>
        /// <param name="row">The row, one of <see cref="Rows"/> that can be renamed.</param>
        /// <param name="name">The new name; the spaces around it are dropped.</param>
        /// <returns>Null; or the message AutoCAD shows for a name it refuses, with nothing changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the row cannot be renamed (World and Previous).</exception>
        public CadUcsDialogError Rename(CadUcsDialogRow row, string name)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            if (!row.CanRename)
            {
                throw new ArgumentException("The row " + row.Name + " cannot be renamed.", nameof(row));
            }

            string trimmed = (name ?? string.Empty).Trim();

            if (trimmed.Length == 0)
            {
                return new CadUcsDialogError(
                    "UCS - Text Length",
                    "The name must be at least one character long.",
                    string.Empty
                );
            }

            if (trimmed.IndexOfAny(UnsupportedCharacters.ToCharArray()) >= 0)
            {
                return new CadUcsDialogError(
                    "UCS - Unsupported Characters",
                    "The name contains unsupported characters.",
                    "The following characters are not supported: \\ < > / ? \" : ; * | , = `"
                );
            }

            if (trimmed.Length > LongestName)
            {
                return new CadUcsDialogError(
                    "UCS - Text Length",
                    "The name must be at most 255 characters long.",
                    string.Empty
                );
            }

            if (string.Equals(trimmed, row.Name, StringComparison.Ordinal))
            {
                return null;
            }

            if (InUse(trimmed, row))
            {
                return new CadUcsDialogError(
                    "UCS - Name Already Exists",
                    "The name, " + trimmed + ", is already in use.",
                    "You will need to enter a different name."
                );
            }

            if (row.Kind == CadUcsRowKind.Unnamed)
            {
                row.Kind = CadUcsRowKind.Named;
                _named.Add(row);
                _unnamed = null;
            }

            row.Name = trimmed;
            row.System = row.System.WithName(trimmed);
            Rebuild();
            return null;
        }

        /// <summary>
        /// Deletes a named system from the list, with no question. A current system of that name goes on, with no name, as
        /// Unnamed.
        /// </summary>
        /// <param name="row">The row, one of <see cref="Rows"/> that can be deleted.</param>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the row cannot be deleted (Unnamed, World and Previous).</exception>
        public void Delete(CadUcsDialogRow row)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            if (!row.CanDelete)
            {
                throw new ArgumentException("The row " + row.Name + " cannot be deleted.", nameof(row));
            }

            _named.Remove(row);

            if (ReferenceEquals(row, _current))
            {
                Place(row.System.WithName(null));
            }
            else
            {
                Rebuild();
            }

            if (ReferenceEquals(row, _orthoBase))
            {
                _orthoBase = _world;
            }
        }

        /// <summary>Gets what the Details dialog shows of a row, relative to a system.</summary>
        /// <param name="row">The row.</param>
        /// <param name="relativeTo">The system the numbers are relative to, one of <see cref="RelativeToRows"/>.</param>
        /// <returns>The name, the origin and the axes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row or the system is null.</exception>
        public CadUcsDetails Details(CadUcsDialogRow row, CadUcsDialogRow relativeTo)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            return Describe(row.Name, row.System, relativeTo);
        }

        /// <summary>Gets what the Details dialog shows of an orthographic row, the system it makes relative to <see cref="OrthoBase"/>, shown relative to another.</summary>
        /// <param name="row">The row.</param>
        /// <param name="relativeTo">The system the numbers are relative to, one of <see cref="RelativeToRows"/>.</param>
        /// <returns>The name, the origin and the axes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row or the system is null.</exception>
        public CadUcsDetails Details(CadUcsOrthoRow row, CadUcsDialogRow relativeTo)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            return Describe(row.Name, Orthographic(row), relativeTo);
        }

        /// <summary>Gets the system an orthographic row makes: its axes in those of <see cref="OrthoBase"/>, the origin moved along its Z axis by the depth.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The system, with no name.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        public CadUcs Orthographic(CadUcsOrthoRow row)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            CadUcs basis = _orthoBase.System;
            GeoVector3 x = basis.XAxis * row.XAxis.X + basis.YAxis * row.XAxis.Y + basis.ZAxis * row.XAxis.Z;
            GeoVector3 y = basis.XAxis * row.YAxis.X + basis.YAxis * row.YAxis.Y + basis.ZAxis * row.YAxis.Z;
            GeoVector3 z = x.CrossProduct(y);
            return new CadUcs(basis.Origin + z * row.Depth, x, y);
        }

        /// <summary>Makes an orthographic system current in the dialog, as <see cref="SetCurrent(CadUcsDialogRow)"/> does a named one.</summary>
        /// <param name="row">The row.</param>
        /// <returns>Null; or why it was refused, with nothing changed: the system is not parallel to the view.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        public string SetCurrent(CadUcsOrthoRow row)
        {
            CadUcs ucs = Orthographic(row);

            if (!ucs.IsPlanar)
            {
                return CadCommands.NotInPlan;
            }

            _previousChosen = false;
            Place(ucs);
            return null;
        }

        /// <summary>Sets the depth of an orthographic row, as the Orthographic UCS depth dialog does.</summary>
        /// <param name="row">The row.</param>
        /// <param name="depth">The depth in millimetres, negative to go the other way along the normal.</param>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the depth is not a finite number.</exception>
        public void SetDepth(CadUcsOrthoRow row, double depth)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            if (double.IsNaN(depth) || double.IsInfinity(depth))
            {
                throw new ArgumentException("A depth has to be a finite number.", nameof(depth));
            }

            row.Depth = depth;
        }

        /// <summary>Sets the depth of an orthographic row to nought, as Reset does.</summary>
        /// <param name="row">The row.</param>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        public void ResetDepth(CadUcsOrthoRow row) => SetDepth(row, 0.0);

        /// <summary>
        /// Gives <see cref="PickRow"/> the depth of a point picked in the drawing, the distance of the point from the origin of
        /// <see cref="OrthoBase"/> along the normal of the plane; the pick is done.
        /// </summary>
        /// <param name="world">The point, in world coordinates.</param>
        /// <exception cref="InvalidOperationException">Thrown when no row is being picked for.</exception>
        public void TakeDepthPoint(GeoPoint3 world)
        {
            CadUcsOrthoRow row = PickRow ?? throw new InvalidOperationException("No orthographic row is being picked.");
            row.Depth = 0.0;
            CadUcs flat = Orthographic(row);
            row.Depth = (world - flat.Origin).DotProduct(flat.ZAxis);
            PickRow = null;
        }

        /// <summary>Gives the drawing what the dialog changed, in the undo step the command's group is: the named systems, the current system, then the icon settings.</summary>
        internal void Apply()
        {
            bool tableChanged = _named.Count != _manager.Named.Count;
            var after = new List<KeyValuePair<string, CadUcs>>();

            foreach (CadUcsDialogRow row in _named)
            {
                after.Add(new KeyValuePair<string, CadUcs>(row.Original, row.System));
                tableChanged |=
                    row.Original == null || !string.Equals(row.Original, row.Name, StringComparison.Ordinal);
            }

            if (tableChanged)
            {
                _manager.ReplaceNamed(after);
            }

            if (_previousChosen)
            {
                _manager.Previous();
            }

            CadUcs target = _current.System;
            CadUcs now = _manager.Current;

            if (
                !(
                    target.IsSameAs(now, Tolerance.Default)
                    && string.Equals(target.Name, now.Name, StringComparison.Ordinal)
                )
            )
            {
                _manager.SetCurrent(target);
            }

            _manager.IconOn = IconOn;
            _manager.IconAtOrigin = IconAtOrigin;
            _manager.IconSelectable = IconSelectable;
            _manager.SaveWithViewport = SaveWithViewport;
            _manager.FollowToPlan = FollowToPlan;
        }

        /// <summary>Whether a name is taken: by World, Previous or Unnamed, by another named system, or by one of the drawing the presenter could not read.</summary>
        private bool InUse(string name, CadUcsDialogRow except)
        {
            if (
                string.Equals(name, "World", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Previous", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Unnamed", StringComparison.OrdinalIgnoreCase)
                || _manager.KeepsUnread(name)
            )
            {
                return true;
            }

            foreach (CadUcsDialogRow row in _named)
            {
                if (!ReferenceEquals(row, except) && string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Makes a system the current one of the dialog: the row of its name, World, or Unnamed holding it.</summary>
        private void Place(CadUcs system)
        {
            CadUcsDialogRow target = null;

            if (system.Name != null)
            {
                foreach (CadUcsDialogRow row in _named)
                {
                    if (string.Equals(row.Name, system.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        target = row;
                    }
                }
            }

            if (target == null && system.Name == null && system.IsWorld)
            {
                target = _world;
            }

            if (target == null)
            {
                CadUcs unnamed = system.Name == null ? system : system.WithName(null);

                if (_unnamed == null)
                {
                    _unnamed = new CadUcsDialogRow(CadUcsRowKind.Unnamed, "Unnamed", unnamed, null);
                }
                else
                {
                    _unnamed.System = unnamed;
                }

                target = _unnamed;
            }

            _current = target;
            Rebuild();
        }

        /// <summary>Lists the rows again in AutoCAD's order, and marks the current one.</summary>
        private void Rebuild()
        {
            _named.Sort(
                (a, b) =>
                {
                    int order = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    return order != 0 ? order : string.CompareOrdinal(a.Name, b.Name);
                }
            );
            _rows.Clear();

            if (_unnamed != null)
            {
                _rows.Add(_unnamed);
            }

            _rows.Add(_world);
            _rows.Add(_previous);
            _rows.AddRange(_named);

            foreach (CadUcsDialogRow row in _rows)
            {
                row.IsCurrent = ReferenceEquals(row, _current);
            }
        }

        /// <summary>The numbers of a system relative to another: its origin and axes in the axes of that one.</summary>
        private static CadUcsDetails Describe(string name, CadUcs system, CadUcsDialogRow relativeTo)
        {
            if (relativeTo == null)
            {
                throw new ArgumentNullException(nameof(relativeTo));
            }

            CadUcs basis = relativeTo.System;
            return new CadUcsDetails(
                name,
                basis.ToUcs(system.Origin),
                InAxes(basis, system.XAxis),
                InAxes(basis, system.YAxis),
                InAxes(basis, system.ZAxis)
            );
        }

        private static GeoVector3 InAxes(CadUcs basis, GeoVector3 direction) =>
            new GeoVector3(
                direction.DotProduct(basis.XAxis),
                direction.DotProduct(basis.YAxis),
                direction.DotProduct(basis.ZAxis)
            );
    }
}
