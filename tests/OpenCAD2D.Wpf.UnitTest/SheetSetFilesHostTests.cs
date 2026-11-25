using System;
using System.IO;
using System.Linq;
using System.Text;
using GeometryHelper.Dxf;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// What the app answers the sheet set requests of the commands (A12 3d): SSMSTATE follows the palette, OPENSHEETSET typed opens the
    /// set in the Sheet Set Manager and shows it, the variables are shared among the drawings and kept in the settings file, a drawing
    /// that is a sheet opens its set (SSLOCATE) and shows the palette (SSMAUTOOPEN), and a set the Manager opens takes the .DBL
    /// lock file or opens read-only where another user holds it. The drawings and sets are built in code in a temporary folder; the
    /// settings go to a file there.
    /// </summary>
    public sealed class SheetSetFilesHostTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        private string SetFolder => Path.Combine(_folder.FullPath, "set");

        private string Dst => Path.Combine(SetFolder, "Tower.dst");

        private MainViewModel Model()
        {
            var model = new MainViewModel
            {
                SettingsPath = Path.Combine(_folder.FullPath, "settings", "settings.json"),
            };
            model.Open(_folder.Write("plain.dxf", TestFiles.Room(out _, out _)));
            return model;
        }

        /// <summary>A set Tower with the sheet A-101, made from a layout of a template; the path of the sheet's drawing.</summary>
        private string MadeSet()
        {
            Directory.CreateDirectory(SetFolder);
            string dwt = Path.Combine(_folder.FullPath, "sheets.dwt");
            var database = new DxfDatabase("AC1032");
            database.Layouts.Single(l => l.LayoutName == "Layout1").LayoutName = "A1 sheet";
            CadDocument.From(database, "sheets.dwt").SaveAs(dwt);
            CadSheetSetFile file = CadSheetSetFile.Create(Dst, "Tower", null);
            CadSheetSetSheet sheet = null;
            file.Change(set =>
            {
                set.Template = new CadDrawingReference(
                    CadFileReference.FromPath(dwt, SetFolder),
                    "A1 sheet",
                    null,
                    null
                );
                sheet = set.NewSheet("A-101", "Ground floor");
            });
            return sheet.Layout.File.Resolve(file.Folder);
        }

        [Fact]
        public void SSMSTATE_FollowsThePalette()
        {
            MainViewModel model = Model();

            model.Execute("SSMSTATE");
            Assert.Equal("SSMSTATE = 0 (read only)", model.History.Last());
            model.NoteSheetSetShown(true);
            model.Execute("SSMSTATE");

            Assert.Equal("SSMSTATE = 1 (read only)", model.History.Last());
        }

        [Fact]
        public void ADashOpensheetset_OpensTheSetInTheManager_ShowsThePalette_AndKeepsTheSetInRecent()
        {
            MadeSet();
            MainViewModel model = Model();
            int shown = 0;
            model.ShowSheetSetManager = manager => shown++;

            model.Execute("-OPENSHEETSET");
            model.Execute(Dst);

            Assert.Equal(1, shown);
            Assert.Equal(Dst, model.SheetSetManager.Current.Path, ignoreCase: true);
            Assert.Equal(Dst, model.Settings.RecentSheetSets.First(), ignoreCase: true);
        }

        [Fact]
        public void Opensheetset_WithFileDialogs_AsksThePalettesOpenDialog()
        {
            MadeSet();
            MainViewModel model = Model();
            model.ShowSheetSetManager = manager => manager.AskOpenFile = () => Dst;

            model.Execute("OPENSHEETSET");

            Assert.Equal(Dst, model.SheetSetManager.Current.Path, ignoreCase: true);
            Assert.False(model.Session.IsRunning);
        }

        [Fact]
        public void TheVariables_AreSharedByTheDrawings_AndKeptInTheSettingsFile()
        {
            MainViewModel model = Model();
            model.Execute("SSMPOLLTIME");
            model.Execute("120");
            model.Execute("SSLOCATE");
            model.Execute("0");
            model.Open(_folder.Write("other.dxf", TestFiles.Room(out _, out _)));

            model.Execute("SSMPOLLTIME");
            Assert.Equal("Enter new value for SSMPOLLTIME <120>:", model.Session.PromptText);
            model.Execute("");

            GlobalSettings saved = GlobalSettings.Load(model.SettingsPath);
            Assert.False(saved.SheetSetLocate);
            Assert.Equal(120, saved.SheetSetPollSeconds);
            Assert.False(model.SheetSetOptions.Locate);
        }

        [Fact]
        public void ADrawingThatIsASheet_OpensItsSet_AndShowsThePalette_WhereSSLOCATEAndSSMAUTOOPENAreOn()
        {
            string sheet = MadeSet();
            MainViewModel model = Model();
            int shown = 0;
            model.ShowSheetSetManager = manager => shown++;

            Assert.True(model.Open(sheet));

            Assert.Equal(Dst, model.SheetSetManager.Current.Path, ignoreCase: true);
            Assert.Equal(1, shown);
            model.Execute("SSFOUND");
            Assert.StartsWith("SSFOUND = \"", model.History.Last(), StringComparison.Ordinal);
            Assert.Contains("Tower.dst", model.History.Last());
        }

        [Fact]
        public void ADrawingThatIsASheet_OpensItsSetWithNoPalette_WhereSSMAUTOOPENIsOff_AndNothingWhereSSLOCATEIsOff()
        {
            string sheet = MadeSet();
            MainViewModel model = Model();
            int shown = 0;
            model.ShowSheetSetManager = manager => shown++;
            model.SheetSetOptions.AutoOpen = false;

            model.Open(sheet);

            Assert.Equal(Dst, model.SheetSetManager.Current.Path, ignoreCase: true);
            Assert.Equal(0, shown);

            MainViewModel off = Model();
            off.SheetSetOptions.Locate = false;
            off.Open(sheet);
            Assert.Null(off.SheetSetManager.Current);
        }

        [Fact]
        public void ASetTheManagerOpens_TakesTheLockFile_AndClosingItDeletesIt()
        {
            MadeSet();
            MainViewModel model = Model();

            Assert.True(model.SheetSetManager.OpenSet(Dst));

            Assert.True(File.Exists(CadSheetSetLock.PathOf(Dst)));
            Assert.False(model.SheetSetManager.IsLocked);
            model.SheetSetManager.CloseSet();
            Assert.False(File.Exists(CadSheetSetLock.PathOf(Dst)));
        }

        [Fact]
        public void ASetWhoseLockIsAnotherUsers_OpensLocked_WithTheUserInTheReason_AndLeavesTheLockFile()
        {
            MadeSet();
            File.WriteAllBytes(
                CadSheetSetLock.PathOf(Dst),
                Encoding.ASCII.GetBytes("[UserInfo]\r\r\nUserName=SOMEONE\r\r\nMachine=OTHER-PC\r\r\n\r\n")
            );
            MainViewModel model = Model();

            Assert.True(model.SheetSetManager.OpenSet(Dst));

            Assert.True(model.SheetSetManager.IsLocked);
            Assert.Equal("Read-only: Locked for Edit by SOMEONE on OTHER-PC.", model.SheetSetManager.LockText);
            model.SheetSetManager.CloseSet();
            Assert.True(File.Exists(CadSheetSetLock.PathOf(Dst)));
        }
    }
}
