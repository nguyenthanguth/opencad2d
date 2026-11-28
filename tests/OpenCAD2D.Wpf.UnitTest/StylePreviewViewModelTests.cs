using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeometryHelper;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// A14 package P2: the preview of the dimension and multileader style dialogs. The view model that links the field being edited
    /// or hovered to the parts of the sample (lit parts, callouts, chips, the part under the pointer and the fields that set it), the
    /// picture drawn at the device's pixels at 100 % and 150 %, the hit test of the parts, the zoom and Fit, the sizes of the dialogs
    /// and the time of a redraw per keystroke.
    /// </summary>
    [Collection(ViewerSpeedCollection.Name)]
    public sealed class StylePreviewViewModelTests
    {
        private static readonly Tolerance Tol = new Tolerance(1E-9, 1E-9);

        private static void OnSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                throw new Xunit.Sdk.XunitException(failure.ToString());
            }
        }

        private static CadDocument Document() => CadDocument.From(CadTemplate.CreateMetric(), "preview.dxf");

        private static DimStyleEditViewModel Editor(CadDocument document) =>
            new DimStyleEditViewModel(document, DimStyleEditMode.Modify, CadDimStyleEditor.Current(document.Database));

        private static DimFieldViewModel Field(DimStyleEditViewModel editor, string tab, string key) =>
            editor.FieldsOf(tab).First(f => f.PreviewKey == key);

        private static MLeaderStyleEditViewModel MLeaderEditor(CadDocument document) =>
            new MLeaderStyleManagerViewModel(document).CreateModify();

        private static MLeaderFieldViewModel MLeaderField(MLeaderStyleEditViewModel editor, string tab, string key) =>
            editor.FieldsOf(tab).First(f => f.PreviewKey == key);

        private static double Length(CadStyleMeasure measure) => (measure.To - measure.From).Length;

        private static CadStylePart[] Sorted(IEnumerable<CadStylePart> parts) => parts.OrderBy(p => (int)p).ToArray();

        [Fact]
        public void FocusingDimexe_LightsTheTwoExtensionLines_MeasuresItAndHasNoChip()
        {
            CadDocument document = Document();
            DimStyleEditViewModel editor = Editor(document);
            editor.Tab = "Lines";
            DimFieldViewModel field = Field(editor, "Lines", "DIMEXE");

            editor.SetFocused(field, true);

            StylePreviewViewModel preview = editor.PreviewModel;
            Assert.Same(field, editor.ActiveField);
            Assert.True(field.IsActive);
            Assert.Equal(new[] { CadStylePart.ExtensionLine1, CadStylePart.ExtensionLine2 }, Sorted(preview.LitParts));
            CadStyleMeasure measure = Assert.Single(preview.Measures);
            Assert.Equal("DIMEXE", measure.Key);
            Assert.Equal("DIMEXE = 1.25", measure.Label);
            Assert.Equal(1.25, Length(measure), 9);
            Assert.Equal(string.Empty, preview.Chip);
            Assert.Equal(CadStylePart.None, preview.HoverPart);
            Assert.NotEmpty(preview.LitHandles);
        }

        [Fact]
        public void TheFieldUnderThePointer_WinsOverTheOneWithTheFocus_AndLeavingTheFormGivesTheFocusedOneBack()
        {
            DimStyleEditViewModel editor = Editor(Document());
            editor.Tab = "Lines";
            DimFieldViewModel focused = Field(editor, "Lines", "DIMEXE");
            DimFieldViewModel hovered = Field(editor, "Lines", "DIMEXO");
            editor.SetFocused(focused, true);

            editor.SetHovered(hovered, true);

            Assert.Same(hovered, editor.ActiveField);
            Assert.Equal("DIMEXO", editor.PreviewModel.ActiveKey);
            Assert.True(hovered.IsActive);
            Assert.False(focused.IsActive);

            editor.SetHovered(hovered, false);

            Assert.Same(focused, editor.ActiveField);
            Assert.Equal("DIMEXE", editor.PreviewModel.ActiveKey);
            Assert.True(focused.IsActive);
            Assert.False(hovered.IsActive);

            editor.SetFocused(focused, false);

            Assert.Null(editor.ActiveField);
            Assert.Empty(editor.PreviewModel.LitParts);
        }

        [Fact]
        public void ThePointerOnTheSecondArrowhead_LightsItsFieldsOfTheTab_AndDotsTheTabsThatHoldOthers()
        {
            DimStyleEditViewModel editor = Editor(Document());
            editor.Tab = "Lines";

            editor.PreviewModel.PointerAt(CadStylePart.Arrow2);

            string[] lit = editor.Fields.Where(f => f.IsLit).Select(f => f.PreviewKey).ToArray();
            Assert.Equal(new[] { "DIMCLRD", "DIMLWD" }, lit);
            Assert.False(editor.TabChips.Single(t => t.Name == "Lines").HasLitField);
            Assert.True(editor.TabChips.Single(t => t.Name == "Fit").HasLitField); // DIMATFIT moves the arrowheads of the narrow dimension
            Assert.False(editor.TabChips.Single(t => t.Name == "Text").HasLitField);
            Assert.True(editor.TabChips.Single(t => t.Name == "Symbols and arrows").HasLitField);
            string text = editor.PreviewModel.HoverText;
            Assert.StartsWith("Arrowhead 2 · DIMCLRD, DIMLWD, DIMBLK2, DIMASZ", text, StringComparison.Ordinal);
            Assert.EndsWith(" · click to edit", text, StringComparison.Ordinal);
            Assert.Equal(new[] { CadStylePart.Arrow2 }, editor.PreviewModel.LitParts.ToArray());
            Assert.Empty(editor.PreviewModel.Measures);

            // Leaving the part takes the marks away again.
            editor.PreviewModel.PointerAt(CadStylePart.None);
            Assert.DoesNotContain(editor.Fields, f => f.IsLit);
            Assert.DoesNotContain(editor.TabChips, t => t.HasLitField);
            Assert.Equal(string.Empty, editor.PreviewModel.HoverText);
        }

        [Fact]
        public void AClickOnAPart_GoesToTheFirstFieldOfTheTabShown_ElseToTheFirstInTabOrder()
        {
            DimStyleEditViewModel editor = Editor(Document());
            var focusAsked = new List<DimFieldViewModel>();
            editor.FocusRequested += (s, f) => focusAsked.Add(f);

            // The Lines tab lists the arrowhead's colour first.
            editor.Tab = "Lines";
            editor.PreviewModel.PointerAt(CadStylePart.Arrow2);
            editor.PreviewModel.Click();
            Assert.Equal("Lines", editor.Tab);
            Assert.Equal("DIMCLRD", editor.ActiveField.PreviewKey);
            Assert.Equal("DIMCLRD", Assert.Single(focusAsked).PreviewKey);

            // The Text tab has no field of the arrowhead: the first in tab order is on the Lines tab.
            editor.Tab = "Text";
            editor.PreviewModel.PointerAt(CadStylePart.Arrow2);
            editor.PreviewModel.Click();
            Assert.Equal("Lines", editor.Tab);
            Assert.Equal("DIMCLRD", editor.ActiveField.PreviewKey);
            Assert.Equal(2, focusAsked.Count);

            // The Symbols tab has Second.
            editor.Tab = "Symbols and arrows";
            editor.PreviewModel.PointerAt(CadStylePart.Arrow2);
            editor.PreviewModel.Click();
            Assert.Equal("Symbols and arrows", editor.Tab);
            Assert.Equal("DIMBLK2", editor.ActiveField.PreviewKey);
            Assert.Equal(3, focusAsked.Count);
        }

        [Fact]
        public void TheOverallScaleField_LightsNothing_AndSaysItActsOnEveryPart()
        {
            DimStyleEditViewModel editor = Editor(Document());
            editor.Tab = "Fit";

            editor.SetFocused(Field(editor, "Fit", "DIMSCALE"), true);

            Assert.Empty(editor.PreviewModel.LitParts);
            Assert.Empty(editor.PreviewModel.LitHandles);
            Assert.Equal("Every part · DIMSCALE = 1", editor.PreviewModel.Chip);
            Assert.False(editor.PreviewModel.ChipBesideLit);
        }

        [Fact]
        public void APartTheStyleLeavesOut_IsSaidSuppressed_AndItsGhostIsMeasured()
        {
            CadDocument document = Document();
            DimStyleEditViewModel editor = Editor(document);
            editor.Tab = "Lines";
            DimFieldViewModel suppress = Field(editor, "Lines", "DIMSE1");
            suppress.IsChecked = true;

            editor.SetFocused(suppress, true);

            Assert.Equal("Suppressed", editor.PreviewModel.Chip);
            Assert.Empty(editor.PreviewModel.LitParts);
            CadStyleMeasure ghost = Assert.Single(editor.PreviewModel.Measures);
            Assert.Equal("DIMSE1", ghost.Key);
            Assert.True(Length(ghost) > 1.0);
        }

        [Fact]
        public void AFieldOfTheUnitsTabs_ShowsTheValueItSetsAndTheWordsItMakes_InAChip()
        {
            DimStyleEditViewModel editor = Editor(Document());
            editor.Tab = "Primary units";

            editor.SetFocused(Field(editor, "Primary units", "DIMDEC"), true);

            Assert.Equal(new[] { CadStylePart.Text }, editor.PreviewModel.LitParts.ToArray());
            Assert.Equal("DIMDEC = 0.00 · 28,38", editor.PreviewModel.Chip);
            Assert.True(editor.PreviewModel.ChipBesideLit);
        }

        [Fact]
        public void ATypedValue_MakesTheSampleAgain_AndTheMeasureFollowsIt()
        {
            DimStyleEditViewModel editor = Editor(Document());
            editor.Tab = "Lines";
            DimFieldViewModel exe = Field(editor, "Lines", "DIMEXE");
            editor.SetFocused(exe, true);
            CadStyleSample before = editor.PreviewModel.Sample;
            Assert.Equal(1.25, Length(Assert.Single(editor.PreviewModel.Measures)), 9);

            exe.Text = "2";

            Assert.NotSame(before, editor.PreviewModel.Sample);
            CadStyleMeasure now = Assert.Single(editor.PreviewModel.Measures);
            Assert.Equal(2.0, Length(now), 9);
            Assert.Equal("DIMEXE = 2.0", now.Label);

            // Another tab has another sample.
            editor.Tab = "Text";
            Assert.NotSame(before, editor.PreviewModel.Sample);
            Assert.Equal(CadDimSampleKind.Text, CadDimSampleKindOf(editor.PreviewModel));
        }

        private static CadDimSampleKind CadDimSampleKindOf(StylePreviewViewModel preview) => preview.Sample.Kind;

        [Fact]
        public void TheMultileaderLandingGap_LightsTheLandingAndTheWords_AndMeasuresTwo()
        {
            MLeaderStyleEditViewModel editor = MLeaderEditor(Document());
            editor.Tab = "Content";
            MLeaderFieldViewModel gap = MLeaderField(editor, "Content", "LandingGap");

            editor.SetFocused(gap, true);

            Assert.Same(gap, editor.ActiveField);
            Assert.Equal(new[] { CadStylePart.Text, CadStylePart.Landing }, Sorted(editor.PreviewModel.LitParts));
            CadStyleMeasure measure = Assert.Single(editor.PreviewModel.Measures);
            Assert.Equal("LandingGap", measure.Key);
            Assert.Equal("Landing gap = 2", measure.Label);
            Assert.Equal(2.0, Length(measure), 9);
            Assert.Equal(string.Empty, editor.PreviewModel.Chip);
        }

        [Fact]
        public void APartOfTheMultileader_LightsItsFieldsOfTheTab_AndAClickGoesToTheFirstOfThem()
        {
            MLeaderStyleEditViewModel editor = MLeaderEditor(Document());
            editor.Tab = "Leader Structure";

            editor.PreviewModel.PointerAt(CadStylePart.Landing);

            string[] lit = editor.Fields.Where(f => f.IsLit).Select(f => f.PreviewKey).ToArray();
            Assert.Equal(new[] { "SecondSegmentAngle", "IncludeLanding", "LandingDistance" }, lit);
            Assert.True(editor.TabChips.Single(t => t.Name == "Leader Format").HasLitField);
            Assert.True(editor.TabChips.Single(t => t.Name == "Content").HasLitField);
            Assert.False(editor.TabChips.Single(t => t.Name == "Leader Structure").HasLitField);
            Assert.StartsWith(
                "Landing · Second segment angle, Automatically include landing",
                editor.PreviewModel.HoverText
            );

            editor.PreviewModel.Click();

            Assert.Equal("Leader Structure", editor.Tab);
            Assert.Equal("SecondSegmentAngle", editor.ActiveField.PreviewKey);
        }

        [Fact]
        public void AMultileaderStyleThatLeavesTheLandingOut_ShowsItsGhostAndSaysSuppressed()
        {
            MLeaderStyleEditViewModel editor = MLeaderEditor(Document());
            editor.Tab = "Leader Structure";
            MLeaderFieldViewModel include = MLeaderField(editor, "Leader Structure", "IncludeLanding");
            include.LeadChecked = false;

            editor.SetFocused(include, true);

            Assert.Empty(editor.PreviewModel.LitParts);
            Assert.Equal("Suppressed", editor.PreviewModel.Chip);
            Assert.Equal("IncludeLanding", Assert.Single(editor.PreviewModel.Measures).Key);
            Assert.DoesNotContain(editor.PreviewModel.Sample.Parts, p => p.Part == CadStylePart.Landing);
        }

        [Fact]
        public void TheManagersPreview_NamesAPartByATooltipAndHasNoFieldToGoTo()
        {
            CadDocument document = Document();
            var manager = new DimStyleManagerViewModel(document);
            StylePreviewViewModel preview = manager.PreviewModel;
            var asked = new List<string>();
            preview.FieldRequested += (s, k) => asked.Add(k);
            Assert.Equal(CadDimSampleKind.Overview, preview.Sample.Kind);

            preview.PointerAt(CadStylePart.ExtensionLine1);
            preview.Click();

            Assert.StartsWith("Extension line 1 · DIMCLRE", preview.HoverText, StringComparison.Ordinal);
            Assert.DoesNotContain("click to edit", preview.HoverText);
            Assert.Empty(asked);

            var leaders = new MLeaderStyleManagerViewModel(document);
            Assert.Contains(leaders.PreviewModel.Sample.Parts, p => p.Part == CadStylePart.Arrow);
        }

        [Fact]
        public void TheGround_IsKeptForTheSessionAndRaisesItsEvent()
        {
            StylePreviewGround before = StylePreviewViewModel.Ground;
            int raised = 0;
            EventHandler count = (s, e) => raised++;
            StylePreviewViewModel.GroundChanged += count;

            try
            {
                StylePreviewViewModel.Ground = StylePreviewGround.White;
                StylePreviewViewModel.Ground = StylePreviewGround.White;
                Assert.Equal(1, raised);
                Assert.Equal(StylePreviewGround.White, StylePreviewViewModel.Ground);
            }
            finally
            {
                StylePreviewViewModel.GroundChanged -= count;
                StylePreviewViewModel.Ground = before;
            }
        }

        private static CadStyleSample LinesSample(CadDocument document, out SortedDictionary<string, object> values)
        {
            values = CadDimStyleEditor.Read(
                document.Database.DimStyleTable[CadDimStyleEditor.Current(document.Database)]
            );
            return CadDimStyleEditor.Sample(document.Database, values, CadDimSampleKind.Lines);
        }

        private static DisplayItem2 ItemOf(
            CadStyleSample sample,
            CadDocument document,
            CadStylePart part,
            int dimension
        )
        {
            string handle = sample.Parts.First(p => p.Part == part && p.Dimension == dimension).Handles[0];
            CadDocument drawing = CadStylePreview.DocumentOf(sample, document.Fonts);
            return drawing.GetDisplay(drawing.ModelSpace).Items.First(i => i.Handle == handle);
        }

        private static (int B, int G, int R) PixelAt(BitmapSource bitmap, int x, int y)
        {
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            return (pixel[0], pixel[1], pixel[2]);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        public void ThePicture_IsAsManyDevicePixelsAsTheFrameHas_AndItsLinesAreAtLeastOneAndAHalfDips(double dpi)
        {
            CadDocument document = Document();
            CadStyleSample sample = LinesSample(document, out _);
            int width = (int)Math.Ceiling(432 * dpi);
            int height = (int)Math.Ceiling(340 * dpi);
            var view = new CadView(width, height);
            view.ZoomTo(CadStylePreview.BoundsOf(sample, document.Fonts), 0.08);

            BitmapSource picture = CadStylePreview.Render(
                sample,
                document.Fonts,
                view,
                StylePreviewGround.Black,
                null,
                width,
                height,
                dpi
            );

            Assert.Equal(width, picture.PixelWidth);
            Assert.Equal(height, picture.PixelHeight);
            Assert.Equal(96.0 * dpi, picture.DpiX, 6);

            // The first extension line of the first dimension is vertical: across it, the light of one row is its width in pixels.
            DisplayItem2 line = ItemOf(sample, document, CadStylePart.ExtensionLine1, 0);
            view.DrawingToPixel(
                (line.Bounds.MinX + line.Bounds.MaxX) / 2.0,
                (line.Bounds.MinY + line.Bounds.MaxY) / 2.0,
                out double px,
                out double py
            );
            double light = 0.0;

            for (int x = (int)px - 6; x <= (int)px + 6; x++)
            {
                light += PixelAt(picture, x, (int)py).R / 255.0;
            }

            Assert.InRange(light, 1.5 * dpi - 0.15, 1.5 * dpi + 0.6);
        }

        [Fact]
        public void OnAWhiteGround_TheGroundIsWhiteAndALitLineIsTheDarkerAccent()
        {
            CadDocument document = Document();
            CadStyleSample sample = LinesSample(document, out _);
            var view = new CadView(432, 340);
            view.ZoomTo(CadStylePreview.BoundsOf(sample, document.Fonts), 0.08);
            string[] lit = sample
                .Parts.Where(p => p.Part == CadStylePart.ExtensionLine1 || p.Part == CadStylePart.ExtensionLine2)
                .SelectMany(p => p.Handles)
                .ToArray();

            BitmapSource picture = CadStylePreview.Render(
                sample,
                document.Fonts,
                view,
                StylePreviewGround.White,
                lit,
                432,
                340,
                1.0
            );

            Assert.Equal((255, 255, 255), PixelAt(picture, 2, 2));
            DisplayItem2 line = ItemOf(sample, document, CadStylePart.ExtensionLine1, 0);
            view.DrawingToPixel(
                (line.Bounds.MinX + line.Bounds.MaxX) / 2.0,
                (line.Bounds.MinY + line.Bounds.MaxY) / 2.0,
                out double px,
                out double py
            );
            bool found = false;

            for (int x = (int)px - 3; x <= (int)px + 3; x++)
            {
                (int b, int g, int r) = PixelAt(picture, x, (int)py);
                found |= Math.Abs(r - 0x0A) <= 2 && Math.Abs(g - 0x6B) <= 2 && Math.Abs(b - 0xD6) <= 2;
            }

            Assert.True(found, "no pixel of #0A6BD6 across the lit extension line");
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        public void ThePartAtAPoint_IsTheArrowheadOnIt_AndNoneSevenDipsAway(double dpi)
        {
            CadDocument document = Document();
            CadStyleSample sample = LinesSample(document, out _);
            CadDocument drawing = CadStylePreview.DocumentOf(sample, document.Fonts);
            int width = (int)Math.Ceiling(432 * dpi);
            int height = (int)Math.Ceiling(340 * dpi);
            var view = new CadView(width, height);
            view.ZoomTo(CadStylePreview.BoundsOf(sample, document.Fonts), 0.08);
            DisplayItem2 arrow = ItemOf(sample, document, CadStylePart.Arrow2, 0);
            view.DrawingToPixel(
                (arrow.Bounds.MinX + arrow.Bounds.MaxX) / 2.0,
                (arrow.Bounds.MinY + arrow.Bounds.MaxY) / 2.0,
                out double px,
                out double py
            );
            view.DrawingToPixel(
                arrow.Bounds.MaxX,
                (arrow.Bounds.MinY + arrow.Bounds.MaxY) / 2.0,
                out double tipX,
                out _
            );

            Assert.Equal(CadStylePart.Arrow2, CadStylePreview.PartAt(sample, drawing, view, px, py, 6.0 * dpi));
            Assert.Equal(
                CadStylePart.None,
                CadStylePreview.PartAt(sample, drawing, view, tipX + 7.0 * dpi, py, 6.0 * dpi)
            );
            Assert.Equal(CadStylePart.None, CadStylePreview.PartAt(sample, drawing, view, 1.0, 1.0, 6.0 * dpi));
        }

        [Fact]
        public void TheFramesView_ZoomsAboutThePointerAndFitsAgain_KeepingTheZoomThroughAValueChange()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                CadDocument document = Document();
                DimStyleEditViewModel editor = Editor(document);
                editor.Tab = "Lines";
                var view = new StylePreviewView { Model = editor.PreviewModel };
                view.DpiOverride = 1.0;
                view.Measure(new Size(432, 340));
                view.Arrange(new Rect(0, 0, 432, 340));
                view.Redraw();
                Assert.Equal(100.0, editor.PreviewModel.ZoomPercent);
                Assert.Equal(432, view.Picture.PixelWidth);
                Assert.Equal(340, view.Picture.PixelHeight);

                view.View.ScreenToDrawing(100, 100, out double x0, out double y0);
                view.ZoomAtDip(new Point(100, 100), 1.2);

                Assert.Equal(120.0, editor.PreviewModel.ZoomPercent);
                view.View.ScreenToDrawing(100, 100, out double x1, out double y1);
                Assert.Equal(x0, x1, 6);
                Assert.Equal(y0, y1, 6);

                // A value that changes the sample a little keeps the zoom the user chose.
                Field(editor, "Lines", "DIMEXE").Text = "1.5";
                view.Redraw();
                Assert.Equal(120.0, editor.PreviewModel.ZoomPercent);

                editor.PreviewModel.FitCommand.Execute(null);
                Assert.Equal(100.0, editor.PreviewModel.ZoomPercent);

                // Another tab has another sample: shown whole.
                view.ZoomAtDip(new Point(100, 100), 1.2);
                editor.Tab = "Symbols and arrows";
                view.Redraw();
                Assert.Equal(100.0, editor.PreviewModel.ZoomPercent);
            });
        }

        [Fact]
        public void TheFramesView_HoversThePartUnderThePointer_AndDrawsAMagnifierForASmallCallout()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                CadDocument document = Document();
                DimStyleEditViewModel editor = Editor(document);
                editor.Tab = "Lines";
                var view = new StylePreviewView { Model = editor.PreviewModel };
                view.DpiOverride = 1.0;
                view.Measure(new Size(432, 340));
                view.Arrange(new Rect(0, 0, 432, 340));
                view.Redraw();
                DisplayItem2 arrow = ItemOf(editor.PreviewModel.Sample, document, CadStylePart.Arrow2, 0);
                view.View.DrawingToPixel(
                    (arrow.Bounds.MinX + arrow.Bounds.MaxX) / 2.0,
                    (arrow.Bounds.MinY + arrow.Bounds.MaxY) / 2.0,
                    out double px,
                    out double py
                );

                Assert.Equal(CadStylePart.Arrow2, view.HoverAt(new Point(px, py)));
                Assert.Equal(CadStylePart.Arrow2, editor.PreviewModel.HoverPart);
                Assert.Equal(CadStylePart.None, view.HoverAt(new Point(2, 2)));

                // DIMEXE 1.25 is a few dips long at the Fit zoom of the Lines sample: the callout gets a magnifier.
                editor.SetFocused(Field(editor, "Lines", "DIMEXE"), true);
                view.Redraw();
                Assert.NotNull(view.Magnified);
                Assert.Equal("DIMEXE", view.Magnified.Key);
                Assert.Equal(132.0, view.InsetFrame.Width);
                Assert.Equal(100.0, view.InsetFrame.Height);
                Assert.Contains(view.InsetZoom, new[] { 4.0, 8.0 });

                // The offset 0.625 is the same; the arrowhead size on the Symbols tab, the length of the sample, is the same kind of case; a text height is not.
                editor.Tab = "Text";
                editor.SetFocused(Field(editor, "Text", "DIMTXT"), true);
                view.Redraw();
                Assert.Null(view.Magnified);
            });
        }

        [Fact]
        public void APreviewWhoseScaleChangesAfterItsPictureWasMade_DrawsTheNextFrameAtDevicePixels_NotScaledUp()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                DimStyleEditViewModel editor = Editor(Document());
                editor.Tab = "Lines";
                var view = new StylePreviewView { Model = editor.PreviewModel, DpiOverride = 1.0 };
                view.Measure(new Size(432, 340));
                view.Arrange(new Rect(0, 0, 432, 340));
                view.Redraw();
                Assert.Equal(432, view.Picture.PixelWidth);

                // The display's scale reaches the preview late (a window moved to a 150 % screen): the frame drawn is made again.
                view.DpiOverride = 1.5;
                view.InvalidateVisual();
                view.UpdateLayout();
                var target = new RenderTargetBitmap(648, 510, 144, 144, PixelFormats.Pbgra32);
                target.Render(view);

                Assert.Equal(648, view.Picture.PixelWidth);
                Assert.Equal(510, view.Picture.PixelHeight);
                Assert.Equal(144.0, view.Picture.DpiX, 6);
            });
        }

        [Fact]
        public void TheUnitsChip_StandsAboveTheWordsItSets_AndTheCornerChipsStayInTheCorner()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                DimStyleEditViewModel editor = Editor(Document());
                editor.Tab = "Primary units";
                var view = new StylePreviewView { Model = editor.PreviewModel };
                view.DpiOverride = 1.0;
                view.Measure(new Size(432, 340));
                view.Arrange(new Rect(0, 0, 432, 340));
                editor.SetFocused(Field(editor, "Primary units", "DIMDEC"), true);
                view.Redraw();

                var target = new RenderTargetBitmap(432, 340, 96, 96, PixelFormats.Pbgra32);
                target.Render(view);

                Assert.True(editor.PreviewModel.ChipBesideLit);

                // The corner chip (lower left) is not drawn for it: the lower left of the frame is the ground.
                var corner = new byte[4];
                target.CopyPixels(new Int32Rect(12, 340 - 20, 1, 1), corner, 4, 0);
                Assert.Equal((0, 0, 0), (corner[0], corner[1], corner[2]));
            });
        }

        [Fact]
        public void TheDialogs_AreTheSizesOfTheDesign_AndTheirPreviewsFillTheirFrames()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                var model = new MainViewModel();
                CadDocument document = Document();
                model.OpenDocument(document);
                var edit = new DimStyleEditWindow(model.CreateDimStyleManager().CreateModify());
                var change = new MLeaderStyleEditWindow(model.CreateMLeaderStyleManager().CreateModify());
                var dimensions = new DimStyleManagerWindow(model.CreateDimStyleManager());
                var leaders = new MLeaderStyleManagerWindow(model.CreateMLeaderStyleManager());

                foreach (
                    (
                        Window window,
                        FrameworkElement body,
                        double width,
                        double height,
                        double previewWidth,
                        double previewHeight
                    ) in new (Window, FrameworkElement, double, double, double, double)[]
                    {
                        (edit, edit.Body, 960, 660, 432, 340),
                        (change, change.Body, 960, 660, 432, 300),
                        (dimensions, dimensions.Body, 780, 540, 0, 250),
                        (leaders, leaders.Body, 780, 540, 0, 250),
                    }
                )
                {
                    Assert.Equal(width, window.Width);
                    Assert.Equal(height, window.Height);
                    object context = window.DataContext;
                    window.Content = null;
                    body.DataContext = context;
                    body.Measure(new Size(window.Width, window.Height));
                    body.Arrange(new Rect(0, 0, window.Width, window.Height));
                    body.UpdateLayout();
                    StylePreviewView preview = Descendants<StylePreviewView>(body).Single();
                    Assert.True(
                        preview.ActualWidth >= Math.Max(previewWidth, 200) - 3,
                        window.GetType().Name + " preview width " + preview.ActualWidth
                    );
                    Assert.True(
                        preview.ActualHeight >= previewHeight - 3
                            && (previewWidth > 0 || preview.ActualHeight < previewHeight + 2),
                        window.GetType().Name + " preview height " + preview.ActualHeight
                    );
                    window.Close();
                }

                Assert.Equal(ResizeMode.CanResizeWithGrip, edit.ResizeMode);
                Assert.Equal(960, edit.MinWidth);
                Assert.Equal(660, edit.MinHeight);
                Assert.Equal(ResizeMode.CanResizeWithGrip, change.ResizeMode);
            });
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject parent)
            where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                if (child is T found)
                {
                    yield return found;
                }

                foreach (T deeper in Descendants<T>(child))
                {
                    yield return deeper;
                }
            }
        }

        [Fact]
        public void ARowOfAFieldTheFocusIsIn_IsMarkedByTheWindowAndTheClickedFieldIsFocused()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                var model = new MainViewModel();
                model.OpenDocument(Document());
                DimStyleEditViewModel editor = model.CreateDimStyleManager().CreateModify();
                var window = new DimStyleEditWindow(editor);
                object context = window.DataContext;
                FrameworkElement body = window.Body;
                window.Content = null;
                body.DataContext = context;
                body.Measure(new Size(960, 660));
                body.Arrange(new Rect(0, 0, 960, 660));
                body.UpdateLayout();
                DimFieldViewModel exe = Field(editor, "Lines", "DIMEXE");

                // Focus entering the box of the field is told to the dialog by the window.
                TextBox box = Descendants<TextBox>(body).First(t => ReferenceEquals(t.DataContext, exe));
                box.RaiseEvent(
                    new System.Windows.Input.KeyboardFocusChangedEventArgs(
                        System.Windows.Input.Keyboard.PrimaryDevice,
                        0,
                        null,
                        box
                    )
                    {
                        RoutedEvent = System.Windows.Input.Keyboard.GotKeyboardFocusEvent,
                    }
                );

                Assert.Same(exe, editor.ActiveField);
                window.Close();
            });
        }

        [Theory]
        [Trait("Category", "Timing")]
        [InlineData("Lines", "DIMEXE")]
        [InlineData("Symbols and arrows", "DIMASZ")]
        public void ARedrawPerKeystroke_IsTimedAndStaysFast_OnTheOverviewAndTheSymbolsSamples(string tab, string key)
        {
            CadDocument document = Document();
            DimStyleEditViewModel editor = Editor(document);
            editor.Tab = tab;
            DimFieldViewModel field = Field(editor, tab, key);
            editor.SetFocused(field, true);
            StylePreviewViewModel preview = editor.PreviewModel;
            const double dpi = 1.5;
            int width = (int)Math.Ceiling(432 * dpi);
            int height = (int)Math.Ceiling(340 * dpi);
            var view = new CadView(width, height);
            var times = new List<double>();

            for (int i = 0; i < 26; i++)
            {
                var clock = Stopwatch.StartNew();
                field.Text = (2.0 + i * 0.01).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                CadStyleSample sample = preview.Sample;
                view.ZoomTo(CadStylePreview.BoundsOf(sample, document.Fonts), 0.08);
                CadStylePreview.Render(
                    sample,
                    document.Fonts,
                    view,
                    StylePreviewGround.Black,
                    preview.LitHandles,
                    width,
                    height,
                    dpi
                );
                clock.Stop();

                // The first rounds warm the code and the fonts up.
                if (i >= 6)
                {
                    times.Add(clock.Elapsed.TotalMilliseconds);
                }
            }

            times.Sort();
            double median = times[times.Count / 2];
            Console.WriteLine(
                "A14 redraw per keystroke, "
                    + tab
                    + ": median "
                    + median.ToString("0.0")
                    + " ms, max "
                    + times[times.Count - 1].ToString("0.0")
                    + " ms"
            );
            Assert.True(median < 60.0, "median " + median.ToString("0.0") + " ms");
        }
    }
}
