using System;
using ModelContextProtocol;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// The writers of a plan as one undo step that can be taken back whole. On a drawing file it is a group of edits of the
    /// engine (the tools' own groups nest inside it); on an app it is an UNDO group typed on the app's command line, with a
    /// snapshot of the drawing taken first for the way back.
    /// </summary>
    internal abstract class PlanTransaction
    {
        /// <summary>Opens the transaction on a drawing.</summary>
        /// <param name="drawing">The drawing the plan works on.</param>
        /// <returns>The transaction.</returns>
        /// <exception cref="McpException">Thrown when the app is busy with a command or will not take the group.</exception>
        public static PlanTransaction Open(IDrawingBackend drawing)
        {
            if (drawing is LocalBackend local)
            {
                return new FileTransaction(local.Document);
            }

            if (drawing is RemoteBackend remote)
            {
                return new AppTransaction(remote.Document);
            }

            throw new McpException("A plan cannot work on this kind of drawing.");
        }

        /// <summary>Gets whether the group is still open: a step that failed in the engine's own way takes the group back and closes it.</summary>
        public abstract bool IsOpen { get; }

        /// <summary>Closes the group: what the writers made is one undo step.</summary>
        public abstract void Commit();

        /// <summary>Takes back everything the writers made, so that the drawing is as it was when the transaction opened.</summary>
        public abstract void Rollback();

        private sealed class FileTransaction : PlanTransaction
        {
            private readonly CadDocument _document;

            public FileTransaction(CadDocument document)
            {
                _document = document;
                _document.BeginGroup("MCP plan");
            }

            public override bool IsOpen => _document.IsInGroup;

            public override void Commit() => _document.EndGroup();

            public override void Rollback()
            {
                // A step of a tool that cancelled its own group has taken the whole plan back already, and closed it.
                if (_document.IsInGroup)
                {
                    _document.CancelGroup();
                }
            }
        }

        private sealed class AppTransaction : PlanTransaction
        {
            private readonly CadRemoteDocument _document;
            private readonly CadRemoteSnapshot _snapshot;

            public AppTransaction(CadRemoteDocument document)
            {
                _document = document;
                EnsureIdle("start");
                _snapshot = document.TakeSnapshot();

                try
                {
                    Type("UNDO BE");
                }
                catch
                {
                    document.Release(_snapshot);
                    throw;
                }
            }

            public override bool IsOpen => true;

            public override void Commit()
            {
                try
                {
                    EnsureIdle("end");
                    Type("UNDO End");
                }
                finally
                {
                    _document.Release(_snapshot);
                }
            }

            public override void Rollback()
            {
                try
                {
                    EnsureIdle("take back");
                    Type("UNDO End");
                    _document.Reset(_snapshot);
                }
                finally
                {
                    _document.Release(_snapshot);
                }
            }

            /// <summary>
            /// Refuses to go on while a command runs in the app: what is typed then would be an answer to that command, not a
            /// line of the UNDO group. CMDACTIVE is 0 when no command runs.
            /// </summary>
            private void EnsureIdle(string when)
            {
                if (
                    Convert.ToInt32(
                        _document.GetVariable("CMDACTIVE"),
                        System.Globalization.CultureInfo.InvariantCulture
                    ) != 0
                )
                {
                    throw new McpException(
                        "The app is busy with a command, so the plan cannot "
                            + when
                            + " its group of steps: end the command in the app (Esc), then run the plan again. Nothing was typed into the command."
                    );
                }
            }

            /// <summary>Types one of the two fixed lines of the UNDO group on the app's command line.</summary>
            private void Type(string line)
            {
                CadRemoteCommandResult result = _document.SendCommand(line, TimeSpan.FromSeconds(10));

                if (!result.Completed)
                {
                    throw new McpException(
                        "The app is busy with a command, so the plan cannot group its steps: end the command in the app, then run the plan again."
                    );
                }
            }
        }
    }
}
