using System.Linq;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The group boxes of the Modify Dimension Style dialog as its view model hands them to the window: each tab's boxes in
    /// their columns, the flat list of fields that every other part reads kept as it was, a pair of boxes to tick on one row.
    /// </summary>
    public sealed class DimStyleGroupViewModelTests
    {
        private static DimStyleEditViewModel Editor()
        {
            CadDocument document = CadDocument.From(CadTemplate.CreateMetric(), "groups.dxf");
            return new DimStyleEditViewModel(
                document,
                DimStyleEditMode.Modify,
                CadDimStyleEditor.Current(document.Database)
            );
        }

        [Fact]
        public void EveryTabsFlatFields_AreItsGroupTitlesEachBeforeItsFields()
        {
            DimStyleEditViewModel editor = Editor();

            foreach (string tab in editor.Tabs)
            {
                string[] flat = editor
                    .FieldsOf(tab)
                    .Select(f => f.Kind == DimFieldKind.Header ? "# " + f.Label : f.Variable)
                    .ToArray();
                string[] grouped = editor
                    .GroupsOf(tab)
                    .SelectMany(g => new[] { "# " + g.Title }.Concat(g.Fields.Select(f => f.Variable)))
                    .ToArray();

                Assert.Equal(grouped, flat);
            }
        }

        [Fact]
        public void TheLinesTab_HasItsDimensionLinesAtTheLeft_AndTheExtensionLinesWideInTwoColumns()
        {
            DimStyleEditViewModel editor = Editor();
            DimGroupViewModel[] groups = editor.GroupsOf("Lines").ToArray();

            Assert.Equal(new[] { "Dimension lines", "Extension lines" }, groups.Select(g => g.Title));
            Assert.Equal(new[] { DimGroupColumn.Left, DimGroupColumn.Wide }, groups.Select(g => g.Column));
            Assert.Single(groups[0].Columns);
            Assert.Equal(2, groups[1].Columns.Count);
            Assert.Equal(
                new[] { "DIMCLRE", "DIMLTEX1", "DIMLTEX2", "DIMLWE", "DIMSE1", "DIMSE2" },
                groups[1].Columns[0].SelectMany(r => r.Fields).Select(f => f.Variable)
            );
            Assert.Equal(
                new[] { "DIMEXE", "DIMEXO", "DIMFXLON", "DIMFXL" },
                groups[1].Columns[1].SelectMany(r => r.Fields).Select(f => f.Variable)
            );
        }

        [Fact]
        public void TheSuppressPairs_AreOneRowOfTwoFields_WithTheirWordsAndBothVariables()
        {
            DimStyleEditViewModel editor = Editor();
            DimGroupViewModel[] lines = editor.GroupsOf("Lines").ToArray();
            DimRowViewModel dim = lines[0].Columns[0].Single(r => r.IsPair);
            DimRowViewModel ext = lines[1].Columns[0].Single(r => r.IsPair);

            Assert.Equal("Suppress", dim.Caption);
            Assert.Equal("DIMSD1 · DIMSD2", dim.Variables);
            Assert.Equal(new[] { "DIMSD1", "DIMSD2" }, dim.Fields.Select(f => f.Variable));
            Assert.Equal(new[] { "Dim line 1", "Dim line 2" }, dim.Fields.Select(f => f.PairWords));
            Assert.Equal("DIMSE1 · DIMSE2", ext.Variables);
            Assert.Equal(new[] { "Ext line 1", "Ext line 2" }, ext.Fields.Select(f => f.PairWords));

            // Only those two rows are pairs: every other box to tick keeps a row of its own.
            Assert.Equal(
                2,
                editor
                    .Tabs.SelectMany(t => editor.GroupsOf(t))
                    .SelectMany(g => g.Columns)
                    .SelectMany(c => c)
                    .Count(r => r.IsPair)
            );
        }

        [Fact]
        public void TheShownTabsColumns_FollowTheTab_AndTheRightColumnsBoxesAreNarrower()
        {
            DimStyleEditViewModel editor = Editor();

            Assert.Equal(new[] { "Dimension lines" }, editor.LeftGroups.Select(g => g.Title));
            Assert.Empty(editor.RightGroups);
            Assert.Equal(new[] { "Extension lines" }, editor.WideGroups.Select(g => g.Title));

            editor.Tab = "Fit";

            Assert.Equal(new[] { "Fit options", "Text placement" }, editor.LeftGroups.Select(g => g.Title));
            Assert.Equal(
                new[] { "Scale for dimension features", "Fine tuning" },
                editor.RightGroups.Select(g => g.Title)
            );
            Assert.Empty(editor.WideGroups);
            Assert.All(editor.RightGroups.SelectMany(g => g.Fields), f => Assert.Equal(150, f.FieldWidth));
            Assert.All(editor.LeftGroups.SelectMany(g => g.Fields), f => Assert.Equal(190, f.FieldWidth));

            // The two long lists of the Fit tab sit under their labels too, so that their words are not cut.
            Assert.All(editor.LeftGroups.SelectMany(g => g.Fields), f => Assert.True(f.IsStacked));

            // A list of the right column sits under its label, the width of its box; a number or a box to tick stays beside it.
            editor.Tab = "Primary units";
            Assert.All(editor.RightGroups.SelectMany(g => g.Fields), f =>
                Assert.Equal(f.Kind == DimFieldKind.Choice, f.IsStacked)
            );
            Assert.DoesNotContain(editor.LeftGroups.SelectMany(g => g.Fields), f => f.IsStacked);
        }
    }
}
