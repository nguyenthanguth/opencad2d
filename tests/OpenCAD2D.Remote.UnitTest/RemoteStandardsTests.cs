using System;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// A drawing checked against a drawing standard from outside the app (<see cref="CadRemoteDocument.CheckStandard"/>): the scores, the
    /// total and the violations are the ones <see cref="CadStandards.Check"/> gives in the app, rule for rule and number for number,
    /// and a standard the app cannot read is a <see cref="CadRemoteException"/> that says why.
    /// </summary>
    public class RemoteStandardsTests
    {
        private const string Standard =
            "{\"schema\":\"opencad2d.standard\",\"version\":1,\"name\":\"remote\","
            + "\"layers\":{\"allowed\":[{\"name\":\"WALL*\"}],\"allowLayer0\":false},\"byLayer\":{\"color\":true},"
            + "\"textHeights\":{\"paper\":[2.5,3.5,5]},\"gaps\":{\"wordLine\":1.0},\"weights\":{\"text-height\":3}}";

        /// <summary>Words 3.1 high (not listed), a line through them, a line on layer 0, a line in a colour of its own on WALL-1.</summary>
        private static CadDocument Plan()
        {
            var db = new DxfDatabase("AC1027");
            db.LayerTable.Add(new DxfLayerTableRecord { Name = "WALL-1" });
            db.ModelSpace.AppendEntity(
                new DxfDBText
                {
                    TextString = "Plate A-12",
                    Position = new GeoPoint3(10, 20, 0),
                    Height = 3.1,
                }
            );
            db.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(20, 0, 0), new GeoPoint3(20, 60, 0)));
            var coloured = new DxfLine(new GeoPoint3(0, 100, 0), new GeoPoint3(50, 100, 0));
            db.ModelSpace.AppendEntity(coloured);
            coloured.Layer = "WALL-1";
            coloured.ColorIndex = 1;
            return CadDocument.From(db, "standards.dxf");
        }

        private static (Harness harness, CadRemoteDocument remote) Start(CadDocument document)
        {
            var harness = new Harness();
            var host = new TestHost();
            host.Add(document);
            harness.Serve(host);
            return (harness, harness.Connect().Documents.Single());
        }

        [Fact]
        public void TheCheckOfADrawing_IsTheAppsOwn_RuleForRuleAndNumberForNumber()
        {
            CadDocument document = Plan();
            CadStandardResult inApp = CadStandards.Check(
                document,
                CadStandard.Parse(Standard),
                0.0,
                new Tolerance(1E-3, 1E-5)
            );
            var (harness, remote) = Start(document);

            using (harness)
            {
                CadRemoteStandardResult got = remote.CheckStandard(Standard, 0.0, 1E-3);

                Assert.Equal("remote", got.Name);
                Assert.Equal(inApp.Total, got.Total, 12);
                Assert.Equal(inApp.Rules.Select(r => r.Rule), got.Rules.Select(r => r.Rule));
                Assert.Equal(inApp.Rules.Select(r => r.Score), got.Rules.Select(r => r.Score));
                Assert.Equal(
                    inApp.Rules.Select(r => (r.Subjects, r.Offenders, r.Weight)),
                    got.Rules.Select(r => (r.Subjects, r.Offenders, r.Weight))
                );
                Assert.Equal(
                    inApp.Violations.Select(v =>
                        (v.Rule, v.Handle, v.Other, v.Space, v.Through, v.Measured, v.Allowed)
                    ),
                    got.Violations.Select(v => (v.Rule, v.Handle, v.Other, v.Space, v.Through, v.Measured, v.Allowed))
                );
                Assert.Equal(inApp.Violations.Select(v => v.Value), got.Violations.Select(v => v.Value));
                Assert.Equal(inApp.Boxes, got.Boxes);
                Assert.True(got.Violations.Count >= 3);
                Assert.Contains(got.Violations, v => v.Rule == "text-height" && Math.Abs(v.Value - 3.1) < 1E-9);
                Assert.Contains(got.Violations, v => v.Rule == "gap-word-line" && v.Other != null);
                Assert.Contains(got.Violations, v => v.Rule == "layer-zero" && v.Box != null);
            }
        }

        [Fact]
        public void AModelScaleAndATolerance_ReachTheApp_AndAStandardItCannotReadIsRefusedWithTheReason()
        {
            var (harness, remote) = Start(Plan());

            using (harness)
            {
                CadRemoteStandardResult scaled = remote.CheckStandard(Standard, 0.01, 1E-3);
                Assert.Equal(3.1 * 0.01, scaled.Violations.Single(v => v.Rule == "text-height").Value, 12);

                CadRemoteException bad = Assert.Throws<CadRemoteException>(() =>
                    remote.CheckStandard(
                        "{\"schema\":\"opencad2d.standard\",\"version\":1,\"gaps\":{\"wordLin\":1}}",
                        0.0,
                        1E-3
                    )
                );
                Assert.Contains("standard.gaps.wordLin", bad.Message);
                Assert.Throws<ArgumentNullException>(() => remote.CheckStandard(null, 0.0, 1E-3));
            }
        }
    }
}
