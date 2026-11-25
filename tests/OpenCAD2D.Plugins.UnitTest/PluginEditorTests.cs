using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>
    /// What a plugin reaches the editor of a drawing's session by (A6), through the public API only: the command's context, and
    /// the plugin API 1.3 that states it. The callback of a ribbon button is given its editor by the window, which the tests of
    /// the window check (Cad.Wpf.UnitTest).
    /// </summary>
    public sealed class PluginEditorTests : IDisposable
    {
        private readonly PluginFiles _files = new PluginFiles();

        public void Dispose()
        {
            _files.Dispose();
        }

        private static IEnumerable<CadPrompt> ZoomThenSave(CadCommandContext c)
        {
            c.Editor.ZoomWindow(new GeoPoint2(0, -30), new GeoPoint2(100, 20));
            c.Editor.SaveView("Plan");
            c.Editor.SetVariable("LTSCALE", 2.0);
            yield break;
        }

        [Fact]
        public void APluginCommandZoomsSavesAViewAndSetsAVariableThroughItsContextsEditor_AsOneUndoStep()
        {
            CadDocument document = CadDocument.From(new GeometryHelper.Dxf.DxfDatabase("AC1027"), "plan.dxf");
            var registry = CadCommandRegistry.CreateDefault();
            registry.Add(new CadCommand("ZOOMPLAN", ZoomThenSave));
            CadCommandSession session = PluginFiles.Session(document, registry);

            session.Feed("ZOOMPLAN");

            Assert.Equal(CadCommandOutcome.Completed, session.LastOutcome);
            Assert.Same(session.Editor, session.Editor);
            DisplayBox shown = session.Editor.GetCurrentView();
            Assert.True(shown.MinX <= 0.0 && shown.MaxX >= 100.0);
            Assert.True(shown.MinY <= -30.0 && shown.MaxY >= 20.0);
            Assert.Equal(new[] { "Plan" }, session.Editor.NamedViews);
            Assert.Equal(2.0, session.Editor.GetVariable("LTSCALE"));
            Assert.Equal("ZOOMPLAN", document.NextUndo.Name);

            Assert.True(document.Undo());

            Assert.Empty(session.Editor.NamedViews);
            Assert.Equal(1.0, session.Editor.GetVariable("LTSCALE"));
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void APluginThatStatesApiOnePointThreeIsAccepted_AndOnePointFourIsRefused()
        {
            string log = Path.Combine(_files.Root, "log.txt");
            Assembly editor = EmittedPlugin.InMemory(
                EmittedPlugin.UniqueName("Editor"),
                "1.3",
                log,
                new EmittedClass("Editor")
            );
            Assembly newer = EmittedPlugin.InMemory(
                EmittedPlugin.UniqueName("Newer"),
                "1.4",
                log,
                new EmittedClass("Newer")
            );

            CadPlugin accepted = CadPlugin.FromAssemblies("Uses the editor", new[] { editor });
            CadPluginException refused = Assert.Throws<CadPluginException>(() =>
                CadPlugin.FromAssemblies("Uses a later one", new[] { newer })
            );

            Assert.Equal(new Version(1, 3), accepted.RequiredApiVersion);
            Assert.Equal(new Version(1, 3), CadPlugin.ApiVersion);
            Assert.Equal(
                "The plugin Uses a later one needs version 1.4 of the plugin API, and this presenter has 1.3: it needs a newer presenter.",
                refused.Message
            );
        }
    }
}
