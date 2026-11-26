using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The preview of a dimension or multileader style, drawn at the real size and the real scale of the display: the sample of a
    /// <see cref="StylePreviewViewModel"/> rendered by the canvas's renderer into a picture of as many device pixels as the frame
    /// has (shown one pixel to a pixel, so lines stay sharp at 150 %), the lit parts in the accent and the rest faded, with the
    /// callout of the distance a field sets and a magnifier for a small one drawn over it as vectors. The wheel zooms about the
    /// pointer, a double click shows the whole sample (Fit), the part under the pointer lights with a tooltip and a click on it goes
    /// to the field that sets it.
    /// </summary>
    public sealed class StylePreviewView : FrameworkElement
    {
        /// <summary>The view model drawn.</summary>
        public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
            nameof(Model),
            typeof(StylePreviewViewModel),
            typeof(StylePreviewView),
            new FrameworkPropertyMetadata(null, (d, e) => ((StylePreviewView)d).OnModelChanged(e))
        );

        /// <summary>How far, in device-independent pixels, a part may lie from the pointer to be the one hovered.</summary>
        private const double HoverReach = 6.0;

        /// <summary>The factor one notch of the wheel zooms by.</summary>
        private const double WheelFactor = 1.2;

        /// <summary>The length on screen, in device-independent pixels, below which a callout gets a magnifier.</summary>
        private const double SmallCallout = 16.0;

        private CadView _view;
        private CadStyleSample _shown;
        private DisplayBox _fitBounds = DisplayBox.Empty;
        private bool _zoomed;
        private double _centreX;
        private double _centreY;
        private double _ppuDip = 1.0;
        private double _fitPpuDip = 1.0;
        private BitmapSource _picture;
        private BitmapSource _inset;
        private CadView _insetView;
        private Rect _insetFrame;
        private double _insetZoom;
        private CadStyleMeasure _magnified;
        private Point _pointer;
        private bool _pointerIn;
        private double _drawnDpi;
        private bool _redrawPending;
        private StylePreviewViewModel _subscribed;

        /// <summary>Makes a preview.</summary>
        public StylePreviewView()
        {
            ClipToBounds = true;
            SnapsToDevicePixels = true;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        }

        /// <summary>Gets or sets the view model drawn.</summary>
        public StylePreviewViewModel Model
        {
            get => (StylePreviewViewModel)GetValue(ModelProperty);
            set => SetValue(ModelProperty, value);
        }

        /// <summary>Gets the picture of the sample as last drawn, as many device pixels as the frame has; null before the first draw.</summary>
        internal BitmapSource Picture => _picture;

        /// <summary>Gets the view of the sample as last drawn, in device pixels; null before the first draw.</summary>
        internal CadView View => _view;

        /// <summary>Gets the display's scale this preview is drawn at: 1 at 96 dots an inch, 1.5 at 150 %.</summary>
        internal double Dpi => DpiOverride ?? VisualTreeHelper.GetDpi(this).DpiScaleX;

        /// <summary>Gets or sets a scale to draw at instead of the display's, for a preview that is drawn with no window to give it one (a test, a screenshot); null for the display's.</summary>
        internal double? DpiOverride { get; set; }

        /// <summary>Gets the callout the magnifier shows, if one is drawn.</summary>
        internal CadStyleMeasure Magnified => _magnified;

        /// <summary>Gets the rectangle of the magnifier's inset, in device-independent pixels; empty where none is drawn.</summary>
        internal Rect InsetFrame => _magnified == null ? Rect.Empty : _insetFrame;

        /// <summary>Gets how many times the magnifier magnifies; 0 where none is drawn.</summary>
        internal double InsetZoom => _magnified == null ? 0.0 : _insetZoom;

        /// <inheritdoc/>
        protected override Size MeasureOverride(Size availableSize) =>
            new Size(
                double.IsInfinity(availableSize.Width) ? 432.0 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 340.0 : availableSize.Height
            );

        /// <inheritdoc/>
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            Redraw();
        }

        /// <inheritdoc/>
        protected override void OnDpiChanged(DpiScale oldDpiScaleInfo, DpiScale newDpiScaleInfo)
        {
            base.OnDpiChanged(oldDpiScaleInfo, newDpiScaleInfo);
            Redraw();
        }

        private void OnModelChanged(DependencyPropertyChangedEventArgs e)
        {
            if (_subscribed != null)
            {
                _subscribed.PropertyChanged -= OnModelPropertyChanged;
                _subscribed.FitRequested -= OnFitRequested;
            }

            _subscribed = e.NewValue as StylePreviewViewModel;

            if (_subscribed != null)
            {
                _subscribed.PropertyChanged += OnModelPropertyChanged;
                _subscribed.FitRequested += OnFitRequested;
            }

            RequestRedraw();
        }

        private void OnFitRequested(object sender, EventArgs e) => Fit();

        private void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e) => RequestRedraw();

        /// <summary>Draws again soon, once for however many changes come together (a keystroke changes the sample and the parts lit).</summary>
        private void RequestRedraw()
        {
            if (_redrawPending)
            {
                return;
            }

            _redrawPending = true;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(() =>
                {
                    _redrawPending = false;
                    Redraw();
                })
            );
        }

        /// <summary>Shows the whole sample again.</summary>
        internal void Fit()
        {
            _zoomed = false;
            Redraw();
        }

        /// <summary>
        /// Draws the sample into a picture of the frame's size in device pixels, the magnifier's inset if a callout is small, and
        /// asks for the overlay to be drawn over it. A change of the sample keeps the zoom the user chose unless what it draws grows
        /// or shrinks by more than a fifth each way (or is another tab's sample), when the whole sample is shown.
        /// </summary>
        internal void Redraw()
        {
            if (Rebuild())
            {
                InvalidateVisual();
            }
        }

        /// <summary>Makes the picture and the magnifier; false where there is nothing to draw yet (no view model, no size).</summary>
        private bool Rebuild()
        {
            StylePreviewViewModel model = Model;

            if (model == null || ActualWidth < 1.0 || ActualHeight < 1.0)
            {
                return false;
            }

            double dpi = Dpi;
            _drawnDpi = dpi;
            int width = (int)Math.Ceiling(ActualWidth * dpi);
            int height = (int)Math.Ceiling(ActualHeight * dpi);
            CadStyleSample sample = model.Sample;
            CadFonts fonts = model.Fonts;
            DisplayBox bounds = CadStylePreview.BoundsOf(sample, fonts);
            bool refit = !_zoomed || _shown == null || _shown.Kind != sample.Kind || Differs(bounds, _fitBounds);

            if (!ReferenceEquals(_shown, sample))
            {
                _shown = sample;
                _fitBounds = bounds;
            }

            var fit = new CadView(width, height);

            if (!bounds.IsEmpty)
            {
                fit.ZoomTo(bounds, 0.08);
            }

            _fitPpuDip = fit.PixelsPerUnit / dpi;

            if (refit)
            {
                _zoomed = false;
                _centreX = fit.CenterX;
                _centreY = fit.CenterY;
                _ppuDip = _fitPpuDip;
            }

            _view = new CadView(width, height);
            _view.Set(_centreX, _centreY, _ppuDip * dpi);
            model.ZoomPercent = Math.Round(_ppuDip / _fitPpuDip * 100.0);
            IReadOnlyList<string> lit = model.LitHandles;
            _picture = CadStylePreview.Render(
                sample,
                fonts,
                _view,
                StylePreviewViewModel.Ground,
                lit,
                width,
                height,
                dpi,
                model.HoverPart != CadStylePart.None
            );
            DrawMagnifier(model, sample, fonts, lit, dpi);
            return true;
        }

        /// <summary>Whether a box differs from another by more than a fifth in its width or height.</summary>
        private static bool Differs(DisplayBox now, DisplayBox before)
        {
            if (now.IsEmpty || before.IsEmpty)
            {
                return now.IsEmpty != before.IsEmpty;
            }

            return Math.Abs(now.Width - before.Width) > 0.2 * before.Width
                || Math.Abs(now.Height - before.Height) > 0.2 * before.Height;
        }

        /// <summary>Makes the magnifier's inset where the callout of the field active is under 16 device-independent pixels long.</summary>
        private void DrawMagnifier(
            StylePreviewViewModel model,
            CadStyleSample sample,
            CadFonts fonts,
            IReadOnlyList<string> lit,
            double dpi
        )
        {
            _magnified = null;
            _inset = null;

            foreach (CadStyleMeasure measure in model.Measures)
            {
                if (StylePreviewSource.IsGhost(measure.Key) || measure.Angle.HasValue)
                {
                    continue;
                }

                Point a = Dip(_view, measure.From.X, measure.From.Y);
                Point b = Dip(_view, measure.To.X, measure.To.Y);
                double length = (b - a).Length;

                if (length >= SmallCallout)
                {
                    break;
                }

                _insetZoom = length < 6.0 ? 8.0 : 4.0;
                var middle = new Point((measure.From.X + measure.To.X) / 2.0, (measure.From.Y + measure.To.Y) / 2.0);
                Size size = StylePreviewOverlay.InsetSize;
                int w = (int)Math.Ceiling(size.Width * dpi);
                int h = (int)Math.Ceiling(size.Height * dpi);
                _insetView = new CadView(w, h);
                _insetView.Set(middle.X, middle.Y, _ppuDip * dpi * _insetZoom);
                _inset = CadStylePreview.Render(
                    sample,
                    fonts,
                    _insetView,
                    StylePreviewViewModel.Ground,
                    lit,
                    w,
                    h,
                    dpi
                );
                Point centre = new Point((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
                _insetFrame = FreeCorner(sample, fonts, size, centre);
                _magnified = measure;
                break;
            }
        }

        /// <summary>
        /// The corner of the frame the inset goes in: the one where the sample draws least under it (by the boxes of its items), the one
        /// farthest from the callout where two draw as little.
        /// </summary>
        private Rect FreeCorner(CadStyleSample sample, CadFonts fonts, Size size, Point callout)
        {
            const double Margin = 8.0;
            CadDocument document = CadStylePreview.DocumentOf(sample, fonts);
            var boxes = new List<Rect>();

            foreach (DisplayItem2 item in document.GetDisplay(document.ModelSpace).Items)
            {
                Point p = Dip(_view, item.Bounds.MinX, item.Bounds.MaxY);
                Point q = Dip(_view, item.Bounds.MaxX, item.Bounds.MinY);
                boxes.Add(new Rect(p, q));
            }

            Rect best = Rect.Empty;
            double bestCost = double.PositiveInfinity;
            double bestFar = -1.0;

            foreach (bool right in new[] { false, true })
            {
                foreach (bool bottom in new[] { false, true })
                {
                    var frame = new Rect(
                        new Point(
                            right ? ActualWidth - size.Width - Margin : Margin,
                            bottom ? ActualHeight - size.Height - Margin : Margin
                        ),
                        size
                    );
                    double cost = 0.0;

                    foreach (Rect box in boxes)
                    {
                        Rect common = Rect.Intersect(box, frame);
                        cost += common.IsEmpty ? 0.0 : common.Width * common.Height;
                    }

                    double far = (new Point(frame.X + size.Width / 2.0, frame.Y + size.Height / 2.0) - callout).Length;

                    if (cost < bestCost - 1E-6 || (cost <= bestCost + 1E-6 && far > bestFar))
                    {
                        best = frame;
                        bestCost = Math.Min(bestCost, cost);
                        bestFar = far;
                    }
                }
            }

            return best;
        }

        private Point Dip(CadView view, double x, double y)
        {
            view.DrawingToPixel(x, y, out double px, out double py);
            double dpi = Dpi;
            return new Point(px / dpi, py / dpi);
        }

        /// <inheritdoc/>
        protected override void OnRender(DrawingContext dc)
        {
            // Where the display's scale reached the preview after its picture was made (a window shown on another screen, the root
            // scale set late), the picture is made again at the scale it is drawn at, so that no frame is drawn scaled.
            if (_picture != null && Math.Abs(Dpi - _drawnDpi) > 1E-9)
            {
                Rebuild();
            }

            StylePreviewGround ground = StylePreviewViewModel.Ground;
            var within = new Rect(0.0, 0.0, ActualWidth, ActualHeight);
            dc.DrawRectangle(
                new SolidColorBrush(
                    Color.FromRgb(
                        (byte)(CadStylePreview.GroundColour(ground) >> 16),
                        (byte)(CadStylePreview.GroundColour(ground) >> 8),
                        (byte)CadStylePreview.GroundColour(ground)
                    )
                ),
                null,
                within
            );
            StylePreviewViewModel model = Model;

            if (_picture == null || model == null || _view == null)
            {
                return;
            }

            double dip = Dpi;
            dc.DrawImage(_picture, new Rect(0.0, 0.0, _picture.Width, _picture.Height));
            var middle = Dip(
                _view,
                (_fitBounds.MinX + _fitBounds.MaxX) / 2.0,
                (_fitBounds.MinY + _fitBounds.MaxY) / 2.0
            );
            Draw(dc, model, _view, middle, within, dip);

            if (_magnified != null && _inset != null)
            {
                StylePreviewOverlay.Region(
                    dc,
                    Dip(
                        _view,
                        (_magnified.From.X + _magnified.To.X) / 2.0,
                        (_magnified.From.Y + _magnified.To.Y) / 2.0
                    ),
                    StylePreviewOverlay.InsetSize.Height / 2.0 / _insetZoom,
                    ground
                );
                StylePreviewOverlay.Inset(dc, _insetFrame, _inset, "x" + _insetZoom.ToString("0"), ground, dip);
                dc.PushClip(new RectangleGeometry(_insetFrame));
                var origin = new Vector(_insetFrame.X, _insetFrame.Y);
                dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
                var local = new Rect(0.0, 0.0, _insetFrame.Width, _insetFrame.Height);
                Point centre = new Point(local.Width / 2.0, local.Height);
                DrawCallouts(dc, model, _insetView, centre, local, dip, true);
                dc.Pop();
                dc.Pop();
            }

            if (model.Chip.Length > 0)
            {
                var box = StylePreviewOverlay.ChipBox(model.Chip, new Point(0, 0), dip, within, out _);
                Rect beside = model.ChipBesideLit ? LitBox(model) : Rect.Empty;

                if (!beside.IsEmpty)
                {
                    // Above the parts lit, below them where there is no room above.
                    double above = beside.Top - 8.0 - box.Height / 2.0;
                    StylePreviewOverlay.Chip(
                        dc,
                        model.Chip,
                        new Point(
                            beside.Left + beside.Width / 2.0,
                            above - box.Height / 2.0 < 2.0 ? beside.Bottom + 8.0 + box.Height / 2.0 : above
                        ),
                        ground,
                        dip,
                        within
                    );
                }
                else
                {
                    StylePreviewOverlay.Chip(
                        dc,
                        model.Chip,
                        new Point(8.0 + box.Width / 2.0, ActualHeight - 8.0 - box.Height / 2.0),
                        ground,
                        dip,
                        within
                    );
                }
            }

            if (model.HoverPart != CadStylePart.None && _pointerIn)
            {
                StylePreviewOverlay.Tooltip(dc, model.HoverText, _pointer, within, dip);
            }
        }

        private void Draw(
            DrawingContext dc,
            StylePreviewViewModel model,
            CadView view,
            Point away,
            Rect within,
            double dip
        )
        {
            DrawCallouts(dc, model, view, away, within, dip, false);

            if (model.GhostsWordsBox)
            {
                DisplayBox words = WordsBox(model);

                if (!words.IsEmpty)
                {
                    Point p = Dip(view, words.MinX, words.MaxY);
                    Point q = Dip(view, words.MaxX, words.MinY);
                    var box = new Rect(p, q);
                    box.Inflate(2.0, 2.0);
                    StylePreviewOverlay.GhostBox(dc, box, StylePreviewViewModel.Ground);
                }
            }
        }

        /// <summary>Draws the callouts and the ghosts of the field active, over a view of the sample.</summary>
        private void DrawCallouts(
            DrawingContext dc,
            StylePreviewViewModel model,
            CadView view,
            Point away,
            Rect within,
            double dip,
            bool inset
        )
        {
            StylePreviewGround ground = StylePreviewViewModel.Ground;

            foreach (CadStyleMeasure measure in model.Measures)
            {
                Point a = Dip(view, measure.From.X, measure.From.Y);
                Point b = Dip(view, measure.To.X, measure.To.Y);

                if (StylePreviewSource.IsGhost(measure.Key))
                {
                    StylePreviewOverlay.Ghost(dc, a, b, ground);
                }
                else if (measure.Angle.HasValue)
                {
                    if (!inset)
                    {
                        StylePreviewOverlay.ArcCallout(
                            dc,
                            ArcPoints(view, measure),
                            measure.Label,
                            ground,
                            dip,
                            within
                        );
                    }
                }
                else
                {
                    StylePreviewOverlay.Callout(dc, a, b, away, measure.Label, ground, dip, within);
                }
            }
        }

        /// <summary>The points of an angle callout's arc, in device-independent pixels.</summary>
        private IReadOnlyList<Point> ArcPoints(CadView view, CadStyleMeasure measure)
        {
            double dx = measure.To.X - measure.From.X;
            double dy = measure.To.Y - measure.From.Y;
            double radius = Math.Sqrt(dx * dx + dy * dy);
            double start = Math.Atan2(dy, dx);
            double sweep = measure.Angle ?? 0.0;
            const int steps = 24;
            var points = new List<Point>(steps + 1);

            for (int i = 0; i <= steps; i++)
            {
                double t = start + sweep * i / steps;
                points.Add(Dip(view, measure.From.X + radius * Math.Cos(t), measure.From.Y + radius * Math.Sin(t)));
            }

            return points;
        }

        /// <summary>The box of the parts lit, in device-independent pixels; empty where none is lit.</summary>
        private Rect LitBox(StylePreviewViewModel model)
        {
            CadDocument document = CadStylePreview.DocumentOf(model.Sample, model.Fonts);
            var handles = new HashSet<string>(model.LitHandles, StringComparer.Ordinal);
            Rect box = Rect.Empty;

            foreach (DisplayItem2 item in document.GetDisplay(document.ModelSpace).Items)
            {
                if (item.Handle != null && handles.Contains(item.Handle))
                {
                    box.Union(
                        new Rect(
                            Dip(_view, item.Bounds.MinX, item.Bounds.MaxY),
                            Dip(_view, item.Bounds.MaxX, item.Bounds.MinY)
                        )
                    );
                }
            }

            return box;
        }

        /// <summary>The box of the words of the sample, for the ghost of the frame or fill they lack.</summary>
        private DisplayBox WordsBox(StylePreviewViewModel model)
        {
            CadStyleSample sample = model.Sample;
            CadDocument document = CadStylePreview.DocumentOf(sample, model.Fonts);
            var handles = new HashSet<string>(
                sample.Parts.Where(p => p.Part == CadStylePart.Text).SelectMany(p => p.Handles),
                StringComparer.Ordinal
            );
            DisplayBox box = DisplayBox.Empty;

            foreach (DisplayItem2 item in document.GetDisplay(document.ModelSpace).Items)
            {
                if (item.Handle != null && handles.Contains(item.Handle))
                {
                    box = box.Union(item.Bounds);
                }
            }

            return box;
        }

        /// <summary>The part under a point of the frame, as the pointer moved to it finds it; tells the view model.</summary>
        internal CadStylePart HoverAt(Point dip)
        {
            StylePreviewViewModel model = Model;

            if (model == null || _view == null)
            {
                return CadStylePart.None;
            }

            double dpi = Dpi;
            CadStylePart part = CadStylePreview.PartAt(
                model.Sample,
                CadStylePreview.DocumentOf(model.Sample, model.Fonts),
                _view,
                dip.X * dpi,
                dip.Y * dpi,
                HoverReach * dpi
            );
            _pointer = dip;
            _pointerIn = true;
            model.PointerAt(part);
            return part;
        }

        /// <summary>Zooms about a point of the frame by a factor, as a notch of the wheel does.</summary>
        internal void ZoomAtDip(Point dip, double factor)
        {
            if (_view == null)
            {
                return;
            }

            double dpi = Dpi;
            double next = Math.Max(0.25 * _fitPpuDip, Math.Min(40.0 * _fitPpuDip, _ppuDip * factor));
            _view.ZoomAt(dip.X * dpi, dip.Y * dpi, next / _ppuDip);
            _centreX = _view.CenterX;
            _centreY = _view.CenterY;
            _ppuDip = _view.PixelsPerUnit / dpi;
            _zoomed = true;
            Redraw();
        }

        /// <inheritdoc/>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = HoverAt(e.GetPosition(this)) != CadStylePart.None && Model.CanEdit ? Cursors.Hand : Cursors.Arrow;
        }

        /// <inheritdoc/>
        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _pointerIn = false;
            Model?.PointerAt(CadStylePart.None);
        }

        /// <inheritdoc/>
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            ZoomAtDip(e.GetPosition(this), Math.Pow(WheelFactor, e.Delta / 120.0));
            e.Handled = true;
        }

        /// <inheritdoc/>
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (e.ClickCount >= 2)
            {
                Fit();
            }
            else
            {
                Model?.Click();
            }
        }
    }
}
