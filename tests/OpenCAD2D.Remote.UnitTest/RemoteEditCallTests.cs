using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// The edit calls of a Remote drawing (Move, Copy, Rotate, Scale, Mirror, Offset), which ride in the commit with what is
    /// queued: each against the call of the in-process <see cref="CadEditor"/> on a twin drawing of the same app, the ENTITIES
    /// of the two compared, and against the protocol's own edit operations of <c>commit</c>. The drawing, built twice by
    /// <see cref="Build"/>: a line on Steel from (0, 0) to (100, 0), a circle of radius 25 about (50, 50), a line from (0, 0) to
    /// (0, 50), a line on the locked layer Locked, a closed plate (0, 300) to (100, 350) and a TEXT "NOTE" 5 high at (0, 200).
    /// </summary>
    public class RemoteEditCallTests
    {
        private static readonly Tolerance Tight = new Tolerance(1E-9, 1E-9);

        private static readonly GeoVector2 Shift = new GeoVector2(10, 5);

        private sealed class Ids
        {
            public string Line;
            public string Circle;
            public string Side;
            public string Locked;
            public string Plate;
            public string Text;
        }

        private sealed class ModelOwner : ICadEntityOwner
        {
            private readonly CadEditRule _rule;

            public ModelOwner(CadEditRule rule)
            {
                _rule = rule;
            }

            public string Name => "Model";

            public CadEditRule GetEditRule(DxfEntity entity) => _rule;

            public bool TryApply(CadDocument document, CadEdit edit) => false;
        }

        /// <summary>
        /// The drawing described above. With <paramref name="moveOnly"/>, its first line belongs to the owner "Model", which allows
        /// it to be moved and nothing else.
        /// </summary>
        private static CadDocument Build(string name, out Ids ids, bool moveOnly = false)
        {
            var db = new DxfDatabase("AC1027");
            db.LayerTable.Add(new DxfLayerTableRecord { Name = "Steel", ColorIndex = 1 });
            db.LayerTable.Add(
                new DxfLayerTableRecord
                {
                    Name = "Locked",
                    ColorIndex = 3,
                    IsLocked = true,
                }
            );
            var line = new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)) { Layer = "Steel" };
            db.ModelSpace.AppendEntity(line);
            var circle = new DxfCircle(new GeoPoint3(50, 50, 0), new GeoVector3(0, 0, 1), 25);
            db.ModelSpace.AppendEntity(circle);
            var side = new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(0, 50, 0));
            db.ModelSpace.AppendEntity(side);
            var locked = new DxfLine(new GeoPoint3(0, -50, 0), new GeoPoint3(100, -50, 0)) { Layer = "Locked" };
            db.ModelSpace.AppendEntity(locked);
            var plate = new DxfPolyline();
            plate.AddVertexAt(0, new GeoPoint2(0, 300), 0.0, 0.0, 0.0);
            plate.AddVertexAt(1, new GeoPoint2(100, 300), 0.0, 0.0, 0.0);
            plate.AddVertexAt(2, new GeoPoint2(100, 350), 0.0, 0.0, 0.0);
            plate.AddVertexAt(3, new GeoPoint2(0, 350), 0.0, 0.0, 0.0);
            plate.Closed = true;
            db.ModelSpace.AppendEntity(plate);
            var text = new DxfDBText();
            db.ModelSpace.AppendEntity(text);
            text.TextString = "NOTE";
            text.Height = 5;
            text.Position = new GeoPoint3(0, 200, 0);

            if (moveOnly)
            {
                new CadOwnerTag("Model", "view-1").WriteTo(line);
            }

            CadDocument document = CadDocument.From(db, name);

            if (moveOnly)
            {
                document.Owners.Register(new ModelOwner(CadEditRule.MoveOnly));
            }

            ids = new Ids
            {
                Line = line.Handle,
                Circle = circle.Handle,
                Side = side.Handle,
                Locked = locked.Handle,
                Plate = plate.Handle,
                Text = text.Handle,
            };
            return document;
        }

        /// <summary>The app with the drawing A-101.dxf, shown, and its twin B-202.dxf, which the in-process calls go to.</summary>
        private sealed class Scene : IDisposable
        {
            public readonly Harness Harness = new Harness();
            public readonly TestHost Host = new TestHost();
            public readonly CadDocument Served;
            public readonly CadDocument Twin;
            public readonly Ids Ids;
            public readonly CadApplication App;
            public readonly CadRemoteDocument Remote;

            public Scene(bool moveOnly = false)
            {
                Twin = Host.Add(Build("B-202.dxf", out _, moveOnly));
                Served = Host.Add(Build("A-101.dxf", out Ids, moveOnly));
                Harness.Serve(Host);
                App = Harness.Connect();
                Remote = App.Documents.Get("A-101.dxf");
            }

            /// <summary>The editor of the twin's session: what an in-process plugin calls.</summary>
            public CadEditor Editor => Host.SessionOf(Twin).Editor;

            /// <summary>Copies of objects of the served drawing, as the client reads them.</summary>
            public DxfEntity[] Read(params string[] handles) => handles.Select(h => Remote.GetObject(h)).ToArray();

            /// <summary>The twin's own objects of the same handles.</summary>
            public DxfEntity[] Live(params string[] handles) => handles.Select(h => Twin.FindEntity(h)).ToArray();

            public string Text(CadDocument document) => Host.OnAppThread(() => document.Database.ToText());

            public void Dispose() => Harness.Dispose();
        }

        /// <summary>The ENTITIES section of a drawing as its groups: each group's code, then its value, trimmed.</summary>
        private static List<string> EntitiesOf(CadDocument document)
        {
            string[] lines = document.Database.ToText().Replace("\r\n", "\n").Split('\n');
            var groups = new List<string>();
            bool inside = false;

            for (int i = 0; i + 1 < lines.Length; i += 2)
            {
                string code = lines[i].Trim();
                string value = lines[i + 1].Trim();

                if (inside && code == "0" && value == "ENDSEC")
                {
                    break;
                }

                if (inside)
                {
                    groups.Add(code);
                    groups.Add(value);
                }
                else if (code == "2" && value == "ENTITIES")
                {
                    inside = true;
                }
            }

            return groups;
        }

        /// <summary>Checks that two drawings hold the same ENTITIES, a number within the tolerance's EqualPoint of the other; says the first differences.</summary>
        private static void AssertSameEntities(CadDocument expected, CadDocument actual, Tolerance tolerance)
        {
            List<string> a = EntitiesOf(expected);
            List<string> b = EntitiesOf(actual);
            var differences = new List<string>();

            if (a.Count != b.Count)
            {
                differences.Add("groups: " + a.Count / 2 + " against " + b.Count / 2);
            }

            for (int i = 0; i + 1 < Math.Min(a.Count, b.Count) && differences.Count < 6; i += 2)
            {
                bool same =
                    a[i] == b[i]
                    && (
                        a[i + 1] == b[i + 1]
                        || (
                            double.TryParse(a[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                            && double.TryParse(b[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                            && Math.Abs(x - y) <= tolerance.EqualPoint
                        )
                    );

                if (!same)
                {
                    differences.Add(
                        "group " + i / 2 + " (code " + a[i] + "): " + a[i + 1] + " against " + b[i + 1] + "."
                    );
                }
            }

            Assert.Empty(differences);
        }

        private static void Near(GeoPoint3 expected, GeoPoint3 actual)
        {
            Assert.True(expected.DistanceTo(actual) <= 1E-9, "Expected " + expected + " but was " + actual + ".");
        }

        private static GeoPoint3 P3(double x, double y) => new GeoPoint3(x, y, 0);

        /// <summary>Checks that a call is refused by the client (an argument it cannot take) or by the app, and by nothing else.</summary>
        private static void AssertRefused(Action call)
        {
            Exception thrown = Record.Exception(call);

            Assert.True(
                thrown is ArgumentException || thrown is CadRemoteException,
                "Refused with " + (thrown?.GetType().Name ?? "nothing") + "."
            );
        }

        private static DxfLine Line(double x1, double y1, double x2, double y2) =>
            new DxfLine(new GeoPoint3(x1, y1, 0), new GeoPoint3(x2, y2, 0));

        #region Each call against the in-process call

        [Fact]
        public void AMoveByTheCall_GivesTheDrawingTheInProcessMoveGives_AndComesBackAsFreshCopiesOfTheObjectsMoved()
        {
            using (var scene = new Scene())
            {
                var line = (DxfLine)scene.Read(scene.Ids.Line)[0];

                IReadOnlyList<DxfLine> moved = scene.Remote.Move(new[] { line }, Shift);
                scene.Editor.Move(scene.Live(scene.Ids.Line), Shift);

                // The line of the app is at (10, 5) to (110, 5), and so is the twin's; the copy given still says (0, 0) to (100, 0),
                // and the copy returned says where the line is now, under the same handle.
                AssertSameEntities(scene.Twin, scene.Served, Tight);
                DxfLine now = Assert.Single(moved);
                Assert.Equal(scene.Ids.Line, scene.Remote.HandleOf(now));
                Assert.NotSame(line, now);
                Near(P3(0, 0), line.StartPoint);
                Near(P3(10, 5), now.StartPoint);
                Near(P3(110, 5), now.EndPoint);
                Assert.Equal("REMOTE", scene.Served.NextUndo.Name);
            }
        }

        [Fact]
        public void TwoLinesCopiedByTheCall_ComeBackAsTwoLinesOfNewHandles_AndGiveTheInProcessDrawing()
        {
            using (var scene = new Scene())
            {
                var line = (DxfLine)scene.Read(scene.Ids.Line)[0];
                var side = (DxfLine)scene.Read(scene.Ids.Side)[0];

                IReadOnlyList<DxfLine> copies = scene.Remote.Copy(new[] { side, line }, new GeoVector2(0, 50));
                scene.Editor.Copy(scene.Live(scene.Ids.Side, scene.Ids.Line), new GeoVector2(0, 50));

                // COPY by (0, 50), the copy of the second line first as they were given: from (0, 50) to (0, 100) on layer 0, then from
                // (0, 50) to (100, 50) on Steel. Each has a handle the drawing did not have, and is in the drawing under it.
                AssertSameEntities(scene.Twin, scene.Served, Tight);
                Assert.Equal(2, copies.Count);
                Near(P3(0, 100), copies[0].EndPoint);
                Assert.Equal("0", copies[0].Layer);
                Near(P3(100, 50), copies[1].EndPoint);
                Assert.Equal("Steel", copies[1].Layer);

                foreach (DxfLine copy in copies)
                {
                    string handle = scene.Remote.HandleOf(copy);
                    Assert.NotNull(handle);
                    Assert.DoesNotContain(handle, new[] { scene.Ids.Line, scene.Ids.Side });
                    Assert.IsType<DxfLine>(scene.Served.FindEntity(handle));
                }
            }
        }

        [Theory]
        [InlineData("Move")]
        [InlineData("Copy")]
        [InlineData("Rotate")]
        [InlineData("Scale")]
        [InlineData("Mirror")]
        [InlineData("MirrorAndErase")]
        [InlineData("Offset")]
        public void EachEditCall_GivesTheDrawingTheInProcessCallGives(string call)
        {
            using (var scene = new Scene())
            {
                Ids ids = scene.Ids;
                string[] kinds = { ids.Line, ids.Circle, ids.Plate, ids.Text };
                DxfEntity[] read = scene.Read(kinds);
                DxfEntity[] live = scene.Live(kinds);
                var origin = new GeoPoint2(0, 0);

                switch (call)
                {
                    case "Move":
                        scene.Remote.Move(read, Shift);
                        scene.Editor.Move(live, Shift);
                        break;
                    case "Copy":
                        scene.Remote.Copy(read, Shift);
                        scene.Editor.Copy(live, Shift);
                        break;
                    case "Rotate":
                        scene.Remote.Rotate(read, origin, 1.0);
                        scene.Editor.Rotate(live, origin, 1.0);
                        break;
                    case "Scale":
                        scene.Remote.Scale(read, origin, 2.0);
                        scene.Editor.Scale(live, origin, 2.0);
                        break;
                    case "Mirror":
                        scene.Remote.Mirror(read, new GeoPoint2(20, 5), new GeoPoint2(30, 20));
                        scene.Editor.Mirror(live, new GeoPoint2(20, 5), new GeoPoint2(30, 20));
                        break;
                    case "MirrorAndErase":
                        scene.Remote.Mirror(read, new GeoPoint2(20, 5), new GeoPoint2(30, 20), true);
                        scene.Editor.Mirror(live, new GeoPoint2(20, 5), new GeoPoint2(30, 20), true);
                        break;
                    default:
                        scene.Remote.Offset((DxfCurve)read[0], 5.0, new GeoPoint2(50, 10));
                        scene.Editor.Offset((DxfCurve)live[0], 5.0, new GeoPoint2(50, 10));
                        break;
                }

                // A line, a circle, a closed polyline and a text, each turned, scaled or mirrored (the text kept readable by MIRRTEXT 0,
                // the drawing's default) as the app's own engine does it for its twin: the same ENTITIES, handles and all.
                AssertSameEntities(scene.Twin, scene.Served, Tight);
                Assert.Equal("REMOTE", scene.Served.NextUndo.Name);
            }
        }

        [Fact]
        public void AnOffsetByTheCall_ComesBackAsTheCurvesOffsetMade_OnTheSourcesLayer()
        {
            using (var scene = new Scene())
            {
                var line = (DxfLine)scene.Read(scene.Ids.Line)[0];

                IReadOnlyList<DxfCurve> made = scene.Remote.Offset(line, 5.0, new GeoPoint2(50, 10));

                // OFFSET 5 toward (50, 10): the line from (0, 5) to (100, 5) on Steel, a new object; the source stays.
                DxfLine offset = Assert.IsType<DxfLine>(Assert.Single(made));
                Assert.Equal("Steel", offset.Layer);
                Near(P3(0, 5), offset.StartPoint);
                Near(P3(100, 5), offset.EndPoint);
                string handle = scene.Remote.HandleOf(offset);
                Assert.NotNull(handle);
                Assert.NotEqual(scene.Ids.Line, handle);
                Assert.IsType<DxfLine>(scene.Served.FindEntity(handle));
                Near(P3(100, 0), ((DxfLine)scene.Served.FindEntity(scene.Ids.Line)).EndPoint);
            }
        }

        [Fact]
        public void AnEmptyList_IsNoCommit_AndComesBackEmpty()
        {
            using (var scene = new Scene())
            {
                IReadOnlyList<DxfEntity> moved = scene.Remote.Move(new DxfEntity[0], Shift);

                Assert.Empty(moved);
                Assert.False(scene.Served.CanUndo);
            }
        }

        #endregion

        #region What is queued and the edit: one commit

        [Fact]
        public void AnInsertQueuedAndAnEditCalled_AreOneCommit_OneUndoStepNamedRemote_AndTheInsertsTicketStandsForItsHandle()
        {
            using (var scene = new Scene())
            {
                string start = scene.Text(scene.Served);
                var line = (DxfLine)scene.Read(scene.Ids.Line)[0];

                string ticket = scene.Remote.Insert(Line(0, 100, 50, 100));
                scene.Remote.Move(new[] { line }, Shift);

                // The new line is in the drawing, the line is moved, and the step is the one of a commit: one U takes both back.
                Assert.Equal(7, scene.Served.Database.ModelSpace.Count(e => !e.IsErased));
                Near(P3(10, 5), ((DxfLine)scene.Served.FindEntity(scene.Ids.Line)).StartPoint);
                Assert.Equal("REMOTE", scene.Served.NextUndo.Name);
                var inserted = (DxfLine)scene.Remote.GetObject(ticket);
                Near(P3(50, 100), inserted.EndPoint);
                Assert.Empty(scene.Remote.CommitChanges());

                Assert.True(scene.Served.Undo());
                Assert.Equal(start, scene.Text(scene.Served));
                Assert.False(scene.Served.CanUndo);
            }
        }

        [Fact]
        public void AModifyQueuedBeforeAnEdit_IsAppliedBeforeIt()
        {
            using (var scene = new Scene())
            {
                var line = (DxfLine)scene.Read(scene.Ids.Line)[0];

                line.EndPoint = P3(500, 0);
                scene.Remote.Modify(line);
                scene.Remote.Move(new[] { line }, new GeoVector2(0, 10));

                // The change of the end first (the line from (0, 0) to (500, 0)), then the move up by 10: (0, 10) to (500, 10). Done the
                // other way round, the end would be at (500, 0).
                var now = (DxfLine)scene.Served.FindEntity(scene.Ids.Line);
                Near(P3(0, 10), now.StartPoint);
                Near(P3(500, 10), now.EndPoint);
                Assert.Equal("REMOTE", scene.Served.NextUndo.Name);
            }
        }

        [Fact]
        public void AnObjectOnALockedLayer_RefusesTheEditAndTheInsertQueuedWithIt_NothingIsApplied()
        {
            using (var scene = new Scene())
            {
                string start = scene.Text(scene.Served);
                var locked = (DxfLine)scene.Read(scene.Ids.Locked)[0];

                scene.Remote.Insert(Line(0, 100, 50, 100));
                CadRemoteException refused = Assert.Throws<CadRemoteException>(() =>
                    scene.Remote.Move(new[] { locked }, Shift)
                );

                // The in-process call says the same words; the insert queued with it is not applied, and not kept for the next commit.
                Assert.Contains(
                    "The object " + scene.Ids.Locked + " is on the locked layer Locked.",
                    refused.Message,
                    StringComparison.Ordinal
                );
                Assert.Equal(start, scene.Text(scene.Served));
                Assert.False(scene.Served.CanUndo);
                Assert.Empty(scene.Remote.CommitChanges());
                Assert.Equal(start, scene.Text(scene.Served));
            }
        }

        [Fact]
        public void AnObjectErasedAfterItWasRead_RefusesTheEdit_NothingIsApplied()
        {
            using (var scene = new Scene())
            {
                var line = (DxfLine)scene.Read(scene.Ids.Line)[0];
                scene.Host.OnAppThread(() => scene.Served.FindEntity(scene.Ids.Line).Erase());
                string start = scene.Text(scene.Served);

                CadRemoteException refused = Assert.Throws<CadRemoteException>(() =>
                    scene.Remote.Rotate(new[] { line }, new GeoPoint2(0, 0), 1.0)
                );

                Assert.Contains(scene.Ids.Line, refused.Message, StringComparison.Ordinal);
                Assert.Equal(start, scene.Text(scene.Served));
                Assert.False(scene.Served.CanUndo);
            }
        }

        [Fact]
        public void AnObjectNotReadFromThisDrawing_IsRefusedByTheCall_AsModifyRefusesIt()
        {
            using (var scene = new Scene())
            {
                string start = scene.Text(scene.Served);
                var other = (DxfLine)scene.App.Documents.Get("B-202.dxf").GetObject(scene.Ids.Line);
                DxfLine made = Line(0, 0, 1, 1);

                // An object of the twin drawing, one made in code and one queued by Insert (not in the drawing until the commit) are not
                // objects this drawing gave.
                Assert.Throws<ArgumentException>(() => scene.Remote.Move(new[] { other }, Shift));
                Assert.Throws<ArgumentException>(() => scene.Remote.Copy(new[] { made }, Shift));
                scene.Remote.Insert(made);
                Assert.Throws<ArgumentException>(() => scene.Remote.Rotate(new[] { made }, new GeoPoint2(0, 0), 1.0));

                Assert.Equal(start, scene.Text(scene.Served));
                Assert.False(scene.Served.CanUndo);
            }
        }

        [Fact]
        public void AnOwnerThatAllowsOnlyMoving_RefusesARotate_WithTheOwnersWords_AndTheInsertQueuedWithIt()
        {
            using (var scene = new Scene(moveOnly: true))
            {
                string start = scene.Text(scene.Served);
                var line = (DxfLine)scene.Read(scene.Ids.Line)[0];
                var free = (DxfLine)scene.Read(scene.Ids.Side)[0];

                scene.Remote.Insert(Line(0, 100, 50, 100));
                CadRemoteException refused = Assert.Throws<CadRemoteException>(() =>
                    scene.Remote.Rotate(new[] { free, line }, new GeoPoint2(0, 0), 1.0)
                );

                Assert.Contains(
                    "This line belongs to the model and can only be moved.",
                    refused.Message,
                    StringComparison.Ordinal
                );
                Assert.Equal(start, scene.Text(scene.Served));
                Assert.False(scene.Served.CanUndo);
            }
        }

        [Fact]
        public void ANumberThatIsNoEdit_IsRefused_NothingIsApplied()
        {
            using (var scene = new Scene())
            {
                string start = scene.Text(scene.Served);
                var line = (DxfLine)scene.Read(scene.Ids.Line)[0];
                var origin = new GeoPoint2(0, 0);

                // A factor of nought, a mirror line of two points 1E-12 apart and an offset distance of nought: refused, whether by
                // the client or by the app, and nothing is changed.
                AssertRefused(() => scene.Remote.Scale(new[] { line }, origin, 0.0));
                AssertRefused(() => scene.Remote.Mirror(new[] { line }, origin, new GeoPoint2(1E-12, 0)));
                AssertRefused(() => scene.Remote.Offset(line, 0.0, new GeoPoint2(50, 10)));

                Assert.Equal(start, scene.Text(scene.Served));
                Assert.False(scene.Served.CanUndo);
            }
        }

        #endregion

        #region The operations of commit, as the protocol has them

        private static JsonObject Transform(string how, params string[] handles) =>
            new JsonObject().Set("op", "transform").Set("how", how).Set("handles", new JsonArray(handles));

        private static JsonObject Commit(Scene scene, params JsonObject[] operations) =>
            scene.App.Connection.Request(
                new JsonObject()
                    .Set("op", RemoteOps.Commit)
                    .Set("doc", (long)scene.Remote.Id)
                    .Set("fragment", string.Empty)
                    .Set("operations", new JsonArray(operations))
            );

        [Theory]
        [InlineData("Move")]
        [InlineData("Copy")]
        [InlineData("Rotate")]
        [InlineData("Scale")]
        [InlineData("Mirror")]
        [InlineData("MirrorAndErase")]
        [InlineData("Offset")]
        public void ARawCommitOfAnEditOperation_GivesTheDrawingTheInProcessCallGives(string call)
        {
            using (var scene = new Scene())
            {
                Ids ids = scene.Ids;
                string[] kinds = { ids.Line, ids.Circle, ids.Plate, ids.Text };
                DxfEntity[] live = scene.Live(kinds);
                var origin = new GeoPoint2(0, 0);
                JsonObject operation;

                switch (call)
                {
                    case "Move":
                        operation = Transform("move", kinds).Set("dx", 10.0).Set("dy", 5.0);
                        scene.Editor.Move(live, Shift);
                        break;
                    case "Copy":
                        operation = Transform("copy", kinds).Set("dx", 10.0).Set("dy", 5.0);
                        scene.Editor.Copy(live, Shift);
                        break;
                    case "Rotate":
                        operation = Transform("rotate", kinds).Set("x", 0.0).Set("y", 0.0).Set("angle", 1.0);
                        scene.Editor.Rotate(live, origin, 1.0);
                        break;
                    case "Scale":
                        operation = Transform("scale", kinds).Set("x", 0.0).Set("y", 0.0).Set("factor", 2.0);
                        scene.Editor.Scale(live, origin, 2.0);
                        break;
                    case "Mirror":
                    case "MirrorAndErase":
                        bool erase = call == "MirrorAndErase";
                        operation = Transform("mirror", kinds)
                            .Set("x", 20.0)
                            .Set("y", 5.0)
                            .Set("x2", 30.0)
                            .Set("y2", 20.0)
                            .Set("eraseSource", erase);
                        scene.Editor.Mirror(live, new GeoPoint2(20, 5), new GeoPoint2(30, 20), erase);
                        break;
                    default:
                        operation = new JsonObject()
                            .Set("op", "offset")
                            .Set("handle", ids.Line)
                            .Set("distance", 5.0)
                            .Set("x", 50.0)
                            .Set("y", 10.0);
                        scene.Editor.Offset((DxfCurve)live[0], 5.0, new GeoPoint2(50, 10));
                        break;
                }

                JsonObject answer = Commit(scene, operation);

                // One edit operation, one list in `edited`; the app made what the in-process call made, and the step is the commit's.
                Assert.Single(answer.GetArray("edited"));
                AssertSameEntities(scene.Twin, scene.Served, Tight);
                Assert.Equal("REMOTE", scene.Served.NextUndo.Name);
            }
        }

        [Fact]
        public void TwoCopyOperationsInOneRawCommit_AnswerOneListOfHandlesEach_InTheOrderOfTheOperations_AndTheObjectsMade()
        {
            using (var scene = new Scene())
            {
                JsonObject answer = Commit(
                    scene,
                    Transform("copy", scene.Ids.Line).Set("dx", 0.0).Set("dy", 10.0),
                    Transform("copy", scene.Ids.Line, scene.Ids.Side).Set("dx", 0.0).Set("dy", 20.0)
                );

                // The first operation made one line, the second two, in the order the handles were given; `objects` is what `get` answers
                // for the three, in that order.
                JsonArray edited = answer.GetArray("edited");
                Assert.Equal(2, edited.Count);
                var first = (JsonArray)edited[0];
                var second = (JsonArray)edited[1];
                Assert.Single(first);
                Assert.Equal(2, second.Count);
                var all = first.Concat(second).Cast<string>().ToList();
                Assert.Equal(3, all.Distinct().Count());
                JsonObject objects = answer.GetObject("objects");
                Assert.NotNull(objects);
                Assert.Equal(all, objects.GetArray("handles").Cast<string>());
                Near(P3(100, 10), ((DxfLine)scene.Served.FindEntity(first[0] as string)).EndPoint);
                Near(P3(100, 20), ((DxfLine)scene.Served.FindEntity(second[0] as string)).EndPoint);
                Near(P3(0, 70), ((DxfLine)scene.Served.FindEntity(second[1] as string)).EndPoint);
            }
        }

        [Fact]
        public void AnEditOperationNamingAHandleTheDrawingHasNot_IsRefused_NothingIsApplied()
        {
            using (var scene = new Scene())
            {
                string start = scene.Text(scene.Served);

                CadRemoteException refused = Assert.Throws<CadRemoteException>(() =>
                    Commit(scene, Transform("move", scene.Ids.Line, "ZZZ").Set("dx", 1.0).Set("dy", 1.0))
                );

                Assert.Contains("ZZZ", refused.Message, StringComparison.Ordinal);
                Assert.Equal(start, scene.Text(scene.Served));
                Assert.False(scene.Served.CanUndo);
            }
        }

        [Fact]
        public void AnEditOperationNamingATicket_IsRefused_NothingIsApplied()
        {
            using (var scene = new Scene())
            {
                string start = scene.Text(scene.Served);

                // The client never sends a ticket as a handle (an object not committed is no object it gave, see above); a program
                // speaking the protocol that does is refused with some words, as a handle the drawing has not is.
                CadRemoteException refused = Assert.Throws<CadRemoteException>(() =>
                    Commit(scene, Transform("move", "~1").Set("dx", 1.0).Set("dy", 1.0))
                );

                Assert.Contains("~1", refused.Message, StringComparison.Ordinal);
                Assert.Equal(start, scene.Text(scene.Served));
                Assert.False(scene.Served.CanUndo);
            }
        }

        #endregion

        #region Inside a run of a tool

        private const string Waiting = "EDITSPY is working... (Esc to stop)";

        /// <summary>A tool's run of EDITSPY in the drawing A-101.dxf, joined from a tool's connection, as RemoteRunTests make one.</summary>
        private sealed class RunScene : IDisposable
        {
            public readonly Harness Harness = new Harness();
            public readonly TestHost Host = new TestHost();
            public readonly CadDocument Document;
            public readonly Ids Ids;
            public readonly CadCommandSession Session;
            public readonly CadRemoteServer Tools;
            public readonly string Token;
            public readonly CadCommand Command;
            public CadRemoteToolRun Run;
            public CadRemoteRun Joined;
            public CadRemoteDocument Drawing;

            public RunScene()
            {
                Document = Host.Add(Build("A-101.dxf", out Ids));
                Session = Host.SessionOf(Document);
                Tools = Harness.ServeTools(Host);
                Token = Tools.IssueToken();
                Command = Tools.CreateRunCommand("EDITSPY", false, Token, run => Run = run);
                Assert.True(Host.OnAppThread(() => Session.Begin(Command)));
                CadApplication client = Harness.ConnectTool(Tools, Token);
                Joined = client.JoinRun(Run.Id);
                Drawing = client.Documents.First(d => d.Name == Document.Title);
            }

            public string Text() => Host.OnAppThread(() => Document.Database.ToText());

            public void Dispose() => Harness.Dispose();
        }

        [Fact]
        public void EditsInARun_JoinTheRunsStep_ManyCallsOneUndoStepNamedAfterTheCommand()
        {
            using (var scene = new RunScene())
            {
                string start = scene.Text();
                var line = (DxfLine)scene.Drawing.GetObject(scene.Ids.Line);
                var circle = (DxfCircle)scene.Drawing.GetObject(scene.Ids.Circle);

                scene.Drawing.Copy(new[] { line }, new GeoVector2(0, 50));
                scene.Drawing.Move(new[] { circle }, Shift);
                scene.Drawing.Insert(Line(0, 100, 50, 100));
                scene.Drawing.Rotate(new[] { circle }, new GeoPoint2(0, 0), 1.0);
                scene.Joined.End();

                // Nothing is a step of its own while the run lasts; at its end the copy, the move, the insert and the turn are the one
                // step of EDITSPY, which one U takes back.
                Assert.False(scene.Host.OnAppThread(() => scene.Session.IsRunning));
                Assert.Equal("EDITSPY", scene.Host.OnAppThread(() => scene.Document.NextUndo.Name));
                Assert.NotEqual(start, scene.Text());
                Assert.True(scene.Host.OnAppThread(() => scene.Document.Undo()));
                Assert.Equal(start, scene.Text());
                Assert.False(scene.Host.OnAppThread(() => scene.Document.CanUndo));
            }
        }

        [Fact]
        public void AnEditInARunIsTakenBack_ByEscAtTheWaitingPrompt()
        {
            using (var scene = new RunScene())
            {
                string start = scene.Text();
                var line = (DxfLine)scene.Drawing.GetObject(scene.Ids.Line);

                scene.Drawing.Copy(new[] { line }, new GeoVector2(0, 50));
                Assert.NotEqual(start, scene.Text());
                Assert.Equal(Waiting, scene.Host.OnAppThread(() => scene.Session.PromptText));
                scene.Host.OnAppThread(() => scene.Session.Escape());

                Assert.Equal(start, scene.Text());
                Assert.False(scene.Host.OnAppThread(() => scene.Document.CanUndo));
                Assert.Equal(CadRemoteToolRunOutcome.Cancelled, scene.Run.Outcome);
            }
        }

        [Fact]
        public void ARefusedEditInARun_ChangesNothing_AndTheEditsBeforeItKeepToTheRunsStep()
        {
            using (var scene = new RunScene())
            {
                string start = scene.Text();
                var line = (DxfLine)scene.Drawing.GetObject(scene.Ids.Line);
                var locked = (DxfLine)scene.Drawing.GetObject(scene.Ids.Locked);

                scene.Drawing.Move(new[] { line }, Shift);
                string moved = scene.Text();
                CadRemoteException refused = Assert.Throws<CadRemoteException>(() =>
                    scene.Drawing.Move(new[] { locked }, Shift)
                );
                scene.Joined.End();

                Assert.Contains("is on the locked layer Locked.", refused.Message, StringComparison.Ordinal);
                Assert.Equal(moved, scene.Text());
                Assert.True(scene.Host.OnAppThread(() => scene.Document.Undo()));
                Assert.Equal(start, scene.Text());
            }
        }

        #endregion
    }
}
