using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// UCSMAN (UC), as AutoCAD 2021's UCS dialog (probed in acad.exe): the Named UCSs list and its order, Set Current, Rename
    /// with the three refusals, Delete, Details relative to World or a named system, the Orthographic UCSs tab, the Settings
    /// tab, and everything the dialog changed given to the drawing at OK as one undo step named UCSMAN.
    /// </summary>
    public sealed class UcsManagerDialogTests
    {
        private static readonly double Half = Math.Sqrt(0.5);

        /// <summary>
        /// The drawing of the probes of AutoCAD: Rot45 (turned 45 degrees at the origin), Org100 (the same with its origin at
        /// (100, 50) of Rot45, which is (35.3553, 106.0660) of the world), Tilt90 (turned 90 degrees about X, not parallel to
        /// the view), and the current system unnamed, turned 20 degrees at the origin.
        /// </summary>
        private static CadDocument Probed()
        {
            var database = new DxfDatabase("AC1027");
            GeoVector3 x45 = new GeoVector3(Half, Half, 0);
            GeoVector3 y45 = new GeoVector3(-Half, Half, 0);
            database.UcsTable.Add(
                new DxfUcsTableRecord
                {
                    Name = "Rot45",
                    XAxis = x45,
                    YAxis = y45,
                }
            );
            database.UcsTable.Add(
                new DxfUcsTableRecord
                {
                    Name = "Org100",
                    Origin = new GeoPoint3(100 * Half - 50 * Half, 100 * Half + 50 * Half, 0),
                    XAxis = x45,
                    YAxis = y45,
                }
            );
            database.UcsTable.Add(
                new DxfUcsTableRecord
                {
                    Name = "Tilt90",
                    XAxis = GeoVector3.XAxis,
                    YAxis = GeoVector3.ZAxis,
                }
            );
            double angle = 20.0 * Math.PI / 180.0;
            database.Ucs = new GeoCoordinateSystem3(
                GeoPoint3.Origin,
                new GeoVector3(Math.Cos(angle), Math.Sin(angle), 0),
                new GeoVector3(-Math.Sin(angle), Math.Cos(angle), 0)
            );
            return CadDocument.From(database, "probed.dxf");
        }

        /// <summary>Runs a command with a window that answers as the test says; the request the window was given is handed back.</summary>
        private static CadUcsDialogRequest Shown(
            CadCommandSession session,
            string command,
            Action<CadUcsDialogRequest> answer
        ) => ShownEach(session, command, (request, count) => answer(request));

        /// <summary>
        /// A window that answers each time the dialog is shown, told how many times that is. An assertion that fails inside the
        /// answer is kept and raised by <see cref="Check"/>, as the command would otherwise report it as its own failure.
        /// </summary>
        private sealed class Window
        {
            private Exception _failure;

            public CadUcsDialogRequest Shown { get; private set; }

            public int Count { get; private set; }

            public Window(CadCommandSession session, Action<CadUcsDialogRequest, int> answer)
            {
                session.UiRequested += (sender, e) =>
                {
                    if (e.Name != "UCSMAN")
                    {
                        return;
                    }

                    Shown = (CadUcsDialogRequest)e.Payload;
                    e.Handled = true;
                    Count++;

                    try
                    {
                        answer(Shown, Count);
                    }
                    catch (Exception exception)
                    {
                        _failure = _failure ?? exception;
                    }
                };
            }

            public void Check()
            {
                if (_failure != null)
                {
                    ExceptionDispatchInfo.Capture(_failure).Throw();
                }
            }
        }

        private static CadUcsDialogRequest ShownEach(
            CadCommandSession session,
            string command,
            Action<CadUcsDialogRequest, int> answer
        )
        {
            var window = new Window(session, answer);
            Run(session, command);
            window.Check();
            return window.Shown;
        }

        private static CadUcsDialogRow Row(CadUcsDialogRequest request, string name) =>
            request.Rows.Single(r => r.Name == name);

        [Fact]
        public void WithNoWindow_TheCommandSaysTheWindowShowsIt_AndChangesNothing()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);

            Run(session, "UCSMAN");

            Assert.StartsWith("UCSMAN is shown by the window", session.History.Last(), StringComparison.Ordinal);
            Assert.False(session.IsRunning);
            Assert.Null(document.NextUndo);
        }

        [Fact]
        public void TheList_IsUnnamedWorldPreviousThenTheNamedAToZ_WithTheTriangleOnTheCurrentOne()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);
            CadUcsDialogRequest request = null;

            Shown(session, "UCSMAN", r => request = r);

            Assert.Equal(
                new[] { "Unnamed", "World", "Previous", "Org100", "Rot45", "Tilt90" },
                request.Rows.Select(r => r.Name).ToArray()
            );
            Assert.Equal(
                new[] { CadUcsRowKind.Unnamed, CadUcsRowKind.World, CadUcsRowKind.Previous, CadUcsRowKind.Named },
                request.Rows.Select(r => r.Kind).Distinct().ToArray()
            );
            Assert.Equal(new[] { "Unnamed" }, request.Rows.Where(r => r.IsCurrent).Select(r => r.Name).ToArray());
            Assert.Equal("Unnamed", request.CurrentName);
            Assert.False(Row(request, "Unnamed").CanDelete);
            Assert.True(Row(request, "Unnamed").CanRename);
            Assert.False(Row(request, "World").CanRename);
            Assert.False(Row(request, "World").CanDelete);
            Assert.False(Row(request, "Previous").CanRename);
            Assert.False(Row(request, "Previous").CanDelete);
            Assert.True(Row(request, "Org100").CanRename);
            Assert.True(Row(request, "Org100").CanDelete);
        }

        [Fact]
        public void WhereTheCurrentSystemIsTheWorldOrANamedOne_ThereIsNoUnnamedRow_AndTheLabelNamesIt()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);
            Run(session, "UCS", "W");
            CadUcsDialogRequest request = null;
            Shown(session, "UCSMAN", r => request = r);
            Assert.Equal(new[] { "World", "Previous", "Org100", "Rot45", "Tilt90" }, request.Rows.Select(r => r.Name));
            Assert.Equal("World", request.CurrentName);

            Run(session, "UCS", "NA", "R", "Rot45");
            Shown(session, "UCSMAN", r => request = r);
            Assert.Equal("Rot45", request.CurrentName);
            Assert.True(Row(request, "Rot45").IsCurrent);
            Assert.DoesNotContain(request.Rows, r => r.Kind == CadUcsRowKind.Unnamed);
        }

        [Fact]
        public void TheNamedRows_AreSortedAToZ_LettersOfEitherCaseTheSame()
        {
            var database = new DxfDatabase("AC1027");

            foreach (string name in new[] { "b", "Bravo", "alpha", "Zed" })
            {
                database.UcsTable.Add(
                    new DxfUcsTableRecord
                    {
                        Name = name,
                        XAxis = GeoVector3.XAxis,
                        YAxis = GeoVector3.YAxis,
                    }
                );
            }

            CadCommandSession session = Session(CadDocument.From(database, "names.dxf"));
            CadUcsDialogRequest request = null;

            Shown(session, "UC", r => request = r);

            Assert.Equal(
                new[] { "World", "Previous", "alpha", "b", "Bravo", "Zed" },
                request.Rows.Select(r => r.Name).ToArray()
            );
        }

        [Fact]
        public void SetCurrent_MovesTheTriangleAndTheLabel_ButNothingReachesTheDrawingBeforeOk()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);
            CadUcsDialogRequest request = null;

            Shown(session, "UCSMAN", r =>
            {
                request = r;
                Assert.Null(r.SetCurrent(Row(r, "Rot45")));
                Assert.Equal("Rot45", r.CurrentName);
                Assert.Equal(new[] { "Rot45" }, r.Rows.Where(x => x.IsCurrent).Select(x => x.Name).ToArray());
                Assert.Null(document.Ucs.Current.Name);
                Assert.Null(document.NextUndo);
            });

            Assert.Null(document.Ucs.Current.Name);
            Assert.Null(document.NextUndo);
        }

        [Fact]
        public void SetCurrentOnASystemNotParallelToTheView_IsRefused_AndKeepsTheCurrentOne()
        {
            CadCommandSession session = Session(Probed());

            Shown(session, "UCSMAN", r =>
            {
                Assert.Equal(CadCommands.NotInPlan, r.SetCurrent(Row(r, "Tilt90")));
                Assert.Equal("Unnamed", r.CurrentName);
            });
        }

        [Fact]
        public void SetCurrentOnPrevious_GoesBackToTheSystemBeforeTheLastChange_AtOk()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);
            Run(session, "UCS", "NA", "R", "Rot45");

            Shown(session, "UCSMAN", r =>
            {
                Assert.Null(r.SetCurrent(Row(r, "Previous")));
                Assert.Equal("Unnamed", r.CurrentName);
                r.Action = CadDialogAction.Ok;
            });

            Assert.Null(document.Ucs.Current.Name);
            Assert.Equal(20.0 * Math.PI / 180.0, document.Ucs.Current.Angle, 9);
        }

        [Fact]
        public void SetCurrentOnPrevious_WithNoneBefore_SaysSo()
        {
            CadCommandSession session = Session(Probed());

            Shown(session, "UCSMAN", r =>
            {
                Assert.Equal("No previous coordinate system saved.", r.SetCurrent(Row(r, "Previous")));
                Assert.Equal("Unnamed", r.CurrentName);
            });
        }

        [Theory]
        [InlineData("Org100", "UCS - Name Already Exists", "The name, Org100, is already in use.")]
        [InlineData("org100", "UCS - Name Already Exists", "The name, org100, is already in use.")]
        [InlineData("World", "UCS - Name Already Exists", "The name, World, is already in use.")]
        [InlineData("previous", "UCS - Name Already Exists", "The name, previous, is already in use.")]
        [InlineData("Unnamed", "UCS - Name Already Exists", "The name, Unnamed, is already in use.")]
        [InlineData("", "UCS - Text Length", "The name must be at least one character long.")]
        [InlineData("   ", "UCS - Text Length", "The name must be at least one character long.")]
        [InlineData("a/b", "UCS - Unsupported Characters", "The name contains unsupported characters.")]
        [InlineData("a,b", "UCS - Unsupported Characters", "The name contains unsupported characters.")]
        [InlineData("a=b", "UCS - Unsupported Characters", "The name contains unsupported characters.")]
        public void Rename_RefusesANameInUseAnEmptyOneAndOneWithUnsupportedCharacters(
            string name,
            string title,
            string message
        )
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);

            Shown(session, "UCSMAN", r =>
            {
                CadUcsDialogError error = r.Rename(Row(r, "Rot45"), name);

                Assert.NotNull(error);
                Assert.Equal(title, error.Title);
                Assert.Equal(message, error.Message);
                Assert.Equal("Rot45", Row(r, "Rot45").Name);
                Assert.Contains("Rot45", r.Rows.Select(x => x.Name));
            });
        }

        [Fact]
        public void TheErrorTexts_AreAutoCADs()
        {
            CadCommandSession session = Session(Probed());

            Shown(session, "UCSMAN", r =>
            {
                CadUcsDialogError inUse = r.Rename(Row(r, "Rot45"), "Org100");
                Assert.Equal("You will need to enter a different name.", inUse.Detail);
                CadUcsDialogError characters = r.Rename(Row(r, "Rot45"), "a|b");
                Assert.Equal(
                    "The following characters are not supported: \\ < > / ? \" : ; * | , = `",
                    characters.Detail
                );
                CadUcsDialogError empty = r.Rename(Row(r, "Rot45"), string.Empty);
                Assert.Equal(string.Empty, empty.Detail);
            });
        }

        [Fact]
        public void Rename_TakesAFreshNameAndACaseChangeOfItsOwn_AndKeepsTheListSorted()
        {
            CadCommandSession session = Session(Probed());

            Shown(session, "UCSMAN", r =>
            {
                Assert.Null(r.Rename(Row(r, "Rot45"), "  Alpha "));
                Assert.Null(r.Rename(Row(r, "Org100"), "ORG100"));
                Assert.Equal(
                    new[] { "Unnamed", "World", "Previous", "Alpha", "ORG100", "Tilt90" },
                    r.Rows.Select(x => x.Name).ToArray()
                );
            });
        }

        [Fact]
        public void Rename_OnWorldAndPrevious_IsAProgrammingError()
        {
            CadCommandSession session = Session(Probed());

            Shown(session, "UCSMAN", r =>
            {
                Assert.Throws<ArgumentException>(() => r.Rename(Row(r, "World"), "Other"));
                Assert.Throws<ArgumentException>(() => r.Delete(Row(r, "Previous")));
                Assert.Throws<ArgumentException>(() => r.Delete(Row(r, "Unnamed")));
            });
        }

        [Fact]
        public void RenamingUnnamed_SavesTheCurrentSystemUnderTheName_AtOk()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);

            Shown(session, "UCSMAN", r =>
            {
                Assert.Null(r.Rename(Row(r, "Unnamed"), "Slab"));
                Assert.Equal("Slab", r.CurrentName);
                Assert.DoesNotContain(r.Rows, x => x.Kind == CadUcsRowKind.Unnamed);
                r.Action = CadDialogAction.Ok;
            });

            Assert.Equal("Slab", document.Ucs.Current.Name);
            Assert.Equal(new[] { "Org100", "Rot45", "Slab", "Tilt90" }, document.Ucs.Named.Select(u => u.Name));
            Assert.Equal(20.0 * Math.PI / 180.0, document.Ucs.Find("Slab").Angle, 9);
            Assert.Equal("UCSMAN", document.NextUndo.Name);
        }

        [Fact]
        public void Delete_TakesTheRowOutAtOnce_AndACurrentSystemOfThatNameGoesOnUnnamed()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);
            Run(session, "UCS", "NA", "R", "Rot45");

            Shown(session, "UCSMAN", r =>
            {
                Assert.Equal("Rot45", r.CurrentName);
                r.Delete(Row(r, "Rot45"));
                Assert.DoesNotContain(r.Rows, x => x.Name == "Rot45");
                Assert.Equal("Unnamed", r.CurrentName);
                Assert.Equal(Math.PI / 4.0, r.CurrentRow.System.Angle, 9);
                r.Action = CadDialogAction.Ok;
            });

            Assert.Null(document.Ucs.Current.Name);
            Assert.Equal(Math.PI / 4.0, document.Ucs.Current.Angle, 9);
            Assert.Equal(new[] { "Org100", "Tilt90" }, document.Ucs.Named.Select(u => u.Name));
        }

        [Fact]
        public void SetCurrentRenameAndDelete_AreOneUndoStepNamedUcsman_ForTheWholeDialog()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);
            int steps = document.UndoPosition;

            Shown(session, "UCSMAN", r =>
            {
                Assert.Null(r.SetCurrent(Row(r, "Rot45")));
                Assert.Null(r.Rename(Row(r, "Org100"), "Site"));
                r.Delete(Row(r, "Tilt90"));
                r.Action = CadDialogAction.Ok;
            });

            Assert.Equal("Rot45", document.Ucs.Current.Name);
            Assert.Equal(new[] { "Rot45", "Site" }, document.Ucs.Named.Select(u => u.Name));
            Assert.Equal(steps + 1, document.UndoPosition);
            Assert.Equal("UCSMAN", document.NextUndo.Name);

            Assert.True(document.Undo());

            Assert.Equal(steps, document.UndoPosition);
            Assert.Null(document.Ucs.Current.Name);
            Assert.Equal(20.0 * Math.PI / 180.0, document.Ucs.Current.Angle, 9);
            Assert.Equal(new[] { "Org100", "Rot45", "Tilt90" }, document.Ucs.Named.Select(u => u.Name));

            Assert.True(document.Redo());

            Assert.Equal("Rot45", document.Ucs.Current.Name);
            Assert.Equal(new[] { "Rot45", "Site" }, document.Ucs.Named.Select(u => u.Name));
        }

        [Fact]
        public void TheAliasUc_RunsTheSameDialog_AsOneStepNamedUcsman()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);

            Shown(session, "UC", r =>
            {
                r.SetCurrent(Row(r, "World"));
                r.Action = CadDialogAction.Ok;
            });

            Assert.True(document.Ucs.Current.IsWorld);
            Assert.Equal("UCSMAN", document.NextUndo.Name);
        }

        [Fact]
        public void Cancel_ChangesNothing_AfterEveryKindOfChange()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);
            string before = string.Join("|", document.Ucs.Named.Select(u => u.ToString()));

            Shown(session, "UCSMAN", r =>
            {
                r.SetCurrent(Row(r, "Rot45"));
                r.Rename(Row(r, "Org100"), "Site");
                r.Delete(Row(r, "Tilt90"));
                r.IconOn = false;
                r.FollowToPlan = true;
                r.Action = CadDialogAction.Cancel;
            });

            Assert.Null(document.Ucs.Current.Name);
            Assert.Equal(before, string.Join("|", document.Ucs.Named.Select(u => u.ToString())));
            Assert.Null(document.NextUndo);
            Assert.True(document.Ucs.IconOn);
            Assert.False(document.Ucs.FollowToPlan);
            Assert.False(document.IsModified);
        }

        [Fact]
        public void OkWithNothingChanged_MakesNoUndoStep()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);

            Shown(session, "UCSMAN", r => r.Action = CadDialogAction.Ok);

            Assert.Null(document.NextUndo);
            Assert.False(document.IsModified);
        }

        [Fact]
        public void ARenameBackToTheOriginalName_IsNoChange()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);

            Shown(session, "UCSMAN", r =>
            {
                Assert.Null(r.Rename(Row(r, "Org100"), "Temp"));
                Assert.Null(r.Rename(Row(r, "Temp"), "Org100"));
                r.Action = CadDialogAction.Ok;
            });

            Assert.Null(document.NextUndo);
        }

        [Fact]
        public void ANameTakenAfterAnotherRowGaveItUp_IsAccepted_AndTheTwoSwapAtOk()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);
            Run(session, "UCS", "NA", "R", "Org100");

            Shown(session, "UCSMAN", r =>
            {
                Assert.Null(r.Rename(Row(r, "Org100"), "Temp"));
                Assert.Null(r.Rename(Row(r, "Rot45"), "Org100"));
                Assert.Null(r.Rename(Row(r, "Temp"), "Rot45"));
                r.Action = CadDialogAction.Ok;
            });

            // The rows swapped names with the systems: the old Org100, at (35.3553, 106.0660), is Rot45 now, and current.
            Assert.Equal("Rot45", document.Ucs.Current.Name);
            Assert.Equal(106.0660, document.Ucs.Current.Origin.Y, 3);
            Assert.Equal(0.0, document.Ucs.Find("Org100").Origin.Y, 9);
        }

        [Fact]
        public void Details_AreTheSystemInTheCoordinatesOfWorldOrOfAnotherSystem_AsAutoCADShowsThem()
        {
            CadCommandSession session = Session(Probed());

            Shown(session, "UCSMAN", r =>
            {
                CadUcsDialogRow org = Row(r, "Org100");

                CadUcsDetails world = r.Details(org, r.World);
                Assert.Equal("Org100", world.Name);
                Near(35.3553, 106.0660, 0, world.Origin, 1e-4);
                Near(0.7071, 0.7071, 0, world.XAxis, 1e-4);
                Near(-0.7071, 0.7071, 0, world.YAxis, 1e-4);
                Near(0, 0, 1, world.ZAxis, 1e-4);

                // Relative to the unnamed current system, turned 20 degrees: AutoCAD shows 69.4999 and 87.5772.
                CadUcsDetails unnamed = r.Details(org, Row(r, "Unnamed"));
                Near(69.5, 87.5772, 0, unnamed.Origin, 2e-4);
                Near(0.9063, 0.4226, 0, unnamed.XAxis, 1e-4);
                Near(-0.4226, 0.9063, 0, unnamed.YAxis, 1e-4);

                CadUcsDetails itself = r.Details(org, org);
                Near(0, 0, 0, itself.Origin, 1e-9);
                Near(1, 0, 0, itself.XAxis, 1e-9);
                Near(0, 1, 0, itself.YAxis, 1e-9);

                CadUcsDetails rot = r.Details(org, Row(r, "Rot45"));
                Near(100, 50, 0, rot.Origin, 1e-9);
                Near(1, 0, 0, rot.XAxis, 1e-9);

                CadUcsDetails tilt = r.Details(org, Row(r, "Tilt90"));
                Near(35.3553, 0, -106.0660, tilt.Origin, 1e-4);
                Near(0.7071, 0, -0.7071, tilt.XAxis, 1e-4);
                Near(-0.7071, 0, -0.7071, tilt.YAxis, 1e-4);
                Near(0, 1, 0, tilt.ZAxis, 1e-9);
            });
        }

        [Fact]
        public void TheRelativeToChoices_AreUnnamedWhereThereIsOne_ThenWorldThenTheNamedOnes()
        {
            CadCommandSession session = Session(Probed());

            Shown(session, "UCSMAN", r =>
            {
                Assert.Equal(
                    new[] { "Unnamed", "World", "Org100", "Rot45", "Tilt90" },
                    r.RelativeToRows.Select(x => x.Name).ToArray()
                );
                Assert.Equal(
                    new[] { "World", "Org100", "Rot45", "Tilt90" },
                    r.OrthoBaseRows.Select(x => x.Name).ToArray()
                );
            });
        }

        [Fact]
        public void TheOrthographicTab_ListsSixFacesWithDepthNought_AndTopRelativeToWorldIsTheWorld()
        {
            CadCommandSession session = Session(Probed());

            Shown(session, "UCSMAN", r =>
            {
                Assert.Equal(
                    new[] { "Top", "Bottom", "Front", "Back", "Left", "Right" },
                    r.OrthoRows.Select(x => x.Name).ToArray()
                );
                Assert.All(r.OrthoRows, x => Assert.Equal(0.0, x.Depth));
                Assert.Equal("World", r.OrthoBase.Name);
                Assert.True(r.Orthographic(r.OrthoRows[0]).IsWorld);
            });
        }

        [Fact]
        public void TheOrthographicSystems_FollowAutoCADsFaces_AndTheDepthMovesTheOriginAlongTheNormal()
        {
            CadCommandSession session = Session(Probed());

            Shown(session, "UCSMAN", r =>
            {
                CadUcsOrthoRow front = r.OrthoRows.Single(x => x.Name == "Front");
                r.SetDepth(front, 5.0);
                CadUcs ucs = r.Orthographic(front);

                Near(1, 0, 0, ucs.XAxis, 1e-12);
                Near(0, 0, 1, ucs.YAxis, 1e-12);
                Near(0, -1, 0, ucs.ZAxis, 1e-12);
                Near(0, -5, 0, new GeoVector3(ucs.Origin.X, ucs.Origin.Y, ucs.Origin.Z), 1e-12);
                Assert.False(ucs.IsPlanar);

                r.ResetDepth(front);
                Assert.Equal(0.0, front.Depth);

                CadUcsOrthoRow top = r.OrthoRows.Single(x => x.Name == "Top");
                r.SetDepth(top, 12.5);
                Near(
                    0,
                    0,
                    12.5,
                    new GeoVector3(
                        r.Orthographic(top).Origin.X,
                        r.Orthographic(top).Origin.Y,
                        r.Orthographic(top).Origin.Z
                    ),
                    1e-12
                );
                Assert.Throws<ArgumentException>(() => r.SetDepth(top, double.NaN));
            });
        }

        [Fact]
        public void TheOrthographicFaces_ForTheOtherFourAreRightHandedWithTheZAxisTowardsTheViewer()
        {
            CadCommandSession session = Session(Probed());

            Shown(session, "UCSMAN", r =>
            {
                Axes(r, "Bottom", (1, 0, 0), (0, -1, 0), (0, 0, -1));
                Axes(r, "Back", (-1, 0, 0), (0, 0, 1), (0, 1, 0));
                Axes(r, "Left", (0, -1, 0), (0, 0, 1), (-1, 0, 0));
                Axes(r, "Right", (0, 1, 0), (0, 0, 1), (1, 0, 0));
            });
        }

        [Fact]
        public void TopRelativeToANamedSystem_IsThatSystem_AndSetCurrentGivesItUnnamed()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);

            Shown(session, "UCSMAN", r =>
            {
                r.OrthoBase = Row(r, "Rot45");
                CadUcsOrthoRow top = r.OrthoRows.Single(x => x.Name == "Top");
                Assert.Null(r.SetCurrent(top));
                Assert.Equal("Unnamed", r.CurrentName);
                r.Action = CadDialogAction.Ok;
            });

            Assert.Null(document.Ucs.Current.Name);
            Assert.Equal(Math.PI / 4.0, document.Ucs.Current.Angle, 9);
            Assert.Equal(0.0, document.Ucs.Current.Origin.X, 9);
        }

        [Fact]
        public void TopWithDepthRelativeToWorld_RaisesTheOrigin_AndFrontIsRefusedAsNotInThePlan()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);

            Shown(session, "UCSMAN", r =>
            {
                CadUcsOrthoRow front = r.OrthoRows.Single(x => x.Name == "Front");
                Assert.Equal(CadCommands.NotInPlan, r.SetCurrent(front));
                Assert.Equal("Unnamed", r.CurrentName);

                CadUcsOrthoRow top = r.OrthoRows.Single(x => x.Name == "Top");
                r.SetDepth(top, 30.0);
                Assert.Null(r.SetCurrent(top));
                r.Action = CadDialogAction.Ok;
            });

            Assert.Equal(30.0, document.Ucs.Current.Origin.Z, 9);
            Assert.True(document.Ucs.Current.IsPlanar);
            Assert.Equal("UCSMAN", document.NextUndo.Name);
        }

        [Fact]
        public void APickedPoint_BecomesTheDepth_AfterTheCommandAsksForIt_AndTheDialogIsShownAgain()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);
            CadUcsOrthoRow front = null;
            var window = new Window(session, (r, count) =>
            {
                if (count == 1)
                {
                    r.Tab = 1;
                    front = r.OrthoRows.Single(x => x.Name == "Front");
                    r.PickRow = front;
                    r.Action = CadDialogAction.PickPoint;
                }
                else
                {
                    Assert.Equal(1, r.Tab);
                    Assert.Equal(-20.0, front.Depth, 9);
                    r.Action = CadDialogAction.Cancel;
                }
            });

            Run(session, "UCSMAN");
            Assert.True(session.IsRunning);
            Run(session, new GeoPoint2(7, 20));

            window.Check();
            Assert.False(session.IsRunning);
            Assert.Equal(2, window.Count);
        }

        [Fact]
        public void DeletingTheSystemTheOrthographicTabIsRelativeTo_PutsItBackRelativeToWorld()
        {
            CadCommandSession session = Session(Probed());

            Shown(session, "UCSMAN", r =>
            {
                r.OrthoBase = Row(r, "Rot45");
                r.Delete(Row(r, "Rot45"));

                Assert.Equal("World", r.OrthoBase.Name);
                Assert.DoesNotContain(r.OrthoBaseRows, x => x.Name == "Rot45");
            });
        }

        [Fact]
        public void TheUnnamedRow_StaysInTheListWhenAnotherSystemIsMadeCurrent_UntilTheDialogCloses()
        {
            CadCommandSession session = Session(Probed());

            Shown(session, "UCSMAN", r =>
            {
                Assert.Null(r.SetCurrent(Row(r, "World")));

                Assert.Equal("World", r.CurrentName);
                Assert.Equal(new[] { "World" }, r.Rows.Where(x => x.IsCurrent).Select(x => x.Name).ToArray());
                Assert.Contains(r.Rows, x => x.Kind == CadUcsRowKind.Unnamed);

                Assert.Null(r.SetCurrent(Row(r, "Unnamed")));
                Assert.Equal("Unnamed", r.CurrentName);
            });
        }

        [Fact]
        public void TheTabUsedLast_IsTheOneTheNextDialogOpensOn()
        {
            CadCommandSession session = Session(Probed());
            int tab = -1;
            var window = new Window(session, (r, count) =>
            {
                tab = r.Tab;
                r.Tab = 2;
            });

            Run(session, "UCSMAN");
            Assert.Equal(0, tab);
            Run(session, "UCSMAN");
            Assert.Equal(2, tab);
            window.Check();
        }

        [Fact]
        public void TheSettings_OpenAsTheDrawingHasThem_AndOkWritesThem_ApplyToAllOpeningOff()
        {
            CadDocument document = Probed();
            CadCommandSession session = Session(document);

            Shown(session, "UCSMAN", r =>
            {
                Assert.True(r.IconOn);
                Assert.True(r.IconAtOrigin);
                Assert.False(r.ApplyToAllViewports);
                Assert.True(r.IconSelectable);
                Assert.True(r.SaveWithViewport);
                Assert.False(r.FollowToPlan);

                r.IconOn = false;
                r.IconAtOrigin = false;
                r.ApplyToAllViewports = true;
                r.IconSelectable = false;
                r.SaveWithViewport = false;
                r.FollowToPlan = true;
                r.Action = CadDialogAction.Ok;
            });

            Assert.False(document.Ucs.IconOn);
            Assert.False(document.Ucs.IconAtOrigin);
            Assert.False(document.Ucs.IconSelectable);
            Assert.False(document.Ucs.SaveWithViewport);
            Assert.True(document.Ucs.FollowToPlan);

            Shown(session, "UCSMAN", r =>
            {
                Assert.False(r.IconOn);
                Assert.False(r.ApplyToAllViewports);
                Assert.True(r.FollowToPlan);
            });
        }

        [Fact]
        public void InTheBlockEditor_UcsmanIsRefused_AndTheDialogIsNotShown()
        {
            CadDocument document = DoorDrawing();
            CadCommandSession session = Session(document);
            Run(session, "-BEDIT", "DOOR");
            Assert.NotNull(document.BlockEditor);
            bool asked = false;
            session.UiRequested += (sender, e) => asked |= e.Name == "UCSMAN";
            int steps = document.UndoPosition;

            Run(session, "UCSMAN");

            Assert.False(asked);
            Assert.Equal("** UCSMAN command not allowed in block editor. **", session.History.Last());
            Assert.Equal(steps, document.UndoPosition);
        }

        private static CadDocument DoorDrawing()
        {
            DxfDatabase database = CadTemplate.CreateMetric();
            var door = new DxfBlockTableRecord { Name = "DOOR", Origin = GeoPoint3.Origin };
            database.BlockTable.Add(door);
            door.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(900, 0, 0)));
            database.ModelSpace.AppendEntity(new DxfBlockReference(GeoPoint3.Origin, door));
            return CadDocument.From(database, "door.dxf");
        }

        private static void Axes(
            CadUcsDialogRequest request,
            string face,
            (double, double, double) x,
            (double, double, double) y,
            (double, double, double) z
        )
        {
            CadUcs ucs = request.Orthographic(request.OrthoRows.Single(r => r.Name == face));
            Near(x.Item1, x.Item2, x.Item3, ucs.XAxis, 1e-12);
            Near(y.Item1, y.Item2, y.Item3, ucs.YAxis, 1e-12);
            Near(z.Item1, z.Item2, z.Item3, ucs.ZAxis, 1e-12);
        }

        private static void Near(double x, double y, double z, GeoPoint3 actual, double tolerance)
        {
            Assert.Equal(x, actual.X, tolerance);
            Assert.Equal(y, actual.Y, tolerance);
            Assert.Equal(z, actual.Z, tolerance);
        }

        private static void Near(double x, double y, double z, GeoVector3 actual, double tolerance)
        {
            Assert.Equal(x, actual.X, tolerance);
            Assert.Equal(y, actual.Y, tolerance);
            Assert.Equal(z, actual.Z, tolerance);
        }
    }
}
