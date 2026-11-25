using System;
using System.Windows;
using System.Windows.Controls;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Sheet Set Properties, Subset Properties and Sheet Properties, the three dialogs that are a property grid: 585 wide, a header
    /// with the title, the grid in AutoCAD's sections, the red line under it where a value is refused, the description of the
    /// row clicked (a sheet has instead its red reason where the layout is not found and the Rename Options), and the footer with
    /// OK, Cancel and Help (a locked set: Close and Help). A set's has Edit Custom Properties… at the left of the footer. The
    /// logic is in <see cref="SheetSetGridViewModel"/>; the ... buttons ask the file, folder, template and layout dialogs of
    /// <see cref="SheetSetDialogs"/> unless the caller has given its own.
    /// </summary>
    public partial class SheetSetPropertiesWindow : SheetSetDialogWindow
    {
        /// <summary>Makes the dialog over its view model.</summary>
        /// <param name="model">The view model of a set, a subset or a sheet.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public SheetSetPropertiesWindow(SheetSetGridViewModel model)
            : base(model)
        {
            InitializeComponent();
            Wire(Frame);
            Width =
                model is SheetSetSubsetPropertiesViewModel ? 520
                : model is SheetSetSheetPropertiesViewModel ? 565
                : 585;
            model.PickTemplate = model.PickTemplate ?? (current => SheetSetDialogs.PickTemplate(this, model, current));
            if (model is SheetSetPropertiesViewModel set)
            {
                set.PickLabelBlock = set.PickLabelBlock ?? (current => SheetSetDialogs.PickBlock(this, set, current));
                set.ShowCustomProperties = set.ShowCustomProperties
                    ?? (file => SheetSetDialogs.ShowCustomProperties(this, file, set.Options));
                var edit = new Button
                {
                    Content = "Edit Custom Properties...",
                    Style = (Style)FindResource("GhDialogButton"),
                    IsEnabled = set.CanEditCustomProperties,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(14, 5, 14, 5),
                };
                edit.Click += (s, e) => set.EditCustomProperties();
                Frame.FooterContent = edit;
            }

            if (model is SheetSetSheetPropertiesViewModel sheet)
            {
                sheet.PickLayout = sheet.PickLayout ?? (current => SheetSetDialogs.PickLayout(this, sheet, current));
            }
        }

        /// <summary>Shows the dialog over a window, which waits until it is closed.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model.</param>
        /// <returns>True when OK applied the values.</returns>
        public static bool Show(Window owner, SheetSetGridViewModel model) =>
            new SheetSetPropertiesWindow(model).Present(owner);
    }
}
