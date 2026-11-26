using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// What the scans of the dialogs' XAML share: where the presenter's source folder is, which files are the chromeless
    /// windows (the ones that draw their own header and footer), the footer of one and its buttons in the order they show
    /// from left to right, and the class of each button (the primary answer, another answer of a question, Cancel or Close,
    /// Apply, Help) that the order of a footer is judged by.
    /// </summary>
    internal static class DialogXaml
    {
        private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        // What a footer is made of when it is not the buttons themselves: these elements are read for their children, the
        // way WPF lays them out. A property element (Border.Child) is read too, except the ones that hold no layout.
        private static readonly string[] NotLayout =
        {
            ".Resources",
            ".Style",
            ".Triggers",
            ".ToolTip",
            ".ContextMenu",
            ".ItemTemplate",
            ".ItemsPanel",
            ".ContentTemplate",
            ".Template",
            ".RenderTransform",
            ".LayoutTransform",
            ".Effect",
            ".Background",
            ".InputBindings",
            ".ColumnDefinitions",
            ".RowDefinitions",
        };

        /// <summary>The ranks of a footer, left to right: the primary answer, the other answers of a question, Cancel or Close, Apply, Help.</summary>
        public const int Primary = 1;

        /// <summary>The rank of the other answers of a question (No, Don't save).</summary>
        public const int Answer = 2;

        /// <summary>The rank of Cancel and Close.</summary>
        public const int Cancel = 3;

        /// <summary>The rank of Apply when it is not the accent.</summary>
        public const int Apply = 4;

        /// <summary>The rank of Help.</summary>
        public const int Help = 5;

        /// <summary>The rank of a button that opens something or acts without closing; the rule does not order it.</summary>
        public const int Unordered = -1;

        // AutoCAD 2021's EATTEDIT and BATTMAN are [Apply] [OK] [Cancel] [Help]: Apply comes before the primary there.
        private static readonly string[] ApplyFirst =
        {
            "AttributeEditorWindow.xaml",
            "BlockAttributeManagerWindow.xaml",
        };

        private static readonly string[] PrimaryLabels =
        {
            "OK",
            "Open",
            "Save",
            "Publish",
            "Continue",
            "Restore",
            "Translate",
            "Find Next",
            "Finish",
            "Yes",
            "Save & Close",
            "Purge Checked Items",
            "{Binding YesLabel}",
            "?YesButton",
        };

        /// <summary>The folder of the presenter's source, src/OpenCAD2D.Wpf.</summary>
        public static string SourceFolder()
        {
            var bin = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            string folder = Path.Combine(bin.Parent.Parent.Parent.Parent.Parent.FullName, "src", "OpenCAD2D.Wpf");
            Assert.True(Directory.Exists(folder), folder);
            return folder;
        }

        /// <summary>Every XAML file of the presenter, in name order, without the build's own folders.</summary>
        public static IEnumerable<string> AllXaml()
        {
            string separator = Path.DirectorySeparatorChar.ToString();
            return Directory
                .GetFiles(SourceFolder(), "*.xaml", SearchOption.AllDirectories)
                .Where(f => !f.Contains(separator + "obj" + separator) && !f.Contains(separator + "bin" + separator))
                .OrderBy(f => f, StringComparer.Ordinal);
        }

        /// <summary>
        /// The windows that draw their own header and footer (WindowStyle="None" under Controls) in name order, and the sheet set
        /// frame, whose footer the sheet set dialogs share.
        /// </summary>
        public static IEnumerable<string> ChromelessFiles()
        {
            string controls = Path.Combine(SourceFolder(), "Controls");
            IEnumerable<string> windows = Directory
                .GetFiles(controls, "*.xaml")
                .Where(f => File.ReadAllText(f).Contains("WindowStyle=\"None\""))
                .OrderBy(f => f, StringComparer.Ordinal);
            return windows.Concat(new[] { Path.Combine(SourceFolder(), "Themes", "SheetSetDialogs.xaml") });
        }

        private static string Attribute(XElement element, string name) =>
            (string)element.Attribute(name) ?? string.Empty;

        private static bool IsButton(XElement element) =>
            (element.Name.LocalName == "Button" || element.Name.LocalName == "ToggleButton")
            && !Attribute(element, "Style").Contains("GhTitleClose");

        /// <summary>A button's words: its Content, else the first Text inside it, else "?" and its x:Name.</summary>
        public static string LabelOf(XElement button)
        {
            string content = (string)button.Attribute("Content");

            if (content == null)
            {
                content = button
                    .DescendantsAndSelf()
                    .Select(e => (string)e.Attribute("Text"))
                    .FirstOrDefault(t => t != null);
            }

            if (content == null)
            {
                content = "?" + (string)button.Attribute(Xaml + "Name");
            }

            return content.Replace("_", string.Empty);
        }

        private static List<XElement> LayoutChildren(XElement element)
        {
            var children = element.Elements().Where(c => !c.Name.LocalName.Contains('.')).ToList();

            foreach (
                XElement property in element
                    .Elements()
                    .Where(c => c.Name.LocalName.Contains('.') && !NotLayout.Any(s => c.Name.LocalName.EndsWith(s)))
            )
            {
                children.AddRange(property.Elements().Where(c => !c.Name.LocalName.Contains('.')));
            }

            return children;
        }

        private static List<XElement> Visual(XElement element)
        {
            if (IsButton(element))
            {
                return new List<XElement> { element };
            }

            List<XElement> children = LayoutChildren(element);
            List<XElement> order;

            if (element.Name.LocalName == "DockPanel")
            {
                bool fills = Attribute(element, "LastChildFill") != "False";
                var left = new List<XElement>();
                var right = new List<XElement>();
                var fill = new List<XElement>();

                for (int i = 0; i < children.Count; i++)
                {
                    if (fills && i == children.Count - 1)
                    {
                        fill.Add(children[i]);
                    }
                    else if (Attribute(children[i], "DockPanel.Dock") == "Right")
                    {
                        right.Add(children[i]);
                    }
                    else
                    {
                        left.Add(children[i]);
                    }
                }

                right.Reverse();
                order = left.Concat(fill).Concat(right).ToList();
            }
            else if (element.Name.LocalName == "Grid")
            {
                order = children
                    .Select(
                        (c, i) =>
                            new
                            {
                                c,
                                i,
                                column = int.TryParse(Attribute(c, "Grid.Column"), out int n) ? n : 0,
                            }
                    )
                    .OrderBy(x => x.column)
                    .ThenBy(x => x.i)
                    .Select(x => x.c)
                    .ToList();
            }
            else if (element.Name.LocalName == "StackPanel" && Attribute(element, "FlowDirection") == "RightToLeft")
            {
                children.Reverse();
                order = children;
            }
            else
            {
                order = children;
            }

            return order.SelectMany(Visual).ToList();
        }

        /// <summary>
        /// The buttons of a window's footer from left to right, or an empty list for a window with none. The footer is the last
        /// element docked at the bottom with the GhHeader ground that holds a button, else the last one docked at the bottom with
        /// OK, Cancel, Close, Help or Apply in it, else the Border around the first button that is the default or the cancel.
        /// </summary>
        public static List<XElement> FooterButtons(string file)
        {
            XElement root = XDocument.Load(file).Root;
            List<XElement> all = root.DescendantsAndSelf().ToList();
            string[] keys = { "OK", "Cancel", "Close", "Help", "Apply" };
            XElement footer = all.LastOrDefault(e =>
                Attribute(e, "DockPanel.Dock") == "Bottom"
                && Attribute(e, "Background").Contains("GhHeader")
                && Visual(e).Count > 0
            );

            if (footer == null)
            {
                footer = all.LastOrDefault(e =>
                    Attribute(e, "DockPanel.Dock") == "Bottom" && Visual(e).Any(b => keys.Contains(LabelOf(b)))
                );
            }

            if (footer == null)
            {
                XElement first = all.FirstOrDefault(e =>
                    e.Name.LocalName == "Button"
                    && (e.Attribute("IsDefault") != null || e.Attribute("IsCancel") != null)
                );
                footer = first?.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Border");
            }

            return footer == null ? new List<XElement>() : Visual(footer);
        }

        /// <summary>
        /// The rank of a button in the order of a footer: <see cref="Primary"/>, <see cref="Answer"/>, <see cref="Cancel"/>,
        /// <see cref="Apply"/>, <see cref="Help"/>, or <see cref="Unordered"/> for a button the rule does not order. Cancel
        /// and Close are named by their words, then Help, Apply and the answers; a button that is the default or the accent is
        /// the primary one; one that is the cancel is Cancel.
        /// </summary>
        public static int RankOf(XElement button, string file)
        {
            string label = LabelOf(button);
            string style = Attribute(button, "Style");
            bool accent = style.Contains("Primary") || style.Contains("Accent");
            bool isDefault = Attribute(button, "IsDefault") == "True";

            if (label == "Cancel" || label == "Close" || label == "?ThirdButton")
            {
                return Cancel;
            }

            if (PrimaryLabels.Contains(label))
            {
                return Primary;
            }

            if (label == "Help")
            {
                return Help;
            }

            if (label == "No" || label == "Don't save" || label == "{Binding NoLabel}" || label == "?NoButton")
            {
                return Answer;
            }

            if (label == "Apply" && !accent && !isDefault)
            {
                return ApplyFirst.Contains(Path.GetFileName(file)) ? 0 : Apply;
            }

            if (isDefault || accent)
            {
                return Primary;
            }

            return Attribute(button, "IsCancel") == "True" ? Cancel : Unordered;
        }
    }
}
