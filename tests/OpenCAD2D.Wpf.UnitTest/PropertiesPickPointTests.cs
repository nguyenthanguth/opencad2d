using System;
using System.Collections.Generic;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using GeometryHelper.Geometry;
using OpenCAD2D.Plugins;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The pick-point button of the coordinate rows of a point a plugin gave (A2P P6), as AutoCAD 2021's Properties palette has
    /// it: it asks "Pick a point in the drawing:" on the command line with a rubber band from the point, writes no line of history
    /// for the point picked, gives the setter the whole point once, X and Y the picked ones and Z as it was, and is grey while a
    /// command runs. The plugin has the row "Mark at", a point of (10, 20, 3).
    /// </summary>
    public sealed class PropertiesPickPointTests : IDisposable
    {
        private readonly PluginWindowKit _kit = new PluginWindowKit();

        public void Dispose() => _kit.Dispose();

        private MainViewModel Model(List<GeoPoint3> picked, Func<GeoPoint3, string> answer = null, bool editable = true)
        {
            MainViewModel model = _kit.Opened(out string first, out _, out _);
            _kit.LoadHook(model, host => host.AddProperties(request =>
                {
                    Func<GeoPoint3, string> setter = editable
                        ? p =>
                        {
                            picked.Add(p);
                            return answer?.Invoke(p);
                        }
                        : (Func<GeoPoint3, string>)null;
                    var groups = new List<CadPropertyGroup>(request.Shown.Groups)
                    {
                        new CadPropertyGroup(
                            "Hook",
                            CadPropertyRow.Point("Mark at", new GeoPoint3(10, 20, 3), request.Document, setter)
                        ),
                    };
                    return new CadSelectionInfo(request.Shown.Label, groups);
                }));
            model.Document.Selection.Set(new[] { first });
            return model;
        }

        private static PropertyRowViewModel RowOf(MainViewModel model, string label) =>
            model.PropertyGroups.Last().Rows.Single(r => r.Label == label);

        [Fact]
        public void EveryCoordinateOfThePointRow_HasThePickButton()
        {
            MainViewModel model = Model(new List<GeoPoint3>());

            foreach (string label in new[] { "Mark at X", "Mark at Y", "Mark at Z" })
            {
                PropertyRowViewModel row = RowOf(model, label);
                Assert.True(row.CanPickPoint, label);
                Assert.NotNull(row.PickPointCommand);
            }

            // The General rows of the presenter have none (the coordinates of its own are in PropertiesOwnPointPickTests).
            Assert.All(model.PropertyGroups.First().Rows, r => Assert.False(r.CanPickPoint));
        }

        [Fact]
        public void APickedPoint_GoesToTheSetterOnce_WithZKept()
        {
            var picked = new List<GeoPoint3>();
            MainViewModel model = Model(picked);
            int lines = model.History.Count;

            RowOf(model, "Mark at Y").PickPointCommand.Execute(null);

            Assert.True(model.Session.IsRunning);
            Assert.Equal("Pick a point in the drawing:", model.Session.PromptText);
            Assert.Equal(new GeoPoint2(10, 20), model.Session.Prompt.BasePoint);
            Assert.Empty(picked);

            Assert.True(model.Session.Feed(new GeoPoint2(12.5, 40)));

            GeoPoint3 point = Assert.Single(picked);
            Assert.Equal(12.5, point.X, 9);
            Assert.Equal(40.0, point.Y, 9);
            Assert.Equal(3.0, point.Z, 9);
            Assert.False(model.Session.IsRunning);
            // The command line wrote no line for the pick: not the prompt, not the point, not a command name.
            Assert.DoesNotContain(model.History.Skip(lines), l => l.Contains("Pick a point") || l.Contains("12.5"));
            Assert.DoesNotContain(model.History.Skip(lines), l => l.Contains("PICKPOINT"));
        }

        [Fact]
        public void EscAtThePrompt_LeavesThePoint()
        {
            var picked = new List<GeoPoint3>();
            MainViewModel model = Model(picked);
            RowOf(model, "Mark at X").PickPointCommand.Execute(null);
            Assert.True(model.Session.IsRunning);

            model.Escape();

            Assert.False(model.Session.IsRunning);
            Assert.Empty(picked);
        }

        [Fact]
        public void ThePickButton_IsGreyWhileACommandRuns()
        {
            MainViewModel model = Model(new List<GeoPoint3>());
            PropertyRowViewModel row = RowOf(model, "Mark at X");
            int changes = 0;
            row.PickPointCommand.CanExecuteChanged += (sender, e) => changes++;
            Assert.True(row.PickPointCommand.CanExecute(null));

            model.Execute("LINE");

            Assert.False(row.PickPointCommand.CanExecute(null));
            Assert.True(changes > 0);

            // A pick asked while a command runs is not started.
            row.PickPointCommand.Execute(null);
            Assert.Equal("LINE", model.Session.Current.Name);

            model.Escape();

            Assert.True(row.PickPointCommand.CanExecute(null));
        }

        [Fact]
        public void AReadOnlyPointRow_HasNoPickButton()
        {
            MainViewModel model = Model(new List<GeoPoint3>(), editable: false);

            foreach (string label in new[] { "Mark at X", "Mark at Y", "Mark at Z" })
            {
                PropertyRowViewModel row = RowOf(model, label);
                Assert.True(row.IsReadOnly);
                Assert.False(row.CanPickPoint);
                Assert.Null(row.PickPointCommand);
            }
        }

        [Fact]
        public void EnterAfterThePick_RepeatsTheCommandBeforeIt_NotThePick()
        {
            var picked = new List<GeoPoint3>();
            MainViewModel model = Model(picked);
            model.Execute("LINE");
            model.Escape();
            RowOf(model, "Mark at X").PickPointCommand.Execute(null);
            model.Session.Feed(new GeoPoint2(5, 5));
            Assert.Single(picked);

            model.Execute(string.Empty);

            Assert.Equal("LINE", model.Session.Current.Name);
            Assert.Single(picked);
        }

        [Fact]
        public void ARefusalOfTheSetter_IsWrittenOnTheCommandLine()
        {
            var picked = new List<GeoPoint3>();
            MainViewModel model = Model(picked, p => "Mark at: that point is outside the sheet.");
            RowOf(model, "Mark at X").PickPointCommand.Execute(null);

            model.Session.Feed(new GeoPoint2(500, 500));

            Assert.Single(picked);
            Assert.Equal("Mark at: that point is outside the sheet.", model.History.Last());
            Assert.False(model.Session.IsRunning);
        }

        [Fact]
        public void TheWorkspacesRowsHoldThePickButton_HiddenUntilTheRowIsEdited()
        {
            MainViewModel model = Model(new List<GeoPoint3>());
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    _ = PackUriHelper.UriSchemePack;
                    var workspace = new CadWorkspace { DataContext = model };

                    try
                    {
                        RibbonTabHarness.Layout(workspace, 1600);
                        List<Button> buttons = RibbonTabHarness
                            .Descendants(workspace)
                            .OfType<Button>()
                            .Where(b => AutomationProperties.GetName(b) == "Pick a point in the drawing")
                            .ToList();

                        // Every row of the template has its button; it is shown on a coordinate of a point while it is edited.
                        Assert.True(buttons.Count >= 3);
                        Assert.All(buttons, b => Assert.Equal(Visibility.Collapsed, b.Visibility));
                    }
                    finally
                    {
                        workspace.DrawingCanvas.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(failure);
        }
    }
}
