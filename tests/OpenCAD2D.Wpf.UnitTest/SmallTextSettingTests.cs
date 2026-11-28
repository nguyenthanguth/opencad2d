using System;
using System.IO;
using System.Linq;
using System.Threading;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The setting that draws a text too small to read as a bar, as AutoCAD does: on by default, kept in the settings file, set
    /// by the system variable SMALLTEXTBARS.
    /// </summary>
    public sealed class SmallTextSettingTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        [Fact]
        public void SmallTextAsBars_IsOnByDefault_AndSurvivesTheSettingsFileEitherWay()
        {
            Assert.True(new GlobalSettings().SmallTextAsBars);
            Assert.True(new CanvasOptions().SmallTextAsBars);

            var settings = new GlobalSettings { SmallTextAsBars = false };
            string path = Path.Combine(_folder.FullPath, "settings.json");
            settings.Save(path);

            Assert.Contains("\"smallTextAsBars\": false", File.ReadAllText(path));
            Assert.False(GlobalSettings.Load(path).SmallTextAsBars);

            settings.SmallTextAsBars = true;
            settings.Save(path);
            Assert.True(GlobalSettings.Load(path).SmallTextAsBars);
        }

        [Fact]
        public void ASettingsFileOfBeforeTheSetting_ReadsItOn()
        {
            string path = Path.Combine(_folder.FullPath, "old.json");
            File.WriteAllText(path, "{ \"appearance\": { \"showLineWeights\": false } }");

            GlobalSettings read = GlobalSettings.Load(path);

            Assert.False(read.ShowLineWeights);
            Assert.True(read.SmallTextAsBars);
        }

        [Fact]
        public void TheVariableSmallTextBars_SetsTheSettingAndTellsTheWindow_WithNoUndoStep()
        {
            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
            model.OpenDocument(CadDocument.From(TestFiles.Room(out _, out _), "room.dxf"));
            int applied = 0;
            model.SettingsApplied += (s, e) => applied++;

            model.Execute("SMALLTEXTBARS");
            Assert.Equal("Enter new value for SMALLTEXTBARS <ON>:", model.Prompt);
            model.Execute("OFF");

            Assert.False(model.Settings.SmallTextAsBars);
            Assert.Equal(1, applied);
            Assert.False(model.Document.CanUndo);

            model.Execute("SMALLTEXTBARS");
            model.Execute("ON");
            Assert.True(model.Settings.SmallTextAsBars);
            Assert.Equal(2, applied);
        }

        [Fact]
        public void TheDisplaySectionOffersSmallText_AfterGridAndLineweights_AndOkSavesItAndGivesItToTheCanvas()
        {
            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
            GlobalSettingsViewModel dialog = model.CreateGlobalSettings();
            GlobalSettingSection display = dialog.Sections.Single(s => s.Title == "Display");

            Assert.Equal(
                new[]
                {
                    "Grid",
                    "Show lineweights",
                    "Draw text too small to read (as AutoCAD; off hides it when zoomed out)",
                },
                display.Rows.Select(r => r.Label)
            );

            GlobalSettingRow row = display.Rows[2];
            Assert.Equal(GlobalSettingKind.Toggle, row.Kind);
            Assert.True(row.IsOn);

            row.IsOn = false;
            Assert.True(dialog.Ok());

            Assert.False(model.Settings.SmallTextAsBars);
            Assert.False(GlobalSettings.Load(model.SettingsPath).SmallTextAsBars);

            bool bars = true;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var workspace = new CadWorkspace { DataContext = model };
                    model.ApplySettings(model.Settings);
                    bars = workspace.DrawingCanvas.Options.SmallTextAsBars;
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
            Assert.False(bars);
        }
    }
}
