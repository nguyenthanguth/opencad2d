using OpenCAD2D.SheetSets;

namespace OpenCAD2D
{
    /// <summary>The Sheet Set Manager's settings: the values of SSLOCATE, SSMAUTOOPEN, SSMPOLLTIME and SSMSHEETSTATUS, which AutoCAD keeps in the user's settings and the registry, and the folder the new sheet set wizard offers.</summary>
    public sealed partial class GlobalSettings
    {
        private int _sheetSetPollSeconds = CadSheetSetOptions.DefaultPollSeconds;
        private int _sheetSetStatus = CadSheetSetOptions.DefaultSheetStatus;
        private string _sheetSetStoreFolder = string.Empty;

        /// <summary>Gets or sets whether opening a drawing that is a sheet opens its sheet set (SSLOCATE): on by default.</summary>
        public bool SheetSetLocate { get; set; } = true;

        /// <summary>Gets or sets whether the Sheet Set Manager is shown when a sheet's drawing is opened (SSMAUTOOPEN): on by default.</summary>
        public bool SheetSetAutoOpen { get; set; } = true;

        /// <summary>Gets or sets how often the status of the sheets is looked at again, in seconds from 20 to 600 (SSMPOLLTIME): 60 by default.</summary>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown when the value is not from 20 to 600.</exception>
        public int SheetSetPollSeconds
        {
            get => _sheetSetPollSeconds;
            set =>
                _sheetSetPollSeconds = (int)Range(
                    value,
                    CadSheetSetOptions.LeastPollSeconds,
                    CadSheetSetOptions.MostPollSeconds,
                    nameof(SheetSetPollSeconds)
                );
        }

        /// <summary>Gets or sets when the status of the sheets is looked at again, 0, 1 or 2 (SSMSHEETSTATUS): 2 by default.</summary>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown when the value is not 0, 1 or 2.</exception>
        public int SheetSetStatus
        {
            get => _sheetSetStatus;
            set => _sheetSetStatus = (int)Range(value, 0, 2, nameof(SheetSetStatus));
        }

        /// <summary>Gets or sets the folder the new sheet set wizard offers for the .dst; empty, the default, for <c>OpenCAD2D Sheet Sets</c> in the user's Documents.</summary>
        public string SheetSetStoreFolder
        {
            get => _sheetSetStoreFolder;
            set => _sheetSetStoreFolder = (value ?? string.Empty).Trim();
        }

        /// <summary>Gives the Sheet Set Manager's variables the values of these settings.</summary>
        /// <param name="options">The options a host shares among its sessions.</param>
        /// <exception cref="System.ArgumentNullException">Thrown when the options are null.</exception>
        public void CopySheetSetOptionsTo(CadSheetSetOptions options)
        {
            if (options == null)
            {
                throw new System.ArgumentNullException(nameof(options));
            }

            options.Locate = SheetSetLocate;
            options.AutoOpen = SheetSetAutoOpen;
            options.PollSeconds = _sheetSetPollSeconds;
            options.SheetStatus = _sheetSetStatus;
            options.StoreFolder = _sheetSetStoreFolder;
        }

        /// <summary>Takes the values of the Sheet Set Manager's variables into these settings, as SETVAR changed them.</summary>
        /// <param name="options">The options a host shares among its sessions.</param>
        /// <exception cref="System.ArgumentNullException">Thrown when the options are null.</exception>
        public void TakeSheetSetOptionsFrom(CadSheetSetOptions options)
        {
            if (options == null)
            {
                throw new System.ArgumentNullException(nameof(options));
            }

            SheetSetLocate = options.Locate;
            SheetSetAutoOpen = options.AutoOpen;
            _sheetSetPollSeconds = options.PollSeconds;
            _sheetSetStatus = options.SheetStatus;
            _sheetSetStoreFolder = options.StoreFolderIsSet ? options.StoreFolder : string.Empty;
        }

        private void WriteSheetSets(JsonText w)
        {
            w.Open("sheetSets");
            w.Value("locate", SheetSetLocate);
            w.Value("autoOpen", SheetSetAutoOpen);
            w.Value("pollSeconds", _sheetSetPollSeconds);
            w.Value("sheetStatus", _sheetSetStatus);
            w.Value("storeFolder", _sheetSetStoreFolder);
            w.Close();
        }
    }
}
