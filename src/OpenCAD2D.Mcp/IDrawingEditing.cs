using System;
using System.Collections.Generic;
using System.Globalization;
using GeometryHelper.Dxf;

namespace OpenCAD2D.Mcp
{
    /// <summary>One step of a <see cref="CommandScript"/>.</summary>
    internal sealed class ScriptStep
    {
        public ScriptStep(string kind, string text, IReadOnlyList<string> handles, string what, CadMakeArgs args)
        {
            Kind = kind;
            Text = text;
            Handles = handles;
            What = what;
            Args = args;
        }

        /// <summary>Gets the kind: <c>word</c>, <c>text</c>, <c>pick</c>, <c>enter</c> or <c>make</c>.</summary>
        public string Kind { get; }

        public string Text { get; }

        public IReadOnlyList<string> Handles { get; }

        public string What { get; }

        public CadMakeArgs Args { get; }
    }

    /// <summary>
    /// What a tool types on the command line of a drawing, built from numbers, handles and names the tool has checked: a command,
    /// its answers one at a time, the objects it asks for by handle, and (between commands) an edit of the core. No text of the
    /// caller's reaches a command line except as one answer to a prompt for words.
    /// </summary>
    internal sealed class CommandScript
    {
        private readonly List<ScriptStep> _steps = new List<ScriptStep>();

        public IReadOnlyList<ScriptStep> Steps => _steps;

        /// <summary>Starts a command, or answers a prompt with a word (a keyword, a name with no space, a number).</summary>
        public CommandScript Word(string word)
        {
            if (
                string.IsNullOrWhiteSpace(word)
                || word.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '"', ';', '\u001b' }) >= 0
            )
            {
                throw new ArgumentException(
                    "A word of a command is not empty and holds no space or quote: \"" + word + "\"."
                );
            }

            _steps.Add(new ScriptStep("word", word, null, null, null));
            return this;
        }

        /// <summary>Answers a prompt for words with a whole line, spaces and all.</summary>
        public CommandScript Text(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOfAny(new[] { '\r', '\n', '\u001b' }) >= 0)
            {
                throw new ArgumentException("The words are one line and not empty.");
            }

            _steps.Add(new ScriptStep("text", text, null, null, null));
            return this;
        }

        /// <summary>Answers with a number, written so that it reads back as it is.</summary>
        public CommandScript Number(double value) => Word(value.ToString("R", CultureInfo.InvariantCulture));

        /// <summary>Answers with a point, typed as <c>x,y</c>: where a prompt asks for objects it picks at that place.</summary>
        public CommandScript Point(double x, double y) =>
            Word(x.ToString("R", CultureInfo.InvariantCulture) + "," + y.ToString("R", CultureInfo.InvariantCulture));

        /// <summary>Answers an object prompt with objects of the drawing, by handle.</summary>
        public CommandScript Pick(IReadOnlyList<string> handles)
        {
            if (handles == null || handles.Count == 0)
            {
                throw new ArgumentException("Give at least one handle.");
            }

            _steps.Add(new ScriptStep("pick", null, handles, null, null));
            return this;
        }

        /// <summary>Presses Enter.</summary>
        public CommandScript Enter()
        {
            _steps.Add(new ScriptStep("enter", null, null, null, null));
            return this;
        }

        /// <summary>Makes an edit of the core (<see cref="CadMake"/>) between commands.</summary>
        public CommandScript Make(string what, CadMakeArgs args)
        {
            _steps.Add(new ScriptStep("make", null, null, what, args));
            return this;
        }
    }

    /// <summary>
    /// What a backend adds to <see cref="IDrawingBackend"/> for the tools of the second phase: a command script, an edit of the core,
    /// records and objects in one step, layer looks and redo. The two backends implement it beside the first interface.
    /// </summary>
    internal interface IDrawingEditing
    {
        /// <summary>
        /// Types the script on the command line of the drawing and gives what the command line wrote. A command still asking for
        /// more at the end is cancelled and refused with what it asked; a pick the prompt did not take is refused.
        /// </summary>
        string Run(CommandScript script);

        /// <summary>
        /// Runs work as one undo step, whatever commands and edits it makes. Where the work fails, a drawing file takes all of it
        /// back; a drawing of the app keeps what was made before the failure (undo takes it back).
        /// </summary>
        void Group(Action work);

        /// <summary>The objects of the handles the drawing has, whatever the layout, as the tools read them (a copy for the app).</summary>
        IReadOnlyList<EntityRef> Objects(IReadOnlyList<string> handles);

        /// <summary>Makes an edit of the core and gives what it returns.</summary>
        IReadOnlyList<string> Make(string what, CadMakeArgs args);

        /// <summary>
        /// As one undo step: brings in the text styles, linetypes, dimension styles and blocks of a small drawing that the drawing
        /// has not, adds objects to a layout, and erases objects. Gives the handles of the objects added, in order.
        /// </summary>
        IReadOnlyList<string> Apply(
            DxfDatabase records,
            IReadOnlyList<DxfEntity> add,
            string layout,
            IReadOnlyList<string> erase
        );

        /// <summary>Changes the look of a layer the drawing has, as one undo step.</summary>
        void ModifyLayer(LayerRow look);

        /// <summary>Sets a system variable of the drawing, such as <c>MLEADERSCALE</c>.</summary>
        void SetVariable(string name, object value);

        /// <summary>Makes a layer the current one.</summary>
        void SetCurrentLayer(string name);

        /// <summary>Shows a layout (or Model), making it the one new work goes to.</summary>
        void SetCurrentLayout(string name);

        /// <summary>Takes the last undone step forward again; false when there was none.</summary>
        bool Redo();
    }
}
