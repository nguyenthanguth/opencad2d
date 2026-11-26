using System;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>The drawing as JSON and as a graph, answered to a program that reads drawings.</summary>
    public sealed partial class CadRemoteServer
    {
        /// <summary>Answers an <c>export</c>: the text of <see cref="CadDrawingJson"/> or of <see cref="CadDrawingGraph"/> (with the request's <c>maxEdges</c> as its cap, if it has one).</summary>
        private static JsonObject Export(CadDocument document, JsonObject request)
        {
            string what = TextMember(request, "what");
            string layout = TextMember(request, "layout");

            if (what != "json" && what != "graph")
            {
                throw new Refusal("The member what has to be json or graph.");
            }

            double tolerance = NumberMember(request, "tolerance", 0.001);
            double cap = NumberMember(request, "maxEdges", int.MaxValue);

            try
            {
                // A request with no maxEdges has no cap; a cap below 1 is refused by the graph, one above an int is no cap.
                string text =
                    what == "graph"
                        ? CadDrawingGraph
                            .Build(
                                document,
                                layout,
                                tolerance,
                                cap >= int.MaxValue ? int.MaxValue
                                    : cap < 1 ? 0
                                    : (int)cap
                            )
                            .ToJson()
                        : CadDrawingJson.Write(document, layout);
                return new JsonObject().Set("text", text);
            }
            catch (ArgumentException exception)
            {
                throw new Refusal(exception.Message.Split('\n')[0].TrimEnd('\r'));
            }
        }
    }
}
