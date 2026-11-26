using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The one colour of a problem, read from the XAML: GhProblem is #FF8A7A (6.2 to 1 on the dialog panel, 5.5 to 1 on the
    /// footer), nothing writes a problem colour by hand, and the red the dialogs used before (GhError, #F85149) is gone.
    /// </summary>
    public sealed class ProblemColourTests
    {
        private static readonly Regex Hand = new Regex(
            "#(?:[0-9A-Fa-f]{2})?(?:FF8A7A|F26B3A|F85149)(?![0-9A-Fa-f])",
            RegexOptions.IgnoreCase
        );

        [Fact]
        public void NoXamlWritesAProblemColourByHand()
        {
            var faults = new List<string>();

            foreach (string file in DialogXaml.AllXaml())
            {
                foreach (XElement element in XDocument.Load(file).Root.DescendantsAndSelf())
                {
                    string key = (string)
                        element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"));
                    bool named =
                        Path.GetFileName(file) == "Dark.xaml" && (key == "GhProblemColor" || key == "GhCloseCross");

                    // A comment is not read: Dark.xaml explains the colours it keeps.
                    IEnumerable<string> values = element
                        .Attributes()
                        .Select(a => a.Value)
                        .Concat(element.Nodes().OfType<XText>().Select(t => t.Value));

                    if (!named && values.Any(v => Hand.IsMatch(v)))
                    {
                        faults.Add(Path.GetFileName(file) + ": " + element.Name.LocalName + " " + key);
                    }
                }
            }

            Assert.True(
                faults.Count == 0,
                faults.Count + " problem colours written by hand:\n" + string.Join("\n", faults)
            );
        }

        [Fact]
        public void NoXamlNamesTheOldErrorBrush_AndGhProblemIsTheSalmonOfDark()
        {
            var faults = new List<string>();

            foreach (string file in DialogXaml.AllXaml())
            {
                int uses = Regex.Matches(File.ReadAllText(file), @"\bGhError(Color)?\b").Count;

                if (uses > 0)
                {
                    faults.Add(Path.GetFileName(file) + " x" + uses);
                }
            }

            Assert.True(faults.Count == 0, faults.Count + " files name GhError:\n" + string.Join("\n", faults));

            string dark = Path.Combine(DialogXaml.SourceFolder(), "Themes", "Dark.xaml");
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            XElement colour = XDocument
                .Load(dark)
                .Descendants()
                .Single(e => (string)e.Attribute(x + "Key") == "GhProblemColor");
            Assert.Equal("#FF8A7A", colour.Value.Trim(), ignoreCase: true);
            Assert.Contains(XDocument.Load(dark).Descendants(), e => (string)e.Attribute(x + "Key") == "GhProblem");
        }
    }
}
