using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The default-folder tables of Folder preference lay out in a page measured off the screen (no window is shown): every kind
    /// ("Hatch patterns", "Drawing templates") is written whole and leaves a gap before its path, and the paths of all the rows
    /// start at one place, as the rows of AutoCAD's Options > Files do.
    /// </summary>
    public sealed class FolderPreferenceLabelTests : IDisposable
    {
        private const double Gap = 8;

        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        private static IEnumerable<T> Descendants<T>(DependencyObject root)
            where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);

            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);

                if (child is T match)
                {
                    yield return match;
                }

                foreach (T deeper in Descendants<T>(child))
                {
                    yield return deeper;
                }
            }
        }

        [Fact]
        public void EveryKindIsWholeAndLeavesAGapBeforeItsPath_AndThePathsStartAtOnePlace()
        {
            Exception failure = null;
            var kinds = new List<string>();
            var gaps = new List<double>();
            var starts = new List<double>();
            var thread = new Thread(() =>
            {
                try
                {
                    var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
                    GlobalSettingsViewModel dialog = model.CreateGlobalSettings("Folder preference");
                    dialog.SupportRoots = new CadSupportFolders(@"D:\OpenCAD2D-run");
                    var window = new GlobalSettingsWindow(dialog);
                    FrameworkElement body = window.Body;
                    window.Content = null;
                    body.DataContext = dialog;
                    body.Measure(new Size(840, 600));
                    body.Arrange(new Rect(0, 0, 840, 600));
                    body.UpdateLayout();

                    foreach (
                        string kind in dialog
                            .DefaultFontFolders.Concat(dialog.DefaultSupportFolders)
                            .Select(r => r.Kind)
                    )
                    {
                        TextBlock label = Descendants<TextBlock>(body).First(t => t.Text == kind);
                        TextBlock path = Descendants<TextBlock>(body)
                            .First(t => t.Parent == label.Parent && t != label);
                        var natural = new FormattedText(
                            kind,
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch),
                            label.FontSize,
                            Brushes.Black,
                            1.0
                        );
                        double labelLeft = label.TransformToAncestor(body).Transform(new Point(0, 0)).X;
                        double pathLeft = path.TransformToAncestor(body).Transform(new Point(0, 0)).X;
                        kinds.Add(kind);
                        gaps.Add(pathLeft - (labelLeft + natural.WidthIncludingTrailingWhitespace));
                        starts.Add(pathLeft);
                    }

                    window.Close();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(failure);
            Assert.Equal(new[] { "Fonts", "Hatch patterns", "Linetypes", "Lisps", "Drawing templates" }, kinds);

            for (int i = 0; i < kinds.Count; i++)
            {
                Assert.True(gaps[i] >= Gap, kinds[i] + ": gap " + gaps[i] + " before the path");
                Assert.True(Math.Abs(starts[i] - starts[0]) < 0.5, kinds[i] + ": path starts at " + starts[i]);
            }
        }
    }
}
