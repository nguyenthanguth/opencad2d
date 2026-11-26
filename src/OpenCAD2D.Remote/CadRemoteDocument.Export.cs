using System;
using System.Globalization;

namespace OpenCAD2D.Remote
{
    /// <summary>The drawing as JSON and as a graph, for programs that read drawings to learn from them.</summary>
    public sealed partial class CadRemoteDocument
    {
        /// <summary>
        /// Gets the drawing as one JSON document in millimetres, block references walked down to what they draw, as the app
        /// writes it (schema <c>opencad2d.drawing</c>, version 1; see <c>docs/schemas</c>): the entities in the order of their
        /// handles, with their layer, colour, linetype, box, words and geometry, and the tables of layers, styles, linetypes and
        /// blocks.
        /// </summary>
        /// <param name="layout">The layout to take (<c>Model</c> for model space), or null for model space and every layout.</param>
        /// <returns>The JSON text.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed, or has no layout of that name.</exception>
        public string ExportJson(string layout) =>
            Ask(RemoteOps.Export, request =>
                {
                    request.Set("what", "json");
                    request.Set("layout", layout);
                })
                .GetString("text");

        /// <summary>
        /// Gets the graph of the drawing as one JSON document, as the app writes it (schema <c>opencad2d.graph</c>, version 1): a
        /// node for each entity of <see cref="ExportJson"/> and the edges that touch, contain, dimension and annotate.
        /// </summary>
        /// <param name="layout">The layout to take (<c>Model</c> for model space), or null for model space and every layout.</param>
        /// <param name="tolerance">How near two ends must be to touch, in millimetres; above nought.</param>
        /// <returns>The JSON text.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed, has no layout of that name, or the tolerance is not above nought.</exception>
        public string ExportGraph(string layout, double tolerance) =>
            Ask(RemoteOps.Export, request =>
                {
                    request.Set("what", "graph");
                    request.Set("layout", layout);
                    request.Set("tolerance", tolerance);
                })
                .GetString("text");

        /// <summary>
        /// Gets the graph of the drawing as <see cref="ExportGraph(string, double)"/> does, cut at a number of edges: the first
        /// <paramref name="maxEdges"/> edges of the whole graph in its order (kind, source, target), with <c>truncated</c> true and
        /// <c>edgesFound</c> in the text where there were more.
        /// </summary>
        /// <param name="layout">The layout to take (<c>Model</c> for model space), or null for model space and every layout.</param>
        /// <param name="tolerance">How near two ends must be to touch, in millimetres; above nought.</param>
        /// <param name="maxEdges">The most edges to keep; above nought.</param>
        /// <returns>The JSON text.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed, has no layout of that name, or the tolerance or the cap is not above nought.</exception>
        public string ExportGraph(string layout, double tolerance, int maxEdges) =>
            Ask(RemoteOps.Export, request =>
                {
                    request.Set("what", "graph");
                    request.Set("layout", layout);
                    request.Set("tolerance", tolerance);
                    request.Set("maxEdges", (long)maxEdges);
                })
                .GetString("text");
    }
}
