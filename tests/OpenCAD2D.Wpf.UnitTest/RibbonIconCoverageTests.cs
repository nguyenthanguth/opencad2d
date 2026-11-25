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
    /// Every command of the ribbon has a colour icon (A13), read from the XAML sources the way tools/inventory.py of the design
    /// did: each button of an icon style, each split choice and each panel carries a key "&lt;old key&gt;.Color" that
    /// Themes/RibbonIcons.xaml defines, and every icon of that file is used. The tests read the source files, found by walking
    /// up from the test assembly to the folder that holds src.
    /// </summary>
    public sealed class RibbonIconCoverageTests
    {
        private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        private static readonly Regex Resource = new Regex(@"^\{(?:Static|Dynamic)Resource\s+([A-Za-z0-9_.]+)\}$");

        // The workspace and the six contextual ribbons: the files whose buttons are the ribbon's.
        private static readonly string[] Files =
        {
            "CadWorkspace.xaml",
            "ArrayRibbon.xaml",
            "BlockEditorRibbon.xaml",
            "HatchRibbon.xaml",
            "ImageRibbon.xaml",
            "PdfRibbon.xaml",
            "TableCellRibbon.xaml",
        };

        // The buttons of the Text Editor that stay text, as AutoCAD's text editor draws them: B, I, S, U, O and the three menus.
        private static readonly string[] TextButtons =
        {
            "B",
            "I",
            "Strikethrough",
            "Underline  Ctrl+U",
            "Overline",
            "aA ▾",
            "Clear ▾",
            "Background mask…",
        };

        [Fact]
        public void EveryButtonOfAnIconStyle_CarriesAColourIconTheDictionaryHas()
        {
            HashSet<string> known = Known();
            var problems = new List<string>();
            int buttons = 0;

            foreach (string file in Files)
            {
                XDocument document = Load(file);
                HashSet<string> iconStyles = IconStyles(document);

                foreach (
                    XElement button in document
                        .Descendants()
                        .Where(e => IsButton(e) && iconStyles.Contains(StyleKey(e) ?? "") && !HasOwnContent(e))
                )
                {
                    buttons++;
                    string tag = (string)button.Attribute("Tag");
                    string where = Where(file, button) + " " + Label(button);

                    if (tag == null)
                    {
                        problems.Add(where + ": no Tag");
                    }
                    else
                    {
                        Check(tag, known, where, problems);
                    }
                }
            }

            Assert.Empty(problems);
            Assert.True(buttons > 250, buttons.ToString());
        }

        [Fact]
        public void TheTextButtonsOfTheEditor_AreTheOnlyButtonsOfTheEditorStyles_AndKeepNoIcon()
        {
            XDocument document = Load("CadWorkspace.xaml");
            var texts = document
                .Descendants()
                .Where(e => IsButton(e) && StyleKey(e) is ("FormatToggle" or "EditorMenuButton"))
                .ToList();

            Assert.Equal(
                TextButtons.OrderBy(t => t, StringComparer.Ordinal),
                texts.Select(Label).OrderBy(t => t, StringComparer.Ordinal)
            );
            Assert.All(texts, t => Assert.DoesNotMatch(Resource, (string)t.Attribute("Tag") ?? ""));
        }

        [Fact]
        public void EveryChoiceOfASplitMenu_AndEveryPanel_CarriesAColourIconTheDictionaryHas()
        {
            HashSet<string> known = Known();
            var problems = new List<string>();
            int choices = 0;
            int panels = 0;

            foreach (string file in Files)
            {
                foreach (XElement element in Load(file).Descendants())
                {
                    string where = Where(file, element);

                    if (element.Name.LocalName == "MenuItem" && element.Attribute("Tag") != null)
                    {
                        choices++;
                        Check((string)element.Attribute("Tag"), known, where + " " + Label(element), problems);
                    }
                    else if (element.Name.LocalName == "RibbonPanel")
                    {
                        panels++;
                        string icon = (string)element.Attribute("RibbonIcon");

                        if (icon == null)
                        {
                            problems.Add(where + " " + (string)element.Attribute("Title") + ": no RibbonIcon");
                        }
                        else
                        {
                            Check(icon, known, where + " " + (string)element.Attribute("Title"), problems);
                        }
                    }
                }
            }

            Assert.Empty(problems);
            Assert.True(choices > 100, choices.ToString());
            Assert.True(panels > 40, panels.ToString());
        }

        [Fact]
        public void NoIconOfTheRibbon_StaysAMonochromeGeometry_AndEveryColourIconIsUsed()
        {
            HashSet<string> known = Known();
            var used = new HashSet<string>(StringComparer.Ordinal);
            var problems = new List<string>();

            foreach (string file in Files)
            {
                foreach (XElement element in Load(file).Descendants())
                {
                    string name = element.Name.LocalName;

                    foreach (
                        XAttribute attribute in element
                            .Attributes()
                            .Where(a => a.Name.LocalName is "Tag" or "RibbonIcon" or "Icon" or "Data")
                    )
                    {
                        Match match = Resource.Match(attribute.Value);

                        if (!match.Success)
                        {
                            continue;
                        }

                        string key = match.Groups[1].Value;

                        if (key.EndsWith(".Color", StringComparison.Ordinal))
                        {
                            used.Add(key);
                        }
                        else if (
                            name is ("Button" or "ToggleButton" or "MenuItem" or "RibbonPanel")
                            && attribute.Name.LocalName is ("Tag" or "Icon")
                            && Geometries(file).Contains(key)
                        )
                        {
                            problems.Add(Where(file, element) + " " + attribute.Name.LocalName + "=" + key);
                        }
                    }
                }
            }

            Assert.Empty(problems);
            Assert.Empty(known.Except(used).OrderBy(k => k, StringComparer.Ordinal));
            Assert.Empty(used.Except(known).OrderBy(k => k, StringComparer.Ordinal));
        }

        private static void Check(string value, HashSet<string> known, string where, List<string> problems)
        {
            Match match = Resource.Match(value);

            if (!match.Success)
            {
                problems.Add(where + ": " + value + " is not a resource");
            }
            else if (!match.Groups[1].Value.EndsWith(".Color", StringComparison.Ordinal))
            {
                problems.Add(where + ": " + value + " is not a colour icon");
            }
            else if (!known.Contains(match.Groups[1].Value))
            {
                problems.Add(where + ": " + value + " is not in RibbonIcons.xaml");
            }
        }

        // The keys of the RibbonIcons in Themes/RibbonIcons.xaml.
        private static HashSet<string> Known()
        {
            XDocument icons = XDocument.Load(Path.Combine(Source(), "Themes", "RibbonIcons.xaml"));
            return new HashSet<string>(
                icons
                    .Root.Elements()
                    .Where(e => e.Name.LocalName == "RibbonIcon")
                    .Select(e => (string)e.Attribute(Xaml + "Key")),
                StringComparer.Ordinal
            );
        }

        // The keys of the Geometry resources a file can use: its own and those of Themes/Icons.xaml.
        private static HashSet<string> Geometries(string file)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);

            foreach (
                string path in new[]
                {
                    Path.Combine(Source(), "Themes", "Icons.xaml"),
                    Path.Combine(Source(), "Controls", file),
                }
            )
            {
                foreach (
                    XElement geometry in XDocument.Load(path).Descendants().Where(e => e.Name.LocalName == "Geometry")
                )
                {
                    string key = (string)geometry.Attribute(Xaml + "Key");

                    if (key != null)
                    {
                        keys.Add(key);
                    }
                }
            }

            return keys;
        }

        // The keys of the styles whose button draws its Tag as a colour icon, and of those based on them.
        private static HashSet<string> IconStyles(XDocument document)
        {
            var styles = document
                .Descendants()
                .Where(e => e.Name.LocalName == "Style" && e.Attribute(Xaml + "Key") != null)
                .ToList();
            var keys = new HashSet<string>(
                styles
                    .Where(s =>
                        s.Descendants()
                            .Any(i =>
                                i.Name.LocalName == "IconView"
                                && (
                                    ((string)i.Attribute("Icon") ?? "").StartsWith(
                                        "{Binding Tag",
                                        StringComparison.Ordinal
                                    )
                                    || ((string)i.Attribute("Data") ?? "").StartsWith(
                                        "{Binding Tag",
                                        StringComparison.Ordinal
                                    )
                                )
                            )
                    )
                    // The button of a collapsed panel takes the panel's icon (RibbonIcon), which the panels test reads.
                    .Where(s => (string)s.Attribute(Xaml + "Key") != "CollapsedPanelButton")
                    .Select(s => (string)s.Attribute(Xaml + "Key")),
                StringComparer.Ordinal
            );

            bool grew = true;
            while (grew)
            {
                grew = false;

                foreach (XElement style in styles)
                {
                    Match basedOn = Resource.Match((string)style.Attribute("BasedOn") ?? "");

                    if (
                        basedOn.Success
                        && keys.Contains(basedOn.Groups[1].Value)
                        && keys.Add((string)style.Attribute(Xaml + "Key"))
                    )
                    {
                        grew = true;
                    }
                }
            }

            return keys;
        }

        private static string StyleKey(XElement element)
        {
            Match match = Resource.Match((string)element.Attribute("Style") ?? "");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string IconStyleOf(XElement element, HashSet<string> iconStyles)
        {
            string key = StyleKey(element);
            return key != null && iconStyles.Contains(key) ? key : null;
        }

        // A plugin's button draws its own content (ContentTemplate): its icon is the plugin's, not a Tag.
        private static bool HasOwnContent(XElement element) =>
            element.Attribute("ContentTemplate") != null
            || element.Elements().Any(e => e.Name.LocalName.EndsWith(".ContentTemplate", StringComparison.Ordinal));

        private static bool IsButton(XElement element) =>
            element.Name.LocalName is ("Button" or "ToggleButton") && element.Attribute("Style") != null;

        // What names a button: its Content, its automation name, its header, its tooltip, or the first text inside it.
        private static string Label(XElement element)
        {
            foreach (string name in new[] { "Content", "AutomationProperties.Name", "Header", "ToolTip" })
            {
                string value = (string)element.Attribute(name);

                if (!string.IsNullOrEmpty(value) && !value.StartsWith("{", StringComparison.Ordinal))
                {
                    return value.Replace("_", "");
                }
            }

            return element
                .Descendants()
                .Where(e => e.Name.LocalName == "TextBlock" && e.Attribute("Text") != null)
                .Select(e => (string)e.Attribute("Text"))
                .FirstOrDefault(t => !t.StartsWith("{", StringComparison.Ordinal))
                ?? "";
        }

        private static string Where(string file, XElement element) =>
            file
            + ":"
            + (((System.Xml.IXmlLineInfo)element).HasLineInfo() ? ((System.Xml.IXmlLineInfo)element).LineNumber : 0);

        private static XDocument Load(string file) =>
            XDocument.Load(Path.Combine(Source(), "Controls", file), LoadOptions.SetLineInfo);

        // src/OpenCAD2D.Wpf, found by walking up from the test assembly.
        private static string Source()
        {
            string folder = AppDomain.CurrentDomain.BaseDirectory;

            while (folder != null)
            {
                string candidate = Path.Combine(folder, "src", "OpenCAD2D.Wpf");

                if (File.Exists(Path.Combine(candidate, "Controls", "CadWorkspace.xaml")))
                {
                    return candidate;
                }

                folder = Path.GetDirectoryName(folder);
            }

            throw new DirectoryNotFoundException(
                "src\\OpenCAD2D.Wpf was not found above " + AppDomain.CurrentDomain.BaseDirectory
            );
        }
    }
}
