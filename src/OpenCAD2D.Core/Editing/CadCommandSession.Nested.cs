using System;

namespace OpenCAD2D
{
    /// <summary>
    /// A command that a host runs inside the command it holds open, as AutoLISP's <c>command</c> runs one inside a routine: the
    /// host's command waits at its waiting prompt (<see cref="CadPrompt.ForWaiting"/>), the nested command asks its prompts on the
    /// command line and ends, and the host's command is given back with nothing written (no "&gt;&gt;" in front of the prompts, no
    /// "Resuming" line). The nested command's edits join the undo step the host's command holds open, and Esc at it ends the
    /// host's command too, as Esc ends every command a transparent one interrupted.
    /// </summary>
    public sealed partial class CadCommandSession
    {
        /// <summary>Gets whether the command running was started inside the command a host holds (<see cref="BeginNested"/>).</summary>
        internal bool IsNested => _suspended.Count > 0 && _suspended.Peek().Nested;

        /// <summary>Gets how the last command nested in the host's command ended; <see cref="CadCommandOutcome.Completed"/> before any did.</summary>
        internal CadCommandOutcome NestedOutcome { get; private set; }

        /// <summary>
        /// Starts a command inside the command a host holds open: the host's command is put aside with its prompt, the command
        /// asks its own, and when it ends the host's command is back at its prompt. Another command is not nested inside a
        /// nested one, nor a transparent one inside the host's command at that time.
        /// </summary>
        /// <param name="host">The command the host holds, which must be the one running, at a prompt, with nothing nested in it.</param>
        /// <param name="command">The command to run inside it.</param>
        /// <returns>False where nothing was started: the host's command is not the one running, or something runs inside it already.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the host's command or the command is null.</exception>
        internal bool BeginNested(CadCommand host, CadCommand command)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (!ReferenceEquals(Current, host) || _body == null || Prompt == null || _suspended.Count > 0)
            {
                return false;
            }

            NestedOutcome = CadCommandOutcome.Completed;
            _suspended.Push(new Suspended(this) { Nested = true });
            Clear();
            ClearGathering();
            Current = command;
            _context = new CadCommandContext(this, command, Array.Empty<string>());
            RaiseCommandStarts(command);

            try
            {
                _body = command.Body(_context).GetEnumerator();
            }
            catch (Exception exception)
            {
                Fail(exception);
                return true;
            }

            Advance();
            return true;
        }

        /// <summary>
        /// Ends the command nested in the host's command as Esc does, but not the host's: the host asked for it, as when a command
        /// is left asking after the last answer it was given. Does nothing where no nested command runs.
        /// </summary>
        internal void EndNested()
        {
            if (IsNested)
            {
                _suspended.Peek().Abandoned = true;
                Escape();
            }
        }
    }
}
