using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;
using GeometryHelper.Dxf;
using OpenCAD2D.Plugins;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The swatch before the value of a Color row of Properties and Quick Properties (A2P P1), as AutoCAD 2021 draws it: the
    /// colour of the row's text, the colour of the layer for ByLayer, white for ByBlock, White (7) split black and white, none
    /// for *Varies*; the presenter's own Color row and the rows of kind Color that plugins and extensions give alike. The drawing
    /// of the square is the design board's check; here are the numbers it is drawn from. The drawing is two circles on COLUMNS
    /// (red) and a wall on WALLS (white).
    /// </summary>
    public sealed class PropertiesColorSwatchTests : IDisposable
    {
        private readonly PluginWindowKit _kit = new PluginWindowKit();

        public void Dispose() => _kit.Dispose();

        private static PropertyRowViewModel ColorRow(MainViewModel model) =>
            model.PropertyGroups.SelectMany(g => g.Rows).First(r => r.Label == "Color");

        [Fact]
        public void TheColorRowOfARedLine_HasTheSwatchOfRed()
        {
            MainViewModel model = _kit.Opened(out _, out _, out string wall);
            model.Document.Selection.Set(new[] { wall });

            ColorRow(model).Value = "Red";

            PropertyRowViewModel row = ColorRow(model);
            Assert.True(row.IsColor);
            Assert.Equal("Red", row.Value);
            Assert.Equal(0xFF0000, row.SwatchRgb);
        }

        [Fact]
        public void AColorRowOfATrueColour_HasThatColour()
        {
            MainViewModel model = _kit.Opened(out _, out _, out string wall);
            model.Document.Selection.Set(new[] { wall });

            ColorRow(model).Value = "255,128,0";

            PropertyRowViewModel row = ColorRow(model);
            Assert.Equal("255,128,0", row.Value);
            Assert.Equal(0xFF8000, row.SwatchRgb);
            // The true colour is in the list, before Select Color..., which is last.
            Assert.Equal(MainViewModel.SelectColorOption, row.Options[row.Options.Count - 1]);
            Assert.Equal("255,128,0", row.Options[row.Options.Count - 2]);
        }

        [Fact]
        public void ByLayerIsTheColourOfTheLayer_ByBlockIsWhite_AndVariesHasNone()
        {
            MainViewModel model = _kit.Opened(out string first, out _, out string wall);

            // A circle on COLUMNS (red) with its colour ByLayer: red. The wall on WALLS (white): white.
            model.Document.Selection.Set(new[] { first });
            Assert.Equal("ByLayer", ColorRow(model).Value);
            Assert.Equal(0xFF0000, ColorRow(model).SwatchRgb);
            model.Document.Selection.Set(new[] { wall });
            Assert.Equal(0xFFFFFF, ColorRow(model).SwatchRgb);

            ColorRow(model).Value = "ByBlock";
            Assert.Equal("ByBlock", ColorRow(model).Value);
            Assert.Equal(0xFFFFFF, ColorRow(model).SwatchRgb);

            // Two objects of two colours: *Varies*, no swatch.
            model.Document.Selection.Set(new[] { first, wall });
            Assert.Equal(CadPropertyRow.VariesText, ColorRow(model).Value);
            Assert.Null(ColorRow(model).SwatchRgb);
        }

        [Fact]
        public void NothingSelected_ByLayerIsTheColourOfTheCurrentLayer()
        {
            MainViewModel model = _kit.Opened(out _, out _, out _);
            model.Document.Selection.Clear();

            PropertyRowViewModel row = ColorRow(model);

            Assert.Equal("ByLayer", row.Value);
            Assert.Equal(0xFFFFFF, row.SwatchRgb);
        }

        [Fact]
        public void APluginColorRow_HasTheSwatch_AndAReadOnlyOneToo()
        {
            MainViewModel model = _kit.Opened(out string first, out _, out _);
            _kit.LoadHook(model, host => host.AddProperties(request =>
                {
                    var rows = new List<CadPropertyRow>
                    {
                        CadPropertyRow.Color("Mark color", DxfColor.FromColorIndex(DxfColorMethod.ByAci, 3), c => null),
                        CadPropertyRow.Color("Fixed color", DxfColor.FromColorIndex(DxfColorMethod.ByAci, 3), null),
                    };
                    var groups = new List<CadPropertyGroup>(request.Shown.Groups)
                    {
                        new CadPropertyGroup("Hook", rows),
                    };
                    return new CadSelectionInfo(request.Shown.Label, groups);
                }));

            model.Document.Selection.Set(new[] { first });

            IReadOnlyList<PropertyRowViewModel> rows = model.PropertyGroups.Last().Rows;
            PropertyRowViewModel editable = rows.Single(r => r.Label == "Mark color");
            PropertyRowViewModel fixedRow = rows.Single(r => r.Label == "Fixed color");
            Assert.True(editable.IsColor);
            Assert.False(editable.IsReadOnly);
            Assert.Equal(0x00FF00, editable.SwatchRgb);
            Assert.True(fixedRow.IsColor);
            Assert.True(fixedRow.IsReadOnly);
            Assert.Equal(0x00FF00, fixedRow.SwatchRgb);
        }

        [Fact]
        public void ALayerRowAndATextRow_AreNoColorRows()
        {
            MainViewModel model = _kit.Opened(out string first, out _, out _);
            model.Document.Selection.Set(new[] { first });

            PropertyRowViewModel layer = model.PropertyGroups.SelectMany(g => g.Rows).First(r => r.Label == "Layer");
            PropertyRowViewModel radius = model.PropertyGroups.SelectMany(g => g.Rows).First(r => r.Label == "Radius");

            Assert.False(layer.IsColor);
            Assert.Null(layer.SwatchRgb);
            Assert.False(radius.IsColor);
            Assert.Null(radius.SwatchRgb);
        }

        [Fact]
        public void TheSwatchFollowsTheValue_WhenTheColourIsChangedInTheRow()
        {
            MainViewModel model = _kit.Opened(out _, out _, out string wall);
            model.Document.Selection.Set(new[] { wall });
            PropertyRowViewModel row = ColorRow(model);
            Assert.Equal(0xFFFFFF, row.SwatchRgb);
            var raised = new List<string>();
            row.PropertyChanged += (sender, e) => raised.Add(e.PropertyName);

            row.Value = "Blue";

            Assert.Equal(0x0000FF, row.SwatchRgb);
            Assert.Contains(nameof(PropertyRowViewModel.SwatchRgb), raised);
        }

        [Fact]
        public void TheSwatchOfWhiteIsSplitBlackAndWhite_AndOfAnyOtherColourIsSolid()
        {
            Brush white = ColorTextSwatch.Of("White", 0xFF0000);
            Brush seven = ColorTextSwatch.Of("7", 0xFF0000);
            Brush red = ColorTextSwatch.Of("Red", 0x00FF00);
            Brush byLayer = ColorTextSwatch.Of("ByLayer", 0x00FF00);

            Assert.IsType<LinearGradientBrush>(white);
            Assert.IsType<LinearGradientBrush>(seven);
            Assert.Equal(Color.FromRgb(0xFF, 0, 0), Assert.IsType<SolidColorBrush>(red).Color);
            Assert.Equal(Color.FromRgb(0, 0xFF, 0), Assert.IsType<SolidColorBrush>(byLayer).Color);
            Assert.Null(ColorTextSwatch.Of(CadPropertyRow.VariesText, 0xFFFFFF));
            Assert.Null(ColorTextSwatch.Of(MainViewModel.SelectColorOption, 0xFFFFFF));
            Assert.Null(ColorTextSwatch.Of("not a colour", 0xFFFFFF));
        }
    }
}
