using System;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The UCS dialog of UCSMAN (UC) in the window's view model, as AutoCAD 2021's: the Named UCSs list, its marker and label,
    /// Set Current, Rename in place with the three refusals, Delete, Details to four decimals relative to World or another
    /// system, the Orthographic UCSs tab with its depth dialog, the Settings tab, and OK as one undo step named UCSMAN. The
    /// dialog is driven with no window shown.
    /// </summary>
    public class UcsManagerViewModelTests
    {
        private static readonly double Half = Math.Sqrt(0.5);

        /// <summary>The drawing of the probes of AutoCAD: Rot45, Org100, Tilt90 and the current system unnamed, turned 20 degrees.</summary>
        private static DxfDatabase Probed()
        {
            var database = new DxfDatabase("AC1027");
            var x45 = new GeoVector3(Half, Half, 0);
            var y45 = new GeoVector3(-Half, Half, 0);
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
            return database;
        }

        private static MainViewModel Opened(TestFolder folder)
        {
            var model = new MainViewModel();
            Assert.True(model.Open(folder.Write("probed.dxf", Probed())));
            return model;
        }

        /// <summary>Runs UCSMAN with a window that answers as the test says; an assertion failing inside the answer is raised after the command.</summary>
        private static UcsManagerViewModel Ran(MainViewModel model, Action<UcsManagerViewModel> answer)
        {
            UcsManagerViewModel shown = null;
            Exception failure = null;
            model.ShowUcsManager = dialog =>
            {
                shown = dialog;

                try
                {
                    answer(dialog);
                }
                catch (Exception exception)
                {
                    failure = failure ?? exception;
                }

                return true;
            };
            model.Execute("UCSMAN");
            Assert.False(model.IsCommandRunning);
            Assert.NotNull(shown);

            if (failure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }

            return shown;
        }

        private static UcsRowViewModel Row(UcsManagerViewModel dialog, string name) =>
            dialog.Rows.Single(r => r.Name == name);

        [Fact]
        public void TheList_IsUnnamedWorldPreviousThenTheNamedAToZ_WithTheTriangleAndTheLabelOnTheCurrentOne()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog =>
                {
                    Assert.Equal(
                        new[] { "Unnamed", "World", "Previous", "Org100", "Rot45", "Tilt90" },
                        dialog.Rows.Select(r => r.Name).ToArray()
                    );
                    Assert.Equal(new[] { "▶", "", "", "", "", "" }, dialog.Rows.Select(r => r.Mark).ToArray());
                    Assert.Equal("Current UCS:  Unnamed", dialog.CurrentLine);
                    Assert.True(dialog.IsNamedTab);
                    Assert.False(dialog.IsOrthoTab);
                    Assert.False(dialog.IsSettingsTab);
                    Assert.NotEmpty(Row(dialog, "Unnamed").Icon);
                    Assert.NotEmpty(Row(dialog, "World").Icon);
                    Assert.NotEmpty(Row(dialog, "Previous").Icon);
                    Assert.Empty(Row(dialog, "Org100").Icon);
                });
            }
        }

        [Fact]
        public void SetCurrent_MovesTheTriangleAndTheLabel_AndTheDrawingIsGivenItOnlyAtOk()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog =>
                {
                    dialog.SelectedRow = Row(dialog, "Rot45");
                    Assert.True(dialog.SetCurrentCommand.CanExecute(null));
                    dialog.SetCurrentCommand.Execute(null);

                    Assert.Equal("Current UCS:  Rot45", dialog.CurrentLine);
                    Assert.Equal("▶", Row(dialog, "Rot45").Mark);
                    Assert.Equal(string.Empty, Row(dialog, "Unnamed").Mark);
                    Assert.Null(model.Document.Ucs.Current.Name);
                    Assert.Equal(string.Empty, dialog.Notice);

                    dialog.Cancel();
                });

                Assert.Null(model.Document.Ucs.Current.Name);
                Assert.Null(model.Document.NextUndo);
            }
        }

        [Fact]
        public void SetCurrentOnASystemNotParallelToTheView_ExplainsWhy_AndMovesNothing()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog =>
                {
                    dialog.SelectedRow = Row(dialog, "Tilt90");
                    dialog.SetCurrentCommand.Execute(null);

                    Assert.Equal(CadCommands.NotInPlan, dialog.Notice);
                    Assert.Equal("Current UCS:  Unnamed", dialog.CurrentLine);
                });
            }
        }

        [Fact]
        public void TheMenuOfWorldAndPrevious_HasRenameAndDeleteGrey_AndUnnamedHasDeleteGrey()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog =>
                {
                    foreach (string name in new[] { "World", "Previous" })
                    {
                        dialog.SelectedRow = Row(dialog, name);
                        Assert.False(dialog.RenameCommand.CanExecute(null));
                        Assert.False(dialog.DeleteCommand.CanExecute(null));
                        Assert.True(dialog.DetailsCommand.CanExecute(null));
                        Assert.True(dialog.SetCurrentCommand.CanExecute(null));
                    }

                    dialog.SelectedRow = Row(dialog, "Unnamed");
                    Assert.True(dialog.RenameCommand.CanExecute(null));
                    Assert.False(dialog.DeleteCommand.CanExecute(null));

                    dialog.SelectedRow = Row(dialog, "Org100");
                    Assert.True(dialog.RenameCommand.CanExecute(null));
                    Assert.True(dialog.DeleteCommand.CanExecute(null));
                });
            }
        }

        [Fact]
        public void RenameInPlace_TakesAFreshName_AndKeepsTheListSorted()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog =>
                {
                    dialog.SelectedRow = Row(dialog, "Rot45");
                    dialog.RenameCommand.Execute(null);
                    UcsRowViewModel editing = dialog.EditingRow;

                    Assert.Same(dialog.SelectedRow, editing);
                    Assert.Equal("Rot45", editing.EditText);

                    editing.EditText = "Alpha";
                    Assert.Null(dialog.CommitRename());

                    Assert.Null(dialog.EditingRow);
                    Assert.Equal(
                        new[] { "Unnamed", "World", "Previous", "Alpha", "Org100", "Tilt90" },
                        dialog.Rows.Select(r => r.Name).ToArray()
                    );
                });
            }
        }

        [Theory]
        [InlineData("Org100", "UCS - Name Already Exists", "The name, Org100, is already in use.")]
        [InlineData("world", "UCS - Name Already Exists", "The name, world, is already in use.")]
        [InlineData("PREVIOUS", "UCS - Name Already Exists", "The name, PREVIOUS, is already in use.")]
        [InlineData("Unnamed", "UCS - Name Already Exists", "The name, Unnamed, is already in use.")]
        [InlineData("", "UCS - Text Length", "The name must be at least one character long.")]
        [InlineData("a:b", "UCS - Unsupported Characters", "The name contains unsupported characters.")]
        public void ARefusedName_ReturnsAutoCADsMessage_AndTheRowStaysInTheEditor(
            string typed,
            string title,
            string message
        )
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog =>
                {
                    dialog.SelectedRow = Row(dialog, "Rot45");
                    dialog.BeginRename();
                    dialog.EditingRow.EditText = typed;

                    CadUcsDialogError error = dialog.CommitRename();

                    Assert.NotNull(error);
                    Assert.Equal(title, error.Title);
                    Assert.Equal(message, error.Message);
                    Assert.NotNull(dialog.EditingRow);
                    Assert.Equal("Rot45", Row(dialog, "Rot45").Name);

                    dialog.CancelRename();
                    Assert.Null(dialog.EditingRow);
                    Assert.Equal("Rot45", Row(dialog, "Rot45").Name);
                });
            }
        }

        [Fact]
        public void RenamingUnnamed_SavesTheCurrentSystemUnderTheName()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog =>
                {
                    dialog.SelectedRow = Row(dialog, "Unnamed");
                    dialog.BeginRename();
                    dialog.EditingRow.EditText = "Slab";
                    Assert.Null(dialog.CommitRename());

                    Assert.Equal("Current UCS:  Slab", dialog.CurrentLine);
                    Assert.DoesNotContain(dialog.Rows, r => r.Name == "Unnamed");
                    Assert.Equal("▶", Row(dialog, "Slab").Mark);
                    Assert.True(dialog.Accept());
                });

                Assert.Equal("Slab", model.Document.Ucs.Current.Name);
                Assert.Equal("UCSMAN", model.Document.NextUndo.Name);
            }
        }

        [Fact]
        public void Delete_TakesTheRowOutWithNoQuestion_AndSelectsTheNextOne()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog =>
                {
                    dialog.SelectedRow = Row(dialog, "Org100");
                    dialog.DeleteCommand.Execute(null);

                    Assert.DoesNotContain(dialog.Rows, r => r.Name == "Org100");
                    Assert.Equal("Rot45", dialog.SelectedRow.Name);
                    Assert.Equal(5, dialog.Rows.Count);
                });
            }
        }

        [Fact]
        public void SetCurrentRenameAndDelete_AreOneUndoStepNamedUcsman_AndOneUndoTakesThemAllBack()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);
                int steps = model.Document.UndoPosition;

                Ran(model, dialog =>
                {
                    dialog.SelectedRow = Row(dialog, "Rot45");
                    dialog.SetCurrent();
                    dialog.SelectedRow = Row(dialog, "Org100");
                    dialog.BeginRename();
                    dialog.EditingRow.EditText = "Site";
                    Assert.Null(dialog.CommitRename());
                    dialog.SelectedRow = Row(dialog, "Tilt90");
                    dialog.Delete();
                    Assert.True(dialog.Accept());
                });

                Assert.Equal(steps + 1, model.Document.UndoPosition);
                Assert.Equal("UCSMAN", model.Document.NextUndo.Name);
                Assert.Equal("Rot45", model.Document.Ucs.Current.Name);
                Assert.Equal(new[] { "Rot45", "Site" }, model.Document.Ucs.Named.Select(u => u.Name));

                Assert.True(model.Undo());

                Assert.Null(model.Document.Ucs.Current.Name);
                Assert.Equal(new[] { "Org100", "Rot45", "Tilt90" }, model.Document.Ucs.Named.Select(u => u.Name));
            }
        }

        [Fact]
        public void Cancel_AndTheCloseButton_ChangeNothing()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog =>
                {
                    dialog.SelectedRow = Row(dialog, "Rot45");
                    dialog.SetCurrent();
                    dialog.SelectedRow = Row(dialog, "Tilt90");
                    dialog.Delete();
                    dialog.IconOn = false;
                    dialog.Cancel();
                });

                Assert.Null(model.Document.Ucs.Current.Name);
                Assert.Equal(3, model.Document.Ucs.Named.Count);
                Assert.True(model.Document.Ucs.IconOn);
                Assert.Null(model.Document.NextUndo);
            }
        }

        [Fact]
        public void Details_AreInTheCoordinatesOfWorldAtFirst_ToFourDecimals_AndFollowRelativeTo()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog =>
                {
                    dialog.SelectedRow = Row(dialog, "Org100");
                    UcsDetailsViewModel details = dialog.CreateDetails();

                    Assert.Equal("Name:  Org100", details.NameLine);
                    Assert.Equal("World", details.RelativeTo.Name);
                    Assert.Equal(
                        new[] { "Unnamed", "World", "Org100", "Rot45", "Tilt90" },
                        details.RelativeToChoices.Select(c => c.Name).ToArray()
                    );
                    Assert.Equal(
                        new[] { "Origin", "X Axis", "Y Axis", "ZAxis" },
                        details.Groups.Select(g => g.Title).ToArray()
                    );
                    Groups(
                        details,
                        "35.3553",
                        "106.0660",
                        "0.0000",
                        "0.7071",
                        "0.7071",
                        "0.0000",
                        "-0.7071",
                        "0.7071",
                        "0.0000",
                        "0.0000",
                        "0.0000",
                        "1.0000"
                    );

                    details.RelativeTo = Choice(details, "Org100");
                    Groups(
                        details,
                        "0.0000",
                        "0.0000",
                        "0.0000",
                        "1.0000",
                        "0.0000",
                        "0.0000",
                        "0.0000",
                        "1.0000",
                        "0.0000",
                        "0.0000",
                        "0.0000",
                        "1.0000"
                    );

                    details.RelativeTo = Choice(details, "Rot45");
                    Groups(
                        details,
                        "100.0000",
                        "50.0000",
                        "0.0000",
                        "1.0000",
                        "0.0000",
                        "0.0000",
                        "0.0000",
                        "1.0000",
                        "0.0000",
                        "0.0000",
                        "0.0000",
                        "1.0000"
                    );

                    details.RelativeTo = Choice(details, "Tilt90");
                    Groups(
                        details,
                        "35.3553",
                        "0.0000",
                        "-106.0660",
                        "0.7071",
                        "0.0000",
                        "-0.7071",
                        "-0.7071",
                        "0.0000",
                        "-0.7071",
                        "0.0000",
                        "1.0000",
                        "0.0000"
                    );

                    details.RelativeTo = Choice(details, "Unnamed");
                    Assert.Equal("0.9063", details.Groups[1].X);
                    Assert.Equal("0.4226", details.Groups[1].Y);
                    Assert.Equal("87.5772", details.Groups[0].Y);
                });
            }
        }

        [Fact]
        public void TheDetailsOfWorld_AreAllNoughtsAndTheAxesOfTheWorld()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog =>
                {
                    dialog.SelectedRow = Row(dialog, "World");
                    UcsDetailsViewModel details = dialog.CreateDetails();

                    Assert.Equal("Name:  World", details.NameLine);
                    Groups(
                        details,
                        "0.0000",
                        "0.0000",
                        "0.0000",
                        "1.0000",
                        "0.0000",
                        "0.0000",
                        "0.0000",
                        "1.0000",
                        "0.0000",
                        "0.0000",
                        "0.0000",
                        "1.0000"
                    );
                });
            }
        }

        [Fact]
        public void TheDetailsCommand_AsksTheWindowToShowThem()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog =>
                {
                    UcsDetailsViewModel asked = null;
                    dialog.DetailsRequested += (sender, details) => asked = details;
                    dialog.SelectedRow = Row(dialog, "Rot45");

                    dialog.DetailsCommand.Execute(null);

                    Assert.Equal("Name:  Rot45", asked.NameLine);
                });
            }
        }

        [Fact]
        public void TheOrthographicTab_ListsSixFacesWithDepthNought_AndSetCurrentOfTopIsTheWorld()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog =>
                {
                    dialog.IsOrthoTab = true;

                    Assert.True(dialog.IsOrthoTab);
                    Assert.False(dialog.IsNamedTab);
                    Assert.Equal(
                        new[] { "Top", "Bottom", "Front", "Back", "Left", "Right" },
                        dialog.OrthoRows.Select(r => r.Name).ToArray()
                    );
                    Assert.All(dialog.OrthoRows, r => Assert.Equal("0.0000", r.DepthText));
                    Assert.Equal("World", dialog.OrthoBase.Name);
                    Assert.Equal(
                        new[] { "World", "Org100", "Rot45", "Tilt90" },
                        dialog.OrthoBaseChoices.Select(c => c.Name).ToArray()
                    );

                    dialog.SelectedOrtho = dialog.OrthoRows[0];
                    dialog.OrthoSetCurrentCommand.Execute(null);
                    Assert.Equal("Current UCS:  World", dialog.CurrentLine);

                    dialog.SelectedOrtho = dialog.OrthoRows[2];
                    dialog.OrthoSetCurrentCommand.Execute(null);
                    Assert.Equal(CadCommands.NotInPlan, dialog.Notice);
                    Assert.Equal("Current UCS:  World", dialog.CurrentLine);
                });
            }
        }

        [Fact]
        public void TheDepthDialog_GivesTheRowANumber_ResetsToNought_AndRefusesText()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog =>
                {
                    dialog.IsOrthoTab = true;
                    dialog.SelectedOrtho = dialog.OrthoRows[2];
                    UcsDepthViewModel depth = null;
                    dialog.DepthRequested += (sender, d) => depth = d;

                    dialog.OrthoDepthCommand.Execute(null);

                    Assert.Equal("Front Depth:", depth.Label);
                    Assert.Equal("0.0000", depth.Text);

                    depth.Text = "abc";
                    Assert.False(depth.Accept());
                    Assert.NotEmpty(depth.Notice);
                    Assert.Equal("0.0000", dialog.OrthoRows[2].DepthText);

                    depth.Text = "-12.5";
                    Assert.True(depth.Accept());
                    Assert.Equal("-12.5000", dialog.OrthoRows[2].DepthText);

                    dialog.OrthoResetCommand.Execute(null);
                    Assert.Equal("0.0000", dialog.OrthoRows[2].DepthText);
                });
            }
        }

        [Fact]
        public void TheDepthDialogsPickButton_AsksForANewOrigin_AndTheDepthIsTheDistanceFromTheOriginAlongTheNormal()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);
                int shown = 0;
                UcsOrthoRowViewModel front = null;
                model.ShowUcsManager = dialog =>
                {
                    shown++;

                    if (shown == 1)
                    {
                        dialog.IsOrthoTab = true;
                        dialog.SelectedOrtho = dialog.OrthoRows[2];
                        front = dialog.SelectedOrtho;
                        dialog.CreateDepth().Pick();
                    }
                    else
                    {
                        Assert.True(dialog.IsOrthoTab);
                        Assert.Equal("-20.0000", front.DepthText);
                        dialog.Cancel();
                    }

                    return true;
                };

                model.Execute("UCSMAN");
                Assert.True(model.IsCommandRunning);
                model.Execute("7,20");

                Assert.False(model.IsCommandRunning);
                Assert.Equal(2, shown);
            }
        }

        [Fact]
        public void TheSettings_ReadTheirVariablesWhenTheDialogOpens_AndWriteThemAtOk()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog =>
                {
                    dialog.IsSettingsTab = true;
                    Assert.True(dialog.IconOn);
                    Assert.True(dialog.IconAtOrigin);
                    Assert.False(dialog.ApplyToAllViewports);
                    Assert.True(dialog.IconSelectable);
                    Assert.True(dialog.SaveWithViewport);
                    Assert.False(dialog.FollowToPlan);

                    dialog.IconOn = false;
                    dialog.IconAtOrigin = false;
                    dialog.ApplyToAllViewports = true;
                    dialog.IconSelectable = false;
                    dialog.SaveWithViewport = false;
                    dialog.FollowToPlan = true;
                    Assert.True(model.Document.Ucs.IconOn);
                    Assert.True(model.Document.Ucs.IconAtOrigin);
                    Assert.True(dialog.Accept());
                });

                CadUcsManager systems = model.Document.Ucs;
                Assert.False(systems.IconOn);
                Assert.False(systems.IconAtOrigin);
                Assert.False(systems.IconSelectable);
                Assert.False(systems.SaveWithViewport);
                Assert.True(systems.FollowToPlan);

                Ran(model, dialog =>
                {
                    Assert.True(dialog.IsSettingsTab);
                    Assert.False(dialog.IconOn);
                    Assert.False(dialog.ApplyToAllViewports);
                    Assert.True(dialog.FollowToPlan);
                });
            }
        }

        [Fact]
        public void TheTabUsedLast_IsTheOneTheNextDialogOpensOn()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                Ran(model, dialog => dialog.IsOrthoTab = true);
                Ran(model, dialog => Assert.True(dialog.IsOrthoTab));
            }
        }

        [Fact]
        public void WithNoWindow_UcsmanSaysTheWindowShowsIt()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Opened(folder);

                model.Execute("UCSMAN");

                Assert.Contains(model.History, l =>
                    l.StartsWith("UCSMAN is shown by the window", StringComparison.Ordinal)
                );
                Assert.Null(model.Document.NextUndo);
            }
        }

        private static CadUcsDialogRow Choice(UcsDetailsViewModel details, string name) =>
            details.RelativeToChoices.Single(c => c.Name == name);

        private static void Groups(UcsDetailsViewModel details, params string[] twelve)
        {
            string[] actual = details.Groups.SelectMany(g => new[] { g.X, g.Y, g.Z }).ToArray();
            Assert.Equal(twelve, actual);
        }
    }
}
