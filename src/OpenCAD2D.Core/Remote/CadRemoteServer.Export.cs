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
            string what = request.GetString("what");
            string layout = request.GetString("layout");

            try
            {
                // A request with no maxEdges has no cap; a cap below 1 is refused by the graph, one above an int is no cap.
                long cap = request.GetLong("maxEdges", int.MaxValue);
                string text =
                    what == "graph"
                        ? CadDrawingGraph
                            .Build(
                                document,
                                layout,
                                request.GetDouble("tolerance", 0.001),
                                (int)Math.Min(cap, int.MaxValue)
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
