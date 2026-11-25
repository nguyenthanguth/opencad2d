using System;
using System.Collections.Generic;
using Xunit;

namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>
    /// What a plugin reaches the open drawings by (A9), through the public API only: the command's context, and the plugin
    /// API 1.4 that states it. The callback of a ribbon button is given the window's drawings, which the tests of the window
    /// check (Cad.Wpf.UnitTest).
    /// </summary>
    public sealed class PluginDocumentsTests
    {
        [Fact]
        public void APluginCommandInASessionWithNoWindow_ReadsTheDrawingAsTheOnlyOneOpen_AndIsToldThereIsNoWindowToOpenAnother()
        {
            CadDocument document = CadDocument.From(new GeometryHelper.Dxf.DxfDatabase("AC1027"), "plan.dxf");
            var registry = CadCommandRegistry.CreateDefault();
            var said = new List<string>();
            registry.Add(new CadCommand("LISTOPEN", c =>
                {
                    said.Add(c.Documents.HasWindow + " " + c.Documents.Count + " " + c.Documents.Active.Title);

                    try
                    {
                        c.Documents.Open("other.dxf");
                    }
                    catch (InvalidOperationException ex)
                    {
                        said.Add(ex.Message);
                    }

                    return Array.Empty<CadPrompt>();
                }));
            CadCommandSession session = PluginFiles.Session(document, registry);

            session.Feed("LISTOPEN");

            Assert.Equal(CadCommandOutcome.Completed, session.LastOutcome);
            Assert.Equal(
                new[]
                {
                    "False 1 plan.dxf",
                    "Cannot open a drawing: this session has no OpenCAD2D window, which holds the open drawings.",
                },
                said
            );
        }
    }
}
