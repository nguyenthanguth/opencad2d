using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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
            "Adds a layout (a sheet of paper) with its paper size, in millimetres, plotted at 1:1 on that sheet, as LAYOUT New and the page setup do (no command is typed). It is not made the current one (set_current_layout). One undo step."
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
                    editing.AddLayout(label, null, landscape);
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
            Name = "rename_layout",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Renames a layout (a sheet), as LAYOUT Rename does. Refused for Model, for a name the drawing has (either case) and for a name with a character AutoCAD refuses in a layout's name. One undo step."
        )]
        public LayoutResult RenameLayout(
            [Description("The layout's name now.")] string name,
            [Description("The new name.")] string new_name
        ) => _session.Run("rename_layout", true, drawing =>
            {
                string sheet = LayoutNamed(drawing, name);
                Editing(drawing)
                    .Make("rename_layout", new CadMakeArgs().Set("name", sheet).Set("new_name", new_name?.Trim()));
                return new LayoutResult
                {
                    Name = drawing.Layouts.First(l =>
                        string.Equals(l, new_name.Trim(), StringComparison.OrdinalIgnoreCase)
                    ),
                };
            });

        [McpServerTool(
            Name = "delete_layout",
            ReadOnly = false,
            Destructive = true,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Deletes a layout (a sheet) with everything drawn in it and its viewports, as LAYOUT Delete does. Refused for Model and for the layout shown (set_current_layout shows another first). Where it was the last sheet a new, empty Layout1 takes its place, as AutoCAD leaves a drawing one. One undo step."
        )]
        public LayoutResult DeleteLayout([Description("The layout to delete.")] string name) =>
            _session.Run("delete_layout", true, drawing =>
            {
                string sheet = LayoutNamed(drawing, name);
                Editing(drawing).Make("delete_layout", new CadMakeArgs().Set("name", sheet));
                return new LayoutResult { Name = sheet };
            });

        [McpServerTool(
            Name = "set_current_layout",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Shows a layout, or Model, as clicking its tab does. It changes what the app shows and the layout the tools that type commands (the commands of modify tools such as offset, fillet and trim, and run_command) work in; the add_* tools, query tools, plot_pdf, render_png and the rest take their own layout argument and work in model space where it is left out, whatever layout is shown. One undo step in the app."
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
            "Adds a viewport to a layout, as MVIEW with two corners and ZOOM nXP do (no command is typed), showing model space at a scale: the frame is on the sheet in millimetres, the scale is the n of 1:n (10 for 1:10, 0.5 for 2:1), the view is centred on a point of model space, and layers can be frozen in this viewport only. The viewport is on the current layer unless layer says another (a layer that is not plotted keeps the frame off the paper). Gives the viewport's handle. One undo step."
        )]
        public LayoutResult AddViewport(
            [Description("The layout.")] string layout,
            [Description("The frame on the sheet [x1, y1, x2, y2] in millimetres.")] double[] frame,
            [Description("The scale as the n of 1:n, above 0: 10 for 1:10, 5 for 1:5, 0.5 for 2:1.")] double scale,
            [Description(
                "The point of model space at the middle of the frame [x, y]; the middle of what model space draws where left out."
            )]
                double[] center = null,
            [Description("Layers to freeze in this viewport only.")] string[] freeze_layers = null,
            [Description("A layer of the drawing for the viewport's frame; the current layer where left out.")]
                string layer = null
        ) => _session.Run("add_viewport", true, drawing =>
            {
                string sheet = LayoutNamed(drawing, layout);
                DisplayBox box = Box("frame", frame)
                    ?? throw new ArgumentException("frame is [x1, y1, x2, y2], four numbers.");

                if (!(box.Width > Tol.EqualPoint) || !(box.Height > Tol.EqualPoint))
                {
                    throw new ArgumentException("The frame of a viewport has a width and a height.");
                }

                // The scale is written as 1:n with 15 decimals: a smaller one would be written as 1:0.
                if (!(scale >= 1e-15) || double.IsInfinity(scale))
                {
                    throw new ArgumentException(
                        "The scale is the n of 1:n, a number of 0.000000000000001 or more (10 for 1:10, 0.5 for 2:1)."
                    );
                }

                double[] at = center == null ? null : new[] { Point("center", center).X, Point("center", center).Y };
                string[] layers =
                    freeze_layers == null
                        ? new string[0]
                        : freeze_layers.Select(l => FoundLayer(drawing, l).Name).ToArray();
                string on = string.IsNullOrWhiteSpace(layer) ? null : FoundLayer(drawing, layer).Name;
                IDrawingEditing editing = Editing(drawing);
                string handle = null;
                editing.Group(() =>
                {
                    handle = editing.AddViewport(
                        sheet,
                        new[] { box.MinX, box.MinY, box.MaxX, box.MaxY },
                        "1:" + scale.ToString("0.###############", CultureInfo.InvariantCulture),
                        at,
                        layers
                    );
                    Look(editing, new[] { handle }, on, null);
                });
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
