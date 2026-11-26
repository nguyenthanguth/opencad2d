using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;

namespace OpenCAD2D.Remote
{
    /// <summary>What an object prompt of a running command answered when a pick by handle was fed to it.</summary>
    internal sealed class CadRemoteFeed
    {
        public CadRemoteFeed(bool taken, bool completed, IReadOnlyList<string> output)
        {
            Taken = taken;
            Completed = completed;
            Output = output;
        }

        /// <summary>Gets whether the prompt took the objects.</summary>
        public bool Taken { get; }

        /// <summary>Gets whether the command ended.</summary>
        public bool Completed { get; }

        /// <summary>Gets what the command line wrote.</summary>
        public IReadOnlyList<string> Output { get; }
    }

    /// <summary>What typing lines of a command's script on the command line did: whether the command ended, what it wrote, how many lines were left untyped.</summary>
    internal sealed class CadRemoteTyped
    {
        public CadRemoteTyped(bool completed, IReadOnlyList<string> output, int skipped)
        {
            Completed = completed;
            Output = output;
            Skipped = skipped;
        }

        /// <summary>Gets whether the command ended.</summary>
        public bool Completed { get; }

        /// <summary>Gets what the command line wrote.</summary>
        public IReadOnlyList<string> Output { get; }

        /// <summary>Gets how many lines the app did not type because the command had ended: a word typed then would start another command.</summary>
        public int Skipped { get; }
    }

    /// <summary>The calls the MCP server makes beyond the public API: an edit of the core in one call, and a pick by handle.</summary>
    public sealed partial class CadRemoteDocument
    {
        /// <summary>Asks the app to make an edit of <c>CadMake</c>; the arguments are texts, numbers, bools, or lists and tables of them.</summary>
        internal IReadOnlyList<string> Make(string what, IEnumerable<KeyValuePair<string, object>> args)
        {
            var json = new JsonObject();

            foreach (KeyValuePair<string, object> member in args)
            {
                json.Set(member.Key, Carried(member.Value));
            }

            JsonObject answer = Ask(RemoteOps.Make, request => request.Set("what", what).Set("args", json));
            return answer.GetArray("results").Select(r => r as string ?? string.Empty).ToList();
        }

        private static object Carried(object value)
        {
            switch (value)
            {
                case string[] texts:
                    return new JsonArray(texts.Cast<object>());
                case double[] numbers:
                    return new JsonArray(numbers.Cast<object>());
                case string[][] rows:
                    return new JsonArray(rows.Select(r => (object)new JsonArray(r.Cast<object>())));
                default:
                    return value;
            }
        }

        /// <summary>
        /// Types the lines of a script on the command line without waiting, as <see cref="SendCommand(string)"/> does, but types none
        /// once the command has ended, and says how many it left: the first line starts the command unless the script goes on with
        /// one already running.
        /// </summary>
        internal CadRemoteTyped TypeScript(string text, bool continuing)
        {
            JsonObject answer = Ask(RemoteOps.Command, request =>
                request.Set("text", text).Set("timeout", 0L).Set("strict", true).Set("continuing", continuing)
            );
            return new CadRemoteTyped(
                answer.GetBool("completed"),
                answer.GetArray("output").Select(o => o as string ?? string.Empty).ToList(),
                (int)answer.GetLong("skipped")
            );
        }

        /// <summary>Reads objects by their handles in one request: copies as they are now, those the drawing has, in the order asked.</summary>
        internal IReadOnlyList<DxfEntity> ObjectsOf(IEnumerable<string> handles)
        {
            var list = new JsonArray(handles.Select(h => (object)Resolve(h)));
            return Objects(Ask(RemoteOps.Get, request => request.Set("handles", list)));
        }

        /// <summary>Answers the object prompt of the command running in the app with objects of the drawing, as a pick does.</summary>
        internal CadRemoteFeed FeedSelection(IEnumerable<string> handles)
        {
            var list = new JsonArray(handles.Cast<object>());
            JsonObject answer = Ask(RemoteOps.FeedSelection, request => request.Set("handles", list));
            return new CadRemoteFeed(
                answer.GetBool("taken"),
                answer.GetBool("completed"),
                answer.GetArray("output").Select(o => o as string ?? string.Empty).ToList()
            );
        }
    }
}
