using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>The edits a client asks the core to make in one call, and the objects it answers a command's prompt with.</summary>
    public sealed partial class CadRemoteServer
    {
        /// <summary>A line of a command that stands for the Esc key: it ends the command running.</summary>
        private const string EscapeLine = "\u001b";

        private JsonObject Make(CadDocument document, JsonObject request)
        {
            CadCommandSession session = _host.SessionOf(document);

            if (session != null && session.IsRunning)
            {
                throw new Refusal(
                    "The drawing "
                        + document.Title
                        + " is busy with the command "
                        + (session.Current?.Name ?? "running in it")
                        + ": end it, then make the edit again."
                );
            }

            var args = new CadMakeArgs();
            JsonObject given = request.GetObject("args");

            if (given != null)
            {
                foreach (KeyValuePair<string, object> member in given.Members)
                {
                    args.Set(member.Key, Plain(member.Value));
                }
            }

            try
            {
                IReadOnlyList<string> results = CadMake.Run(document, request.GetString("what"), args);
                return new JsonObject().Set("results", new JsonArray(results.Cast<object>()));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                throw new Refusal(ex.Message.Split('\n')[0]);
            }
        }

        /// <summary>A value of a request as the arguments of an edit carry it: text, number, bool, or a list or table of them.</summary>
        private static object Plain(object value)
        {
            switch (value)
            {
                case long whole:
                    return (double)whole;
                case JsonArray array when array.Count > 0 && array.All(i => i is JsonArray):
                    return array
                        .Select(i => ((JsonArray)i).Select(x => x as string ?? string.Empty).ToArray())
                        .ToArray();
                case JsonArray array when array.Count > 0 && array.All(i => i is long || i is double):
                    return array.Select(i => Convert.ToDouble(i, CultureInfo.InvariantCulture)).ToArray();
                case JsonArray array:
                    return array.Select(i => i as string ?? string.Empty).ToArray();
                default:
                    return value;
            }
        }

        private JsonObject FeedSelection(CadDocument document, JsonObject request)
        {
            if (!ReferenceEquals(document, _host.ActiveDocument))
            {
                _host.Activate(document);
            }

            CadCommandSession session = _host.SessionOf(document);
            int before = session.History.Count;
            bool taken = session.FeedSelection(Strings(request.GetArray("handles")).ToList());
            var output = new JsonArray();

            for (int i = before; i < session.History.Count; i++)
            {
                output.Add(session.History[i]);
            }

            if (session.IsRunning && session.PromptText.Length > 0)
            {
                output.Add(session.PromptText);
            }

            return new JsonObject().Set("taken", taken).Set("completed", !session.IsRunning).Set("output", output);
        }
    }
}
