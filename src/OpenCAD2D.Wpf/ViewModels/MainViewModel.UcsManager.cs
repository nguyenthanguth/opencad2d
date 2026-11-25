using System;

namespace OpenCAD2D.Wpf
{
    /// <summary>The UCS dialog of UCSMAN (UC): shown through a function the workspace sets, so that a test drives it with none.</summary>
    public sealed partial class MainViewModel
    {
        /// <summary>Gets or sets how the workspace shows the UCS dialog; the action it closed with is in its request.</summary>
        public Func<UcsManagerViewModel, bool> ShowUcsManager { get; set; }

        /// <summary>A request of UCSMAN: its dialog shown where the workspace shows it.</summary>
        private void OnUcsManagerRequested(CadUiRequestEventArgs e)
        {
            if (e.Payload is CadUcsDialogRequest request && ShowUcsManager != null)
            {
                e.Handled = ShowUcsManager(new UcsManagerViewModel(request));
            }
        }
    }
}
