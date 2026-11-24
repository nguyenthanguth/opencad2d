using System;
using System.Threading;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The names the built-in pattern and linetype sets had before the program was called OpenCAD2D, GeometryHelper.pat and
    /// GeometryHelper.lin, as the Hatch dialog and Load or Reload Linetypes are given them: each the built-in set.
    /// </summary>
    public class OldBuiltInFileNamesTests
    {
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
            Assert.Null(failure);
        }

        [Fact]
        public void TheHatchDialogGivenTheOldPatternFileName_IsOnTheBuiltInPatterns()
        {
            OnSta(() =>
            {
                var dialog = new HatchSettingsViewModel(
                    CadDocument.From(new DxfDatabase("AC1027"), "blank.dxf"),
                    new CadHatchSettings { PatternFile = "GeometryHelper.pat" },
                    false
                );

                Assert.True(dialog.IsBuiltInFile);
                Assert.Equal("Built-in (OpenCAD2D.pat)", dialog.PatternFileText);
                Assert.Equal(48, dialog.Patterns.Count);
                Assert.Equal(string.Empty, dialog.Message);
            });
        }

        [Fact]
        public void LoadLinetypesGivenTheOldLinetypeFileName_ListsTheBuiltInSetAlone()
        {
            var load = new LoadLinetypesViewModel(CadDocument.From(new DxfDatabase("AC1027"), "blank.dxf"))
            {
                File = "GeometryHelper.lin",
            };

            Assert.True(load.IsBuiltIn);
            Assert.Equal("Built-in (OpenCAD2D.lin)", load.FileText);
            Assert.Equal(39, load.Rows.Count);
            Assert.Equal(string.Empty, load.Message);
        }
    }
}
