using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    /// <summary>The tools of layouts: a sheet with its paper, the tab shown, viewports at a scale.</summary>
    internal sealed partial class DrawingTools
    {
        private static readonly char[] LayoutNameFaults =
        {
            '<',
            '>',
            '/',
            '\\',
            '"',
            ':',
            ';',
            '?',
            '*',
            '|',
            ',',
            '=',
            '`',
        };

        private static string LayoutNamed(IDrawingBackend drawing, string name)
        {
            string layout = Layout(drawing, name);

            if (string.Equals(layout, "Model", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Model is not a sheet: give the name of a layout.");
            }

            return layout;
        }

        [McpServerTool(
            Name = "create_layout",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a layout (a sheet of paper) with its paper size, in millimetres, plotted at 1:1 on that sheet, as LAYOUT New and the page setup. It is not made the current one (set_current_layout). One undo step."
        )]
        public LayoutResult CreateLayout(
            [Description("The layout's name, as A3 Plan.")] string name,
            [Description(
                "A sheet as ISO A0, ISO A1, ISO A2, ISO A3, ISO A4, ANSI A to ANSI E, or a size in millimetres as 420x297 (width, height)."
            )]
                string paper = "ISO A3",
            [Description("True for the long side across; false for the sheet turned.")] bool landscape = true
        ) => _session.Run("create_layout", true, drawing =>
            {
                string label = name?.Trim();

                if (
                    string.IsNullOrEmpty(label)
                    || label.Length > 255
                    || label.IndexOfAny(LayoutNameFaults) >= 0
                    || label.Equals("Model", StringComparison.OrdinalIgnoreCase)
                )
                {
                    throw new ArgumentException(
                        "A layout needs a name of up to 255 characters, not Model, without any of < > / \\ \" : ; ? * | , = ` in it."
                    );
                }

                if (drawing.Layouts.Any(l => string.Equals(l, label, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException("Layout \"" + label + "\" already exists.");
                }

                CadMake.Paper(paper ?? "ISO A3");
                IDrawingEditing editing = Editing(drawing);
                editing.Group(() =>
                {
                    editing.Run(new CommandScript().Word("-LAYOUT").Word("N").Text(label));
                    editing.Make(
                        "page_setup",
                        new CadMakeArgs()
                            .Set("layout", label)
                            .Set("paper", paper ?? "ISO A3")
                            .Set("landscape", landscape)
                    );
                });
                return new LayoutResult
                {
                    Name = drawing.Layouts.First(l => string.Equals(l, label, StringComparison.OrdinalIgnoreCase)),
                    Paper = paper,
                };
            });

        [McpServerTool(
            Name = "set_current_layout",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Shows a layout, or Model, as clicking its tab does: the commands of the tools work in the layout shown. One undo step in the app."
        )]
        public LayoutResult SetCurrentLayout([Description("Model, or the name of a layout.")] string name) =>
            _session.Run("set_current_layout", true, drawing =>
            {
                Editing(drawing).SetCurrentLayout(Layout(drawing, name));
                return new LayoutResult { Name = drawing.CurrentLayout };
            });

        [McpServerTool(
            Name = "add_viewport",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a viewport to a layout, as MVIEW with two corners, showing model space at a scale: the frame is on the sheet in millimetres, the scale is the n of 1:n (10 for 1:10, 0.5 for 2:1), the view is centred on a point of model space, and layers can be frozen in this viewport only. Gives the viewport's handle. One undo step."
        )]
        public LayoutResult AddViewport(
            [Description("The layout.")] string layout,
            [Description("The frame on the sheet [x1, y1, x2, y2] in millimetres.")] double[] frame,
            [Description("The scale as the n of 1:n, above 0: 10 for 1:10, 5 for 1:5, 0.5 for 2:1.")] double scale,
            [Description(
                "The point of model space at the middle of the frame [x, y]; where the view the viewport starts with is centred where left out."
            )]
                double[] center = null,
            [Description("Layers to freeze in this viewport only.")] string[] freeze_layers = null
        ) => _session.Run("add_viewport", true, drawing =>
            {
                string sheet = LayoutNamed(drawing, layout);
                DisplayBox box = Box("frame", frame)
                    ?? throw new ArgumentException("frame is [x1, y1, x2, y2], four numbers.");

                if (!(box.Width > Tol.EqualPoint) || !(box.Height > Tol.EqualPoint))
                {
                    throw new ArgumentException("The frame of a viewport has a width and a height.");
                }

                if (!(scale > 0.0) || double.IsInfinity(scale))
                {
                    throw new ArgumentException("The scale is the n of 1:n, a number above nought.");
                }

                double[] at = center == null ? null : new[] { Point("center", center).X, Point("center", center).Y };
                string[] layers =
                    freeze_layers == null ? new string[0] : freeze_layers.Select(l => FoundLayer(drawing, l).Name).ToArray();
                IDrawingEditing editing = Editing(drawing);
                string handle = null;
                editing.Group(() => InLayout(drawing, sheet, () =>
                    {
                        ModifiedResult made = Script(drawing, new List<string>(), true, () =>
                            new CommandScript().Word("MVIEW").Point(box.MinX, box.MinY).Point(box.MaxX, box.MaxY)
                        );
                        handle = made.Created.FirstOrDefault()
                            ?? throw new InvalidOperationException("The viewport was not made.");
                        editing.Make(
                            "viewport_view",
                            new CadMakeArgs().Set("handle", handle).Set("scale", scale).Set("center", at)
                        );

                        if (layers.Length > 0)
                        {
                            editing.Make(
                                "viewport_freeze",
                                new CadMakeArgs().Set("handle", handle).Set("layers", layers).Set("frozen", true)
                            );
                        }
                    }));
                return new LayoutResult { Name = sheet, Handles = new[] { handle } };
            });

        [McpServerTool(
            Name = "set_viewport_scale",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Sets the scale of a viewport as the n of 1:n (10 for 1:10, 0.5 for 2:1), and where given the point of model space at the middle of its frame. Refused for a viewport whose display is locked. One undo step."
        )]
        public ChangedResult SetViewportScale(
            [Description("The handle of the viewport.")] string handle,
            [Description("The scale as the n of 1:n, above 0.")] double scale,
            [Description(
                "The point of model space at the middle of the frame [x, y]; the view's centre stays where left out."
            )]
                double[] center = null
        ) => _session.Run("set_viewport_scale", true, drawing =>
            {
                double[] at = center == null ? null : new[] { Point("center", center).X, Point("center", center).Y };
                Editing(drawing)
                    .Make(
                        "viewport_view",
                        new CadMakeArgs().Set("handle", handle).Set("scale", scale).Set("center", at)
                    );
                return Changed(handle);
            });

        [McpServerTool(
            Name = "lock_viewport",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Locks or unlocks the display of a viewport (its scale and view stay as they are while it is locked). One undo step."
        )]
        public ChangedResult LockViewport(
            [Description("The handle of the viewport.")] string handle,
            [Description("True to lock, false to unlock.")] bool locked = true
        ) => _session.Run("lock_viewport", true, drawing =>
            {
                Editing(drawing)
                    .Make(
                        "set_rows",
                        new CadMakeArgs()
                            .Set("handles", new[] { handle?.Trim() })
                            .Set("rows", new[] { new[] { "Display locked", locked ? "Yes" : "No" } })
                    );
                return Changed(handle);
            });
    }
}
