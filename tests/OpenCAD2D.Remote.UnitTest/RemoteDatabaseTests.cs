using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// The whole drawing of the app asked from outside (<see cref="CadRemoteDocument.GetDatabase"/>): the copy is the drawing as the
    /// app has it now, unsaved changes included, and the file of the drawing is never written.
    /// </summary>
    public class RemoteDatabaseTests
    {
        private static readonly Tolerance Tol = new Tolerance(0.001, 1e-5);

        private static CadDocument Plan()
        {
            var db = new DxfDatabase("AC1027");
            db.ModelSpace.AppendEntity(
                new DxfDBText
                {
                    TextString = "Plate A-12",
                    Position = new GeoPoint3(10, 20, 0),
                    Height = 5.0,
                    Layer = "Notes",
                }
            );
            db.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(20, 0, 0), new GeoPoint3(20, 60, 0)));
            db.ModelSpace.AppendEntity(new DxfCircle { Center = new GeoPoint3(50, 50, 0), Radius = 12.5 });
            CadDocument document = CadDocument.From(db, "database.dxf");
            document
                .Spaces.First(s => !s.IsModel)
                .Block.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            return document;
        }

        [Fact]
        public void TheCopy_IsTheDrawingOfTheApp_WithItsLayoutsAndEveryObjectWithinTheTolerance()
        {
            CadDocument document = Plan();
            var host = new TestHost();
            host.Add(document);

            using (var harness = new Harness())
            {
                harness.Serve(host);
                CadRemoteDocument remote = harness.Connect().Documents.Single();

                CadDocument copy = CadDocument.From(remote.GetDatabase());

                Assert.Equal(
                    document.Spaces.Where(s => !s.IsBlockEditor).Select(s => s.Name),
                    copy.Spaces.Where(s => !s.IsBlockEditor).Select(s => s.Name)
                );
                foreach (CadSpace space in document.Spaces.Where(s => !s.IsBlockEditor))
                {
                    CadSpace other = copy.Spaces.First(s => s.Name == space.Name);
                    CadBoundsTable a = CadSpatial.Bounds(document, space);
                    CadBoundsTable b = CadSpatial.Bounds(copy, other);
                    Assert.Equal(a.Handles, b.Handles);
                    Assert.Equal(a.Boxes.Length, b.Boxes.Length);
                    for (int i = 0; i < a.Boxes.Length; i++)
                    {
                        Assert.True(System.Math.Abs(a.Boxes[i] - b.Boxes[i]) <= Tol.EqualPoint);
                    }
                }

                DxfCircle circle = copy.ModelSpace.Block.OfType<DxfCircle>().Single();
                Assert.Equal(12.5, circle.Radius, 3);
                Assert.Equal("Notes", copy.ModelSpace.Block.OfType<DxfDBText>().Single().Layer);
            }
        }

        [Fact]
        public void TheCopy_HasTheChangesTheAppHasNotSaved_AndNothingIsWrittenToTheFile()
        {
            CadDocument document = Plan();
            var host = new TestHost();
            host.Add(document);

            using (var harness = new Harness())
            {
                harness.Serve(host);
                CadRemoteDocument remote = harness.Connect().Documents.Single();
                remote.Insert(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(7, 7, 0)));
                string added = remote.CommitChanges().Single();
                Assert.True(document.IsModified);

                CadDocument copy = CadDocument.From(remote.GetDatabase());

                DxfLine line = Assert.IsType<DxfLine>(copy.FindEntity(added));
                Assert.Equal(7.0, line.EndPoint.X, 3);
                Assert.Equal(4, copy.ModelSpace.Block.Count(e => !e.IsErased));
                Assert.True(document.IsModified);
                Assert.False(copy.HasFile);
            }
        }
    }
}
