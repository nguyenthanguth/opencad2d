using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.Plugins;
using OpenCAD2D.Remote;
using OpenCAD2D.Wpf;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The other end of the snaps of a Python tool, played by a test: the lines the app sends the tool are kept, the work posted
    /// to the window's thread waits to be run by the test, and the tool's answers are made up by the test and given to the snaps
    /// as the control pipe's reader would. No process is started, and no thread but the test's is waited on.
    /// </summary>
    internal sealed class FakeToolLink
    {
        private readonly object _gate = new object();
        private readonly List<string> _sent = new List<string>();

        public FakeToolLink(string tool = "Circles")
        {
            Snaps = new CadToolSnaps(tool, Send, Posted.Enqueue, () => Starts++, documents => Answered.Add(documents));
        }

        /// <summary>Gets the snaps of the tool.</summary>
        public CadToolSnaps Snaps { get; }

        /// <summary>Gets the work the snaps posted to the window's thread and nobody has run yet.</summary>
        public ConcurrentQueue<Action> Posted { get; } = new ConcurrentQueue<Action>();

        /// <summary>Gets the drawings the snaps told the window an answer came for, one entry for each time it was told.</summary>
        public List<IReadOnlyList<CadDocument>> Answered { get; } = new List<IReadOnlyList<CadDocument>>();

        /// <summary>Gets how many times the snaps asked for the tool's process to be started.</summary>
        public int Starts;

        /// <summary>Gets or sets what is done with each line sent, on the thread that sent it; null for nothing.</summary>
        public Action<string> OnSend { get; set; }

        /// <summary>Gets or sets whether the tool has a process to take the lines.</summary>
        public bool Connected { get; set; } = true;

        /// <summary>Gets the lines sent to the tool, in order.</summary>
        public List<string> Sent
        {
            get
            {
                lock (_gate)
                {
                    return _sent.ToList();
                }
            }
        }

        private bool Send(string line)
        {
            lock (_gate)
            {
                _sent.Add(line);
            }

            OnSend?.Invoke(line);
            return Connected;
        }

        /// <summary>Runs, on this thread, the work posted to the window's thread, and what that posts, until none is left.</summary>
        public void RunPosted()
        {
            while (Posted.TryDequeue(out Action work))
            {
                work();
            }
        }

        /// <summary>Gets a question sent to the tool, read as the tool reads it.</summary>
        public JsonObject Question(int index) => (JsonObject)RemoteJson.Parse(Sent[index]);

        /// <summary>The items of a question.</summary>
        public static List<JsonObject> Items(JsonObject question) =>
            question.GetArray("items").Cast<JsonObject>().ToList();

        /// <summary>The entities of a question, read as the tool reads its fragment, by the handle each item names.</summary>
        public static DxfEntity EntityOf(JsonObject question, JsonObject item) =>
            DxfDatabase
                .Parse(question.GetString("fragment"))
                .ModelSpace.Cast<DxfEntity>()
                .Single(e => e.Handle == item.GetString("ref"));

        /// <summary>
        /// Answers a question as the tool does: one answer for each item, in order. An answer is the points of the item (a
        /// <c>double[][]</c>, each point <c>x, y</c>), or the text of an error line.
        /// </summary>
        public void Reply(int index, params object[] answers) => Reply(Question(index).GetLong("id"), answers);

        /// <summary>Answers the question of an id; see <see cref="Reply(int, object[])"/>.</summary>
        public void Reply(long id, params object[] answers)
        {
            var list = new JsonArray();

            foreach (object answer in answers)
            {
                list.Add(
                    answer is string error
                        ? new JsonObject().Set("error", error)
                        : new JsonObject().Set(
                            "points",
                            new JsonArray(
                                ((double[][])answer).Select(p => (object)new JsonArray(new object[] { p[0], p[1] }))
                            )
                        )
                );
            }

            Snaps.Answered(new JsonObject().Set("op", "snapped").Set("id", id).Set("answers", list));
        }

        /// <summary>The snap of a manifest entry, whose finder is the tool's, on by default.</summary>
        public CadCustomSnap SnapOf(CadPluginSnapEntry entry) =>
            new CadCustomSnap(entry.Name, entry.Keyword, Snaps.FinderOf(entry)) { IsOnByDefault = true };

        /// <summary>A snap entry as plugin.json declares it, for circles.</summary>
        public static CadPluginSnapEntry Entry(
            string keyword = "CTOP",
            string name = "Circle top",
            string types = "[\"CIRCLE\"]"
        ) =>
            CadPluginManifest
                .Parse(
                    "{ \"name\": \"Circles\", \"snaps\": [ { \"keyword\": \""
                        + keyword
                        + "\", \"name\": \""
                        + name
                        + "\", \"entityTypes\": "
                        + types
                        + ", \"onByDefault\": true } ] }"
                )
                .Snaps[0];
    }
}
