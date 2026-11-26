using System;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>The drawing as JSON and as a graph, answered to a program that reads drawings.</summary>
    public sealed partial class CadRemoteServer
    {
        /// <summary>Answers an <c>export</c>: the text of <see cref="CadDrawingJson"/> or of <see cref="CadDrawingGraph"/>.</summary>
        private static JsonObject Export(CadDocument document, JsonObject request)
        {
            string what = request.GetString("what");
            string layout = request.GetString("layout");

            try
            {
                string text =
                    what == "graph"
                        ? CadDrawingGraph.Build(document, layout, request.GetDouble("tolerance", 0.001)).ToJson()
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
