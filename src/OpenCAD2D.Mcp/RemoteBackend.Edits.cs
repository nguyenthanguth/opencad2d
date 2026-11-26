using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GeometryHelper.Dxf;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Mcp
{
    /// <summary>The second phase's calls on a drawing of the app: the command line, the app's own edits, records, layers.</summary>
    internal sealed partial class RemoteBackend
    {
        /// <summary>The line that stands for the Esc key in a command sent to the app.</summary>
        private const string EscapeLine = "\u001b";

        /// <summary>
        /// Refuses before a tool types anything where a command already waits for the user's answer: what the tool typed would be
        /// answers to the user's command (a U at the next point of LINE takes the user's last segment back).
        /// </summary>
        private void RefuseWhileRunning()
        {
            if (Document.GetHistory(1).IsRunning)
            {
                throw new InvalidOperationException(
                    "A command is running in the app: end it (answer it or press Esc) before this tool types its own."
                );
            }
        }

        public string Run(CommandScript script)
        {
            RefuseWhileRunning();
            var output = new List<string>();
            var lines = new List<string>();
            bool completed = true;

            // The next input starts a command: the first of the script, and the first after an edit of the core.
            bool fresh = true;
            bool startsCommand = false;

            // The lines typed one after another go in one request; a pick or an edit is a request of its own, after them. The app
            // types none once the command has ended, so that a word left over never starts another command.
            void Flush()
            {
                if (lines.Count == 0)
                {
                    return;
                }

                CadRemoteTyped sent = Document.TypeScript(string.Join("\n", lines) + "\n", !startsCommand);
                lines.Clear();
                output.AddRange(sent.Output);
                completed = sent.Completed;

                if (sent.Skipped > 0)
                {
                    throw new InvalidOperationException(
                        "The command ended before it had all its inputs: it takes fewer than the tool sent. "
                            + Said(output)
                    );
                }
            }

            void Line(string text)
            {
                if (lines.Count == 0)
                {
                    startsCommand = fresh;
                }

                fresh = false;
                lines.Add(text);
            }

            try
            {
                foreach (ScriptStep step in script.Steps)
                {
                    switch (step.Kind)
                    {
                        case "word":
                        case "text":
                            Line(step.Text);
                            break;
                        case "enter":
                            Line(string.Empty);
                            break;
                        case "pick":
                            Flush();
                            CadRemoteFeed fed = Document.FeedSelection(step.Handles);
                            output.AddRange(fed.Output);
                            completed = fed.Completed;

                            if (!fed.Taken)
                            {
                                throw new InvalidOperationException(
                                    "The command did not take the objects. " + Said(output)
                                );
                            }

                            fresh = false;
                            break;
                        default:
                            Flush();
                            Document.Make(step.What, step.Args.Values);
                            fresh = true;
                            break;
                    }
                }

                Flush();

                if (!completed)
                {
                    string waiting = output.LastOrDefault() ?? string.Empty;
                    Document.SendCommand(EscapeLine + "\n", TimeSpan.Zero);
                    throw new InvalidOperationException(
                        "The command was still asking \"" + waiting + "\" and was cancelled. " + Said(output)
                    );
                }
            }
            catch
            {
                if (!completed)
                {
                    Document.SendCommand(EscapeLine + "\n", TimeSpan.Zero);
                }

                throw;
            }

            return string.Join("\n", output);
        }

        private static string Said(List<string> output) =>
            "The command line said: " + string.Join(" | ", output.Skip(Math.Max(0, output.Count - 6)));

        /// <summary>Types the lines of an UNDO option, as typed words with no command running: the option is not typed unless UNDO waits for it.</summary>
        private void Undoing(string option)
        {
            CadRemoteTyped sent = Document.TypeScript("UNDO\n" + option + "\n", false);

            if (sent.Skipped > 0 || !sent.Completed)
            {
                if (!sent.Completed)
                {
                    Document.SendCommand(EscapeLine + "\n", TimeSpan.Zero);
                }

                throw new InvalidOperationException("UNDO " + option + " was not taken. " + Said(sent.Output.ToList()));
            }
        }

        public void Group(Action work)
        {
            RefuseWhileRunning();
            Undoing("BE");

            bool failed = true;

            try
            {
                work();
                failed = false;
            }
            finally
            {
                try
                {
                    Undoing("E");
                }
                catch (InvalidOperationException) when (failed)
                {
                    // The work's own failure is what the tool says.
                }
            }
        }

        public IReadOnlyList<EntityRef> Objects(IReadOnlyList<string> handles) =>
            Document
                .ObjectsOf(handles)
                .Select(e => new EntityRef(Document.HandleOf(e), e))
                .Where(e => e.Handle != null)
                .ToList();

        public IReadOnlyList<string> Make(string what, CadMakeArgs args) => Document.Make(what, args.Values);

        public IReadOnlyList<string> Apply(
            DxfDatabase records,
            IReadOnlyList<DxfEntity> add,
            string layout,
            IReadOnlyList<string> erase
        )
        {
            if (records != null)
            {
                Document.AddRecords(records);
            }

            foreach (DxfEntity entity in add)
            {
                Document.Insert(entity, string.IsNullOrEmpty(layout) ? null : layout);
            }

            foreach (string handle in erase)
            {
                Document.Delete(handle);
            }

            return Document.CommitChanges();
        }

        public void ModifyLayer(LayerRow look)
        {
            CadRemoteLayer layer = Document.Layers.FirstOrDefault(l =>
                string.Equals(l.Name, look.Name, StringComparison.OrdinalIgnoreCase)
            );

            if (layer == null)
            {
                throw new ArgumentException("The drawing has no layer " + look.Name + ".");
            }

            // A colour the tool did not change stays as it is: a true colour is not an index.
            if (layer.Color.ColorIndex != look.Color)
            {
                layer.Color = DxfColor.FromColorIndex(DxfColorMethod.ByAci, (short)look.Color);
            }

            layer.Linetype = look.Linetype;
            layer.LineWeight = (DxfLineWeight)look.LineWeight;
            layer.IsOn = look.IsOn;
            layer.IsFrozen = look.IsFrozen;
            layer.IsLocked = look.IsLocked;
            layer.IsPlottable = look.IsPlottable;
            Document.ModifyLayer(layer);
            Document.CommitChanges();
        }

        public void SetVariable(string name, object value) => Document.SetVariable(name, value);

        public void SetCurrentLayer(string name) => Document.CurrentLayer = name;

        public void SetCurrentLayout(string name) => Document.CurrentLayout = name;

        public bool Redo()
        {
            RefuseWhileRunning();
            CadRemoteTyped sent = Document.TypeScript("REDO\n", false);
            return sent.Completed && TookBack(sent.Output, "Nothing to redo");
        }
    }
}
