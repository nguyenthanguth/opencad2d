using System;
using System.Collections.Generic;
using System.Linq;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>The sheets of a drawing, made and set up from outside: layouts, their page setup and their viewports.</summary>
    public sealed partial class CadRemoteServer
    {
        /// <summary>
        /// Answers a <c>sheets</c>: <c>list</c> gives the setup of every layout (<c>layouts</c>), <c>add</c> adds one (<c>name</c>,
        /// <c>paper</c>, <c>landscape</c>), <c>setup</c> changes a page setup (<c>layout</c> and what is given of <c>paper</c>,
        /// <c>orientation</c>, <c>area</c>, <c>scale</c>, <c>plotStyle</c>) and <c>viewport</c> adds a viewport (<c>layout</c>,
        /// <c>frame</c>, <c>scale</c>, <c>center</c>, <c>frozen</c>), as <see cref="CadLayouts"/> does. Refused while a command
        /// runs in the drawing, for a write.
        /// </summary>
        private JsonObject Sheets(CadDocument document, JsonObject request)
        {
            string action = request.GetString("action");

            try
            {
                switch (action)
                {
                    case "list":
                        return new JsonObject().Set(
                            "layouts",
                            new JsonArray(CadLayouts.List(document).Select(info => (object)info.ToObject()))
                        );
                    case "add":
                        Busy(document, "add the layout");
                        return new JsonObject().Set(
                            "layout",
                            CadLayouts
                                .Add(
                                    document,
                                    request.GetString("name"),
                                    request.GetString("paper"),
                                    request.GetBool("landscape", true)
                                )
                                .ToObject()
                        );
                    case "setup":
                        Busy(document, "set up the layout");
                        return new JsonObject().Set(
                            "layout",
                            CadLayouts
                                .SetPageSetup(
                                    document,
                                    request.GetString("layout"),
                                    request.GetString("paper"),
                                    request.GetString("orientation"),
                                    request.GetString("area"),
                                    request.GetString("scale"),
                                    request.GetString("plotStyle")
                                )
                                .ToObject()
                        );
                    case "viewport":
                        Busy(document, "add the viewport");
                        double[] frame = request.GetArray("frame").Select(AsNumber).ToArray();
                        JsonArray at = request.GetArray("center");
                        double[] center = at.Count == 0 ? null : at.Select(AsNumber).ToArray();
                        string[] frozen = request.GetArray("frozen").Select(item => item as string).ToArray();
                        return new JsonObject().Set(
                            "handle",
                            CadLayouts.AddViewport(
                                document,
                                request.GetString("layout"),
                                frame,
                                request.GetString("scale"),
                                center,
                                frozen
                            )
                        );
                    default:
                        throw new Refusal("The sheets request has no action " + action + ".");
                }
            }
            catch (ArgumentException exception)
            {
                throw new Refusal(exception.Message.Split('\n')[0].TrimEnd('\r'));
            }
            catch (InvalidOperationException exception)
            {
                throw new Refusal(exception.Message.Split('\n')[0].TrimEnd('\r'));
            }
        }

        private static double AsNumber(object item) =>
            item is double real ? real
            : item is long whole ? whole
            : double.NaN;
    }
}
