using System;

namespace OpenCAD2D.Wpf
{
    /// <summary>The Create Sheet Set wizard of NEWSHEETSET.</summary>
    public sealed partial class MainViewModel
    {
        /// <summary>
        /// Gets or sets how the workspace shows the Create Sheet Set wizard (NEWSHEETSET) over the window and waits until it is
        /// finished or cancelled. Where none is set the request is not answered and the command asks on the command line.
        /// </summary>
        public Action<NewSheetSetViewModel> ShowNewSheetSet { get; set; }

        /// <summary>
        /// A request of NEWSHEETSET: the wizard shown and waited for; a set it made is shown in the Sheet Set Manager, whose
        /// palette is brought forward. The request is answered (handled) whether the wizard was finished or cancelled.
        /// </summary>
        private void OnNewSheetSetRequested(CadUiRequestEventArgs e)
        {
            if (ShowNewSheetSet == null)
            {
                return;
            }

            using (var wizard = new NewSheetSetViewModel(this))
            {
                ShowNewSheetSet(wizard);

                if (wizard.CreatedFile != null)
                {
                    ShowSheetSetManager?.Invoke(SheetSetManager);
                }
            }

            e.Handled = true;
        }

        /// <summary>The folder the wizard offers for the .dst: the shared option StoreFolder of the Sheet Set Manager's variables, which the settings keep.</summary>
        internal string SheetSetStoreFolder
        {
            get => SheetSetOptions.StoreFolder;
            set => SheetSetOptions.StoreFolder = value;
        }

        /// <summary>Changes the settings in use as the wizard keeps its choices, and raises <see cref="SettingsChanged"/> so that the window keeps them in the settings file.</summary>
        /// <param name="change">The change.</param>
        internal void KeepSettings(Action<GlobalSettings> change)
        {
            change(_settings);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
