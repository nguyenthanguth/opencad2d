using System;
using System.Collections.Generic;
using System.Globalization;
using GeometryHelper.Geometry;

namespace OpenCAD2D.Remote
{
    /// <summary>What a tool asks the user inside a run: the kinds of prompt <see cref="CadRemoteRun.Ask"/> takes.</summary>
    public enum CadAskKind
    {
        /// <summary>A point, picked or typed as x,y; the answer is a <see cref="CadAnswerKind.Point"/>.</summary>
        Point,

        /// <summary>A distance, typed or measured from the base point to a point picked; the answer is a <see cref="CadAnswerKind.Number"/>.</summary>
        Distance,

        /// <summary>An angle, typed in degrees or the direction from the base point to a point picked; the answer is a <see cref="CadAnswerKind.Number"/> in radians.</summary>
        Angle,

        /// <summary>A whole number; the answer is a <see cref="CadAnswerKind.Number"/>.</summary>
        Integer,

        /// <summary>A number, asked again after text that is none; the answer is a <see cref="CadAnswerKind.Number"/>.</summary>
        Real,

        /// <summary>A text; the answer is a <see cref="CadAnswerKind.Text"/>.</summary>
        String,

        /// <summary>One of the keywords; the answer is a <see cref="CadAnswerKind.Keyword"/>.</summary>
        Keyword,

        /// <summary>One object, picked; the answer is a <see cref="CadAnswerKind.Selection"/> of one object.</summary>
        Entity,

        /// <summary>Objects, until Enter, those selected before the command taken at once where it uses the selection; the answer is a <see cref="CadAnswerKind.Selection"/>.</summary>
        Selection,
    }

    /// <summary>
    /// A run of a tool's command, as the tool sees it: the command the user typed in the app, which the app holds open (its
    /// command line says "&lt;name&gt; is working... (Esc to stop)") while the tool works. Everything the tool does on the
    /// connection that joined the run (<see cref="CadApplication.JoinRun"/>) is part of the command: its questions are asked
    /// on the command line, its commits are one undo step, and Esc ends it with every change taken back.
    /// </summary>
    /// <remarks>
    /// Once the run has ended, in the app by Esc or by <see cref="End"/> or <see cref="Fail"/>, the app refuses every
    /// request of it with a <see cref="CadRemoteException"/>, <see cref="CadRemoteException.IsCancelled"/> true for Esc.
    /// </remarks>
    public sealed class CadRemoteRun
    {
        private readonly CadApplication _app;

        internal CadRemoteRun(CadApplication app, int id)
        {
            _app = app;
            Id = id;
        }

        /// <summary>The answer of <see cref="RunCommand"/> that leaves one prompt of the command to the user: one backslash, as AutoLISP's <c>pause</c> is.</summary>
        public const string Pause = "\\";

        /// <summary>Gets the number of the run, as the app gave it.</summary>
        public int Id { get; }

        /// <summary>
        /// Asks the user and waits for the answer. The prompt appears on the command line of the drawing the run works in,
        /// the user answers it as any command's: clicking on the canvas, typing a point or a number, a keyword by its capital
        /// letters, or Enter. The app stays free meanwhile, for the user and for every other client.
        /// </summary>
        /// <param name="kind">What the prompt asks for.</param>
        /// <param name="message">What the command line says, such as <c>Specify the first step</c>; it is followed by a colon.</param>
        /// <param name="basePoint">The point a rubber band is drawn from and a distance or an angle is measured from; null for none.</param>
        /// <param name="keywords">The keywords offered, written as AutoCAD writes them (<c>Undo</c>, <c>mOde</c>); null for none, and required for <see cref="CadAskKind.Keyword"/>.</param>
        /// <param name="defaultValue">The default Enter takes, shown in angle brackets; null for none.</param>
        /// <returns>What the user answered.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the message is null.</exception>
        /// <exception cref="CadRemoteException">
        /// Thrown with <see cref="CadRemoteException.IsCancelled"/> true when the user pressed Esc; with it false when the run
        /// has ended, another question of it is still open, or the app is closing.
        /// </exception>
        public CadRemoteAnswer Ask(
            CadAskKind kind,
            string message,
            GeoPoint3? basePoint = null,
            IReadOnlyList<string> keywords = null,
            string defaultValue = null
        )
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            var request = new JsonObject()
                .Set("op", RemoteOps.Ask)
                .Set("kind", kind.ToString().ToLowerInvariant())
                .Set("message", message)
                .Set("default", defaultValue);

