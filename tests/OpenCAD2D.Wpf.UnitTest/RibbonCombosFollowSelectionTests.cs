using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The combos of Home › Layers and Home › Properties (Layer, Color, Linetype, Lineweight) as AutoCAD 2021 behaves, through
    /// the view model alone (no window): with nothing selected they show the current layer, colour, linetype and lineweight
    /// and choosing sets the current one; with one object selected, or several that agree, they show that object's and
    /// choosing applies it to the selected objects as one undo step, the current setting untouched; with several that differ
    /// the combo is blank and choosing applies to all; Esc brings the current values back. The room is a wall (a line) and a
    /// column (a circle, on COLUMNS, red), the wall on WALLS (white); the layers are 0, WALLS, COLUMNS and FLOOR, the linetypes
    /// OC_DASH and OC_DOT are loaded, and the drawing's current layer is 0.
    /// </summary>
    public sealed class RibbonCombosFollowSelectionTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        /// <summary>
        /// One of the four combos: how the view model reads and sets it, the label of its row in Properties, and five values of
        /// it: the wall's, the column's, the current one, one both are given to agree and the one that is chosen.
        /// </summary>
        public sealed class Combo
        {
            public Combo(
                string name,
                Func<MainViewModel, string> read,
                Action<MainViewModel, string> write,
                string wall,
                string column,
                string current,
                string chosen
            )
            {
                Name = name;
                Read = read;
                Write = write;
                Wall = wall;
                Column = column;
                Current = current;
                Chosen = chosen;
            }

            public string Name { get; }
            public Func<MainViewModel, string> Read { get; }
            public Action<MainViewModel, string> Write { get; }
            public string Wall { get; }
            public string Column { get; }
            public string Current { get; }
            public string Chosen { get; }

            public override string ToString() => Name;
        }

        private static readonly Combo[] Combos =
        {
            new Combo(
                "Layer",
                m => m.CurrentLayer?.Name ?? string.Empty,
                (m, v) => m.CurrentLayer = m.Layers.First(l => l.Name == v),
                "WALLS",
                "COLUMNS",
                "0",
                "FLOOR"
            ),
            new Combo(
                "Color",
                m => m.CurrentColor ?? string.Empty,
                (m, v) => m.CurrentColor = v,
                "Green",
                "Blue",
                "Yellow",
                "Cyan"
            ),
            new Combo(
                "Linetype",
                m => m.CurrentLinetype ?? string.Empty,
                (m, v) => m.CurrentLinetype = v,
                "ByLayer",
                "OC_DASH",
                "ByBlock",
                "OC_DOT"
            ),
            new Combo(
                "Lineweight",
                m => m.CurrentLineweight ?? string.Empty,
                (m, v) => m.CurrentLineweight = v,
                "0.25 mm",
                "0.50 mm",
                "0.35 mm",
                "1.00 mm"
            ),
        };

        public static IEnumerable<object[]> AllCombos() => Combos.Select(c => new object[] { c.Name });

        private static Combo Of(string name) => Combos.Single(c => c.Name == name);

        /// <summary>Opens the room with the wall and the column given the values of the combo, and the combo's current value set.</summary>
        private MainViewModel Opened(Combo combo, out string wall, out string column)
        {
            DxfDatabase db = TestFiles.Room(out wall, out column);
            db.LayerTable.Add(new DxfLayerTableRecord { Name = "FLOOR", ColorIndex = 3 });
            string lin = _folder.WriteText(
                "gh.lin",
                "*OC_DASH,Dashes __ __ __\nA,100,-50\n*OC_DOT,Dots . . .\nA,0,-50\n"
            );
            db.LoadLineTypeFile("OC_DASH", lin);
            db.LoadLineTypeFile("OC_DOT", lin);
            db.Clayer = "0";
            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
            Assert.True(model.Open(_folder.Write("room.dxf", db)));
            CadDocument document = model.Document;
            Assert.True(CadPropertyEditor.Set(document, new[] { wall }, combo.Name, combo.Wall).Succeeded);
            Assert.True(CadPropertyEditor.Set(document, new[] { column }, combo.Name, combo.Column).Succeeded);
            Assert.True(CadPropertyEditor.Set(document, null, combo.Name, combo.Current).Succeeded);
            document.Selection.Clear();
            return model;
        }

        private static string ObjectValue(MainViewModel model, string handle, string label) =>
            CadProperties
                .Describe(model.Document, null, new[] { handle })
                .Groups.SelectMany(g => g.Rows)
                .First(r => r.Label == label)
                .Value;

        private static string CurrentSetting(MainViewModel model, string label) =>
            CadProperties
                .Describe(model.Document, null, null)
                .Groups.SelectMany(g => g.Rows)
                .First(r => r.Label == label)
                .Value;

        [Theory]
        [MemberData(nameof(AllCombos))]
        public void NothingSelected_ShowsTheCurrentSetting_AndChoosingSetsIt(string name)
        {
            Combo combo = Of(name);
            MainViewModel model = Opened(combo, out string wall, out string column);

            Assert.Equal(combo.Current, combo.Read(model));

            combo.Write(model, combo.Chosen);

            Assert.Equal(combo.Chosen, combo.Read(model));
            Assert.Equal(combo.Chosen, CurrentSetting(model, name));
            Assert.Equal(combo.Wall, ObjectValue(model, wall, name));
            Assert.Equal(combo.Column, ObjectValue(model, column, name));
        }

        [Theory]
        [MemberData(nameof(AllCombos))]
        public void OneObjectSelected_ShowsItsValue(string name)
        {
            Combo combo = Of(name);
            MainViewModel model = Opened(combo, out string wall, out string column);

            model.Document.Selection.Set(new[] { column });

            Assert.Equal(combo.Column, combo.Read(model));

            model.Document.Selection.Set(new[] { wall });

            Assert.Equal(combo.Wall, combo.Read(model));
        }

        [Theory]
        [MemberData(nameof(AllCombos))]
        public void OneObjectSelected_ChoosingAppliesToItAsOneUndoStep_AndTheCurrentSettingStays(string name)
        {
            Combo combo = Of(name);
            MainViewModel model = Opened(combo, out string wall, out string column);
            model.Document.Selection.Set(new[] { column });

            combo.Write(model, combo.Chosen);

            Assert.Equal(combo.Chosen, combo.Read(model));
            Assert.Equal(combo.Chosen, ObjectValue(model, column, name));
            Assert.Equal(combo.Wall, ObjectValue(model, wall, name));
            Assert.Equal(combo.Current, CurrentSetting(model, name));

            Assert.True(model.Document.Undo());

            Assert.Equal(combo.Column, ObjectValue(model, column, name));
            Assert.Equal(combo.Column, combo.Read(model));
            Assert.Equal(combo.Current, CurrentSetting(model, name));
        }

        [Theory]
        [MemberData(nameof(AllCombos))]
        public void SeveralObjectsThatAgree_ShowTheValue(string name)
        {
            Combo combo = Of(name);
            MainViewModel model = Opened(combo, out string wall, out string column);
            Assert.True(CadPropertyEditor.Set(model.Document, new[] { column }, name, combo.Wall).Succeeded);

            model.Document.Selection.Set(new[] { wall, column });

            Assert.Equal(combo.Wall, combo.Read(model));
        }

        [Theory]
        [MemberData(nameof(AllCombos))]
        public void SeveralObjectsThatDiffer_ShowABlankCombo(string name)
        {
            Combo combo = Of(name);
            MainViewModel model = Opened(combo, out string wall, out string column);

            model.Document.Selection.Set(new[] { wall, column });

            Assert.Equal(string.Empty, combo.Read(model));
        }

        [Theory]
        [MemberData(nameof(AllCombos))]
        public void SeveralObjectsThatDiffer_ChoosingAppliesToAllAsOneUndoStep(string name)
        {
            Combo combo = Of(name);
            MainViewModel model = Opened(combo, out string wall, out string column);
            model.Document.Selection.Set(new[] { wall, column });

            combo.Write(model, combo.Chosen);

            Assert.Equal(combo.Chosen, combo.Read(model));
            Assert.Equal(combo.Chosen, ObjectValue(model, wall, name));
            Assert.Equal(combo.Chosen, ObjectValue(model, column, name));
            Assert.Equal(combo.Current, CurrentSetting(model, name));

            Assert.True(model.Document.Undo());

            Assert.Equal(combo.Wall, ObjectValue(model, wall, name));
            Assert.Equal(combo.Column, ObjectValue(model, column, name));
            Assert.Equal(string.Empty, combo.Read(model));
        }

        [Theory]
        [MemberData(nameof(AllCombos))]
        public void EscapeClearsTheSelection_AndTheComboShowsTheCurrentSettingAgain(string name)
        {
            Combo combo = Of(name);
            MainViewModel model = Opened(combo, out string wall, out string column);
            model.Document.Selection.Set(new[] { column });
            Assert.Equal(combo.Column, combo.Read(model));

            model.Escape();

            Assert.Empty(model.Document.Selection.Handles);
            Assert.Equal(combo.Current, combo.Read(model));
        }

        [Fact]
        public void SelectingAndClearing_RaisesTheFourCombosForTheBoxesToRead()
        {
            MainViewModel model = Opened(Of("Layer"), out _, out string column);
            var raised = new List<string>();
            model.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

            model.Document.Selection.Set(new[] { column });

            Assert.Contains(nameof(MainViewModel.CurrentLayer), raised);
            Assert.Contains(nameof(MainViewModel.CurrentColor), raised);
            Assert.Contains(nameof(MainViewModel.CurrentLinetype), raised);
            Assert.Contains(nameof(MainViewModel.CurrentLineweight), raised);

            raised.Clear();
            model.Document.Selection.Clear();

            Assert.Contains(nameof(MainViewModel.CurrentLayer), raised);
            Assert.Contains(nameof(MainViewModel.CurrentColor), raised);
            Assert.Contains(nameof(MainViewModel.CurrentLinetype), raised);
            Assert.Contains(nameof(MainViewModel.CurrentLineweight), raised);
        }

        [Fact]
        public void AnObjectOnALockedLayer_RefusesTheChoice_SaidOnTheCommandLine_AndTheComboKeepsItsValue()
        {
            Combo combo = Of("Color");
            MainViewModel model = Opened(combo, out _, out string column);
            Assert.True(CadLayerEditor.SetLocked(model.Document, "COLUMNS", true).Succeeded);
            model.Document.Selection.Set(new[] { column });

            model.CurrentColor = "Cyan";

            Assert.Equal("Blue", model.CurrentColor);
            Assert.Equal("Blue", ObjectValue(model, column, "Color"));
            Assert.Contains("locked", model.History.Last());
        }

        [Fact]
        public void TheLayerBox_WithAnObjectSelected_ShowsItsLayerAndNotTheCurrentOne()
        {
            Combo combo = Of("Layer");
            MainViewModel model = Opened(combo, out _, out string column);
            model.Document.Selection.Set(new[] { column });

            Assert.Equal("COLUMNS", model.CurrentLayer.Name);
            Assert.NotEqual(model.Document.Database.Clayer, model.CurrentLayer.Name);
        }

        [Fact]
        public void MoreColors_WithAnObjectSelected_ShowsTheDialogOnItsColour_AndAppliesTheChoiceToIt()
        {
            Combo combo = Of("Color");
            MainViewModel model = Opened(combo, out _, out string column);
            model.Document.Selection.Set(new[] { column });
            DxfColor offered = null;
            model.PickColor = dialog =>
            {
                offered = dialog.Original;
                dialog.PickIndex(30);
                return true;
            };

            model.SelectCurrentColorCommand.Execute(null);

            Assert.Equal(5, offered.ColorIndex);
            Assert.Equal("30", ObjectValue(model, column, "Color"));
            Assert.Equal("30", model.CurrentColor);
            Assert.Equal("Yellow", CurrentSetting(model, "Color"));
        }

        [Fact]
        public void ByLayerIsDrawnInTheColourOfTheLayerOfTheSelectedObjects_TheCurrentLayerWithNoneSelected_AndWhiteWhereTheyDiffer()
        {
            MainViewModel model = Opened(Of("Layer"), out string wall, out string column);

            // Nothing selected: the current layer, 0, white.
            Assert.Equal("ByLayer", model.CurrentColor);
            Assert.Equal(0xFFFFFF, model.ByLayerRgb);
            Assert.Equal(0xFFFFFF, model.CurrentColorRgb);

            // The column is on COLUMNS, red; its colour ByLayer is red.
            Assert.True(CadPropertyEditor.Set(model.Document, new[] { column }, "Color", "ByLayer").Succeeded);
            Assert.True(CadPropertyEditor.Set(model.Document, new[] { column }, "Layer", "COLUMNS").Succeeded);
            model.Document.Selection.Set(new[] { column });

            Assert.Equal("ByLayer", model.CurrentColor);
            Assert.Equal(0xFF0000, model.ByLayerRgb);
            Assert.Equal(0xFF0000, model.CurrentColorRgb);

            // A colour of its own, whatever the layer.
            Assert.True(CadPropertyEditor.Set(model.Document, new[] { column }, "Color", "Green").Succeeded);

            Assert.Equal("Green", model.CurrentColor);
            Assert.Equal(0x00FF00, model.CurrentColorRgb);

            // Two objects on two layers: one layer is not the one, ByLayer is drawn white.
            model.Document.Selection.Set(new[] { wall, column });

            Assert.Equal(0xFFFFFF, model.ByLayerRgb);

            // The current layer changed with an object selected: the swatch stays the selection's.
            Assert.True(CadLayerEditor.SetCurrent(model.Document, "FLOOR").Succeeded);
            model.Document.Selection.Set(new[] { column });

            Assert.Equal(0xFF0000, model.ByLayerRgb);

            model.Document.Selection.Clear();

            Assert.Equal(0x00FF00, model.ByLayerRgb);
        }

        [Fact]
        public void TheSwatchOfTheColorBox_ByLayerHasTheLayersColour_ByBlockWhite_WhiteSplit_AndTheOthersTheirOwn()
        {
            MainViewModel model = Opened(Of("Layer"), out _, out string column);
            model.Document.Selection.Set(new[] { column });
            var converter = new ColorTextSwatchConverter();

            Brush Swatch(string text) =>
                (Brush)
                    converter.Convert(
                        new object[] { text, model, model.ByLayerRgb },
                        typeof(Brush),
                        null,
                        System.Globalization.CultureInfo.InvariantCulture
                    );

            Assert.Equal(Color.FromRgb(0xFF, 0, 0), ((SolidColorBrush)Swatch("ByLayer")).Color);
            Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), ((SolidColorBrush)Swatch("ByBlock")).Color);
            Assert.Equal(Color.FromRgb(0, 0xFF, 0), ((SolidColorBrush)Swatch("Green")).Color);
            Assert.IsType<LinearGradientBrush>(Swatch("White"));
            Assert.Null(Swatch(string.Empty));
        }
    }
}
