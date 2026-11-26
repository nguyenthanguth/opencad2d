using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// A snapshot and a reset of a drawing in memory (PW7.3): after an episode of edits, the drawing is as it was when the
    /// snapshot was taken, to the byte: its DXF text, its JSON export, its handles and <c>$HANDSEED</c>, its bounds; the display
    /// lists, the spatial index and the pictures of dimensions are right after it, whether the undo list still holds the point
    /// (the fast path) or the drawing is read back from the bytes the snapshot keeps (the other).
    /// </summary>
    public class SnapshotResetTests
    {
        /// <summary>The whole state of a drawing, as text: the DXF, the JSON export, and the bounds of model space.</summary>
        internal static string StateOf(CadDocument document)
        {
            CadBoundsTable bounds = CadSpatial.Bounds(document, document.ModelSpace);
            return document.Database.ToText()
                + "\n#json\n"
                + CadDrawingJson.Write(document, null)
                + "\n#bounds\n"
                + string.Join(" ", bounds.Handles)
                + "\n"
                + string.Join(" ", bounds.Boxes.Select(d => d.ToString("R", CultureInfo.InvariantCulture)));
        }

        /// <summary>The display list of model space as the document holds it, then made again by a REGEN: the two must be the same.</summary>
        private static void AssertDisplayIsWhatARegenMakes(CadDocument document)
        {
            List<string> held = DisplayPatchTests.Describe(document.GetDisplay(document.ModelSpace));
            CadBoundsTable heldBounds = CadSpatial.Bounds(document, document.ModelSpace);
            string[] heldHandles = heldBounds.Handles.ToArray();
            double[] heldBoxes = heldBounds.Boxes.ToArray();
            document.Regen();
            List<string> made = DisplayPatchTests.Describe(document.GetDisplay(document.ModelSpace));
            CadBoundsTable madeBounds = CadSpatial.Bounds(document, document.ModelSpace);
            Assert.Equal(made, held);
            Assert.Equal(madeBounds.Handles, heldHandles);
            Assert.Equal(madeBounds.Boxes, heldBoxes);
        }

        private static void Add(CadDocument document, double x, double y) =>
            document.ApplyEdit("LINE", null, s =>
                s.Append(s.Document.ModelSpace, new DxfLine(new GeoPoint3(x, y, 0), new GeoPoint3(x + 10, y + 5, 0)))
            );

        private static void Move(CadDocument document, string handle) =>
            document.ApplyEdit("MOVE", new[] { new CadEditRecord(CadEditRecordKind.Transformed, handle) }, s =>
                s.Document.FindEntity(handle).TransformBy(GeoTransform3.Translation(new GeoVector3(7, 3, 0)))
            );

        private static void Erase(CadDocument document, string handle) =>
            document.ApplyEdit("ERASE", new[] { new CadEditRecord(CadEditRecordKind.Erased, handle) }, s =>
                s.Document.FindEntity(handle).Erase()
            );

        private static void SetLayer(CadDocument document, string handle, string layer) =>
            document.ApplyEdit(
                "CHPROP",
                new[] { new CadEditRecord(CadEditRecordKind.PropertyChanged, handle, "Layer", "0", layer) },
                s => s.Document.FindEntity(handle).Layer = layer
            );

        /// <summary>An episode of every kind of edit a program makes: an entity added, moved, erased and changed, a layer added and made current.</summary>
        private static void Episode(CadDocument document)
        {
            Add(document, 5, 5);
            Move(document, "A1");
            Erase(document, "A3");
            SetLayer(document, "A2", "Steel");
            Assert.True(CadLayerEditor.Add(document, "Episode").Succeeded);
            Assert.True(CadLayerEditor.SetCurrent(document, "Episode").Succeeded);
            Add(document, 50, 50);
            Move(document, "A6");
        }

        private static string FileName() =>
            Path.Combine(Path.GetTempPath(), "snapshot-" + Guid.NewGuid().ToString("N") + ".dxf");

        [Fact]
        public void AnEpisodeOfEdits_IsGoneAfterAReset_ToTheByte()
        {
            CadDocument document = Plan();
            string before = StateOf(document);
            CadSnapshot snapshot = document.TakeSnapshot();

            Episode(document);
            Assert.NotEqual(before, StateOf(document));
            document.Reset(snapshot);

            Assert.Equal(before, StateOf(document));
            Assert.Same(document, snapshot.Document);
            Assert.False(document.IsModified);
            Assert.False(document.CanUndo);
            Assert.False(document.CanRedo);
            Assert.Equal("0", document.Database.Clayer);
            Assert.False(document.Database.LayerTable.Has("Episode"));
            Assert.NotNull(document.FindEntity("A3"));
            Assert.Equal(8, document.EntityCount);
        }

        [Fact]
        public void TheSameSnapshot_IsResetToAgainAndAgain_AndTheHandlesRepeat()
        {
            CadDocument document = Plan();
            string before = StateOf(document);
            CadSnapshot snapshot = document.TakeSnapshot();
            string firstHandle = null;

            for (int episode = 0; episode < 4; episode++)
            {
                Add(document, 5, 5);
                string added = document.ModelSpace.Block.Last().Handle;
                firstHandle = firstHandle ?? added;
                Assert.Equal(firstHandle, added);
                Episode(document);
                document.Reset(snapshot);
                Assert.Equal(before, StateOf(document));
            }
        }

        [Fact]
        public void TheDisplayList_TheSpatialIndexAndTheHandles_AreRightAfterAReset()
        {
            CadDocument document = Plan();
            document.GetDisplay(document.ModelSpace);
            CadSnapshot snapshot = document.TakeSnapshot();
            List<string> start = DisplayPatchTests.Describe(document.GetDisplay(document.ModelSpace));

            Episode(document);
            document.GetDisplay(document.ModelSpace);
            document.Reset(snapshot);

            Assert.Equal(start, DisplayPatchTests.Describe(document.GetDisplay(document.ModelSpace)));
            AssertDisplayIsWhatARegenMakes(document);
            Assert.NotNull(document.FindEntity("A3"));
            Assert.Null(document.FindEntity("A9"));
        }

        [Fact]
        public void ADimensionTiedToALine_IsDrawnAsItWasAfterAReset()
        {
            CadDocument document = TiedDimension(out string[] handles);
            document.GetDisplay(document.ModelSpace);
            CadSnapshot snapshot = document.TakeSnapshot();
            string before = StateOf(document);
            List<string> picture = DisplayPatchTests.Describe(document.GetDisplay(document.ModelSpace));

            Move(document, handles[0]);
            document.ApplyEdit(
                "GRIP",
                new[] { new CadEditRecord(CadEditRecordKind.Reshaped, handles[0], "Grip 1") },
                s => ((DxfLine)s.Document.FindEntity(handles[0])).EndPoint = new GeoPoint3(180, 40, 0)
            );
            Assert.NotEqual(picture, DisplayPatchTests.Describe(document.GetDisplay(document.ModelSpace)));
            document.Reset(snapshot);

            Assert.Equal(before, StateOf(document));
            Assert.Equal(picture, DisplayPatchTests.Describe(document.GetDisplay(document.ModelSpace)));
            AssertDisplayIsWhatARegenMakes(document);
        }

        [Fact]
        public void AResetToAPointBelowWhereTheListStands_GoesForward_AndForgetsWhatIsAfterIt()
        {
            CadDocument document = Plan();
            CadSnapshot start = document.TakeSnapshot();
            Add(document, 1, 1);
            Move(document, "A1");
            string middle = StateOf(document);
            CadSnapshot second = document.TakeSnapshot();
            Erase(document, "A2");
            Assert.True(document.Undo());
            Assert.True(document.Undo());
            Assert.True(document.Undo());
            Assert.False(document.CanUndo);

            document.Reset(second);

            Assert.Equal(middle, StateOf(document));
            Assert.False(document.CanRedo);
            Assert.True(document.CanUndo);
            document.Reset(start);
            Assert.False(document.CanUndo);
            Assert.NotNull(document.FindEntity("A2"));
        }

        [Fact]
        public void ASnapshotTheUndoListNoLongerHolds_IsResetToFromItsOwnBytes()
        {
            CadDocument document = Plan();
            string first = StateOf(document);
            CadSnapshot start = document.TakeSnapshot();
            Add(document, 1, 1);
            Move(document, "A1");
            string second = StateOf(document);
            CadSnapshot later = document.TakeSnapshot();
            Erase(document, "A2");

            document.Reset(start);
            Assert.Equal(first, StateOf(document));
            Assert.False(document.CanUndo);

            // The reset to the first cut the list below the second: its point is gone, the drawing is read from its bytes.
            document.Reset(later);
            Assert.Equal(second, StateOf(document));
            Assert.False(document.CanUndo);
            Assert.True(document.IsModified);
            AssertDisplayIsWhatARegenMakes(document);

            // Edits go on from there as from any drawing, and either snapshot can be reset to again.
            Add(document, 9, 9);
            Assert.True(document.CanUndo);
            document.Reset(start);
            Assert.Equal(first, StateOf(document));
            document.Reset(later);
            Assert.Equal(second, StateOf(document));
        }

        [Fact]
        public void ADrawingUndoneBelowTheSnapshotAndEditedAgain_IsStillResetExactly()
        {
            CadDocument document = Plan();
            Add(document, 1, 1);
            Add(document, 2, 2);
            string at = StateOf(document);
            CadSnapshot snapshot = document.TakeSnapshot();
            Add(document, 3, 3);

            Assert.True(document.Undo());
            Assert.True(document.Undo());
            Move(document, "A1");
            Erase(document, "A2");
            document.Reset(snapshot);

            Assert.Equal(at, StateOf(document));
            Add(document, 3, 3);
            document.Reset(snapshot);
            Assert.Equal(at, StateOf(document));
        }

        [Fact]
        public void ASettingChangedBeforeTheSnapshot_IsBackAfterAResetOnEitherPath()
        {
            CadDocument document = Plan();
            double start = document.Database.Textsize;
            CadSnapshot first = document.TakeSnapshot();

            document.ApplySetting(
                "SETVAR",
                "TEXTSIZE",
                () => document.Database.Textsize.ToString("R", CultureInfo.InvariantCulture),
                () => document.Database.Textsize = 7.5,
                (doc, forward) => doc.Database.Textsize = forward ? 7.5 : start,
                false
            );
            CadSnapshot second = document.TakeSnapshot();
            Add(document, 1, 1);
            document.ApplySetting(
                "SETVAR",
                "TEXTSIZE",
                () => document.Database.Textsize.ToString("R", CultureInfo.InvariantCulture),
                () => document.Database.Textsize = 9,
                (doc, forward) => doc.Database.Textsize = forward ? 9 : 7.5,
                false
            );

            document.Reset(second);
            Assert.Equal(7.5, document.Database.Textsize);
            document.Reset(first);
            Assert.Equal(start, document.Database.Textsize);

            // The list is cut below the second: it is read from its own bytes, which hold the setting it was taken with.
            document.Reset(second);
            Assert.Equal(7.5, document.Database.Textsize);
        }

        [Fact]
        public void AResetRaisesChanged_NamingTheEntitiesTheEpisodeTouched()
        {
            CadDocument document = Plan();
            CadSnapshot snapshot = document.TakeSnapshot();
            Move(document, "A1");
            Erase(document, "A3");
            var seen = new List<CadDocumentChangedEventArgs>();
            document.Changed += (sender, e) => seen.Add(e);

            document.Reset(snapshot);

            CadDocumentChangedEventArgs reset = Assert.Single(seen);
            Assert.Equal(CadChangeKind.Undo, reset.Kind);
            Assert.Equal("RESET", reset.StepName);
            Assert.Contains("A1", reset.Handles);
            Assert.Contains("A3", reset.Handles);
        }

        [Fact]
        public void AResetFromBytes_RaisesRollback_ForEverySpace()
        {
            CadDocument document = Plan();
            CadSnapshot start = document.TakeSnapshot();
            Add(document, 1, 1);
            CadSnapshot later = document.TakeSnapshot();
            document.Reset(start);
            var seen = new List<CadDocumentChangedEventArgs>();
            document.Changed += (sender, e) => seen.Add(e);

            document.Reset(later);

            CadDocumentChangedEventArgs rolled = Assert.Single(seen);
            Assert.Equal(CadChangeKind.Rollback, rolled.Kind);
            Assert.Equal(document.Spaces.Count, rolled.Spaces.Count);
        }

        [Fact]
        public void TheEntitiesSelectedThatAreStillThere_StaySelected()
        {
            CadDocument document = Plan();
            CadSnapshot snapshot = document.TakeSnapshot();
            Add(document, 1, 1);
            Erase(document, "A3");
            document.Selection.Set(new[] { "A1", "A3" });
            document.Reset(snapshot);

            Assert.Equal(new[] { "A1", "A3" }, document.Selection.Handles.OrderBy(h => h, StringComparer.Ordinal));
        }

        [Fact]
        public void TheDrawingIsModifiedAfterAResetOnlyWhereItsFileHoldsTheEpisode()
        {
            string path = FileName();

            try
            {
                CadDocument document = Plan();
                document.SaveAs(path);
                CadSnapshot snapshot = document.TakeSnapshot();
                Add(document, 1, 1);
                Assert.True(document.IsModified);
                document.Reset(snapshot);
                Assert.False(document.IsModified);

                Add(document, 1, 1);
                document.Save();
                Assert.False(document.IsModified);
                document.Reset(snapshot);
                Assert.True(document.IsModified);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ASnapshotAndAReset_AreRefusedWhileACommandRuns_OrOfAnotherDrawing()
        {
            CadDocument document = Plan();
            CadSnapshot snapshot = document.TakeSnapshot();

            document.BeginGroup("LINE");
            InvalidOperationException whenSnapshot = Assert.Throws<InvalidOperationException>(document.TakeSnapshot);
            InvalidOperationException whenReset = Assert.Throws<InvalidOperationException>(() =>
                document.Reset(snapshot)
            );
            document.CancelGroup();

            Assert.Contains("a command is running", whenSnapshot.Message);
            Assert.Contains("a command is running", whenReset.Message);
            Assert.Throws<ArgumentNullException>(() => document.Reset(null));
            ArgumentException other = Assert.Throws<ArgumentException>(() => Plan().Reset(snapshot));
            Assert.Contains("another drawing", other.Message);
            document.Reset(snapshot);
        }

        [Fact]
        public void ASnapshotReleased_IsResetToAllTheSame_TheDrawingReadAheadLetGo()
        {
            CadDocument document = Plan();
            string before = StateOf(document);
            CadSnapshot snapshot = document.TakeSnapshot();
            Add(document, 1, 1);
            document.Reset(snapshot);
            Add(document, 1, 1);

            document.ReleaseSnapshot(snapshot);
            document.ReleaseSnapshot(snapshot);
            document.Reset(snapshot);

            Assert.Equal(before, StateOf(document));
            Assert.Throws<ArgumentNullException>(() => document.ReleaseSnapshot(null));
        }

        [Fact]
        public void ASnapshotOfADrawingNotEditedYet_StartsItsUndoList_AndASecondOneCostsNoMoreSteps()
        {
            CadDocument document = Blank();
            Assert.False(document.CanUndo);
            CadSnapshot first = document.TakeSnapshot();
            CadSnapshot second = document.TakeSnapshot();
            Assert.False(document.CanUndo);
            Add(document, 1, 1);
            Assert.True(document.CanUndo);
            document.Reset(second);
            Assert.False(document.CanUndo);
            Assert.Equal(0, document.EntityCount);
            document.Reset(first);
        }
    }
}
