using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// <see cref="CadEditor.Check"/>, the dry run of an edit: every check the edit makes, those of its arguments, of the objects,
    /// of the plan (an offset that makes no curve, a side point on the curve) and of the owner's gate, and nothing written. The
    /// drawing is <see cref="EditingDrawings.Plan"/> (A1 a line on Steel from (0, 0) to (100, 0), A3 a circle of radius 10 about
    /// (200, 0), A8 a line on the locked layer Locked), the session's tolerance <see cref="EditingDrawings.Tight"/>.
    /// </summary>
    public class EditorCheckTests
    {
        private static CadCommandSession Typed(CadDocument document)
        {
            CadCommandSession session = Session(document);
            session.Tolerance = Tight;
            return session;
        }

        private static string Text(CadDocument document) => document.Database.ToText();

        /// <summary>The words an edit's own call says for the same refusal, which a check says as well.</summary>
        private static string Words(Action call)
        {
            Exception refused = Assert.ThrowsAny<Exception>(call);
            Assert.True(refused is ArgumentException || refused is InvalidOperationException);
            return refused.Message;
        }

        private static readonly EditorEditTests.Call[] Calls =
        {
            new EditorEditTests.Call("Move", (e, l) => e.Move(l, new GeoVector2(10, 5))),
            new EditorEditTests.Call("Copy", (e, l) => e.Copy(l, new GeoVector2(10, 5))),
            new EditorEditTests.Call("Rotate", (e, l) => e.Rotate(l, new GeoPoint2(0, 0), Math.PI / 2.0)),
            new EditorEditTests.Call("Scale", (e, l) => e.Scale(l, new GeoPoint2(0, 0), 2.0)),
            new EditorEditTests.Call("Mirror", (e, l) => e.Mirror(l, new GeoPoint2(0, 0), new GeoPoint2(0, 10), false)),
            new EditorEditTests.Call("MirrorAndErase", (e, l) =>
                e.Mirror(l, new GeoPoint2(0, 0), new GeoPoint2(0, 10), true)
            ),
            new EditorEditTests.Call("Erase", (e, l) =>
            {
                e.Erase(l);
                return null;
            }),
            new EditorEditTests.Call("Offset", (e, l) => e.Offset((DxfCurve)l[0], 5.0, new GeoPoint2(50, 10)), true),
        };

        public static IEnumerable<object[]> EveryCall() => Calls.Select(c => new object[] { c });

        public static IEnumerable<object[]> EveryCallAndEveryReason() =>
            Calls.SelectMany(c =>
                new[] { "Locked", "OtherDrawing", "Erased", "InsideABlock" }.Select(r => new object[] { c, r })
            );

        [Theory]
        [MemberData(nameof(EveryCall))]
        public void AnEditThatWouldBeMade_IsCheckedToNull_AndTheDrawingStaysAsItWas(EditorEditTests.Call call)
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            DxfEntity[] given = { Entity<DxfLine>(document, "A1"), Entity<DxfCircle>(document, "A3") };
            string text = Text(document);
            bool modified = document.IsModified;
            int written = session.History.Count;
            int count = document.Database.ModelSpace.Count();

            string refused = session.Editor.Check(e => call.Run(e, given));

            // Copy, Mirror and Offset would add objects; the check added none, opened no step and no group, and wrote nothing.
            Assert.Null(refused);
            Assert.Equal(text, Text(document));
            Assert.Equal(count, document.Database.ModelSpace.Count());
            Assert.False(document.CanUndo);
            Assert.False(document.IsInGroup);
            Assert.Equal(modified, document.IsModified);
            Assert.Equal(written, session.History.Count);
        }

        [Theory]
        [MemberData(nameof(EveryCall))]
        public void AnEditCheckedToNull_IsMadeByTheSameCallAfterwards(EditorEditTests.Call call)
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            DxfEntity[] given = { Entity<DxfLine>(document, "A1") };

            Assert.Null(session.Editor.Check(e => call.Run(e, given)));
            call.Run(session.Editor, given);

            // The check left nothing switched on: the call that follows it is made, as one step.
            Assert.True(document.IsModified);
            Assert.True(document.CanUndo);
        }

        [Theory]
        [MemberData(nameof(EveryCallAndEveryReason))]
        public void AnObjectTheDrawingWillNotLetBeEdited_IsCheckedToTheWordsTheCallSays_ChangingNothing(
            EditorEditTests.Call call,
            string reason
        )
        {
            CadDocument document = Plan();
            CadDocument other = Plan();
            DxfLine target;

            switch (reason)
            {
                case "Locked":
                    target = Entity<DxfLine>(document, "A8");
                    break;
                case "OtherDrawing":
                    target = Entity<DxfLine>(other, "A1");
                    break;
                case "Erased":
                    target = Entity<DxfLine>(document, "A2");
                    target.Erase();
                    break;
                default:
                    target = AddLineInBox(document);
                    break;
            }

            CadCommandSession session = Typed(document);
            DxfEntity[] given = { target };
            string text = Text(document);

            string checkedWords = session.Editor.Check(e => call.Run(e, given));
            string calledWords = Words(() => call.Run(session.Editor, given));

            // The locked layer, the other drawing, the erased object and the block's object: the same words the call throws.
            Assert.NotNull(checkedWords);
            Assert.Equal(calledWords, checkedWords);
            Assert.Equal(text, Text(document));
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void ALockedObjectAmongFreeOnes_IsCheckedToTheFirstRefusedByName()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            DxfEntity[] given = { Entity<DxfLine>(document, "A1"), Entity<DxfLine>(document, "A8") };

            string refused = session.Editor.Check(e => e.Move(given, new GeoVector2(10, 5)));

            Assert.Equal("The object A8 is on the locked layer Locked.", refused);
        }

        [Fact]
        public void AnOffsetThatMakesNoCurve_IsCheckedToTheCommandsWords_WhichOnlyThePlanKnows()
        {
            // A circle of radius 10 offset inward by 15 makes nothing: OFFSET says "Cannot offset that object."
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            DxfCircle circle = Entity<DxfCircle>(document, "A3");
            string text = Text(document);

            string refused = session.Editor.Check(e => e.Offset(circle, 15.0, new GeoPoint2(205, 0)));

            Assert.Equal("Cannot offset that object.", refused);
            Assert.Equal(text, Text(document));
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void AnOffsetOutwardOfTheSameCircle_IsCheckedToNull()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            DxfCircle circle = Entity<DxfCircle>(document, "A3");

            Assert.Null(session.Editor.Check(e => e.Offset(circle, 15.0, new GeoPoint2(230, 0))));
        }

        [Fact]
        public void AnOffsetWhoseSidePointLiesOnTheCurve_IsCheckedToWords_AndSoIsADistanceUnderTheTolerance()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            DxfLine line = Entity<DxfLine>(document, "A1");

            string onCurve = session.Editor.Check(e => e.Offset(line, 5.0, new GeoPoint2(50, 0)));
            string tiny = session.Editor.Check(e => e.Offset(line, 1E-12, new GeoPoint2(50, 10)));

            Assert.Contains("lies on the curve", onCurve, StringComparison.Ordinal);
            Assert.NotNull(tiny);
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void ANumberThatIsNoEdit_IsCheckedToWords_ForEveryEdit()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            DxfEntity[] given = { Entity<DxfLine>(document, "A1") };

            Assert.NotNull(session.Editor.Check(e => e.Scale(given, new GeoPoint2(0, 0), 0.0)));
            Assert.NotNull(session.Editor.Check(e => e.Scale(given, new GeoPoint2(0, 0), double.NaN)));
            Assert.NotNull(session.Editor.Check(e => e.Move(given, new GeoVector2(double.NaN, 0))));
            Assert.NotNull(session.Editor.Check(e => e.Rotate(given, new GeoPoint2(0, 0), double.PositiveInfinity)));
            Assert.NotNull(session.Editor.Check(e => e.Mirror(given, new GeoPoint2(1, 1), new GeoPoint2(1, 1), false)));
            Assert.False(document.CanUndo);
        }

        [Theory]
        [InlineData("Move", true)]
        [InlineData("Copy", true)]
        [InlineData("Rotate", false)]
        [InlineData("Scale", false)]
        [InlineData("Erase", false)]
        public void AnObjectItsOwnerAllowsOnlyToMove_IsCheckedThroughTheOwnersGate(string how, bool allowed)
        {
            CadDocument document = OwnedLines(CadEditRule.MoveOnly);
            CadCommandSession session = Typed(document);
            DxfEntity[] given = { Entity<DxfLine>(document, "A2"), Entity<DxfLine>(document, "A1") };
            string text = Text(document);

            string refused = session.Editor.Check(e =>
            {
                switch (how)
                {
                    case "Move":
                        e.Move(given, new GeoVector2(10, 5));
                        break;
                    case "Copy":
                        e.Copy(given, new GeoVector2(10, 5));
                        break;
                    case "Rotate":
                        e.Rotate(given, new GeoPoint2(0, 0), 1.0);
                        break;
                    case "Scale":
                        e.Scale(given, new GeoPoint2(0, 0), 2.0);
                        break;
                    default:
                        e.Erase(given);
                        break;
                }
            });

            // The gate's words, as the call throws them; the owner that allows only moving lets a copy through.
            Assert.Equal(allowed ? null : "This line belongs to the model and can only be moved.", refused);
            Assert.Equal(text, Text(document));
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void AnObjectKeptAcrossAnUndo_IsCheckedAsNotInTheDrawing()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            DxfLine kept = Entity<DxfLine>(document, "A1");
            session.Editor.Move(new[] { kept }, new GeoVector2(10, 5));
            Assert.True(document.Undo());

            string refused = session.Editor.Check(e => e.Move(new[] { kept }, new GeoVector2(10, 5)));

            Assert.Equal("The object A1 is not in the drawing " + document.Title + ".", refused);
        }

        [Fact]
        public void AnEmptyList_IsCheckedToNull_AndANullListToWords()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);

            Assert.Null(session.Editor.Check(e => e.Move(new DxfLine[0], new GeoVector2(10, 5))));
            Assert.NotNull(session.Editor.Check(e => e.Move<DxfLine>(null, new GeoVector2(10, 5))));
        }

        [Fact]
        public void ACheckInsideACommandThatRuns_OpensNoGroupAndWritesNoStep()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            DxfLine line = Entity<DxfLine>(document, "A1");
            string refused = "unset";
            bool grouped = true;
            session.Registry.Add(new CadCommand("CHECKING", c =>
                {
                    refused = c.Editor.Check(e => e.Move(new[] { line }, new GeoVector2(10, 5)));
                    grouped = document.IsInGroup;
                    return Array.Empty<CadPrompt>();
                }));

            Run(session, "CHECKING");

            // The check made no group for itself and wrote nothing, so the command that ran it has no step.
            Assert.Null(refused);
            Assert.False(grouped);
            Assert.False(document.CanUndo);
            Near(new GeoPoint3(0, 0, 0), line.StartPoint);
        }

        [Fact]
        public void ACheckWithNoEdit_IsRefused()
        {
            CadCommandSession session = Typed(Plan());

            Assert.Throws<ArgumentNullException>(() => session.Editor.Check(null));
        }

        [Fact]
        public void ACheckWhoseEditThrowsSomethingElse_PassesItOn_AndSwitchesTheDryRunOff()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            DxfLine line = Entity<DxfLine>(document, "A1");

            Assert.Throws<NotSupportedException>(() => session.Editor.Check(e => throw new NotSupportedException()));
            session.Editor.Move(new[] { line }, new GeoVector2(10, 5));

            Assert.True(document.CanUndo);
        }

        [Fact]
        public void ACheckInsideACheck_KeepsTheDryRunUntilTheOuterOneEnds()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            DxfLine line = Entity<DxfLine>(document, "A1");

            string refused = session.Editor.Check(e =>
            {
                Assert.Null(e.Check(inner => inner.Move(new[] { line }, new GeoVector2(10, 5))));
                e.Move(new[] { line }, new GeoVector2(10, 5));
            });

            Assert.Null(refused);
            Assert.False(document.CanUndo);
        }
    }
}
