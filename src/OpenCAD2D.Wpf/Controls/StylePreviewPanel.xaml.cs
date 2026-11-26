using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The preview of the style dialogs as they lay it out: a title row with the toolbar (Fit, the zoom in percent of Fit, the black
    /// and white grounds) over the framed <see cref="StylePreviewView"/> that fills the rest, so that the toolbar never covers the
    /// drawing.
    /// </summary>
    public partial class StylePreviewPanel : UserControl
    {
        /// <summary>The title over the preview: "Preview of ISO-25".</summary>
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(StylePreviewPanel),
            new PropertyMetadata(string.Empty, (d, e) => ((StylePreviewPanel)d).TitleText.Text = (string)e.NewValue)
        );

        /// <summary>The view model drawn.</summary>
        public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
            nameof(Model),
            typeof(StylePreviewViewModel),
            typeof(StylePreviewPanel),
            new PropertyMetadata(null, (d, e) =>
                ((StylePreviewPanel)d).OnModelChanged((StylePreviewViewModel)e.NewValue)
            )
        );

        private StylePreviewViewModel _model;

        /// <summary>Makes the panel.</summary>
        public StylePreviewPanel()
        {
            InitializeComponent();
            FitButton.Click += (s, e) => _model?.FitCommand.Execute(null);
            BlackGround.Click += (s, e) => StylePreviewViewModel.Ground = StylePreviewGround.Black;
            WhiteGround.Click += (s, e) => StylePreviewViewModel.Ground = StylePreviewGround.White;
            Loaded += (s, e) =>
            {
                StylePreviewViewModel.GroundChanged += OnGroundChanged;
                ShowGround();
            };
            Unloaded += (s, e) => StylePreviewViewModel.GroundChanged -= OnGroundChanged;
            ShowGround();
            ShowZoom();
        }

        /// <summary>Gets or sets the title over the preview.</summary>
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>Gets or sets the view model drawn.</summary>
        public StylePreviewViewModel Model
        {
            get => (StylePreviewViewModel)GetValue(ModelProperty);
            set => SetValue(ModelProperty, value);
        }

        /// <summary>Gets the view that draws the sample.</summary>
        public StylePreviewView PreviewView => View;

        private void OnModelChanged(StylePreviewViewModel model)
        {
            if (_model != null)
            {
                _model.PropertyChanged -= OnModelPropertyChanged;
            }

            _model = model;
            View.Model = model;

            if (_model != null)
            {
                _model.PropertyChanged += OnModelPropertyChanged;
            }

            ShowZoom();
        }

        private void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StylePreviewViewModel.ZoomPercent))
            {
                ShowZoom();
            }
        }

        private void OnGroundChanged(object sender, EventArgs e)
        {
            ShowGround();
            View.Redraw();
        }

        private void ShowZoom() =>
            ZoomText.Text = (_model?.ZoomPercent ?? 100.0).ToString("0", CultureInfo.InvariantCulture) + " %";

        private void ShowGround()
        {
            BlackGround.IsChecked = StylePreviewViewModel.Ground == StylePreviewGround.Black;
            WhiteGround.IsChecked = StylePreviewViewModel.Ground == StylePreviewGround.White;
        }
    }
}