            if (basePoint.HasValue)
            {
                request.Set(
                    "base",
                    new JsonObject().Set("x", basePoint.Value.X).Set("y", basePoint.Value.Y).Set("z", basePoint.Value.Z)
                );
            }

            if (keywords != null && keywords.Count > 0)
            {
                var list = new JsonArray();

                foreach (string keyword in keywords)
                {
                    list.Add(keyword);
                }

                request.Set("keywords", list);
            }

            return CadRemoteAnswer.Read(_app, _app.Connection.Request(request));
        }

        /// <summary>Writes a line on the command line of the drawing the run works in, as a command's own messages are written.</summary>
        /// <param name="text">The line.</param>
        /// <exception cref="ArgumentNullException">Thrown when the text is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the run has ended.</exception>
        public void Write(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            _app.Connection.Request(new JsonObject().Set("op", RemoteOps.Write).Set("text", text));
        }

        /// <summary>
        /// Runs a command to its end inside the run, as AutoLISP's <c>command</c> does: it asks its prompts on the command line,
        /// each answered in turn by the answers given, its changes join the one undo step of the run, and nothing is written about
        /// the run being resumed. <see cref="Pause"/> leaves a prompt to the user, and the call waits for them; Esc then ends the
        /// run. A command that is still asking after the last answer is ended, and the call fails.
        /// </summary>
        /// <param name="name">The name or alias of the command, as typed at the command line.</param>
        /// <param name="answers">
        /// The answers, in order: a text is typed and an empty one is Enter; a whole or real number is typed; a
        /// <see cref="GeoPoint2"/> is a point picked; a list of handles answers "Select objects:" and ends it.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when the name or the answers are null.</exception>
        /// <exception cref="ArgumentException">Thrown when an answer is of a kind the call does not take.</exception>
        /// <exception cref="CadRemoteException">
        /// Thrown with <see cref="CadRemoteException.IsCancelled"/> true when the user pressed Esc; with it false when the app has no
        /// such command, the command did not take an answer or asked for more after the last, or the run has ended.
        /// </exception>
        public void RunCommand(string name, params object[] answers)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (answers == null)
            {
                throw new ArgumentNullException(nameof(answers));
            }

            var list = new JsonArray { name };

            foreach (object answer in answers)
            {
                switch (answer)
                {
                    case string text:
                        list.Add(text);
                        break;
                    case int number:
                        list.Add(number.ToString(CultureInfo.InvariantCulture));
                        break;
                    case long number:
                        list.Add(number.ToString(CultureInfo.InvariantCulture));
                        break;
                    case double number:
                        list.Add(number.ToString("R", CultureInfo.InvariantCulture));
                        break;
                    case GeoPoint2 point:
                        list.Add(new JsonObject().Set("x", point.X).Set("y", point.Y));
                        break;
                    case IEnumerable<string> handles:
                        list.Add(new JsonArray(handles));
                        break;
                    default:
                        throw new ArgumentException(
                            (answer?.GetType().Name ?? "null") + " cannot be fed to a command.",
                            nameof(answers)
                        );
                }
            }

            _app.Connection.Request(new JsonObject().Set("op", RemoteOps.Feed).Set("answers", list));
        }

        /// <summary>Ends the run well: the changes it committed stay, as one undo step named after the command. The connection leaves the run.</summary>
        /// <exception cref="CadRemoteException">Thrown when the run has ended already.</exception>
        public void End()
        {
            _app.Connection.Request(new JsonObject().Set("op", RemoteOps.End));
            _app.Connection.RunId = 0;
        }

        /// <summary>Ends the run that failed: the changes it committed are taken back, and the line is written on the command line. The connection leaves the run.</summary>
        /// <param name="message">The line that says what went wrong.</param>
        /// <exception cref="ArgumentNullException">Thrown when the message is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the run has ended already.</exception>
        public void Fail(string message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            _app.Connection.Request(new JsonObject().Set("op", RemoteOps.Fail).Set("message", message));
            _app.Connection.RunId = 0;
        }
    }
}
