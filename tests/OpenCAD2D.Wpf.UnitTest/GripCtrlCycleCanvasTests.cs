using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Ctrl cycling of a polyline's grip on the canvas: with GRIPMULTIFUNCTIONAL's bit 1 set, each tap of Ctrl (down and up, with no other key or mouse button between) while a grip
    /// edit waits for its first point goes on to the next option of the grip (bit 2 is the hover menu, which a test of its own
    /// covers); with the bit clear Ctrl does nothing.
    /// </summary>
    public sealed class GripCtrlCycleCanvasTests
    {
        private const int Width = 800;
        private const int Height = 600;

        private static void OnSta(Action test)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    test();
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

        /// <summary>An open polyline (0, 0), (100, 0), (100, 100) selected under a canvas of 800 by 600 showing -20,-20 to 120,120.</summary>
        private static MainViewModel Elbow(out DxfPolyline polyline)
        {
            var database = new DxfDatabase("AC1027");
            polyline = new DxfPolyline();
            polyline.AddVertexAt(0, new GeoPoint2(0, 0), 0, 0, 0);
            polyline.AddVertexAt(1, new GeoPoint2(100, 0), 0, 0, 0);
            polyline.AddVertexAt(2, new GeoPoint2(100, 100), 0, 0, 0);
            database.ModelSpace.AppendEntity(polyline);
            var model = new MainViewModel();
            model.OpenDocument(CadDocument.From(database, "ctrl.dxf"), "ctrl.dxf");
            model.Document.Selection.Set(new[] { polyline.Handle });
            return model;
        }

        private static CadCanvas CanvasOf(MainViewModel model)
        {
            var canvas = new CadCanvas
            {
                Document = model.Document,
                Space = model.Space,
                View = model.View,
                Session = model.Session,
                Snap = model.Snap,
            };
            canvas.PixelSizeChanged += (s, e) => model.SetCanvasSize(canvas.PixelWidth, canvas.PixelHeight);
            canvas.Measure(new Size(Width, Height));
            canvas.Arrange(new Rect(0, 0, Width, Height));
            canvas.RenderNow();
            canvas.View.ZoomTo(new DisplayBox(-20, -20, 120, 120));
            canvas.RenderNow();
            return canvas;
        }

        private static void OnCanvas(Action<MainViewModel, CadCanvas, DxfPolyline> test) =>
            OnSta(() =>
            {
                MainViewModel model = Elbow(out DxfPolyline polyline);
                CadCanvas canvas = CanvasOf(model);
                test(model, canvas, polyline);
                canvas.Dispose();
            });

        private static Point At(CadCanvas canvas, double x, double y) => CanvasMouseTests.At(canvas, x, y);

        /// <summary>Presses the grip at a point: the grip edit starts, with "** STRETCH **".</summary>
        private static void ClickGrip(MainViewModel model, CadCanvas canvas, double x, double y)
        {
            Point at = At(canvas, x, y);
            CanvasMouseTests.Mouse(canvas, UIElement.MouseMoveEvent, at);
            CanvasMouseTests.Mouse(canvas, UIElement.MouseDownEvent, at);
            CanvasMouseTests.Mouse(canvas, UIElement.MouseUpEvent, at);
            Assert.True(model.Session.IsGripEditing);
        }

        [Fact]
        public void AVertexGripClicked_CtrlCyclesStretchAddVertexRemoveVertexStretch_AndTheGripStaysHot()
        {
            OnCanvas(
                (model, canvas, polyline) =>
                {
                    ClickGrip(model, canvas, 100, 0);
                    Assert.Equal("Specify stretch point or [Base point/Copy/Undo/eXit]:", model.Prompt);

                    Assert.True(Tap(canvas));
                    Assert.Equal("Specify new vertex point or [Base point/Copy/Undo/eXit]:", model.Prompt);
                    Assert.Contains("** ADD VERTEX **", model.History);

                    Assert.True(Tap(canvas));
                    Assert.Equal("Pick to remove vertex:", model.Prompt);
                    Assert.Contains("** REMOVE VERTEX **", model.History);

                    Assert.True(Tap(canvas));
                    Assert.Equal("Specify stretch point or [Base point/Copy/Undo/eXit]:", model.Prompt);
                    Assert.True(model.Session.IsGripEditing);
                    Assert.False(model.Document.CanUndo);

                    // Still the grip clicked: a click stretches that vertex.
                    CanvasMouseTests.Click(canvas, At(canvas, 110, 20));
                    Assert.False(model.Session.IsRunning);
                    DxfPolyline moved = (DxfPolyline)model.Document.FindEntity(polyline.Handle);
                    Assert.Equal(110.0, moved.GetPoint2dAt(1).X, 3);
                    Assert.Equal(20.0, moved.GetPoint2dAt(1).Y, 3);
                }
            );
        }

        [Fact]
        public void AddVertexReachedWithCtrl_AClickPutsTheVertexAfterTheGrip()
        {
            OnCanvas(
                (model, canvas, polyline) =>
                {
                    ClickGrip(model, canvas, 0, 0);
                    Assert.True(Tap(canvas));

                    CanvasMouseTests.Click(canvas, At(canvas, 40, 30));

                    Assert.False(model.Session.IsRunning);
                    DxfPolyline made = (DxfPolyline)model.Document.FindEntity(polyline.Handle);
                    Assert.Equal(4, made.NumberOfVertices);
                    Assert.Equal(40.0, made.GetPoint2dAt(1).X, 3);
                    Assert.Equal(30.0, made.GetPoint2dAt(1).Y, 3);
                    Assert.Equal("GRIP", model.Document.NextUndo.Name);
                }
            );
        }

        [Fact]
        public void RemoveVertexReachedWithCtrl_AClickTakesTheVertexOut_AndTheNextCtrlHasNothingLeftToCycle()
        {
            OnCanvas(
                (model, canvas, polyline) =>
                {
                    ClickGrip(model, canvas, 100, 0);
                    Tap(canvas);
                    Tap(canvas);

                    CanvasMouseTests.Click(canvas, At(canvas, 60, 60));

                    Assert.False(model.Session.IsRunning);
                    Assert.Equal(2, ((DxfPolyline)model.Document.FindEntity(polyline.Handle)).NumberOfVertices);
                    Assert.False(Tap(canvas));
                }
            );
        }

        [Fact]
        public void ASegmentMiddleCyclesStretchAddVertexConvertToArc()
        {
            OnCanvas(
                (model, canvas, polyline) =>
                {
                    ClickGrip(model, canvas, 50, 0);
                    Assert.Equal("Specify stretch point or [Base point/Copy/Undo/eXit]:", model.Prompt);

                    Assert.True(Tap(canvas));
                    Assert.Equal("Specify new vertex point or [Base point/Copy/Undo/eXit]:", model.Prompt);

                    Assert.True(Tap(canvas));
                    Assert.Equal("Specify midpoint of arc segment:", model.Prompt);
                    Assert.Contains("** CONVERT TO ARC **", model.History);

                    CanvasMouseTests.Click(canvas, At(canvas, 50, 20));
                    Assert.Equal(-0.4, ((DxfPolyline)model.Document.FindEntity(polyline.Handle)).GetBulgeAt(0), 9);
                }
            );
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(3, true)]
        public void CtrlCyclesOnlyWithGripMultifunctionalBitOne(int value, bool cycles)
        {
            OnCanvas(
                (model, canvas, polyline) =>
                {
                    canvas.GripMultifunctional = value;
                    ClickGrip(model, canvas, 100, 0);

                    Assert.Equal(cycles, Tap(canvas));

                    Assert.Equal(
                        cycles
                            ? "Specify new vertex point or [Base point/Copy/Undo/eXit]:"
                            : "Specify stretch point or [Base point/Copy/Undo/eXit]:",
                        model.Prompt
                    );
                    Assert.True(model.Session.IsGripEditing);
                }
            );
        }

        [Fact]
        public void CtrlWithNoGripEditRunning_OrAnotherCommand_DoesNothing()
        {
            OnCanvas(
                (model, canvas, polyline) =>
                {
                    Assert.False(Tap(canvas));

                    model.Execute("LINE");
                    Assert.False(Tap(canvas));
                    Assert.True(model.Session.IsRunning);
                    Assert.False(model.Session.IsGripEditing);
                }
            );
        }

        [Fact]
        public void CtrlHeldForAClick_DoesNotCycle_AndTheClickStretchesTheVertex()
        {
            OnCanvas(
                (model, canvas, polyline) =>
                {
                    ClickGrip(model, canvas, 100, 0);

                    canvas.OnControlDown();
                    CanvasMouseTests.Click(canvas, At(canvas, 110, 20));

                    Assert.False(canvas.OnControlUp());
                    Assert.False(model.Session.IsRunning);
                    DxfPolyline moved = (DxfPolyline)model.Document.FindEntity(polyline.Handle);
                    Assert.Equal(3, moved.NumberOfVertices);
                    Assert.Equal(110.0, moved.GetPoint2dAt(1).X, 3);
                }
            );
        }

        [Fact]
        public void AKeyPressedWhileCtrlIsHeld_EndsTheTap_SoCtrlPlusZNeverCycles()
        {
            OnCanvas(
                (model, canvas, polyline) =>
                {
                    ClickGrip(model, canvas, 100, 0);

                    canvas.OnControlDown();
                    canvas.OnOtherInput();

                    Assert.False(canvas.OnControlUp());
                    Assert.Equal("Specify stretch point or [Base point/Copy/Undo/eXit]:", model.Prompt);

                    // A Ctrl let go with no press of its own before it (the key went down elsewhere) is no tap either.
                    Assert.False(canvas.OnControlUp());
                }
            );
        }

        /// <summary>Runs a test on a workspace showing the elbow, the vertex at (100, 0) clicked, so that a grip edit waits.</summary>
        private static void OnWorkspace(Action<MainViewModel, CadWorkspace, HwndSource> test) =>
            OnSta(() =>
            {
                MainViewModel model = Elbow(out _);
                var workspace = new CadWorkspace { DataContext = model };
                var source = new HwndSource(
                    new HwndSourceParameters("GripCtrlCycleCanvasTests")
                    {
                        ParentWindow = new IntPtr(-3),
                        WindowStyle = 0,
                        Width = 1600,
                        Height = 960,
                    }
                )
                {
                    RootVisual = workspace,
                };

                using (source)
                {
                    workspace.Measure(new Size(1600, 960));
                    workspace.Arrange(new Rect(0, 0, 1600, 960));
                    workspace.UpdateLayout();
                    CadCanvas canvas = workspace.CanvasView;
                    canvas.RenderNow();
                    canvas.View.ZoomTo(new DisplayBox(-20, -20, 120, 120));
                    canvas.RenderNow();
                    ClickGrip(model, canvas, 100, 0);
                    test(model, workspace, source);
                }
            });

        private static void Key(CadWorkspace workspace, HwndSource source, Key key, bool down) =>
            workspace.RaiseEvent(
                new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
                {
                    RoutedEvent = down ? Keyboard.PreviewKeyDownEvent : Keyboard.PreviewKeyUpEvent,
                }
            );

        [Fact]
        public void ACtrlTapOnTheWorkspace_CyclesTheGripOption_OnTheKeyUp_Once()
        {
            OnWorkspace(
                (model, workspace, source) =>
                {
                    Key(workspace, source, System.Windows.Input.Key.LeftCtrl, true);

                    // Nothing happens while the key is held down.
                    Assert.Equal("Specify stretch point or [Base point/Copy/Undo/eXit]:", model.Prompt);

                    Key(workspace, source, System.Windows.Input.Key.LeftCtrl, false);

                    Assert.Equal("Specify new vertex point or [Base point/Copy/Undo/eXit]:", model.Prompt);
                    Assert.Single(model.History, l => l == "** ADD VERTEX **");
                }
            );
        }

        [Fact]
        public void CtrlPlusZOnTheWorkspace_DoesNotCycle()
        {
            OnWorkspace(
                (model, workspace, source) =>
                {
                    Key(workspace, source, System.Windows.Input.Key.LeftCtrl, true);
                    Key(workspace, source, System.Windows.Input.Key.Z, true);
                    Key(workspace, source, System.Windows.Input.Key.Z, false);
                    Key(workspace, source, System.Windows.Input.Key.LeftCtrl, false);

                    Assert.True(model.Session.IsGripEditing);
                    Assert.Equal("Specify stretch point or [Base point/Copy/Undo/eXit]:", model.Prompt);
                    Assert.DoesNotContain("** ADD VERTEX **", model.History);
                }
            );
        }

        [Fact]
        public void ACtrlTapAfterAMouseButtonWentDownOnTheWorkspace_DoesNotCycle()
        {
            OnWorkspace(
                (model, workspace, source) =>
                {
                    Key(workspace, source, System.Windows.Input.Key.LeftCtrl, true);
                    workspace.RaiseEvent(
                        new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
                        {
                            RoutedEvent = Mouse.PreviewMouseDownEvent,
                        }
                    );
                    Key(workspace, source, System.Windows.Input.Key.LeftCtrl, false);

                    Assert.Equal("Specify stretch point or [Base point/Copy/Undo/eXit]:", model.Prompt);
                }
            );
        }

        private static bool Tap(CadCanvas canvas)
        {
            canvas.OnControlDown();
            return canvas.OnControlUp();
        }
    }
}
