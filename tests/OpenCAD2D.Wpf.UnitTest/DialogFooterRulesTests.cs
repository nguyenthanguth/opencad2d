using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The footers of the chromeless dialogs read from their XAML: one order (the primary answer, then the other answers of a
    /// question, then Cancel or Close, then Apply, then Help, as AutoCAD 2021's are), one size for a text button (padding 14,5
    /// and a width of 75 at least) and one Close button, so that no dialog is set out its own way.
    /// </summary>
    public sealed class DialogFooterRulesTests
    {
        // Not footers of answers: a list of command links (BlockClose), the three answers of the security prompt in
        // AutoCAD's own order (LoadSecurity), and a toolbar over the drawing (PlotPreview).
        private static readonly string[] NotFooters =
        {
            "BlockCloseWindow.xaml",
            "LoadSecurityWindow.xaml",
            "PlotPreviewWindow.xaml",
        };

        [Fact]
        public void EveryFooterPutsThePrimaryBeforeCancel_ThenApply_ThenHelpLast()
        {
            var faults = new List<string>();

            foreach (string file in DialogXaml.ChromelessFiles())
            {
                string name = Path.GetFileName(file);

                if (NotFooters.Contains(name))
                {
                    continue;
                }

                List<XElement> buttons = DialogXaml.FooterButtons(file);
                var ranked = buttons
                    .Select(b => new { Label = DialogXaml.LabelOf(b), Rank = DialogXaml.RankOf(b, file) })
                    .Where(b => b.Rank != DialogXaml.Unordered)
                    .ToList();

                bool inOrder = true;

                for (int i = 1; i < ranked.Count; i++)
                {
                    inOrder &= ranked[i - 1].Rank <= ranked[i].Rank;
                }

                if (!inOrder)
                {
                    faults.Add(name + ": " + string.Join(", ", buttons.Select(DialogXaml.LabelOf)));
                }
            }

            Assert.True(faults.Count == 0, faults.Count + " footers out of order:\n" + string.Join("\n", faults));
        }

        [Fact]
        public void EveryFooterTextButtonIsSeventyFiveWideAtLeast_WithOnePadding()
        {
            var faults = new List<string>();

            foreach (string file in DialogXaml.ChromelessFiles())
            {
                string name = Path.GetFileName(file);

                if (name == "BlockCloseWindow.xaml")
                {
                    continue;
                }

                foreach (XElement button in DialogXaml.FooterButtons(file))
                {
                    string label = DialogXaml.LabelOf(button);

                    // The "?" and "<" of LayerStates, UcsDepth and Plot are pictures of one sign, not text.
                    if (label.Length == 1)
                    {
                        continue;
                    }

                    bool fits =
                        (string)button.Attribute("Padding") == "14,5"
                        && (string)button.Attribute("MinWidth") == "75"
                        && button.Attribute("Width") == null;

                    if (!fits)
                    {
                        faults.Add(
                            name
                                + ": "
                                + label
                                + " (Padding "
                                + ((string)button.Attribute("Padding") ?? "-")
                                + ", MinWidth "
                                + ((string)button.Attribute("MinWidth") ?? "-")
                                + ", Width "
                                + ((string)button.Attribute("Width") ?? "-")
                                + ")"
                        );
                    }
                }
            }

            Assert.True(
                faults.Count == 0,
                faults.Count + " footer buttons are not 14,5 and 75 wide at least:\n" + string.Join("\n", faults)
            );
        }

        [Fact]
        public void AFooterCloseThatEndsTheDialogIsTheCloseStyle()
        {
            var faults = new List<string>();

            // The sheet set frame's Close (a locked set) is the accent by the design of A12 and is not one of the footers
            // this rule names.
            foreach (string file in DialogXaml.ChromelessFiles().Where(f => !f.EndsWith("SheetSetDialogs.xaml")))
            {
                foreach (XElement button in DialogXaml.FooterButtons(file))
                {
                    string style = (string)button.Attribute("Style") ?? string.Empty;

                    if (DialogXaml.LabelOf(button) == "Close" && !style.Contains("GhCloseButton"))
                    {
                        faults.Add(Path.GetFileName(file) + " (" + (style.Length == 0 ? "no style" : style) + ")");
                    }
                }
            }

            Assert.True(faults.Count == 0, faults.Count + " footer Close buttons:\n" + string.Join("\n", faults));
        }
    }
}
