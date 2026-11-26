using System.Windows;

namespace OpenCAD2D.Wpf
{
    /// <summary>The Create Sheet Set wizard of NEWSHEETSET in the workspace.</summary>
    public partial class CadWorkspace
    {
        /// <summary>Gives the view model the window of NEWSHEETSET.</summary>
        private void AttachNewSheetSet() =>
            _model.ShowNewSheetSet = wizard => NewSheetSetWindow.Show(Window.GetWindow(this), wizard);
    }
}
