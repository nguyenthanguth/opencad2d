using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The frame the sheet set dialogs share: a header with the title and the close cross, a footer with OK, Cancel and Help
    /// (a locked set has one Close and Help), room at the left of the footer for what a dialog adds (Previous and Next, Edit
    /// Custom Properties…), and the dialog's own content between. Its look is the style <c>SheetSetFrame</c> of
    /// <c>Themes/SheetSetDialogs.xaml</c>. It is the content a screenshot draws with no window shown.
    /// </summary>
    public sealed class SheetSetDialogFrame : ContentControl
    {
        /// <summary>The title shown in the header.</summary>
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(SheetSetDialogFrame),
            new PropertyMetadata(string.Empty)
        );

        /// <summary>What stands at the left of the footer.</summary>
        public static readonly DependencyProperty FooterContentProperty = DependencyProperty.Register(
            nameof(FooterContent),
            typeof(object),
            typeof(SheetSetDialogFrame),
            new PropertyMetadata(null)
        );

        /// <summary>The words on the OK button.</summary>
        public static readonly DependencyProperty OkTextProperty = DependencyProperty.Register(
            nameof(OkText),
            typeof(string),
            typeof(SheetSetDialogFrame),
            new PropertyMetadata("OK")
        );

        /// <summary>Whether OK is live.</summary>
        public static readonly DependencyProperty OkEnabledProperty = DependencyProperty.Register(
            nameof(OkEnabled),
            typeof(bool),
            typeof(SheetSetDialogFrame),
            new PropertyMetadata(true)
        );

        /// <summary>Whether the dialog is read-only: the footer shows Close and Help only.</summary>
        public static readonly DependencyProperty IsReadOnlyModeProperty = DependencyProperty.Register(
            nameof(IsReadOnlyMode),
            typeof(bool),
            typeof(SheetSetDialogFrame),
            new PropertyMetadata(false)
        );

        /// <summary>Raised when OK is clicked.</summary>
        public event RoutedEventHandler OkClick;

        /// <summary>Raised when Cancel, Close or the cross is clicked.</summary>
        public event RoutedEventHandler CancelClick;

        /// <summary>Raised when Help is clicked.</summary>
        public event RoutedEventHandler HelpClick;

        /// <summary>Raised when the header is pressed, so that the window can be dragged.</summary>
        public event MouseButtonEventHandler HeaderPressed;

        /// <summary>Gets or sets the title shown in the header.</summary>
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>Gets or sets what stands at the left of the footer.</summary>
        public object FooterContent
        {
            get => GetValue(FooterContentProperty);
            set => SetValue(FooterContentProperty, value);
        }

        /// <summary>Gets or sets the words on the OK button ("OK", or "Import Checked").</summary>
        public string OkText
        {
            get => (string)GetValue(OkTextProperty);
            set => SetValue(OkTextProperty, value);
        }

        /// <summary>Gets or sets whether OK is live.</summary>
        public bool OkEnabled
        {
            get => (bool)GetValue(OkEnabledProperty);
            set => SetValue(OkEnabledProperty, value);
        }

        /// <summary>Gets or sets whether the footer shows Close and Help only, as for a set that is locked.</summary>
        public bool IsReadOnlyMode
        {
            get => (bool)GetValue(IsReadOnlyModeProperty);
            set => SetValue(IsReadOnlyModeProperty, value);
        }

        /// <inheritdoc />
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            Hook("PART_Ok", (s, e) => OkClick?.Invoke(this, e));
            Hook("PART_Cancel", (s, e) => CancelClick?.Invoke(this, e));
            Hook("PART_Close", (s, e) => CancelClick?.Invoke(this, e));
            Hook("PART_Cross", (s, e) => CancelClick?.Invoke(this, e));
            Hook("PART_Help", (s, e) => HelpClick?.Invoke(this, e));
            if (GetTemplateChild("PART_Header") is UIElement header)
            {
                header.MouseLeftButtonDown += (s, e) => HeaderPressed?.Invoke(this, e);
            }
        }

        private void Hook(string part, RoutedEventHandler handler)
        {
            if (GetTemplateChild(part) is Button button)
            {
                button.Click += handler;
            }
        }
    }
}
