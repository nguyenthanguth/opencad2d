using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using GeometryHelper.Dxf;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The swatch of a colour written as the Color rows of Properties write it: ByLayer, ByBlock, Red to White, an index 1 to 255
    /// or 255,128,0. It is the colour of the text; ByLayer is the colour of the layer the objects are on, as AutoCAD 2021 draws
    /// it, and ByBlock is white; the colour 7, White, is a square split black and white along its diagonal, as AutoCAD draws
    /// it, since it shows black on a white sheet. *Varies*, Select Color... and text that is no colour have none.
    /// </summary>
    internal static class ColorTextSwatch
    {
        private static readonly Brush Split = MakeSplit();

        /// <summary>The colour of a text as 0xRRGGBB; null for text that is no colour.</summary>
        /// <param name="text">The text.</param>
        /// <param name="byLayerRgb">The colour of the layer, which ByLayer is drawn in.</param>
        internal static int? Rgb(string text, int byLayerRgb)
        {
            if (!SelectColorViewModel.TryParse(text, out DxfColor color))
            {
                return null;
            }

            if (color.IsByLayer)
            {
                return byLayerRgb;
            }

            return color.IsByBlock ? 0xFFFFFF : color.ColorValue & 0xFFFFFF;
        }

        /// <summary>Whether a text is the colour 7, White, drawn split black and white.</summary>
        internal static bool IsWhite(string text) =>
            SelectColorViewModel.TryParse(text, out DxfColor color)
            && !color.IsByLayer
            && !color.IsByBlock
            && !color.IsByColor
            && color.ColorIndex == 7;

        /// <summary>The brush of a text: a frozen solid brush, the split brush for White, null for none.</summary>
        internal static Brush Of(string text, int byLayerRgb)
        {
            if (IsWhite(text))
            {
                return Split;
            }

            int? rgb = Rgb(text, byLayerRgb);

            if (rgb == null)
            {
                return null;
            }

            var brush = new SolidColorBrush(
                Color.FromRgb((byte)(rgb.Value >> 16), (byte)(rgb.Value >> 8), (byte)rgb.Value)
            );
            brush.Freeze();
            return brush;
        }

        private static Brush MakeSplit()
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0.0, 0.0), EndPoint = new Point(1.0, 1.0) };
            brush.GradientStops.Add(new GradientStop(Colors.White, 0.0));
            brush.GradientStops.Add(new GradientStop(Colors.White, 0.5));
            brush.GradientStops.Add(new GradientStop(Colors.Black, 0.5));
            brush.GradientStops.Add(new GradientStop(Colors.Black, 1.0));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// Turns a colour's text and the row it is in into its swatch (<see cref="ColorTextSwatch"/>): the first value is the text,
    /// the second the <see cref="PropertyRowViewModel"/> whose layer ByLayer is drawn in (the third, where there is one, is that
    /// colour as a number, which makes the swatch drawn again where the layer of the objects changes, the text staying
    /// ByLayer); the brush is null where there is no swatch, and for a row that is no colour row.
    /// With the parameter "Visibility" it gives whether there is one, Visible or Collapsed, instead of the brush.
    /// </summary>
    internal sealed class ColorTextSwatchConverter : IMultiValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string text = values.Length > 0 ? values[0] as string : null;
            var row = values.Length > 1 ? values[1] as PropertyRowViewModel : null;
            int byLayer = values.Length > 2 && values[2] is int rgb ? rgb : row?.ByLayerRgb ?? 0xFFFFFF;
            Brush brush = row != null && !row.IsColor ? null : ColorTextSwatch.Of(text, byLayer);

            return string.Equals(parameter as string, "Visibility", StringComparison.Ordinal)
                ? (object)(brush == null ? Visibility.Collapsed : Visibility.Visible)
                : brush;
        }

        /// <inheritdoc/>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
