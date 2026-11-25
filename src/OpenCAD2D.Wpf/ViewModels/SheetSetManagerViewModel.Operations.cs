using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    public sealed partial class SheetSetManagerViewModel
    {
        // ---- Drag and drop

        /// <summary>
        /// Works out where a drop puts the rows dragged: the group it goes to and the place in it. A row whose group is dragged
        /// with it goes along with that group; the rows keep the order they have in the tree. Nothing is planned where the set is
        /// locked, where the set itself is dragged, where a row would go into itself or into what it holds, where the drop is
        /// on a sheet (a sheet holds nothing), or beside the set (it has no group above it).
        /// </summary>
        /// <param name="dragged">The rows dragged.</param>
        /// <param name="target">The row dropped on.</param>
        /// <param name="position">Where on the row.</param>
        /// <returns>The group and place; null where the drop is not allowed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the rows or the target are null.</exception>
        public SheetSetDrop PlanDrop(
            IReadOnlyList<SheetSetNodeViewModel> dragged,
            SheetSetNodeViewModel target,
            SheetSetDropPosition position
        )
        {
            if (dragged == null)
            {
                throw new ArgumentNullException(nameof(dragged));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            List<SheetSetNodeViewModel> moving = Moving(dragged);

            if (IsLocked || moving.Count == 0 || moving.Any(n => n.IsSheetSet))
            {
                return null;
            }

            SheetSetNodeViewModel parent;

            if (position == SheetSetDropPosition.Into)
            {
                if (target.IsSheet)
                {
                    return null;
                }

                parent = target;
            }
            else
            {
                if (target.Parent == null || moving.Contains(target))
                {
                    return null;
                }

                parent = target.Parent;
            }

            if (moving.Any(n => n == parent || IsInside(parent, n)))
            {
                return null;
            }

            List<SheetSetNodeViewModel> staying = parent.Children.Where(c => !moving.Contains(c)).ToList();
            int index =
                position == SheetSetDropPosition.Into
                    ? staying.Count
                    : staying.IndexOf(target) + (position == SheetSetDropPosition.After ? 1 : 0);
            return new SheetSetDrop(parent, index);
        }

        /// <summary>Moves the rows dragged where <see cref="PlanDrop"/> puts them, as one change of the set.</summary>
        /// <param name="dragged">The rows dragged.</param>
        /// <param name="target">The row dropped on.</param>
        /// <param name="position">Where on the row.</param>
        /// <returns>Whether they moved; false where the drop is not allowed or the set could not be changed.</returns>
        public bool Move(
            IReadOnlyList<SheetSetNodeViewModel> dragged,
            SheetSetNodeViewModel target,
            SheetSetDropPosition position
        )
        {
            SheetSetDrop plan = PlanDrop(dragged, target, position);

            if (plan == null)
            {
                return false;
            }

            List<SheetSetNodeViewModel> moving = Moving(dragged);
            return Change(set =>
            {
                var parent = Live<CadSheetSubset>(set, plan.Parent);
                int index = plan.Index;

                foreach (SheetSetNodeViewModel node in moving)
                {
                    set.Move(Live<CadSheetComponent>(set, node), parent, index++);
                }
            });
        }

        /// <summary>The rows that move: those dragged that are not inside another dragged row, in the order of the tree.</summary>
        private List<SheetSetNodeViewModel> Moving(IReadOnlyList<SheetSetNodeViewModel> dragged)
        {
            var all = new HashSet<SheetSetNodeViewModel>(dragged);
            return (_root == null ? Enumerable.Empty<SheetSetNodeViewModel>() : Walk(_root))
                .Where(n => all.Contains(n) && !all.Any(o => IsInside(n, o)))
                .ToList();
        }

        /// <summary>Whether a row is somewhere under another.</summary>
        private static bool IsInside(SheetSetNodeViewModel node, SheetSetNodeViewModel above)
        {
            for (SheetSetNodeViewModel up = node.Parent; up != null; up = up.Parent)
            {
                if (up == above)
                {
                    return true;
                }
            }

            return false;
        }

        // ---- The operations of the menus

        /// <summary>
        /// Adds a subset as New Subset does, at the end of the set or subset picked, or after the sheet picked. The core refuses
        /// an empty name, a sibling's name and, with a folder, a name no folder can have.
        /// </summary>
        /// <param name="at">The row the menu was opened on.</param>
        /// <param name="name">The name.</param>
        /// <param name="description">The description, or null for none.</param>
        /// <param name="createFolder">Whether the subset gets a folder of its own for its sheets.</param>
        /// <returns>Whether the subset was added; why not is said on the command line.</returns>
        public bool NewSubset(SheetSetNodeViewModel at, string name, string description, bool createFolder)
        {
            Place(at, out SheetSetNodeViewModel parent, out int index);
            return Change(
                set => Live<CadSheetSubset>(set, parent).NewSubset(name, description, createFolder, index),
                reveal: parent
            );
        }

        /// <summary>
        /// Makes a sheet as New Sheet does (a drawing made from the sheet creation template, its layout named "Number Title"),
        /// at the end of the set or subset picked, or after the sheet picked.
        /// </summary>
        /// <param name="at">The row the menu was opened on.</param>
        /// <param name="number">The sheet number, free text; empty for none.</param>
        /// <param name="title">The sheet title.</param>
        /// <param name="template">The layout to make it from, or null for the group's own.</param>
        /// <param name="fileName">The drawing's file name, or null for "Number Title".</param>
        /// <param name="openInEditor">Whether the new drawing is opened, on its layout, once made.</param>
        /// <returns>Whether the sheet was made; why not is said on the command line.</returns>
        public bool NewSheet(
            SheetSetNodeViewModel at,
            string number,
            string title,
            CadDrawingReference template = null,
            string fileName = null,
            bool openInEditor = false
        )
        {
            Place(at, out SheetSetNodeViewModel parent, out int index);
            string made = null;

            if (
                !Change(
                    set =>
                        made = Live<CadSheetSubset>(set, parent).NewSheet(number, title, template, fileName, index).Id,
                    reveal: parent
                )
            )
            {
                return false;
            }

            SheetSetNodeViewModel node = AllSheets().FirstOrDefault(s => s.Id == made);

            if (openInEditor && node != null)
            {
                OpenSheets(new[] { node }, false);
            }

            return true;
        }

        /// <summary>
        /// Makes layouts of a drawing sheets of the set, as Import Layouts as Sheets does, at the end of the set or subset picked
        /// (of the one the sheet picked is in). The drawing is saved and closed first where it is open in the app, and opened
        /// again after.
        /// </summary>
        /// <param name="into">The row the menu was opened on.</param>
        /// <param name="drawingFile">The path of the drawing, a DWG.</param>
        /// <param name="layouts">The names of the layouts.</param>
        /// <param name="prefixTitleWithFileName">Whether the sheet titles carry the file name.</param>
        /// <returns>Whether the sheets were made; why not is said on the command line.</returns>
        public bool ImportLayouts(
            SheetSetNodeViewModel into,
            string drawingFile,
            IReadOnlyList<string> layouts,
            bool prefixTitleWithFileName
        )
        {
            Place(into, out SheetSetNodeViewModel parent, out _);
            return Change(
                set => Live<CadSheetSubset>(set, parent).ImportLayouts(drawingFile, layouts, prefixTitleWithFileName),
                new[] { drawingFile },
                reveal: parent
            );
        }

        /// <summary>
        /// Renames and renumbers a sheet as Rename &amp; Renumber Sheet does. Where its layout or its file is renamed and the
        /// drawing is open in the app, it is saved, closed, renamed and opened again (B18): the changes not saved stay in it.
        /// </summary>
        /// <param name="sheet">The sheet's row.</param>
        /// <param name="number">The new number; empty for none.</param>
        /// <param name="title">The new title.</param>
        /// <param name="layoutName">The new layout name, or null to keep it.</param>
        /// <param name="fileName">The new file name, or null to keep it.</param>
        /// <returns>Whether the sheet was renamed; why not is said on the command line.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        public bool RenameAndRenumber(
            SheetSetNodeViewModel sheet,
            string number,
            string title,
            string layoutName,
            string fileName
        )
        {
            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }

            string drawing = sheet.Found?.DrawingFile;
            bool touches =
                drawing != null
                && (
                    (layoutName != null && !string.Equals(layoutName, sheet.Found.LayoutName, StringComparison.Ordinal))
                    || (
                        fileName != null
                        && !string.Equals(SheetFileName(fileName), Path.GetFileName(drawing), StringComparison.Ordinal)
                    )
                );
            return Change(
                set => Live<CadSheetSetSheet>(set, sheet).RenameAndRenumber(number, title, layoutName, fileName),
                touches ? new[] { drawing } : null,
                follow: touches ? sheet.Id : null
            );
        }

        /// <summary>
        /// Takes sheets and subsets out of the set as Remove Sheet and Remove Subset do. A sheet's drawing is never deleted; the
        /// question asked first says so. A subset that holds sheets or subsets is not removed.
        /// </summary>
        /// <param name="nodes">The rows; the set itself is left alone.</param>
        /// <returns>Whether they were removed; false where a subset holds something, the question was answered no, or the set could not be changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the rows are null.</exception>
        public bool Remove(IReadOnlyList<SheetSetNodeViewModel> nodes)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            List<SheetSetNodeViewModel> targets = Moving(nodes.Where(n => !n.IsSheetSet).ToList());

            if (targets.Count == 0)
            {
                return false;
            }

            SheetSetNodeViewModel full = targets.FirstOrDefault(n => n.IsSubset && n.Children.Count > 0);

            if (full != null)
            {
                _main.Say("The subset " + full.Text + " holds sheets or subsets: move or remove them first.");
                return false;
            }

            List<SheetSetNodeViewModel> sheets = targets.Where(n => n.IsSheet).ToList();

            if (sheets.Count > 0 && ConfirmRemove != null)
            {
                string question =
                    sheets.Count == 1
                        ? "Remove the sheet " + sheets[0].Text + " from the sheet set? Its drawing is not deleted."
                        : "Remove " + sheets.Count + " sheets from the sheet set? Their drawings are not deleted.";

                if (!ConfirmRemove(question))
                {
                    return false;
                }
            }

            return Change(set =>
            {
                foreach (SheetSetNodeViewModel node in targets)
                {
                    set.Remove(Live<CadSheetComponent>(set, node));
                }
            });
        }

        /// <summary>
        /// Opens every sheet's drawing and saves it again as Resave All Sheets does, so that the drawings and the set agree. The
        /// sheets open in the app are saved, closed and opened again around it. A sheet that is missing is left as it is and
        /// the command line says so.
        /// </summary>
        /// <returns>Whether the sheets were resaved.</returns>
        public bool ResaveAllSheets()
        {
            IEnumerable<string> drawings = AllSheets().Where(s => !s.IsMissing).Select(s => s.Found.DrawingFile);
            int missing = 0;
            bool done = Change(set => missing = set.ResaveAllSheets().Count, drawings);

            if (done && missing > 0)
            {
                _main.Say(missing + (missing == 1 ? " sheet is" : " sheets are") + " missing and was left as it is.");
            }

            return done;
        }

        /// <summary>
        /// Opens the drawings of sheets with each sheet's layout current, as Open does; a drawing open already is shown. A sheet that is
        /// missing is not opened. The presenter has no read-only drawings, so Open Read-Only opens for editing and says so.
        /// </summary>
        /// <param name="sheets">The rows of the sheets.</param>
        /// <param name="readOnly">Whether Open Read-Only was chosen.</param>
        /// <returns>Whether a drawing was asked to open.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the rows are null.</exception>
        public bool OpenSheets(IEnumerable<SheetSetNodeViewModel> sheets, bool readOnly)
        {
            if (sheets == null)
            {
                throw new ArgumentNullException(nameof(sheets));
            }

            bool opened = false;

            foreach (SheetSetNodeViewModel sheet in sheets.Where(s => s.IsSheet))
            {
                if (sheet.IsMissing)
                {
                    _main.Say("The sheet " + sheet.Text + " is not found: there is no drawing to open.");
                    continue;
                }

                OpenDrawing(sheet.Found.DrawingFile, sheet.Found.LayoutName);
                opened = true;
            }

            if (opened && readOnly)
            {
                _main.Say("The presenter has no read-only drawings: the sheet is opened for editing.");
            }

            RefreshOpenState();
            return opened;
        }

        /// <summary>Puts sheets in or out of publishing, as the Include for Publish item does.</summary>
        /// <param name="sheets">The rows of the sheets.</param>
        /// <param name="include">Whether they are published with the set.</param>
        /// <returns>Whether the set was changed.</returns>
        public bool IncludeForPublish(IReadOnlyList<SheetSetNodeViewModel> sheets, bool include) =>
            Change(set =>
            {
                foreach (SheetSetNodeViewModel node in sheets.Where(s => s.IsSheet))
                {
                    Live<CadSheetSetSheet>(set, node).IncludeForPublish = include;
                }
            });

        // ---- Helpers

        /// <summary>Where a new subset or sheet goes for the row a menu was opened on: at the end of a set or subset, after a sheet.</summary>
        private static void Place(SheetSetNodeViewModel at, out SheetSetNodeViewModel parent, out int index)
        {
            if (at == null)
            {
                throw new ArgumentNullException(nameof(at));
            }

            parent = at.IsSheet ? at.Parent : at;
            index = at.IsSheet ? at.Parent.Children.ToList().IndexOf(at) + 1 : -1;
        }

        /// <summary>The part of the set a row stands for, found again by its ID in the file as it is now.</summary>
        private T Live<T>(CadSheetSet set, SheetSetNodeViewModel node)
            where T : CadSheetComponent =>
            set.Find(node.Id) as T
            ?? throw new InvalidOperationException(
                "The part " + node.Text + " is no longer in the sheet set: refresh the palette."
            );

        /// <summary>
        /// Changes the current set as one change of the file. A locked set is refused. The drawings the change rewrites that are
        /// open in the app are saved and closed first and opened again after; the one named by <paramref name="follow"/>
        /// is opened again under the name its sheet has then. What the core refuses is said on the command line.
        /// </summary>
        /// <param name="edit">The change.</param>
        /// <param name="drawings">The drawings it rewrites, or null for none.</param>
        /// <param name="follow">The ID of a sheet whose drawing, the first of the drawings, may be renamed by it; null for none.</param>
        /// <param name="reveal">A row to show open afterwards, so that what was added under it is seen; null for none.</param>
        /// <returns>Whether the change was made.</returns>
        private bool Change(
            Action<CadSheetSet> edit,
            IEnumerable<string> drawings = null,
            string follow = null,
            SheetSetNodeViewModel reveal = null
        )
        {
            if (_current == null)
            {
                return false;
            }

            if (IsLocked)
            {
                _main.Say("The sheet set is read-only and cannot be changed. " + _lockReason);
                return false;
            }

            var closed = new List<(string Path, string Layout)>();

            foreach (
                string drawing in (drawings ?? Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase)
            )
            {
                CadDocument open = _main.OpenDocumentOf(drawing);

                if (open == null)
                {
                    continue;
                }

                string layout = open.CurrentSpace == null || open.CurrentSpace.IsModel ? null : open.CurrentSpace.Name;

                if (!_main.CloseForSheetSet(open))
                {
                    _main.Say("The drawing " + drawing + " could not be saved, so the sheet set was not changed.");
                    return false;
                }

                closed.Add((drawing, layout));
            }

            bool changed;

            try
            {
                _current.Change(edit);
                changed = true;
            }
            catch (Exception ex) when (IsFileProblem(ex))
            {
                _main.Say(ex.Message);
                changed = false;
            }

            if (reveal != null)
            {
                _collapsed.Remove(reveal.Id);
            }

            foreach ((string path, string layout) in closed)
            {
                string again = path;
                string where = layout;

                if (
                    !File.Exists(path)
                    && follow != null
                    && string.Equals(path, closed[0].Path, StringComparison.Ordinal)
                )
                {
                    var sheet = _current.SheetSet.Find(follow) as CadSheetSetSheet;
                    again = sheet?.Layout?.File.Resolve(_current.Folder);
                    where = sheet?.Layout?.Name;
                }

                if (again != null && File.Exists(again))
                {
                    OpenDrawing(again, where);
                }
            }

            Rebuild();
            return changed;
        }

        /// <summary>Opens a drawing in the app, or shows it where it is open, and makes a layout of it current. In the background (the app's way) the layout is made current once it is shown.</summary>
        private void OpenDrawing(string path, string layout)
        {
            if (_main.OpensInBackground)
            {
                // The drawing is shown on the window's thread once drawn; its layout is made current there.
                Dispatcher window = Dispatcher.CurrentDispatcher;
                _main
                    .OpenAsync(path)
                    .ContinueWith(
                        t =>
                        {
                            if (t.Status == TaskStatus.RanToCompletion && t.Result)
                            {
                                window.BeginInvoke(new Action(() => ShowLayout(path, layout)));
                            }
                        },
                        TaskScheduler.Default
                    );
                return;
            }

            if (_main.Open(path))
            {
                ShowLayout(path, layout);
            }
        }

        private void ShowLayout(string path, string layout)
        {
            CadDocument document = _main.OpenDocumentOf(path);
            CadSpace space =
                layout == null
                    ? null
                    : document?.Spaces.FirstOrDefault(s =>
                        !s.IsModel && string.Equals(s.Name, layout, StringComparison.Ordinal)
                    );

            if (space != null && _main.Document == document)
            {
                _main.SelectSpace(space, false);
            }

            RefreshOpenState();
        }

        /// <summary>The name a file name given is saved under: with .dwg, as the core's rename finds it.</summary>
        private static string SheetFileName(string fileName) =>
            fileName.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".dwg";
    }
}
