using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The DRAWINGS list with its button left of Open: off, every file of the folders; on, only the drawings open in the app,
    /// each once under its folder (the folders in the order their first drawing was opened, the drawings in the order they
    /// were opened), a drawing never saved under "Not saved". The choice is kept in the global settings.
    /// </summary>
    public sealed class DrawingsOpenOnlyTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();
        private readonly TestFolder _other = new TestFolder();

        public void Dispose()
        {
            _folder.Dispose();
            _other.Dispose();
        }

        [Fact]
        public void TheListShowsTheOpenDrawingsOnlyByDefault_AndTheSettingIsOn()
        {
            WriteIn(_folder, "a.dxf");
            WriteIn(_folder, "b.dxf");
            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
            Assert.True(model.Open(Path.Combine(_folder.FullPath, "a.dxf")));

            Assert.True(model.DrawingsShowOpenOnly);
            Assert.True(new GlobalSettings().DrawingsShowOpenOnly);
            Assert.Equal(new[] { "a.dxf" }, Files(model));
            Assert.Equal("Show every file of the folder", model.DrawingsShowOpenOnlyToolTip);
        }

        [Fact]
        public void ASettingsFileWithoutTheKeyReadsOn_AndOneThatStoresFalseKeepsFalse()
        {
            string old = Path.Combine(_folder.FullPath, "old.json");
            File.WriteAllText(old, "{\"panels\":{\"fileListOnRight\":true}}");
            GlobalSettings read = GlobalSettings.Load(old);
            Assert.True(read.FileListOnRight);
            Assert.True(read.DrawingsShowOpenOnly);

            string off = Path.Combine(_folder.FullPath, "off.json");
            File.WriteAllText(off, "{\"panels\":{\"drawingsShowOpenOnly\":false}}");
            Assert.False(GlobalSettings.Load(off).DrawingsShowOpenOnly);

            string on = Path.Combine(_folder.FullPath, "on.json");
            new GlobalSettings { DrawingsShowOpenOnly = false }.Save(on);
            Assert.False(GlobalSettings.Load(on).DrawingsShowOpenOnly);
            new GlobalSettings().Save(on);
            Assert.True(GlobalSettings.Load(on).DrawingsShowOpenOnly);
        }

        [Fact]
        public void ResetToDefaultsTurnsItOn()
        {
            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
            model.DrawingsShowOpenOnly = false;
            GlobalSettingsViewModel dialog = model.CreateGlobalSettings();
            Assert.False(dialog.Working.DrawingsShowOpenOnly);

            dialog.Reset();

            Assert.True(dialog.Working.DrawingsShowOpenOnly);
        }

        [Fact]
        public void On_TheListHoldsTheOpenDrawingsOnly_UnderTheirFolders_InTheOrderTheyWereOpened()
        {
            MainViewModel model = ThreeFiles(out string a, out _, out string c);

            // The folder of d is opened from first, and its name sorts after that of the other: a sort by name would list it last.
            string zeta = Path.Combine(_folder.FullPath, "zeta");
            Directory.CreateDirectory(zeta);
            string d = Path.Combine(zeta, "d.dxf");
            TestFiles.Room(out _, out _).Save(d);

            // d is opened first, so its folder is first; c before a, so that c stands above a in the folder they share.
            Assert.True(model.Open(d));
            Assert.True(model.Open(c));
            Assert.True(model.Open(a));

            model.DrawingsShowOpenOnly = true;

            Assert.True(string.CompareOrdinal(_folder.FullPath, zeta) < 0);
            Assert.Equal(
                new[] { zeta + " · DXF", "d.dxf", _folder.FullPath + " · DXF", "c.dxf", "a.dxf" },
                model.FileRows.Select(r => r.Label).ToArray()
            );
            Assert.All(model.FileRows.Where(r => !r.IsHeader), r => Assert.True(r.IsOpen));
            Assert.Equal("a.dxf", model.FileRows.Single(r => r.IsCurrent).Label);

            // The blue dot means changes not saved, here as in the full list; the front drawing is the selected row, with no dot of its own.
            Assert.All(model.FileRows.Where(r => !r.IsHeader), r => Assert.Equal(CadFileState.None, r.State));
            Assert.DoesNotContain(model.FileRows, r => r.Label == "b.dxf");
            Assert.Equal("Show every file of the folder", model.DrawingsShowOpenOnlyToolTip);
        }

        [Fact]
        public void ADrawingNeverSaved_IsListedUnderNotSaved_AfterTheFolders()
        {
            MainViewModel model = ThreeFiles(out string a, out _, out _);
            model.OpenDocument(CadDocument.From(CadTemplate.CreateMetric(), "Drawing2"), "Drawing2");
            Assert.True(model.Open(a));

            model.DrawingsShowOpenOnly = true;

            Assert.Equal(
                new[] { _folder.FullPath + " · DXF", "a.dxf", "Not saved", "Drawing2" },
                model.FileRows.Select(r => r.Label).ToArray()
            );
            Assert.True(model.FileRows.Single(r => r.Label == "Drawing2").IsOpen);
        }

        [Fact]
        public void TheChoiceIsKeptInTheSettingsFile_AndReadBackWhenTheSettingsAreApplied()
        {
            MainViewModel model = ThreeFiles(out string a, out _, out _);
            model.SettingsPath = Path.Combine(_folder.FullPath, "settings.json");

            model.DrawingsShowOpenOnly = true;

            Assert.True(model.Settings.DrawingsShowOpenOnly);
            Assert.True(GlobalSettings.Load(model.SettingsPath).DrawingsShowOpenOnly);

            var again = new MainViewModel { SettingsPath = model.SettingsPath };
            Assert.True(again.Open(a));
            again.ApplySettings(new GlobalSettings { DrawingsShowOpenOnly = false });
            Assert.Equal(3, again.FileRows.Count(r => !r.IsHeader));
            again.ApplySettings(GlobalSettings.Load(model.SettingsPath));

            Assert.True(again.DrawingsShowOpenOnly);
            Assert.Equal(new[] { "a.dxf" }, Files(again));

            model.DrawingsShowOpenOnly = false;
            Assert.False(GlobalSettings.Load(model.SettingsPath).DrawingsShowOpenOnly);
        }

        [Fact]
        public void OpeningOrClosingADrawing_UpdatesTheListAtOnce()
        {
            MainViewModel model = ThreeFiles(out string a, out string b, out _);
            Assert.True(model.Open(a));
            model.DrawingsShowOpenOnly = true;
            Assert.Equal(new[] { "a.dxf" }, Files(model));

            Assert.True(model.Open(b));
            Assert.Equal(new[] { "a.dxf", "b.dxf" }, Files(model));

            Assert.True(model.CloseDocument());
            Assert.Equal(new[] { "a.dxf" }, Files(model));

            // The file of the one closed is still in the folder; it is not in this list.
            Assert.True(File.Exists(b));
            Assert.DoesNotContain(model.FileRows, r => r.Label == "b.dxf");
        }

        [Fact]
        public void TheChipsCountTheRowsOfTheOpenDrawingsOnly()
        {
            MainViewModel model = ThreeFiles(out string a, out string b, out _);
            Assert.True(model.Open(a));
            Assert.True(model.Open(b));
            Assert.Equal("All 3", model.AllChipText);
            Assert.Equal("DWG/DXF 3", model.DxfChipText);

            model.DrawingsShowOpenOnly = true;

            Assert.Equal("All 2", model.AllChipText);
            Assert.Equal("DXF 2", model.DxfChipText);

            model.DrawingsShowOpenOnly = false;

            Assert.Equal("All 3", model.AllChipText);
            Assert.Equal("DWG/DXF 3", model.DxfChipText);
        }

        [Fact]
        public void SearchAndDoubleClickWorkAsInTheFullList()
        {
            MainViewModel model = ThreeFiles(out string a, out string b, out _);
            Assert.True(model.Open(a));
            Assert.True(model.Open(b));
            model.DrawingsShowOpenOnly = true;

            model.SearchText = "a.";
            Assert.Equal(new[] { "a.dxf" }, Files(model));

            model.SearchText = string.Empty;
            model.Activate(model.FileRows.Single(r => r.Label == "a.dxf"));

            Assert.Equal("a.dxf", model.Document.Title);
            Assert.True(model.FileRows.Single(r => r.Label == "a.dxf").IsCurrent);
            Assert.False(model.FileRows.Single(r => r.Label == "b.dxf").IsCurrent);
        }

        [Fact]
        public void TheRightClickMenuOfARowIsTheSame_AndRefreshDoesNothingInThisView()
        {
            MainViewModel model = ThreeFiles(out string a, out _, out _);
            Assert.True(model.Open(a));
            string headers = string.Join(
                "|",
                model.FileRowMenu(model.FileRows.Single(r => r.Label == "a.dxf")).Select(i => i.Header)
            );

            model.DrawingsShowOpenOnly = true;

            Assert.Equal(
                headers,
                string.Join(
                    "|",
                    model.FileRowMenu(model.FileRows.Single(r => r.Label == "a.dxf")).Select(i => i.Header)
                )
            );

            // The panel's Refresh looks for nothing here: a file that came into the folder is not listed when the full list comes back.
            WriteIn(_folder, "new.dxf");
            model.RefreshCommand.Execute(null);
            model.DrawingsShowOpenOnly = false;
            Assert.DoesNotContain(model.FileRows, r => r.Label == "new.dxf");

            model.RefreshCommand.Execute(null);
            Assert.Contains(model.FileRows, r => r.Label == "new.dxf");
        }

        [Fact]
        public void TheMethodRefresh_StillLooksInTheFolders_WhateverTheView()
        {
            MainViewModel model = ThreeFiles(out string a, out _, out _);
            Assert.True(model.Open(a));
            model.DrawingsShowOpenOnly = true;

            WriteIn(_folder, "new.dxf");
            model.Refresh();
            model.DrawingsShowOpenOnly = false;

            Assert.Contains(model.FileRows, r => r.Label == "new.dxf");
        }

        [Fact]
        public void SaveAsIntoAnotherFolder_MovesTheRowUnderThatFoldersHeaderAtOnce()
        {
            MainViewModel model = ThreeFiles(out string a, out _, out _);
            Assert.True(model.Open(a));
            model.DrawingsShowOpenOnly = true;
            Assert.Equal(new[] { _folder.FullPath + " · DXF", "a.dxf" }, model.FileRows.Select(r => r.Label).ToArray());

            Assert.True(model.SaveAs(Path.Combine(_other.FullPath, "moved.dxf")));

            Assert.Equal(
                new[] { _other.FullPath + " · DXF", "moved.dxf" },
                model.FileRows.Select(r => r.Label).ToArray()
            );
            Assert.True(model.FileRows.Single(r => r.Label == "moved.dxf").IsOpen);
        }

        [Fact]
        public void TheButton_IsPressedAndSaysWhatAClickDoesNow()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
                var workspace = new CadWorkspace { DataContext = model };
                workspace.Measure(new Size(1600, 960));
                workspace.Arrange(new Rect(0, 0, 1600, 960));
                workspace.UpdateLayout();
                var button = (ToggleButton)workspace.FindName("OpenOnlyToggle");

                // On by default: the button comes pressed.
                Assert.True(button.IsChecked);
                model.DrawingsShowOpenOnly = false;
                workspace.UpdateLayout();

                Assert.Equal(20, button.ActualWidth);
                Assert.Equal(20, button.ActualHeight);
                Assert.False(button.IsChecked);
                Assert.Equal("Show open drawings only", button.ToolTip);
                Assert.Equal(Color.FromRgb(0x9A, 0xA4, 0xB1), StrokeOf(button));
                Assert.Equal(Colors.Transparent, ((SolidColorBrush)ChromeOf(button).Background).Color);

                model.DrawingsShowOpenOnly = true;
                workspace.UpdateLayout();

                Assert.True(button.IsChecked);
                Assert.Equal("Show every file of the folder", button.ToolTip);
                Assert.Equal(Color.FromRgb(0xE6, 0xE9, 0xED), StrokeOf(button));

                // Hovered, the pressed button stays #1F4E79: the hover colour of its template holds only while it is not pressed.
                MultiTrigger hover = button
                    .Template.Triggers.OfType<MultiTrigger>()
                    .Single(t => t.Conditions.Any(c => c.Property == UIElement.IsMouseOverProperty));
                Assert.Contains(hover.Conditions, c =>
                    c.Property == ToggleButton.IsCheckedProperty && Equals(c.Value, false)
                );
                var chrome = ChromeOf(button);
                Assert.Equal(Color.FromRgb(0x1F, 0x4E, 0x79), ((SolidColorBrush)chrome.Background).Color);

                // A click on the button turns it back.
                button.IsChecked = false;
                Assert.False(model.DrawingsShowOpenOnly);
                Assert.Equal("Show open drawings only", button.ToolTip);
            });
        }

        private static Border ChromeOf(ToggleButton button) =>
            (Border)Descendants(button).First(d => d is Border b && b.Name == "Chrome");

        private static Color StrokeOf(ToggleButton button) =>
            ((SolidColorBrush)Descendants(button).OfType<IconView>().Single().Stroke).Color;

        private MainViewModel ThreeFiles(out string a, out string b, out string c)
        {
            a = WriteIn(_folder, "a.dxf");
            b = WriteIn(_folder, "b.dxf");
            c = WriteIn(_folder, "c.dxf");
            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };

            // The list is on open drawings only by default; these tests start from the full list.
            model.DrawingsShowOpenOnly = false;
            return model;
        }

        private static string WriteIn(TestFolder folder, string name) =>
            folder.Write(name, TestFiles.Room(out _, out _));

        private static string[] Files(MainViewModel model) =>
            model.FileRows.Where(r => !r.IsHeader).Select(r => r.Label).ToArray();

        private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                yield return child;

                foreach (DependencyObject below in Descendants(child))
                {
                    yield return below;
                }
            }
        }

        private static void OnSta(Action test)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    test();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }
}
