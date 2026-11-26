using GeometryHelper.Geometry;

namespace OpenCAD2D.Wpf
{
    /// <summary>What the Sheet Views and Model Views tabs of the Sheet Set Manager ask of the window: a command of their own run on the command line, and a view dropped on the drawing area.</summary>
    public sealed partial class MainViewModel
    {
        /// <summary>
        /// Runs a command the Sheet Set Manager made (Place on Sheet, Display) on the command line of the drawing current, as a
        /// command typed: its prompts, Esc and the rubber band are those of any command. Refused where no drawing is current or a
        /// command is running.
        /// </summary>
        /// <param name="command">The command.</param>
        /// <returns>Whether it was started.</returns>
        internal bool StartSheetSetCommand(CadCommand command)
        {
            if (_current == null || _current.Session.IsRunning)
            {
                return false;
            }

            AttachTiles(_current);

            if (!_current.Session.Start(command))
            {
                return false;
            }

            SyncHistory();
            RunAfterCommand();
            return true;
        }

        /// <summary>
        /// Places a view dropped from the Model Views tab on the layout current, at the point it was dropped: the same as Place on
        /// Sheet with the click made there.
        /// </summary>
        /// <param name="text">What <see cref="SheetSetModelViewDrag.Write"/> wrote.</param>
        /// <param name="at">Where it is dropped, on the paper; null where it is not known.</param>
        /// <returns>Whether the text was one a drag of the tab wrote and the view was placed.</returns>
        public bool DropFromSheetSetManager(string text, GeoPoint2? at) =>
            at.HasValue && SheetSetManager.PlaceDropped(text, at.Value);
    }
}
