using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// An object snap word typed at a point prompt takes AutoCAD's international underscore: "_END", "_mid" and "_NONE" are
    /// the overrides END, mid and NONE are, as AutoCAD 2021 takes them.
    /// </summary>
    public sealed class OsnapUnderscoreTests
    {
        /// <summary>A line from (0, 0) to (100, 0), with the grid snap off so that a point not snapped is the point typed.</summary>
        private static MainViewModel Open()
        {
            var database = new DxfDatabase("AC1027");
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            var model = new MainViewModel();
            model.OpenDocument(CadDocument.From(database, "line.dxf"));
            model.Toggles.Single(t => t.Name == "SNAP").IsOn = false;
            model.Session.SnapAperture = 1.3;
            return model;
        }

        [Theory]
        [InlineData("_END", CadSnapModes.Endpoint)]
        [InlineData("_end", CadSnapModes.Endpoint)]
        [InlineData("_ENDPOINT", CadSnapModes.Endpoint)]
        [InlineData("_MID", CadSnapModes.Midpoint)]
        [InlineData("_Cen", CadSnapModes.Center)]
        [InlineData("_INT", CadSnapModes.Intersection)]
        [InlineData("_PER", CadSnapModes.Perpendicular)]
        [InlineData("_NEA", CadSnapModes.Nearest)]
        [InlineData("_NONE", CadSnapModes.None)]
        public void ASnapWordWithAnUnderscore_IsTheOverride_AtThePointPrompt(string word, CadSnapModes expected)
        {
            MainViewModel model = Open();
            model.StartCommand("LINE");

            model.Execute(word);

            Assert.Equal(expected, model.Session.SnapOverride);
            Assert.Equal(expected, model.Snap.Override);
            Assert.Equal("Specify first point:", model.Prompt);
        }

        [Fact]
        public void EndWithAnUnderscore_ThenAPointNearTheLine_GivesTheNearerEnd()
        {
            MainViewModel model = Open();
            model.StartCommand("LINE");

            model.Execute("_END");
            model.Execute("60,1");

            Assert.Equal(100.0, model.Session.LastPoint.X, 9);
            Assert.Equal(0.0, model.Session.LastPoint.Y, 9);
        }

        [Fact]
        public void EndWithAnUnderscore_AtSelectObjects_IsTheOverrideToo()
        {
            MainViewModel model = Open();
            model.StartCommand("ERASE");

            model.Execute("_END");

            Assert.Equal(CadSnapModes.Endpoint, model.Session.SnapOverride);
        }

        [Theory]
        [InlineData("_")]
        [InlineData("_E")]
        [InlineData("__END")]
        [InlineData("_ENDX")]
        public void AWordThatIsNoSnapWithAnUnderscore_IsNotTheOverride(string word)
        {
            MainViewModel model = Open();
            model.StartCommand("LINE");

            model.Execute(word);

            Assert.Null(model.Session.SnapOverride);
        }

        [Fact]
        public void AMacroWordOfPluginRun_StillTakesTheUnderscoreSnap()
        {
            MainViewModel model = Open();
            model.StartCommand("LINE");

            model.RunPluginMacro("HookPlugin", "_END");

            Assert.Equal(CadSnapModes.Endpoint, model.Snap.Override);
        }

        [Theory]
        [InlineData("END")]
        [InlineData("_END")]
        public void ALispCommandWithASnapWord_GivesTheNearerEnd(string word)
        {
            MainViewModel model = Open();

            model.Execute("(command \"_LINE\" \"" + word + "\" \"60,1\")");

            Assert.Equal(100.0, model.Session.LastPoint.X, 9);
            Assert.Equal(0.0, model.Session.LastPoint.Y, 9);
        }

        [Fact]
        public void OsnapTyped_TakesTheModesWithAnUnderscore()
        {
            MainViewModel model = Open();

            model.StartCommand("-OSNAP");
            model.Execute("_END,_mid");

            Assert.Equal(CadSnapModes.Endpoint | CadSnapModes.Midpoint, model.SnapModes);
        }

        [Fact]
        public void AScriptLineWithASnapWord_TakesItAsTheOverride()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "osnap-underscore-" + System.Guid.NewGuid().ToString("N") + ".scr"
            );
            System.IO.File.WriteAllText(path, "LINE\n_END\n60,1\n");

            try
            {
                MainViewModel model = Open();
                model.Execute("SCRIPT");
                model.Execute("\"" + path + "\"");

                Assert.Equal("LINE", model.Session.Current.Name);
                Assert.Equal(100.0, model.Session.LastPoint.X, 9);
                Assert.Equal(0.0, model.Session.LastPoint.Y, 9);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Theory]
        [InlineData("LINE _END 60,1 \n")]
        [InlineData("LINE END 60,1 \n")]
        [InlineData("LINE\t_end\t60,1\n")]
        public void AScriptLineOfWords_TakesTheSnapWordAsTheOverrideOfTheNextPoint(string script)
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "osnap-underscore-" + System.Guid.NewGuid().ToString("N") + ".scr"
            );
            System.IO.File.WriteAllText(path, script);

            try
            {
                MainViewModel model = Open();
                model.Execute("SCRIPT");
                model.Execute("\"" + path + "\"");

                Assert.Equal(100.0, model.Session.LastPoint.X, 9);
                Assert.Equal(0.0, model.Session.LastPoint.Y, 9);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void ATransparentEnd_IsNotASnap()
        {
            MainViewModel model = Open();
            model.StartCommand("LINE");

            model.Execute("'_END");

            Assert.Null(model.Session.SnapOverride);
            Assert.Equal("LINE", model.Session.Current.Name);
        }

        [Theory]
        [InlineData("END")]
        [InlineData("_end")]
        public void AWordFedToTheSession_AsRemoteDoes_IsTheOverride(string word)
        {
            MainViewModel model = Open();
            model.StartCommand("LINE");

            Assert.True(model.Session.FeedTyped(word));

            Assert.Equal(CadSnapModes.Endpoint, model.Session.SnapOverride);
        }

        [Theory]
        [InlineData("_tan")]
        [InlineData("tan")]
        public void ALispCircle3PWithTanWords_StillDrawsTheCircleTouchingThreeLines(string tan)
        {
            var database = new DxfDatabase("AC1027");
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(100, 0, 0), new GeoPoint3(50, 86.60254, 0)));
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(50, 86.60254, 0), new GeoPoint3(0, 0, 0)));
            var model = new MainViewModel();
            model.OpenDocument(CadDocument.From(database, "triangle.dxf"));
            model.Toggles.Single(t => t.Name == "SNAP").IsOn = false;

            model.Execute(
                "(command \"_CIRCLE\" \"3P\" \""
                    + tan
                    + "\" '(50 0) \""
                    + tan
                    + "\" '(75 43.3) \""
                    + tan
                    + "\" '(25 43.3))"
            );

            DxfCircle circle = Assert.Single(model.Document.ModelSpace.Block.OfType<DxfCircle>());
            Assert.Equal(28.8675, circle.Radius, 3);
        }

        [Theory]
        [InlineData("_tan")]
        [InlineData("tan")]
        public void TanTypedAtCircle3P_IsTheCirclesOwnTangent_NotTheSnap(string tan)
        {
            var database = new DxfDatabase("AC1027");
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(100, 0, 0), new GeoPoint3(50, 86.60254, 0)));
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(50, 86.60254, 0), new GeoPoint3(0, 0, 0)));
            var model = new MainViewModel();
            model.OpenDocument(CadDocument.From(database, "triangle.dxf"));
            model.Toggles.Single(t => t.Name == "SNAP").IsOn = false;

            model.Execute("CIRCLE");
            model.Execute("3P");

            foreach (string word in new[] { tan, "50,0", tan, "75,43.3", tan, "25,43.3" })
            {
                model.Execute(word);
            }

            DxfCircle circle = Assert.Single(model.Document.ModelSpace.Block.OfType<DxfCircle>());
            Assert.Equal(28.8675, circle.Radius, 3);
        }
    }
}
