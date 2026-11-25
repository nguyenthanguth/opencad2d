using System.Windows;
using System.Windows.Media;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// A colour icon of the ribbon: two drawings of one picture, the large one for a button 32 pixels wide and the small one
    /// for a button 16 pixels wide, as the icons of AutoCAD's ribbon come in two sizes. An <see cref="IconView"/> draws the
    /// one that suits the size it is shown at, so that the lines stay whole pixels at 100 % and at 150 %.
    /// </summary>
    /// <remarks>
    /// Both drawings are on a 32 by 32 grid in the strokes and colours of the picture; the small one holds its own scale
    /// of 0.5, so that its bounds lie in 0 to 16 and its 2 unit lines are 1 pixel wide at 16 pixels. The icons the ribbon
    /// uses are in <c>Themes/RibbonIcons.xaml</c>, one per command, keyed as the monochrome icon of the command with
    /// <c>.Color</c> after it (<c>GhIconLine.Color</c>), frozen when loaded.
    /// </remarks>
    public sealed class RibbonIcon : Freezable
    {
        /// <summary>Identifies the <see cref="Large"/> property.</summary>
        public static readonly DependencyProperty LargeProperty = DependencyProperty.Register(
            nameof(Large),
            typeof(Drawing),
            typeof(RibbonIcon),
            new PropertyMetadata(null)
        );

        /// <summary>Identifies the <see cref="Small"/> property.</summary>
        public static readonly DependencyProperty SmallProperty = DependencyProperty.Register(
            nameof(Small),
            typeof(Drawing),
            typeof(RibbonIcon),
            new PropertyMetadata(null)
        );

        /// <summary>Gets or sets the drawing shown at 24 pixels and more, on a 32 by 32 grid with its lines 2 units thick.</summary>
        public Drawing Large
        {
            get => (Drawing)GetValue(LargeProperty);
            set => SetValue(LargeProperty, value);
        }

        /// <summary>
        /// Gets or sets the drawing shown under 24 pixels, on a 16 by 16 grid (the 32 grid at a scale of 0.5, its lines 1
        /// unit thick); where it is null the large drawing is shown scaled instead.
        /// </summary>
        public Drawing Small
        {
            get => (Drawing)GetValue(SmallProperty);
            set => SetValue(SmallProperty, value);
        }

        /// <inheritdoc/>
        protected override Freezable CreateInstanceCore() => new RibbonIcon();
    }
}
