using System;
using System.IO;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// What AutoCAD 2021 keeps of the Sheet Set Manager as system variables, one set for the whole app: SSLOCATE and SSMAUTOOPEN
    /// (user settings), SSMPOLLTIME and SSMSHEETSTATUS (AutoCAD keeps them in the registry, here the settings file does), and the
    /// folder the new sheet set wizard offers for the .dst. A host that has several drawings gives each session
    /// (<see cref="CadCommandSession.SheetSetOptions"/>) the same object, so that SETVAR in one drawing is seen in the others,
    /// and keeps the values in <see cref="GlobalSettings"/> (<see cref="Changed"/> tells it to save them).
    /// </summary>
    public sealed class CadSheetSetOptions
    {
        /// <summary>The least SSMPOLLTIME, in seconds.</summary>
        public const int LeastPollSeconds = 20;

        /// <summary>The greatest SSMPOLLTIME, in seconds.</summary>
        public const int MostPollSeconds = 600;

        /// <summary>The SSMPOLLTIME a session starts with, in seconds.</summary>
        public const int DefaultPollSeconds = 60;

        /// <summary>The SSMSHEETSTATUS a session starts with.</summary>
        public const int DefaultSheetStatus = 2;

        private bool _locate = true;
        private bool _autoOpen = true;
        private int _pollSeconds = DefaultPollSeconds;
        private int _status = DefaultSheetStatus;
        private string _storeFolder;

        /// <summary>Occurs when a value is set to another value than it had.</summary>
        public event EventHandler Changed;

        /// <summary>
        /// Gets or sets whether opening a drawing that is a sheet opens its sheet set as well (SSLOCATE): on by default, as AutoCAD
        /// 2021's 1. SSFOUND then holds the file of the set found.
        /// </summary>
        public bool Locate
        {
            get => _locate;
            set => Take(ref _locate, value);
        }

        /// <summary>
        /// Gets or sets whether the Sheet Set Manager is shown when a sheet's drawing is opened (SSMAUTOOPEN): on by default, as
        /// AutoCAD 2021's 1; it takes effect only while <see cref="Locate"/> is on.
        /// </summary>
        public bool AutoOpen
        {
            get => _autoOpen;
            set => Take(ref _autoOpen, value);
        }

        /// <summary>
        /// Gets or sets how often the status of the sheets is looked at again while the Sheet Set Manager is shown and
        /// <see cref="SheetStatus"/> is 2 (SSMPOLLTIME), in seconds from 20 to 600: 60 by default, as AutoCAD 2021's.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is below 20 or above 600.</exception>
        public int PollSeconds
        {
            get => _pollSeconds;
            set
            {
                if (value < LeastPollSeconds || value > MostPollSeconds)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        value,
                        "The poll time is from " + LeastPollSeconds + " to " + MostPollSeconds + " seconds."
                    );
                }

                Take(ref _pollSeconds, value);
            }
        }

        /// <summary>
        /// Gets or sets when the status of the sheets (a sheet's file there or missing, a drawing open, the set's file changed) is
        /// looked at again (SSMSHEETSTATUS): 0 never, 1 when the set is loaded or refreshed, 2 as well on the timer of
        /// <see cref="PollSeconds"/>. 2 by default, as AutoCAD 2021's.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is not 0, 1 or 2.</exception>
        public int SheetStatus
        {
            get => _status;
            set
            {
                if (value < 0 || value > 2)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The sheet status is 0, 1 or 2.");
                }

                Take(ref _status, value);
            }
        }

        /// <summary>
        /// Gets or sets the folder the new sheet set wizard offers for the .dst (AutoCAD's "AutoCAD Sheet Sets" in the user's
        /// Documents): <c>OpenCAD2D Sheet Sets</c> in the user's Documents unless a host sets another. Not a system variable.
        /// </summary>
        public string StoreFolder
        {
            get =>
                _storeFolder
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "OpenCAD2D Sheet Sets"
                );
            set => Take(ref _storeFolder, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        }

        /// <summary>Gets whether <see cref="StoreFolder"/> was set, rather than the default folder.</summary>
        internal bool StoreFolderIsSet => _storeFolder != null;

        private void Take<T>(ref T field, T value)
        {
            if (!Equals(field, value))
            {
                field = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// What a sheet set command hands the window (the <see cref="CadUiRequestEventArgs.Payload"/> of OPENSHEETSET, NEWSHEETSET and
    /// SSLOCATE): the sheet set data file to open in the Sheet Set Manager, or none where the window is to ask the user for it in
    /// its own dialog (the Open Sheet Set dialog, the Create Sheet Set wizard).
    /// </summary>
    public sealed class CadSheetSetRequest
    {
        internal CadSheetSetRequest(string path, bool showPalette)
        {
            Path = path;
            ShowPalette = showPalette;
        }

        /// <summary>Gets the full path of the .dst to open and make current; null where the window asks the user, with its dialog, which to open or make.</summary>
        public string Path { get; }

        /// <summary>Gets whether the Sheet Set Manager is to be shown as well as the set opened: true for a drawing's set found with SSMAUTOOPEN on.</summary>
        public bool ShowPalette { get; }
    }
}
