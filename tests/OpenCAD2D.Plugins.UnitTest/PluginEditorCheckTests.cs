using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>
    /// What a plugin does with the dry run of the editor (<see cref="CadEditor.Check"/>, plugin API 1.5), through the public API
    /// only: it checks an offset, and makes the changes it has to make before it only where the offset would be made.
    /// </summary>
    public sealed class PluginEditorCheckTests
    {
        private static CadDocument Drawing(out DxfCircle circle)
        {
            var database = new DxfDatabase("AC1027");
            circle = new DxfCircle(new GeoPoint3(0, 0, 0), new GeoVector3(0, 0, 1), 10);
            database.ModelSpace.AppendEntity(circle);
            return CadDocument.From(database, "plan.dxf");
        }

        [Fact]
        public void APluginCommandChecksAnOffsetBeforeItDrawsAnything_AndDrawsNothingWhereTheOffsetIsRefused()
        {
            CadDocument document = Drawing(out DxfCircle circle);
            var registry = CadCommandRegistry.CreateDefault();
            var said = new List<string>();
            registry.Add(new CadCommand("RINGS", c =>
                {
                    // A circle of radius 10 offset inward by 15 makes no curve; outward by 15 does.
                    string inward = c.Editor.Check(e => e.Offset(circle, 15.0, new GeoPoint2(5, 0)));
                    string outward = c.Editor.Check(e => e.Offset(circle, 15.0, new GeoPoint2(30, 0)));
                    said.Add(inward ?? "none");
                    said.Add(outward ?? "none");

                    if (outward == null)
                    {
                        c.Editor.Offset(circle, 15.0, new GeoPoint2(30, 0));
                    }

                    return Array.Empty<CadPrompt>();
                }));
            CadCommandSession session = PluginFiles.Session(document, registry);

            session.Feed("RINGS");

            Assert.Equal(CadCommandOutcome.Completed, session.LastOutcome);
            Assert.Equal(new[] { "Cannot offset that object.", "none" }, said);
            Assert.Equal(2, CountOf(document));
            Assert.Equal("RINGS", document.NextUndo.Name);
        }

        private static int CountOf(CadDocument document)
        {
            int count = 0;

            foreach (DxfEntity entity in document.Database.ModelSpace)
            {
                count++;
            }

            return count;
        }
    }
}
