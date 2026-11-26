using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// A14 package P1: the samples of the style previews with their parts told apart (every entity of a sample dimension is a
    /// part with a handle of its own), the tables from a field of a style dialog to the parts it sets, and the measures a
    /// preview shows as callouts, for dimension styles and for multileader styles.
    /// </summary>
    public sealed class StylePreviewSampleTests
    {
        private static readonly Tolerance Tol = new Tolerance(1E-9, 1E-9);

        private static readonly CadDimSampleKind[] Kinds = (CadDimSampleKind[])Enum.GetValues(typeof(CadDimSampleKind));

        // The fields of the four dialogs by tab, as DimStyleEditViewModel lays them out: key and the tab's sample.
        private static readonly string[] LinesKeys =
        {
            "DIMCLRD",
            "DIMLTYPE",
            "DIMLWD",
            "DIMDLE",
            "DIMDLI",
            "DIMSD1",
            "DIMSD2",
            "DIMCLRE",
            "DIMLTEX1",
            "DIMLTEX2",
            "DIMLWE",
            "DIMSE1",
            "DIMSE2",
            "DIMEXE",
            "DIMEXO",
            "DIMFXLON",
            "DIMFXL",
        };

        private static readonly string[] SymbolKeys =
        {
            "DIMBLK1",
            "DIMBLK2",
            "DIMLDRBLK",
            "DIMASZ",
            "DIMCEN",
            "DIMCEN#",
            "DIMARCSYM",
            "DIMJOGANG",
        };

        private static readonly string[] TextKeys =
        {
            "DIMTXSTY",
            "DIMCLRT",
            "DIMTFILL",
            "DIMTXT",
            "DIMTFAC",
            "DIMGAP#",
            "DIMTAD",
            "DIMJUST",
            "DIMTXTDIRECTION",
            "DIMGAP",
            "DIMTIH",
        };

        private static readonly string[] FitKeys = { "DIMATFIT", "DIMTMOVE", "DIMTOFL" };

        private static readonly string[] ChipOnlyKeys = { "Annotative", "DIMSCALE", "DIMUPT" };

        private static readonly string[] PrimaryKeys =
        {
            "DIMLUNIT",
            "DIMDEC",
            "DIMFRAC",
            "DIMDSEP",
            "DIMRND",
            "DIMPOST",
            "DIMLFAC",
            "DIMZIN",
            "DIMAUNIT",
            "DIMADEC",
            "DIMAZIN",
        };

        private static readonly string[] AlternateKeys =
        {
            "DIMALT",
            "DIMALTU",
            "DIMALTD",
            "DIMALTF",
            "DIMALTRND",
            "DIMAPOST",
            "DIMALTZ",
            "DIMPOST\\X",
        };

        private static readonly string[] ToleranceKeys =
        {
            "DIMTOL",
            "DIMTDEC",
            "DIMTP",
            "DIMTM",
            "DIMTFAC",
            "DIMTOLJ",
            "DIMTZIN",
        };

        private static SortedDictionary<string, object> Values(CadDocument document, double dimscale = 1.0)
        {
            SortedDictionary<string, object> values = CadDimStyleEditor.Read(
                document.Database.DimStyleTable[CadDimStyleEditor.Current(document.Database)]
            );
            values["Dimscale"] = dimscale;
            values["Dimasz"] = 2.5;
            values["Dimtxt"] = 2.5;
            values["Dimexe"] = 1.25;
            values["Dimexo"] = 0.625;
            values["Dimgap"] = 0.625;
            values["Dimdli"] = 3.75;
            values["Dimcen"] = 2.5;
            values["Dimtad"] = 1;
            values["Dimtih"] = false;
            values["Dimtoh"] = false;
            return values;
        }

        private static CadStyleSample Sample(
            CadDocument document,
            IDictionary<string, object> values,
            CadDimSampleKind kind
        ) => CadDimStyleEditor.Sample(document.Database, values, kind);

        private static IEnumerable<DxfEntity> Entities(CadStyleSample sample) => sample.Database.ModelSpace;

        private static DxfEntity Entity(CadStyleSample sample, string handle) =>
            Entities(sample).Single(e => e.Handle == handle);

        private static CadStyleSamplePart PartOfDimension(CadStyleSample sample, int dimension, CadStylePart part) =>
            sample.Parts.Single(p => p.Dimension == dimension && p.Part == part);

        private static void AssertNear(double expected, double actual, string what = "")
        {
            Assert.True(
                Math.Abs(expected - actual) <= Tol.EqualPoint,
                what
                    + " expected "
                    + expected.ToString("R", CultureInfo.InvariantCulture)
                    + ", got "
                    + actual.ToString("R", CultureInfo.InvariantCulture)
            );
        }

        private static void AssertPoint(double x, double y, GeoPoint2 actual, string what = "")
        {
            AssertNear(x, actual.X, what + " x");
            AssertNear(y, actual.Y, what + " y");
        }

        private static double LengthOf(CadStyleMeasure measure) => (measure.To - measure.From).Length;

        // ------------------------------------------------------------------- the tables

        [Fact]
        public void EveryFieldOfTheDialogs_SetsPartsThatItsTabsSampleHas_ButTheThreeThatSetNone()
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document);
            values["Dimtfill"] = 1;
            values["Dimgap"] = -0.625;
            var tabs = new List<KeyValuePair<CadDimSampleKind, string[]>>
            {
                new KeyValuePair<CadDimSampleKind, string[]>(CadDimSampleKind.Lines, LinesKeys),
                new KeyValuePair<CadDimSampleKind, string[]>(CadDimSampleKind.SymbolsAndArrows, SymbolKeys),
                new KeyValuePair<CadDimSampleKind, string[]>(CadDimSampleKind.Text, TextKeys),
                new KeyValuePair<CadDimSampleKind, string[]>(
                    CadDimSampleKind.Fit,
                    FitKeys.Concat(ChipOnlyKeys).ToArray()
                ),
                new KeyValuePair<CadDimSampleKind, string[]>(CadDimSampleKind.PrimaryUnits, PrimaryKeys),
                new KeyValuePair<CadDimSampleKind, string[]>(CadDimSampleKind.AlternateUnits, AlternateKeys),
                new KeyValuePair<CadDimSampleKind, string[]>(CadDimSampleKind.Tolerances, ToleranceKeys),
            };
            int fields = 0;

            foreach (KeyValuePair<CadDimSampleKind, string[]> tab in tabs)
            {
                CadStyleSample sample = Sample(document, values, tab.Key);

                foreach (string key in tab.Value)
                {
                    fields++;
                    IReadOnlyList<CadStylePart> parts = CadDimStyleEditor.PartsOf(key);

                    if (ChipOnlyKeys.Contains(key))
                    {
                        Assert.Empty(parts);
                        continue;
                    }

                    Assert.True(parts.Count > 0, key + " sets no part");
                    Assert.True(
                        sample.Parts.Any(p => parts.Contains(p.Part)),
                        key + " sets " + string.Join(", ", parts) + ", none of them in the " + tab.Key + " sample"
                    );
                }
            }

            // The 68 fields of the dialogs: Scaling for height of the Tolerances tab is DIMTFAC of the Text tab again.
            Assert.Equal(68, fields);
            Assert.Equal(2, tabs.SelectMany(t => t.Value).Count(k => k == "DIMTFAC"));
        }

        [Fact]
        public void EveryPartOfEverySample_ListsAKeyThatSetsIt_ButDefinitionPoints()
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document);
            values["Dimtfill"] = 1;
            values["Dimgap"] = -0.625;

            foreach (CadDimSampleKind kind in Kinds)
            {
                CadStyleSample sample = Sample(document, values, kind);

                foreach (CadStyleSamplePart part in sample.Parts)
                {
                    if (part.Part == CadStylePart.DefinitionPoint)
                    {
                        // A point nothing draws: no variable sets it.
                        continue;
                    }

                    Assert.True(
                        CadDimStyleEditor.KeysOf(part.Part).Count > 0,
                        part.Part + " of the " + kind + " sample is set by no key"
                    );
                }
            }
        }

        [Fact]
        public void KeysOfAPart_AreInTheOrderOfTheDialog_ForFiveParts()
        {
            Assert.Equal(
                new[] { "DIMCLRE", "DIMLTEX1", "DIMLWE", "DIMSE1", "DIMEXE", "DIMEXO", "DIMFXLON", "DIMFXL" },
                CadDimStyleEditor.KeysOf(CadStylePart.ExtensionLine1).ToArray()
            );
            Assert.Equal(
                new[] { "DIMCLRD", "DIMLWD", "DIMBLK2", "DIMASZ", "DIMATFIT" },
                CadDimStyleEditor.KeysOf(CadStylePart.Arrow2).ToArray()
            );
            Assert.Equal(
                new[] { "DIMCLRD", "DIMLTYPE", "DIMLWD", "DIMDLE", "DIMDLI", "DIMSD1", "DIMGAP", "DIMTOFL" },
                CadDimStyleEditor.KeysOf(CadStylePart.DimensionLine1).ToArray()
            );
            Assert.Equal(new[] { "DIMCLRD", "DIMGAP#" }, CadDimStyleEditor.KeysOf(CadStylePart.TextFrame).ToArray());
            Assert.Equal(new[] { "DIMCEN", "DIMCEN#" }, CadDimStyleEditor.KeysOf(CadStylePart.CentreMark).ToArray());

            // The words come last of the lines and arrows, tab after tab: Text, Fit, then the units.
            string[] words = CadDimStyleEditor.KeysOf(CadStylePart.Text).ToArray();
            Assert.Equal(
                new[]
                {
                    "DIMTXSTY",
                    "DIMCLRT",
                    "DIMTXT",
                    "DIMTFAC",
                    "DIMTAD",
                    "DIMJUST",
                    "DIMTXTDIRECTION",
                    "DIMGAP",
                    "DIMTIH",
                    "DIMATFIT",
                    "DIMTMOVE",
                    "DIMLUNIT",
                },
                words.Take(12).ToArray()
            );
            Assert.Equal("DIMTZIN", words[words.Length - 1]);
        }

        [Fact]
        public void AKeyIsTakenInEitherCase_AndAKeyNoFieldHasSetsNoPart()
        {
            Assert.Equal(CadDimStyleEditor.PartsOf("DIMEXE"), CadDimStyleEditor.PartsOf("dimexe"));
            Assert.Equal(CadDimStyleEditor.PartsOf("DIMPOST\\X"), CadDimStyleEditor.PartsOf("DIMPOST \\X"));
            Assert.Equal(
                new[] { CadStylePart.ExtensionLine1, CadStylePart.ExtensionLine2 },
                CadDimStyleEditor.PartsOf("DIMEXE").ToArray()
            );
            Assert.Empty(CadDimStyleEditor.PartsOf("DIMNOTHING"));
            Assert.Empty(CadDimStyleEditor.PartsOf(null));
        }

        // ------------------------------------------------------------------- tagging the entities

        [Fact]
        public void NoEntityOfASampleDimensionIsUntagged_OfAnyKind_AtDimscaleOneAndTwentyFive()
        {
            CadDocument document = Blank();

            foreach (double scale in new[] { 1.0, 25.0 })
            {
                SortedDictionary<string, object> values = Values(document, scale);
                values["Dimtfill"] = 1;

                foreach (CadDimSampleKind kind in Kinds)
                {
                    CadStyleSample sample = Sample(document, values, kind);
                    string where = kind + " at " + scale;

                    Assert.Empty(sample.Untagged);
                    Assert.NotEmpty(sample.Parts);

                    // Each handle of a part is an entity of the sample's model space, and a part lists it once.
                    var handles = new HashSet<string>(Entities(sample).Select(e => e.Handle));

                    foreach (CadStyleSamplePart part in sample.Parts)
                    {
                        Assert.NotEmpty(part.Handles);
                        Assert.Equal(part.Handles.Count, part.Handles.Distinct().Count());
                        Assert.All(part.Handles, h => Assert.True(handles.Contains(h), where + ": " + h));
                    }

                    // No dimension is left in model space: the entities of its picture are.
                    Assert.Empty(Entities(sample).OfType<DxfDimension>());
                }
            }
        }

        [Fact]
        public void TheDimensionLineOfALinearDimension_IsBothHalvesWhole_AndTwoHalvesWhereTheWordsBreakIt()
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document);

            CadStyleSample whole = Sample(document, values, CadDimSampleKind.Overview);
            string[] first = PartOfDimension(whole, 0, CadStylePart.DimensionLine1).Handles.ToArray();
            string[] second = PartOfDimension(whole, 0, CadStylePart.DimensionLine2).Handles.ToArray();
            Assert.Single(first);
            Assert.Equal(first, second);

            // Words on the line (DIMTAD 0) break it: the first half before them, the second after.
            values["Dimtad"] = 0;
            CadStyleSample broken = Sample(document, values, CadDimSampleKind.Overview);
            string[] before = PartOfDimension(broken, 0, CadStylePart.DimensionLine1).Handles.ToArray();
            string[] after = PartOfDimension(broken, 0, CadStylePart.DimensionLine2).Handles.ToArray();
            Assert.Single(before);
            Assert.Single(after);
            Assert.NotEqual(before[0], after[0]);
            var firstLine = (DxfLine)Entity(broken, before[0]);
            var secondLine = (DxfLine)Entity(broken, after[0]);
            Assert.True(firstLine.EndPoint.X < 16.0 && secondLine.StartPoint.X > 16.0);
        }

        [Fact]
        public void ThePartsOfTheOverviewsLinearDimension_AreEntitiesOfTheirOwn_WithTheirKinds()
        {
            CadDocument document = Blank();
            CadStyleSample sample = Sample(document, Values(document), CadDimSampleKind.Overview);

            var one = (DxfLine)Entity(sample, PartOfDimension(sample, 0, CadStylePart.ExtensionLine1).Handles.Single());
            var two = (DxfLine)Entity(sample, PartOfDimension(sample, 0, CadStylePart.ExtensionLine2).Handles.Single());
            AssertPoint(0.0, 20.625, new GeoPoint2(one.StartPoint.X, one.StartPoint.Y), "extension line 1 start");
            AssertPoint(0.0, 33.25, new GeoPoint2(one.EndPoint.X, one.EndPoint.Y), "extension line 1 end");
            AssertPoint(32.0, 20.625, new GeoPoint2(two.StartPoint.X, two.StartPoint.Y), "extension line 2 start");
            Assert.IsType<DxfSolid>(Entity(sample, PartOfDimension(sample, 0, CadStylePart.Arrow1).Handles.Single()));
            Assert.IsType<DxfSolid>(Entity(sample, PartOfDimension(sample, 0, CadStylePart.Arrow2).Handles.Single()));
            var words = (DxfMText)Entity(sample, PartOfDimension(sample, 0, CadStylePart.Text).Handles.Single());
            Assert.Equal("32", words.Contents);
            Assert.Equal(CadStylePart.Text, sample.PartOf(words.Handle));
            Assert.Equal(CadStylePart.ExtensionLine1, sample.PartOf(one.Handle));

            // The radius and the angle are dimensions 1 and 2.
            Assert.Contains(sample.Parts, p => p.Dimension == 1 && p.Part == CadStylePart.DimensionLine1);
            Assert.Contains(sample.Parts, p => p.Dimension == 2 && p.Part == CadStylePart.DimensionLine2);
            Assert.Contains(sample.Parts, p => p.Dimension == 2 && p.Part == CadStylePart.Arrow2);

            // The part and the angle's rays are the outline: no part.
            DxfEntity outline = Entities(sample).First();
            Assert.Equal(CadStylePart.None, sample.PartOf(outline.Handle));
            Assert.Equal(CadStylePart.None, sample.PartOf(null));
            Assert.Equal(CadStylePart.None, sample.PartOf("FFFFFF"));
        }

        [Fact]
        public void WordsFilledBehind_AreTheTextAndTheTextFill_OfOneEntity_AndAFrameIsFourLines()
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document);
            values["Dimtfill"] = 1;
            values["Dimgap"] = -0.625;
            CadStyleSample sample = Sample(document, values, CadDimSampleKind.Overview);

            string text = PartOfDimension(sample, 0, CadStylePart.Text).Handles.Single();
            Assert.Equal(new[] { text }, PartOfDimension(sample, 0, CadStylePart.TextFill).Handles.ToArray());
            Assert.Equal(CadStylePart.Text, sample.PartOf(text));
            Assert.Equal(4, PartOfDimension(sample, 0, CadStylePart.TextFrame).Handles.Count);

            values["Dimtfill"] = 0;
            values["Dimgap"] = 0.625;
            CadStyleSample plain = Sample(document, values, CadDimSampleKind.Overview);
            Assert.DoesNotContain(plain.Parts, p =>
                p.Part == CadStylePart.TextFill || p.Part == CadStylePart.TextFrame
            );
        }

        [Fact]
        public void TheDisplayItemsOfASample_CarryTheHandleOfThePartNotOfTheDimension()
        {
            CadDocument document = Blank();
            CadStyleSample sample = Sample(document, Values(document), CadDimSampleKind.Overview);
            CadDocument drawn = CadDocument.From(sample.Database, "preview.dxf");
            DisplayList2 list = drawn.GetDisplay(drawn.ModelSpace);

            foreach (CadStylePart part in new[] { CadStylePart.ExtensionLine1, CadStylePart.Arrow2, CadStylePart.Text })
            {
                string handle = PartOfDimension(sample, 0, part).Handles.First();
                Assert.Contains(list.Items, i => i.Handle == handle);
            }

            // Each part's items name only its own part: an arrowhead's item is not the dimension's.
            string arrow = PartOfDimension(sample, 0, CadStylePart.Arrow2).Handles.Single();
            Assert.Single(list.Items, i => i.Handle == arrow);
        }

        // ------------------------------------------------------------------- the picture is the one drawn before

        [Theory]
        [InlineData(1.0, "")]
        [InlineData(25.0, "")]
        [InlineData(0.0, "")]
        [InlineData(1.0, "Dot")]
        public void TheOverviewSample_DrawsExactlyWhatTheOldSamplesDraw_ItemForItem(double dimscale, string arrow)
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document, dimscale);

            if (arrow.Length > 0)
            {
                values["Dimblk"] = arrow;
            }

            DxfDatabase old = CadDimStyleEditor.Samples(document.Database, values);
            CadStyleSample made = Sample(document, values, CadDimSampleKind.Overview);

            List<Item> before = Items(old);
            List<Item> after = Items(made.Database);

            Assert.True(before.Count > 20, "the old sample draws " + before.Count + " items");
            Assert.Equal(before.Count, after.Count);

            for (int i = 0; i < before.Count; i++)
            {
                Assert.Equal(before[i].Kind, after[i].Kind);
                Assert.Equal(before[i].Numbers.Length, after[i].Numbers.Length);

                for (int n = 0; n < before[i].Numbers.Length; n++)
                {
                    AssertNear(
                        before[i].Numbers[n],
                        after[i].Numbers[n],
                        "item " + i + " (" + before[i].Kind + ") number " + n
                    );
                }
            }
        }

        private sealed class Item
        {
            public string Kind;
            public double[] Numbers;
        }

        private static List<Item> Items(DxfDatabase database)
        {
            CadDocument document = CadDocument.From(database, "preview.dxf");
            var items = new List<Item>();

            foreach (DisplayItem2 item in document.GetDisplay(document.ModelSpace).Items)
            {
                var numbers = new List<double>();
                string kind =
                    item.GetType().Name
                    + "|"
                    + item.Style?.Rgb.ToString("X6", CultureInfo.InvariantCulture)
                    + "|"
                    + item.Style?.LineWeight.ToString("R", CultureInfo.InvariantCulture)
                    + "|"
                    + item.Layer;

                switch (item)
                {
                    case DisplayPolyline2 polyline:
                        numbers.AddRange(polyline.Xy);
                        numbers.Add(polyline.Closed ? 1.0 : 0.0);
                        break;
                    case DisplaySegments2 segments:
                        numbers.AddRange(segments.Xy);
                        break;
                    case DisplayArc2 arc:
                        numbers.AddRange(new[] { arc.Cx, arc.Cy, arc.Radius, arc.Start, arc.Sweep });
                        break;
                    case DisplayFill2 fill:
                        kind += "|" + fill.IsMask;
                        foreach (double[] ring in fill.Rings)
                        {
                            numbers.AddRange(ring);
                        }

                        break;
                    case DisplayText2 text:
                        kind += "|" + text.Text + "|" + text.FontName;
                        numbers.AddRange(new[] { text.X, text.Y, text.Height, text.Rotation, text.WidthFactor });
                        numbers.Add(text.Horizontal);
                        numbers.Add(text.Vertical);
                        break;
                    default:
                        DisplayBox box = item.Bounds;
                        numbers.AddRange(new[] { box.MinX, box.MinY, box.MaxX, box.MaxY });
                        break;
                }

                items.Add(new Item { Kind = kind, Numbers = numbers.ToArray() });
            }

            return items;
        }

        [Fact]
        public void ADimensionCommandsPicture_IsAsItWasBeforeTheTagging_AndTagsNothingOutsideASample()
        {
            CadDocument document = Blank();
            var dimension = new DxfRotatedDimension(
                0.0,
                new GeoPoint3(0, 20, 0),
                new GeoPoint3(32, 20, 0),
                new GeoPoint3(16, 32, 0),
                string.Empty,
                null
            );
            DxfBlockTableRecord block = new DxfBlockTableRecord { Name = "*D" };
            document.Database.BlockTable.Add(block);
            dimension.DimensionStyle = document.Database.DimStyleTable[CadDimStyleEditor.Current(document.Database)];
            dimension.DimBlock = block;
            document.Database.ModelSpace.AppendEntity(dimension);

            Assert.True(CadCommands.Redraw(dimension));

            // Tagging is a flag of the sample's builder, off for the commands: no entity of the block is tagged.
            Assert.False(CadCommands.Tagging);
            Assert.All(block, e => Assert.Empty(CadCommands.PartsOfEntity(e)));
            Assert.Contains(block, e => e is DxfMText);
        }

        // ------------------------------------------------------------------- linetypes

        [Fact]
        public void ADimensionLinetypeOfTheDrawing_IsGivenToTheSamplesDimensionLines_AndTheExtensionLinesTheirs()
        {
            CadDocument document = Blank();
            CadLinetypes.Add(document.Database, CadLinetypeFile.ParsePattern("DASHED", "Dashed", "A,12.7,-6.35"));
            SortedDictionary<string, object> values = Values(document);
            values["Dimltype"] = "DASHED";
            CadStyleSample sample = Sample(document, values, CadDimSampleKind.Lines);

            IEnumerable<DxfEntity> lines = PartOfDimension(sample, 0, CadStylePart.DimensionLine1)
                .Handles.Concat(PartOfDimension(sample, 1, CadStylePart.DimensionLine2).Handles)
                .Select(h => Entity(sample, h))
                .ToList();
            Assert.All(lines, e => Assert.Equal("DASHED", e.Linetype, ignoreCase: true));
            Assert.True(sample.Database.LinetypeTable.Has("DASHED"));

            // The extension lines have their own (DIMLTEX1, DIMLTEX2), and the arrowheads none.
            Assert.All(
                PartOfDimension(sample, 0, CadStylePart.ExtensionLine1).Handles.Select(h => Entity(sample, h)),
                e => Assert.NotEqual("DASHED", e.Linetype, StringComparer.OrdinalIgnoreCase)
            );

            values["Dimltype"] = null;
            values["Dimltex2"] = "DASHED";
            CadStyleSample second = Sample(document, values, CadDimSampleKind.Lines);
            Assert.Equal(
                "DASHED",
                Entity(second, PartOfDimension(second, 0, CadStylePart.ExtensionLine2).Handles.Single()).Linetype,
                ignoreCase: true
            );
            Assert.NotEqual(
                "DASHED",
                Entity(second, PartOfDimension(second, 0, CadStylePart.ExtensionLine1).Handles.Single()).Linetype,
                StringComparer.OrdinalIgnoreCase
            );
            Assert.NotEqual(
                "DASHED",
                Entity(second, PartOfDimension(second, 0, CadStylePart.DimensionLine1).Handles.First()).Linetype,
                StringComparer.OrdinalIgnoreCase
            );
        }

        // ------------------------------------------------------------------- the samples of the tabs

        [Fact]
        public void TheLinesSample_HasTwoLinearDimensions_TheSecondsLineDimdliAboveTheFirsts()
        {
            CadDocument document = Blank();
            CadStyleSample sample = Sample(document, Values(document), CadDimSampleKind.Lines);

            Assert.Equal(new[] { 0, 1 }, sample.Parts.Select(p => p.Dimension).Distinct().OrderBy(d => d).ToArray());
            var words = sample.Parts.Where(p => p.Part == CadStylePart.Text).OrderBy(p => p.Dimension).ToList();
            Assert.Equal("20", ((DxfMText)Entity(sample, words[0].Handles.Single())).Contents);
            Assert.Equal("32", ((DxfMText)Entity(sample, words[1].Handles.Single())).Contents);
            var first = (DxfLine)Entity(
                sample,
                PartOfDimension(sample, 0, CadStylePart.DimensionLine1).Handles.Single()
            );
            var second = (DxfLine)Entity(
                sample,
                PartOfDimension(sample, 1, CadStylePart.DimensionLine1).Handles.Single()
            );
            AssertNear(32.0, first.StartPoint.Y, "first dimension line");
            AssertNear(35.75, second.StartPoint.Y, "second dimension line");
        }

        [Fact]
        public void TheSymbolsSample_HasTheRadiusWithItsCentreMark_TheArcLength_TheJoggedRadius_AndTheLeader()
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document);
            CadStyleSample sample = Sample(document, values, CadDimSampleKind.SymbolsAndArrows);

            Assert.Contains(sample.Parts, p => p.Dimension == 1 && p.Part == CadStylePart.CentreMark);
            Assert.Contains(sample.Parts, p => p.Dimension == 2 && p.Part == CadStylePart.ArcSymbol);
            Assert.Contains(sample.Parts, p => p.Dimension == 3 && p.Part == CadStylePart.Jog);
            Assert.Contains(sample.Parts, p => p.Dimension == 4 && p.Part == CadStylePart.LeaderArrow);
            Assert.Single(PartOfDimension(sample, 3, CadStylePart.Jog).Handles);
            Assert.Equal(5, sample.Parts.Select(p => p.Dimension).Distinct().Count());

            // A style whose line runs through the circle (DIMTOFL) still shows the mark of its DIMCEN here.
            values["Dimtofl"] = true;
            CadStyleSample through = Sample(document, values, CadDimSampleKind.SymbolsAndArrows);
            Assert.Contains(through.Parts, p => p.Dimension == 1 && p.Part == CadStylePart.CentreMark);

            // The leader takes its arrowhead from DIMLDRBLK.
            values["Dimldrblk"] = "Dot";
            CadStyleSample dotted = Sample(document, values, CadDimSampleKind.SymbolsAndArrows);
            DxfEntity head = Entity(dotted, PartOfDimension(dotted, 4, CadStylePart.LeaderArrow).Handles.Single());
            Assert.IsType<DxfBlockReference>(head);
        }

        [Fact]
        public void TheTextSample_HasAVerticalDimensionOnTheLeft_TheFitSampleAMovedOneAndANarrowOne()
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document);

            CadStyleSample text = Sample(document, values, CadDimSampleKind.Text);
            var vertical = (DxfMText)Entity(text, PartOfDimension(text, 1, CadStylePart.Text).Handles.Single());
            Assert.Equal("20", vertical.Contents);
            Assert.True(vertical.Location.X < 0.0);

            CadStyleSample fit = Sample(document, values, CadDimSampleKind.Fit);
            Assert.Equal(3, fit.Parts.Select(p => p.Dimension).Distinct().Count());
            var narrow = (DxfMText)Entity(fit, PartOfDimension(fit, 1, CadStylePart.Text).Handles.Single());
            Assert.Equal("5", narrow.Contents);
            var moved = (DxfMText)Entity(fit, PartOfDimension(fit, 2, CadStylePart.Text).Handles.Single());
            Assert.Equal("32", moved.Contents);
            Assert.True(moved.Location.X > 32.0, "the moved words stand right of the dimension: " + moved.Location.X);
        }

        [Fact]
        public void TheUnitsSamples_HaveALinearOfTwentyEightPointThreeSevenFive_AndAnAngle()
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document);
            values["Dimdec"] = 3;
            values["Dimadec"] = 1;
            values["Dimzin"] = 0;

            foreach (
                CadDimSampleKind kind in new[]
                {
                    CadDimSampleKind.PrimaryUnits,
                    CadDimSampleKind.AlternateUnits,
                    CadDimSampleKind.Tolerances,
                }
            )
            {
                CadStyleSample sample = Sample(document, values, kind);
                var linear = (DxfMText)Entity(sample, PartOfDimension(sample, 0, CadStylePart.Text).Handles.Single());
                Assert.Equal("28,375", linear.Contents.Replace('.', ','));
                var angle = (DxfMText)Entity(sample, PartOfDimension(sample, 1, CadStylePart.Text).Handles.Single());
                Assert.Equal("37.5", angle.Contents.TrimEnd((char)176).Replace(',', '.'));
            }
        }

        [Fact]
        public void ASampleIsAsManyTimesBiggerAsDimscaleSays_ZeroTakenForOne()
        {
            CadDocument document = Blank();

            foreach (CadDimSampleKind kind in Kinds)
            {
                CadStyleSample one = Sample(document, Values(document, 1.0), kind);
                CadStyleSample none = Sample(document, Values(document, 0.0), kind);
                CadStyleSample big = Sample(document, Values(document, 25.0), kind);

                // The first extension line of the first dimension runs from 0.625 over the part to 1.25 over the line, times the scale.
                foreach (
                    KeyValuePair<CadStyleSample, double> scaled in new[]
                    {
                        new KeyValuePair<CadStyleSample, double>(one, 1.0),
                        new KeyValuePair<CadStyleSample, double>(none, 1.0),
                        new KeyValuePair<CadStyleSample, double>(big, 25.0),
                    }
                )
                {
                    var line = (DxfLine)Entity(
                        scaled.Key,
                        PartOfDimension(scaled.Key, 0, CadStylePart.ExtensionLine1).Handles.Single()
                    );
                    AssertNear(0.0, line.StartPoint.X, kind + " extension line x");
                    AssertNear(20.625 * scaled.Value, line.StartPoint.Y, kind + " extension line start");
                    AssertNear(33.25 * scaled.Value, line.EndPoint.Y, kind + " extension line end");
                }
            }
        }

        // ------------------------------------------------------------------- the measures

        [Theory]
        [InlineData(1.0)]
        [InlineData(25.0)]
        public void TheCalloutsOfTheLinesSample_AreTheValuesTimesTheScale_AtTheSamplesPoints(double k)
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document, k);
            CadStyleSample sample = Sample(document, values, CadDimSampleKind.Lines);

            CadStyleMeasure exe = Assert.Single(CadDimStyleEditor.Measures(sample, values, "DIMEXE"));
            Assert.Equal("DIMEXE", exe.Key);
            Assert.Equal("DIMEXE = 1.25", exe.Label);
            Assert.Null(exe.Angle);
            AssertNear(1.25 * k, LengthOf(exe), "DIMEXE length");
            AssertPoint(0.0, 32.0 * k, exe.From, "DIMEXE from");
            AssertPoint(0.0, 33.25 * k, exe.To, "DIMEXE to");

            CadStyleMeasure exo = Assert.Single(CadDimStyleEditor.Measures(sample, values, "DIMEXO"));
            Assert.Equal("DIMEXO = 0.625", exo.Label);
            AssertNear(0.625 * k, LengthOf(exo), "DIMEXO length");
            AssertPoint(0.0, 20.0 * k, exo.From, "DIMEXO from");
            AssertPoint(0.0, 20.625 * k, exo.To, "DIMEXO to");

            CadStyleMeasure asz = Assert.Single(CadDimStyleEditor.Measures(sample, values, "DIMASZ"));
            Assert.Equal("DIMASZ = 2.5", asz.Label);
            AssertNear(2.5 * k, LengthOf(asz), "DIMASZ length");
            AssertPoint(20.0 * k, 32.0 * k, asz.From, "DIMASZ from the tip of arrowhead 2");
            AssertPoint(17.5 * k, 32.0 * k, asz.To, "DIMASZ to its back");

            CadStyleMeasure dli = Assert.Single(CadDimStyleEditor.Measures(sample, values, "DIMDLI"));
            Assert.Equal("DIMDLI = 3.75", dli.Label);
            AssertNear(3.75 * k, LengthOf(dli), "DIMDLI length");
            AssertPoint(20.0 * k, 32.0 * k, dli.From, "DIMDLI from");
            AssertPoint(20.0 * k, 35.75 * k, dli.To, "DIMDLI to");
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(25.0)]
        public void TheCalloutsOfTheTextSample_AreTheGapAndTheHeightOfTheWords(double k)
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document, k);
            CadStyleSample sample = Sample(document, values, CadDimSampleKind.Text);

            CadStyleMeasure gap = Assert.Single(CadDimStyleEditor.Measures(sample, values, "DIMGAP"));
            Assert.Equal("DIMGAP = 0.625", gap.Label);
            AssertNear(0.625 * k, LengthOf(gap), "DIMGAP length");
            AssertPoint(16.0 * k, 32.0 * k, gap.From, "DIMGAP from the dimension line");
            AssertPoint(16.0 * k, 32.625 * k, gap.To, "DIMGAP to the words' box");

            CadStyleMeasure height = Assert.Single(CadDimStyleEditor.Measures(sample, values, "DIMTXT"));
            Assert.Equal("DIMTXT = 2.5", height.Label);
            AssertNear(2.5 * k, LengthOf(height), "DIMTXT length");
            AssertNear(height.From.X, height.To.X, "DIMTXT runs up the words' left edge");
            Assert.True(height.To.Y > height.From.Y);

            // The words stand where the gap puts them: above the line by the gap and half their height.
            DxfMText words = (DxfMText)Entity(sample, PartOfDimension(sample, 0, CadStylePart.Text).Handles.Single());
            Assert.True(words.Location.Y > gap.To.Y);
            Assert.True(Math.Abs(words.Location.X - 16.0 * k) <= 1E-6 * k);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(25.0)]
        public void TheCalloutsOfTheSymbolsSample_AreTheCentreMarkAndTheJogAngle(double k)
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document, k);
            CadStyleSample sample = Sample(document, values, CadDimSampleKind.SymbolsAndArrows);

            CadStyleMeasure mark = Assert.Single(CadDimStyleEditor.Measures(sample, values, "DIMCEN#"));
            Assert.Equal("DIMCEN = 2.5", mark.Label);
            AssertNear(2.5 * k, LengthOf(mark), "DIMCEN length");
            AssertPoint(50.0 * k, 10.0 * k, mark.From, "DIMCEN from the centre");

            CadStyleMeasure jog = Assert.Single(CadDimStyleEditor.Measures(sample, values, "DIMJOGANG"));
            Assert.Equal("DIMJOGANG = 45", jog.Label);
            Assert.NotNull(jog.Angle);
            AssertNear(Math.PI / 4.0, Math.Abs(jog.Angle.Value), "DIMJOGANG angle");
            Assert.True(LengthOf(jog) > 0.0);
        }

        [Fact]
        public void TheFixedLengthAndTheRunOnOfTicks_AreMeasuredWhenTheyAreOn_AndNotOtherwise()
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document);
            values["DimfxlenOn"] = true;
            values["Dimfxlen"] = 7.5;
            values["Dimblk"] = "Architectural tick";
            values["Dimdle"] = 1.25;
            CadStyleSample sample = Sample(document, values, CadDimSampleKind.Lines);

            CadStyleMeasure fixedLength = Assert.Single(CadDimStyleEditor.Measures(sample, values, "DIMFXL"));
            Assert.Equal("DIMFXL = 7.5", fixedLength.Label);
            AssertPoint(0.0, 24.5, fixedLength.From, "DIMFXL from the start");
            AssertPoint(0.0, 32.0, fixedLength.To, "DIMFXL to the line");

            CadStyleMeasure run = Assert.Single(CadDimStyleEditor.Measures(sample, values, "DIMDLE"));
            Assert.Equal("DIMDLE = 1.25", run.Label);
            AssertPoint(20.0, 32.0, run.From, "DIMDLE from the second extension line");
            AssertPoint(21.25, 32.0, run.To, "DIMDLE to the end of the run");

            values["Dimblk"] = "Closed filled";
            Assert.Empty(
                CadDimStyleEditor.Measures(Sample(document, values, CadDimSampleKind.Lines), values, "DIMDLE")
            );
        }

        [Fact]
        public void AFieldThatSetsNoDistance_HasNoMeasure_AndASampleLackingTheDimensionHasNone()
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document);
            CadStyleSample sample = Sample(document, values, CadDimSampleKind.Overview);

            Assert.Empty(CadDimStyleEditor.Measures(sample, values, "DIMCLRD"));
            Assert.Empty(CadDimStyleEditor.Measures(sample, values, "DIMSCALE"));
            Assert.Empty(CadDimStyleEditor.Measures(sample, values, "DIMNOTHING"));
            Assert.Empty(CadDimStyleEditor.Measures(sample, values, null));

            // The spacing of baseline dimensions is measured on the Lines sample alone.
            Assert.Empty(CadDimStyleEditor.Measures(sample, values, "DIMDLI"));
            Assert.Throws<ArgumentNullException>(() => CadDimStyleEditor.Measures(null, values, "DIMEXE"));
            Assert.Throws<ArgumentNullException>(() => CadDimStyleEditor.Measures(sample, null, "DIMEXE"));
        }

        [Fact]
        public void AnExtensionLineLeftOut_IsAbsentFromThePartsAndGivenAsAGhostMeasure()
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document);
            values["Dimse1"] = true;
            CadStyleSample sample = Sample(document, values, CadDimSampleKind.Overview);

            Assert.DoesNotContain(sample.Parts, p => p.Dimension == 0 && p.Part == CadStylePart.ExtensionLine1);
            Assert.Contains(sample.Parts, p => p.Dimension == 0 && p.Part == CadStylePart.ExtensionLine2);

            CadStyleMeasure ghost = Assert.Single(CadDimStyleEditor.Measures(sample, values, "DIMSE1"));
            Assert.Equal("DIMSE1", ghost.Key);
            AssertPoint(0.0, 20.625, ghost.From, "the ghost's start");
            AssertPoint(0.0, 33.25, ghost.To, "the ghost's end");
            Assert.Empty(CadDimStyleEditor.Measures(sample, values, "DIMSE2"));

            // Not left out, nothing is a ghost: the part itself is lit.
            values["Dimse1"] = false;
            Assert.Empty(
                CadDimStyleEditor.Measures(Sample(document, values, CadDimSampleKind.Overview), values, "DIMSE1")
            );
        }

        [Fact]
        public void ADimensionLineHalfAndTheCentreMarkLeftOut_AreGhostMeasuresToo()
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document);
            values["Dimsd2"] = true;
            values["Dimcen"] = 0.0;
            CadStyleSample sample = Sample(document, values, CadDimSampleKind.SymbolsAndArrows);

            CadStyleMeasure half = Assert.Single(CadDimStyleEditor.Measures(sample, values, "DIMSD2"));
            AssertPoint(16.0, 32.0, half.From, "the second half starts at the middle");
            AssertPoint(29.5, 32.0, half.To, "and ends at the back of arrowhead 2");
            Assert.DoesNotContain(sample.Parts, p => p.Part == CadStylePart.CentreMark);

            CadStyleMeasure mark = Assert.Single(CadDimStyleEditor.Measures(sample, values, "DIMCEN"));
            AssertPoint(50.0, 10.0, mark.From, "the ghost mark's centre");
            AssertNear(2.5, LengthOf(mark), "the ghost mark's half");
            Assert.Empty(CadDimStyleEditor.Measures(sample, values, "DIMCEN#"));
        }

        [Fact]
        public void AFieldOfOneDimensionOfASample_LightsThatDimensionsPartsAlone()
        {
            CadDocument document = Blank();
            SortedDictionary<string, object> values = Values(document);

            CadStyleSample lines = Sample(document, values, CadDimSampleKind.Lines);
            IReadOnlyList<CadStyleSamplePart> spacing = CadDimStyleEditor.LitParts(lines, "DIMDLI");
            Assert.NotEmpty(spacing);
            Assert.All(spacing, p => Assert.Equal(1, p.Dimension));

            IReadOnlyList<CadStyleSamplePart> both = CadDimStyleEditor.LitParts(lines, "DIMEXE");
            Assert.Equal(new[] { 0, 0, 1, 1 }, both.Select(p => p.Dimension).ToArray());

            CadStyleSample units = Sample(document, values, CadDimSampleKind.PrimaryUnits);
            Assert.Equal(new[] { 0 }, CadDimStyleEditor.LitParts(units, "DIMDEC").Select(p => p.Dimension).ToArray());
            Assert.Equal(new[] { 1 }, CadDimStyleEditor.LitParts(units, "DIMADEC").Select(p => p.Dimension).ToArray());
            Assert.Empty(CadDimStyleEditor.LitParts(units, "DIMSCALE"));
        }

        // ------------------------------------------------------------------- multileaders

        private static CadMLeaderStyleSettings Settings() => new CadMLeaderStyleSettings();

        private static CadStylePart[] PartsInOrder(CadStyleSample sample) => sample.Parts.Select(p => p.Part).ToArray();

        private static void AssertPieceOrder(
            CadDocument document,
            CadMLeaderStyleSettings settings,
            CadStylePart[] expected,
            Type[] types
        )
        {
            CadStyleSample sample = CadMLeaderStyleEditor.PreviewSample(document.Database, settings);

            // The pieces come in the order the library's explode gives them: what the preview draws is the multileader's own.
            DxfDatabase whole = CadMLeaderStyleEditor.Sample(document.Database, settings);
            DxfMLeader mleader = whole.ModelSpace.OfType<DxfMLeader>().Single();
            Type[] exploded = mleader.ExplodeBroken().Select(e => e.GetType()).ToArray();
            Assert.Equal(exploded, Entities(sample).Select(e => e.GetType()).ToArray());
            Assert.Equal(types, exploded);
            Assert.Equal(expected, Entities(sample).Select(e => sample.PartOf(e.Handle)).ToArray());
            Assert.Equal(expected, PartsInOrder(sample));
            Assert.Empty(sample.Untagged);
            Assert.Empty(Entities(sample).OfType<DxfMLeader>());
        }

        [Fact]
        public void TheMultileadersPieces_AreTheWordsTheLandingTheLeaderLineAndTheArrow_ByDefault()
        {
            AssertPieceOrder(
                Blank(),
                Settings(),
                new[] { CadStylePart.Text, CadStylePart.Landing, CadStylePart.LeaderLine, CadStylePart.Arrow },
                new[] { typeof(DxfMText), typeof(DxfLine), typeof(DxfPolyline), typeof(DxfSolid) }
            );
        }

        [Fact]
        public void TheMultileadersPieces_AreTheBlockFirst_ForBlockContent()
        {
            CadDocument document = Blank();
            DxfBlockTableRecord tag = new DxfBlockTableRecord { Name = "TAG" };
            document.Database.BlockTable.Add(tag);
            tag.AppendEntity(new DxfCircle(new GeoPoint3(0, 0, 0), new GeoVector3(0, 0, 1), 5.0));
            CadMLeaderStyleSettings settings = Settings();
            settings.ContentType = DxfContentType.BlockContent;
            settings.BlockName = "TAG";

            CadStyleSample sample = CadMLeaderStyleEditor.PreviewSample(document.Database, settings);

            Assert.Equal(CadStylePart.Block, PartsInOrder(sample)[0]);
            Assert.Equal(typeof(DxfBlockReference), Entities(sample).First().GetType());
            Assert.Equal(CadStylePart.Block, sample.PartOf(Entities(sample).First().Handle));
            Assert.DoesNotContain(CadStylePart.Text, PartsInOrder(sample));
            Assert.Equal(CadStylePart.Arrow, PartsInOrder(sample).Last());
            Assert.Empty(sample.Untagged);
        }

        [Fact]
        public void TheMultileadersPieces_HaveNoLandingForASplineOrForNoLanding_AndNoArrowForNone()
        {
            CadDocument document = Blank();

            CadMLeaderStyleSettings spline = Settings();
            spline.LeaderType = DxfLeaderType.SplineLeader;
            CadStyleSample splined = CadMLeaderStyleEditor.PreviewSample(document.Database, spline);
            Assert.Equal(
                new[] { CadStylePart.Text, CadStylePart.LeaderLine, CadStylePart.Arrow },
                PartsInOrder(splined)
            );
            Assert.Contains(Entities(splined), e => e is DxfSpline);

            CadMLeaderStyleSettings none = Settings();
            none.SetLandingDistance = false;
            CadStyleSample unlanded = CadMLeaderStyleEditor.PreviewSample(document.Database, none);
            Assert.Equal(
                new[] { CadStylePart.Text, CadStylePart.LeaderLine, CadStylePart.Arrow },
                PartsInOrder(unlanded)
            );

            CadMLeaderStyleSettings noArrow = Settings();
            noArrow.Arrowhead = "None";
            CadStyleSample bare = CadMLeaderStyleEditor.PreviewSample(document.Database, noArrow);
            // Arrowhead None is the insert of an empty block (_None), which draws nothing, and still the multileader's arrow piece.
            Assert.Equal(
                new[] { CadStylePart.Text, CadStylePart.Landing, CadStylePart.LeaderLine, CadStylePart.Arrow },
                PartsInOrder(bare)
            );
            Assert.IsType<DxfBlockReference>(
                Entity(bare, bare.Parts.Single(p => p.Part == CadStylePart.Arrow).Handles.Single())
            );
            Assert.Empty(bare.Untagged);
        }

        [Fact]
        public void AMultileaderStyleThatLeavesTheLandingOut_DrawsNoLanding_AsAutoCadDoesNot()
        {
            CadDocument document = Blank();
            CadMLeaderStyleSettings off = Settings();
            off.IncludeLanding = false;

            CadStyleSample sample = CadMLeaderStyleEditor.PreviewSample(document.Database, off);

            Assert.Equal(
                new[] { CadStylePart.Text, CadStylePart.LeaderLine, CadStylePart.Arrow },
                PartsInOrder(sample)
            );
            Assert.Empty(Entities(sample).OfType<DxfLine>());
            Assert.Empty(CadMLeaderStyleEditor.Measures(sample, off, "LandingDistance"));
            Assert.Equal(
                new[] { CadStylePart.Text, CadStylePart.Landing, CadStylePart.LeaderLine, CadStylePart.Arrow },
                PartsInOrder(CadMLeaderStyleEditor.PreviewSample(document.Database, Settings()))
            );
        }

        [Fact]
        public void EverySettingOfTheMultileaderDialog_NamesItsParts_InTheOrderOfTheDialog()
        {
            Assert.Equal(new[] { CadStylePart.LeaderLine }, CadMLeaderStyleEditor.PartsOf("LeaderType").ToArray());
            Assert.Equal(
                new[] { CadStylePart.LeaderLine, CadStylePart.Landing, CadStylePart.Arrow },
                CadMLeaderStyleEditor.PartsOf("LeaderColor").ToArray()
            );
            Assert.Equal(
                new[] { CadStylePart.Landing, CadStylePart.Text },
                CadMLeaderStyleEditor.PartsOf("landinggap").ToArray()
            );
            Assert.Equal(
                new[] { CadStylePart.Text, CadStylePart.Block },
                CadMLeaderStyleEditor.PartsOf("ContentType").ToArray()
            );
            Assert.Empty(CadMLeaderStyleEditor.PartsOf("BreakSize"));
            Assert.Empty(CadMLeaderStyleEditor.PartsOf("Annotative"));
            Assert.Empty(CadMLeaderStyleEditor.PartsOf("Scale"));
            Assert.Empty(CadMLeaderStyleEditor.PartsOf("NoSuchSetting"));

            Assert.Equal(
                new[] { "LeaderColor", "Arrowhead", "ArrowSize" },
                CadMLeaderStyleEditor.KeysOf(CadStylePart.Arrow).ToArray()
            );
            Assert.Equal(
                new[]
                {
                    "LeaderType",
                    "LeaderColor",
                    "LeaderLinetype",
                    "LeaderLineweight",
                    "MaxLeaderPoints",
                    "FirstSegmentAngle",
                },
                CadMLeaderStyleEditor.KeysOf(CadStylePart.LeaderLine).ToArray()
            );
            Assert.Equal(
                new[] { "ContentType", "BlockName", "BlockConnection", "BlockColor", "BlockScale" },
                CadMLeaderStyleEditor.KeysOf(CadStylePart.Block).ToArray()
            );

            // Every setting of the dialog has a row, and each part of the sample is set by one.
            CadDocument document = Blank();
            CadStyleSample sample = CadMLeaderStyleEditor.PreviewSample(document.Database, Settings());
            Assert.All(sample.Parts, p => Assert.NotEmpty(CadMLeaderStyleEditor.KeysOf(p.Part)));
        }

        [Fact]
        public void TheMultileadersCallouts_AreTheArrowSizeTheLandingDistanceAndTheLandingGap()
        {
            CadDocument document = Blank();
            CadMLeaderStyleSettings settings = Settings();
            CadStyleSample sample = CadMLeaderStyleEditor.PreviewSample(document.Database, settings);

            // Arrow tip (0, 0), landing from (15, 10) to (23, 10), the words from x 25.
            var landing = (DxfLine)Entity(
                sample,
                sample.Parts.Single(p => p.Part == CadStylePart.Landing).Handles.Single()
            );
            AssertPoint(15.0, 10.0, new GeoPoint2(landing.StartPoint.X, landing.StartPoint.Y), "landing start");
            AssertPoint(23.0, 10.0, new GeoPoint2(landing.EndPoint.X, landing.EndPoint.Y), "landing end");

            CadStyleMeasure arrow = Assert.Single(CadMLeaderStyleEditor.Measures(sample, settings, "ArrowSize"));
            Assert.Equal("ArrowSize = 2.5", arrow.Label);
            AssertNear(2.5, LengthOf(arrow), "ArrowSize length");
            AssertPoint(0.0, 0.0, arrow.From, "ArrowSize from the tip");

            CadStyleMeasure distance = Assert.Single(
                CadMLeaderStyleEditor.Measures(sample, settings, "LandingDistance")
            );
            Assert.Equal("LandingDistance = 8.0", distance.Label);
            AssertPoint(15.0, 10.0, distance.From, "LandingDistance from");
            AssertPoint(23.0, 10.0, distance.To, "LandingDistance to");

            CadStyleMeasure gap = Assert.Single(CadMLeaderStyleEditor.Measures(sample, settings, "LandingGap"));
            Assert.Equal("LandingGap = 2.0", gap.Label);
            AssertPoint(23.0, 10.0, gap.From, "LandingGap from the landing's end");
            AssertPoint(25.0, 10.0, gap.To, "LandingGap to the words");

            CadStyleMeasure height = Assert.Single(CadMLeaderStyleEditor.Measures(sample, settings, "TextHeight"));
            AssertNear(2.5, LengthOf(height), "TextHeight length");

            Assert.Empty(CadMLeaderStyleEditor.Measures(sample, settings, "LeaderColor"));
            Assert.Empty(CadMLeaderStyleEditor.Measures(sample, settings, "BreakSize"));
            Assert.Throws<ArgumentNullException>(() => CadMLeaderStyleEditor.Measures(sample, null, "ArrowSize"));
            Assert.Throws<ArgumentNullException>(() => CadMLeaderStyleEditor.PreviewSample(document.Database, null));

            // A first segment held to 45 degrees is an arc from the x axis.
            settings.FirstSegmentAngle = DxfAngleConstraint.Degrees045;
            CadStyleMeasure angle = Assert.Single(
                CadMLeaderStyleEditor.Measures(sample, settings, "FirstSegmentAngle")
            );
            AssertNear(Math.PI / 4.0, angle.Angle.Value, "FirstSegmentAngle");
        }

        [Fact]
        public void TheMultileaderSample_DrawsWhatTheOldSampleDraws_ItemForItem()
        {
            CadDocument document = Blank();
            CadMLeaderStyleSettings settings = Settings();

            List<Item> before = Items(CadMLeaderStyleEditor.Sample(document.Database, settings));
            List<Item> after = Items(CadMLeaderStyleEditor.PreviewSample(document.Database, settings).Database);

            Assert.True(before.Count >= 4, "the old sample draws " + before.Count + " items");
            Assert.Equal(before.Count, after.Count);

            for (int i = 0; i < before.Count; i++)
            {
                Assert.Equal(before[i].Kind, after[i].Kind);
                Assert.Equal(before[i].Numbers.Length, after[i].Numbers.Length);

                for (int n = 0; n < before[i].Numbers.Length; n++)
                {
                    AssertNear(
                        before[i].Numbers[n],
                        after[i].Numbers[n],
                        "item " + i + " (" + before[i].Kind + ") number " + n
                    );
                }
            }
        }
    }
}
