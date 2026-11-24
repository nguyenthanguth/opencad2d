using System;
using System.Threading;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>How a run of a tool's command ended.</summary>
    public enum CadRemoteToolRunOutcome
    {
        /// <summary>The tool ended it well: what it committed stays, as one undo step named after the command.</summary>
        Completed,

        /// <summary>The tool ended it with an error, or the command could not begin: what it committed is taken back.</summary>
        Failed,

        /// <summary>Esc, or the app, ended it: what it committed is taken back.</summary>
        Cancelled,
    }

    /// <summary>
    /// A run of a tool's command, the command the user typed in a drawing and a program outside the app works for (see
    /// <see cref="CadRemoteServer.CreateRunCommand"/>). It lives from the command's start to its end, whichever way the
    /// command ends; its members are read and called on the thread of the drawings.
    /// </summary>
    public sealed class CadRemoteToolRun
    {
        internal CadRemoteToolRun(string name, string token, CadCommandSession session, CadCommand command)
        {
            Name = name;
            Token = token;
            Session = session;
            Command = command;
            Document = session.Document;
        }

        /// <summary>Gets the number of the run, which the tool names in every request of it; from 1, the next one for each run of the server.</summary>
        public int Id { get; internal set; }

        /// <summary>Gets the name of the command, in capitals.</summary>
        public string Name { get; }

        /// <summary>Gets the drawing the command was typed in, which the run works in.</summary>
        public CadDocument Document { get; }

        /// <summary>Gets the number the tools channel gives that drawing, which a tool names in its requests of the drawing.</summary>
        public int DocumentId { get; internal set; }

        /// <summary>Gets whether the run has ended.</summary>
        public bool IsEnded => Outcome.HasValue;

        /// <summary>Gets how the run ended; null while it runs.</summary>
        public CadRemoteToolRunOutcome? Outcome { get; private set; }

        /// <summary>Gets the line of the error the tool ended the run with, or the one that kept it from beginning; null for any other end.</summary>
        public string Error { get; internal set; }

        /// <summary>
        /// Raised when the run has ended, its changes kept or taken back, on the thread of the drawings, as the command line ends
        /// the command: a handler may write a line, but starts no command.
        /// </summary>
        public event EventHandler Ended;

        /// <summary>
        /// Ends the run as Esc does, from the app's side: the tool process is gone or hangs. Its changes are taken back, the
        /// question open, if any, is answered as cancelled, and every later request of the run is refused. Does nothing once
        /// the run has ended.
        /// </summary>
        public void Cancel()
        {
            if (!IsEnded && Session.IsRunning)
            {
                Session.Escape();
            }
        }

        internal string Token { get; }

        internal CadCommandSession Session { get; }

        internal CadCommand Command { get; }

        /// <summary>The question the tool has asked and the user has not answered; null for none.</summary>
        internal RunQuestion Pending { get; set; }

        /// <summary>Whether the tool has ended the run, well or with an error, so that the body of the command is to leave.</summary>
        internal bool Ending { get; private set; }

        /// <summary>How the tool ended the run, once <see cref="Ending"/>.</summary>
        internal CadRemoteToolRunOutcome Intended { get; private set; }

        /// <summary>Marks that the tool ended the run, the body of the command to leave at its next step.</summary>
        internal void Finish(CadRemoteToolRunOutcome outcome)
        {
            Ending = true;
            Intended = outcome;
        }

        /// <summary>Records the end of the run and tells whoever listens.</summary>
        internal void Close(CadRemoteToolRunOutcome outcome)
        {
            Outcome = outcome;
            Ended?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>A question of the tool, asked on the command line of the drawing, and what the user answered.</summary>
        internal sealed class RunQuestion
        {
            public string Kind;
            public string Message;
            public GeoPoint2? Base;
            public string[] Keywords;
            public string Default;

            // Set by the body of the command, on the thread of the drawings, before Done is: the answer, or for a real number
            // asked the number the text came to.
            public CadInput Input;
            public double? Parsed;
            public bool Cancelled;

            // Whether it was cancelled because Esc (or the app) ended the run, not because the tool did.
            public bool ByEsc;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }
    }
}
