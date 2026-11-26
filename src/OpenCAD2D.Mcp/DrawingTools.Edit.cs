using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using GeometryHelper.Geometry;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    /// <summary>The tools that change what is drawn (each call one undo step), take a step back, draw a picture and type a command.</summary>
    internal sealed partial class DrawingTools
    {
        private const string HandlesText = "Handles of the entities, as [\"2F\", \"30\"] (query_entities gives them).";

        [McpServerTool(Name = "move", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description(
            "Moves entities by a displacement, as MOVE. Refused for an entity on a locked layer. One undo step."
        )]
        public ChangedResult Move(
            [Description(HandlesText)] string[] handles,
            [Description("The displacement [dx, dy] in millimetres.")] double[] delta
        ) => Change("move", handles, (drawing, list) =>
            {
                GeoPoint3 d = Point("delta", delta);
                drawing.Move(list, d.X, d.Y);
            });

        [McpServerTool(Name = "rotate", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description(
            "Rotates entities about a base point by an angle in degrees, counter-clockwise, as ROTATE. Refused for an entity on a locked layer. One undo step."
        )]
        public ChangedResult Rotate(
            [Description(HandlesText)] string[] handles,
            [Description("The base point [x, y] they turn about.")] double[] base_point,
            [Description("The angle in degrees, counter-clockwise.")] double angle
        ) => Change("rotate", handles, (drawing, list) =>
            {
                GeoPoint3 b = Point("base_point", base_point);
                drawing.Rotate(list, b.X, b.Y, Radians(angle));
            });

        [McpServerTool(Name = "scale", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description(
            "Scales entities about a base point by a factor, as SCALE. Refused for an entity on a locked layer. One undo step."
        )]
        public ChangedResult Scale(
            [Description(HandlesText)] string[] handles,
            [Description("The base point [x, y] that stays where it is.")] double[] base_point,
            [Description("The factor, above 0.")] double factor
        ) => Change("scale", handles, (drawing, list) =>
            {
                GeoPoint3 b = Point("base_point", base_point);

                if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0.0)
                {
                    throw new ArgumentException("The factor is a finite number above nought.");
                }

                drawing.Scale(list, b.X, b.Y, factor);
            });

        [McpServerTool(Name = "erase", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
        [Description(
            "Erases entities, as ERASE; undo brings them back. Refused for an entity on a locked layer. One undo step."
        )]
        public ChangedResult Erase([Description(HandlesText)] string[] handles) =>
            Change("erase", handles, (drawing, list) => drawing.Erase(list));

        [McpServerTool(
            Name = "set_properties",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Sets the layer, colour, linetype, linetype scale, lineweight or transparency of entities. A property left out stays as it is. Refused for an entity on a locked layer. One undo step."
        )]
        public ChangedResult SetProperties(
            [Description(HandlesText)] string[] handles,
            [Description("The layer to move them to; it must exist (list_layers).")] string layer = null,
            [Description("AutoCAD colour index 1 to 255, 256 for BYLAYER, 0 for BYBLOCK.")] int? color = null,
            [Description("A linetype of the drawing, or BYLAYER or BYBLOCK.")] string linetype = null,
            [Description("The linetype scale of each object, above 0.")] double? linetype_scale = null,
            [Description(
                "Lineweight in hundredths of a millimetre, one of AutoCAD's (0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211), or -1 BYLAYER, -2 BYBLOCK, -3 default."
            )]
                int? lineweight = null,
            [Description("Transparency in per cent, 0 to 90, or -1 for BYLAYER.")] int? transparency = null
        ) => Change("set_properties", handles, (drawing, list) =>
            {
                if (linetype_scale.HasValue || lineweight.HasValue || transparency.HasValue)
                {
                    SetPropertyRows(drawing, list, layer, color, linetype, linetype_scale, lineweight, transparency);
                    return;
                }

                if (string.IsNullOrEmpty(layer) && !color.HasValue && string.IsNullOrEmpty(linetype))
                {
                    throw new ArgumentException("Give a layer, a color or a linetype to set.");
                }

                if (color.HasValue && (color.Value < 0 || color.Value > 256))
                {
                    throw new ArgumentException("A colour index is 0 to 256.");
                }

                if (
                    !string.IsNullOrEmpty(layer)
                    && !drawing.Layers().Any(l => string.Equals(l.Name, layer, StringComparison.OrdinalIgnoreCase))
                )
                {
                    throw new ArgumentException(
                        "The drawing has no layer " + layer + ": list_layers shows the ones it has."
                    );
                }

                drawing.SetProperties(list, layer, color, linetype);
            });

        /// <summary>The lineweights AutoCAD has, in hundredths of a millimetre.</summary>
        private static readonly int[] LineWeights =
        {
            0,
            5,
            9,
            13,
            15,
            18,
            20,
            25,
            30,
            35,
            40,
            50,
            53,
            60,
            70,
            80,
            90,
            100,
            106,
            120,
            140,
            158,
            200,
            211,
        };

        [McpServerTool(
            Name = "add_layer",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a layer with the look given, as the Layer manager's New layer does. Refused when the drawing has the name (either case), when the name has a character AutoCAD refuses (< > / \\ \" : ; ? * | = `), or when the linetype is not in the drawing. One undo step."
        )]
        public LayerInfo AddLayer(
            [Description("The layer's name.")] string name,
            [Description("AutoCAD colour index 1 to 255; 7 (white or black by the ground) where left out.")]
                int color = 7,
            [Description("A linetype of the drawing; Continuous where left out.")] string linetype = "Continuous",
            [Description(
                "Lineweight in hundredths of a millimetre, one of AutoCAD's: 0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211; the default weight (-3) where left out."
            )]
                int? lineweight = null,
            [Description("False to make the layer off.")] bool on = true,
            [Description("True to make the layer frozen.")] bool frozen = false,
            [Description("True to make the layer locked.")] bool locked = false
        ) => _session.Run("add_layer", true, drawing =>
            {
                string trimmed = name?.Trim();

                if (string.IsNullOrEmpty(trimmed))
                {
                    throw new ArgumentException("A layer needs a name.");
                }

                if (color < 1 || color > 255)
                {
                    throw new ArgumentException("A layer's colour index is 1 to 255.");
                }

                if (lineweight.HasValue && !LineWeights.Contains(lineweight.Value))
                {
                    throw new ArgumentException(
                        "The lineweight "
                            + lineweight.Value
                            + " is none of AutoCAD's: "
                            + string.Join(", ", LineWeights)
                            + "."
                    );
                }

                string type = string.IsNullOrWhiteSpace(linetype) ? "Continuous" : linetype.Trim();

                if (!drawing.Linetypes().Any(l => string.Equals(l, type, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException("The drawing has no linetype " + type + ".");
                }

                if (drawing.Layers().Any(l => string.Equals(l.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException("Layer \"" + trimmed + "\" already exists.");
                }

                var row = new LayerRow
                {
                    Name = trimmed,
                    Color = color,
                    Linetype = type,
                    LineWeight = lineweight ?? -3,
                    IsOn = on,
                    IsFrozen = frozen,
                    IsLocked = locked,
                    IsPlottable = true,
                    Description = string.Empty,
                };
                drawing.AddLayer(row);
                return new LayerInfo
                {
                    Name = row.Name,
                    Color = row.Color,
                    Linetype = row.Linetype,
                    LineWeight = row.LineWeight,
                    On = row.IsOn,
                    Frozen = row.IsFrozen,
                    Locked = row.IsLocked,
                    Plottable = true,
                    Description = string.Empty,
                };
            });

        private ChangedResult Change(string tool, string[] handles, Action<IDrawingBackend, List<string>> work) =>
            _session.Run(tool, true, drawing =>
            {
                List<string> list = Handles(handles);
                work(drawing, list);
                return new ChangedResult { Changed = list.Count, Handles = list };
            });

        [McpServerTool(Name = "undo", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
        [Description(
            "Takes back the last undo step: the last thing an add, move, rotate, scale, erase or set_properties call did. In a server attached to an app this types U into the app, which also takes back what the user drew last."
        )]
        public UndoResult Undo() => _session.Run("undo", true, drawing =>
            {
                bool undone = drawing.Undo();
                return new UndoResult
                {
                    Undone = undone,
                    Message = undone ? "The last step was taken back." : "There was nothing to take back.",
                };
            });

        [McpServerTool(Name = "render_png", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "Draws a layout (or a part of it) to a PNG and returns the picture as an image, as the app's canvas draws it, on a white ground. Size: width or height in pixels (the other follows the part's shape), or dpi for a layout; 1024 pixels wide where none is given. At most 16384 pixels a side."
        )]
        public CallToolResult RenderPng(
            [Description(
                "The part to draw, [minX, minY, maxX, maxY] in millimetres; all of the layout where left out."
            )]
                double[] box = null,
            [Description("The layout; model space where left out.")] string layout = null,
            [Description("Width of the picture in pixels.")] int width = 0,
            [Description("Height of the picture in pixels.")] int height = 0,
            [Description(
                "Pixels to an inch of paper for a layout, where width and height are left out; 96 is the default."
            )]
                double dpi = 0.0
        ) => _session.Run("render_png", false, drawing =>
            {
                double[] part = Normalised("box", box);

                if (width < 0 || height < 0 || dpi < 0.0 || double.IsNaN(dpi))
                {
                    throw new ArgumentException("A size is not negative.");
                }

                byte[] png = drawing.RenderPng(
                    new RenderRequest
                    {
                        Layout = Layout(drawing, layout),
                        Box = part,
                        Width = width,
                        Height = height,
                        Dpi = dpi,
                    }
                );
                return new CallToolResult
                {
                    Content = new List<ContentBlock>
                    {
                        ImageContentBlock.FromBytes(png, "image/png"),
                        new TextContentBlock { Text = PngSize(png) },
                    },
                };
            });

        /// <summary>The size of a PNG as a line of text, read from its header.</summary>
        private static string PngSize(byte[] png)
        {
            int w = png.Length >= 24 ? (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19] : 0;
            int h = png.Length >= 24 ? (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23] : 0;
            return string.Format(CultureInfo.InvariantCulture, "PNG {0} by {1} pixels, {2} bytes.", w, h, png.Length);
        }

        [McpServerTool(
            Name = "run_command",
            ReadOnly = false,
            Destructive = true,
            Idempotent = false,
            OpenWorld = true
        )]
        [Description(
            "Types text on the command line of the drawing as a user would, and returns what the command line wrote: a command and its answers a line at a time, each word of a line one answer and an empty line Enter (the text \"LINE 0,0 100,0\" and a new line after it draws a line, and a second new line ends the command), or AutoLISP. It is a script, so the server only allows it when started with --allow-scripts, and never when started with --read-only. On a drawing file a command still waiting for an answer at the end is cancelled; attached to an app it is left waiting (the answer says so, after 10 seconds) and the next run_command answers it."
        )]
        public CommandResult RunCommand([Description("The command line text; a new line is Enter.")] string text)
        {
            _session.RequireScripts("run_command");
            return _session.Run("run_command", true, drawing =>
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new ArgumentException("Give the command text to type.");
                }

                return new CommandResult { Output = drawing.RunCommand(text) };
            });
        }
    }
}
