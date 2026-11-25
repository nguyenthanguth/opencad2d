using System;
using System.Collections.Generic;
using System.Text;

namespace OpenCAD2D
{
    /// <summary>
    /// The macro of a ribbon button, as AutoCAD's CUI buttons run it: "^C^C_CIRCLE;0,0;\" cancels what runs, starts CIRCLE, gives
    /// it the centre 0,0 and waits for the radius the user types.
    /// </summary>
    public sealed partial class CadCommandSession
    {
        private enum MacroKind
        {
            /// <summary><c>^C</c>: Esc.</summary>
            Cancel,

            /// <summary><c>;</c>, a space, a tab or <c>^M</c> with nothing typed before it: Enter.</summary>
            Enter,

            /// <summary>Text typed, up to the <c>;</c> or space that ends it, or an expression of AutoLISP.</summary>
            Text,

            /// <summary><c>\</c>: the rest waits for the user's answer.</summary>
            Pause,
        }

        private struct MacroToken
        {
            public MacroToken(MacroKind kind, string text)
            {
                Kind = kind;
                Text = text;
            }

            public MacroKind Kind { get; }

            public string Text { get; }
        }

        // The macro paused at a backslash: its tokens, where it goes on, the command it waits at and how the window types a word;
        // null tokens for none. It goes on when that command asks its next prompt or ends, and is dropped when Esc or an error
        // ends the command. What the macro feeds itself does not resume it.
        private List<MacroToken> _macroTokens;
        private int _macroNext;
        private CadCommand _macroCommand;
        private Func<string, bool> _macroType;
        private bool _macroFeeding;

        /// <summary>
        /// Runs a macro: <c>^C</c> ends the command running (two in a row as one), <c>;</c>, a space, a tab and <c>^M</c> are Enter,
        /// <c>\</c> waits for the user, <c>(</c>...<c>)</c> is one expression of AutoLISP, <c>^P</c> is passed over, and a word
        /// is typed through <paramref name="type"/>, so that "_END" and "CTOP" are the snap overrides as typed. Text left at the
        /// end is typed as well. The macro stops, with "Macro stopped at abc.", at the first word <paramref name="type"/>
        /// refuses. One that uses <c>*</c> at its start, <c>$M=</c> or another <c>^</c> pair is refused before anything runs.
        /// A <c>\</c> keeps the rest until the command it waits at asks its next prompt or ends; Esc, an error or a command the
        /// user starts drops it.
        /// </summary>
        /// <param name="macro">The macro.</param>
        /// <param name="owner">The name of what the macro belongs to (a plugin), which the line of a refused macro starts with.</param>
        /// <param name="type">Types a word or, for an empty one, Enter, and says whether it was taken.</param>
        internal void RunMacro(string macro, string owner, Func<string, bool> type)
        {
            _macroTokens = null;

            if (!TryMacroTokens(macro, out List<MacroToken> tokens, out string refused))
            {
                Write(owner + ": the macro " + macro + " uses " + refused + ", which the presenter does not run.");
                return;
            }

            _macroType = type;
            ContinueMacro(tokens, 0);
        }

        private void ContinueMacro(List<MacroToken> tokens, int from)
        {
            _macroTokens = null;
            _macroFeeding = true;

            try
            {
                for (int i = from; i < tokens.Count; i++)
                {
                    MacroToken token = tokens[i];

                    switch (token.Kind)
                    {
                        case MacroKind.Cancel:
                            Escape();
                            break;
                        case MacroKind.Enter:
                            _macroType(string.Empty);
                            break;
                        case MacroKind.Pause:
                            if (!IsRunning)
                            {
                                Write("Macro stopped at \\.");
                                return;
                            }

                            if (i + 1 < tokens.Count)
                            {
                                _macroTokens = tokens;
                                _macroNext = i + 1;
                                _macroCommand = Current;
                            }

                            return;
                        default:
                            if (!_macroType(token.Text))
                            {
                                Write("Macro stopped at " + token.Text + ".");
                                return;
                            }

                            break;
                    }
                }
            }
            finally
            {
                _macroFeeding = false;
            }
        }

        /// <summary>The command a <c>\</c> waited at asked its next prompt: the macro goes on.</summary>
        private bool MacroWaitsAt(CadCommand command) =>
            _macroTokens != null && !_macroFeeding && ReferenceEquals(command, _macroCommand);

        private void ResumeMacro() => ContinueMacro(_macroTokens, _macroNext);

        /// <summary>The command a <c>\</c> waited at ended: the macro goes on where it was completed, and is dropped where Esc or an error ended it.</summary>
        private void MacroCommandEnded(CadCommand ended, CadCommandOutcome outcome)
        {
            if (!MacroWaitsAt(ended))
            {
                return;
            }

            if (outcome == CadCommandOutcome.Completed)
            {
                ResumeMacro();
            }
            else
            {
                _macroTokens = null;
            }
        }

        /// <summary>Reads a macro into its tokens, or the first of its words the presenter does not run.</summary>
        private static bool TryMacroTokens(string macro, out List<MacroToken> tokens, out string refused)
        {
            var made = new List<MacroToken>();
            tokens = made;
            refused = null;
            var word = new StringBuilder();
            int start = 0;

            while (start < macro.Length && char.IsWhiteSpace(macro[start]))
            {
                start++;
            }

            if (start < macro.Length && macro[start] == '*')
            {
                refused = "*";
                return false;
            }

            void EndWord()
            {
                if (word.Length > 0)
                {
                    made.Add(new MacroToken(MacroKind.Text, word.ToString()));
                    word.Clear();
                }
                else
                {
                    made.Add(new MacroToken(MacroKind.Enter, null));
                }
            }

            for (int i = 0; i < macro.Length; i++)
            {
                char c = macro[i];

                if (c == '^')
                {
                    char next = i + 1 < macro.Length ? char.ToUpperInvariant(macro[i + 1]) : '\0';

                    if (next != 'C' && next != 'M' && next != 'P')
                    {
                        refused = next == '\0' ? "^" : "^" + macro[i + 1];
                        return false;
                    }

                    i++;

                    if (next == 'C')
                    {
                        if (word.Length > 0)
                        {
                            made.Add(new MacroToken(MacroKind.Text, word.ToString()));
                            word.Clear();
                        }

                        if (made.Count == 0 || made[made.Count - 1].Kind != MacroKind.Cancel)
                        {
                            made.Add(new MacroToken(MacroKind.Cancel, null));
                        }
                    }
                    else if (next == 'M')
                    {
                        EndWord();
                    }
                }
                else if (c == ';' || c == ' ' || c == '\t')
                {
                    EndWord();
                }
                else if (c == '\\')
                {
                    if (word.Length > 0)
                    {
                        made.Add(new MacroToken(MacroKind.Text, word.ToString()));
                        word.Clear();
                    }

                    made.Add(new MacroToken(MacroKind.Pause, null));
                }
                else if (c == '(' && word.Length == 0)
                {
                    int end = EndOfExpression(macro, i);
                    made.Add(new MacroToken(MacroKind.Text, macro.Substring(i, end - i)));
                    i = end - 1;

                    // The blank after an expression only parts it from what follows.
                    if (end < macro.Length && macro[end] == ' ')
                    {
                        i = end;
                    }
                }
                else if (
                    c == '$'
                    && word.Length == 0
                    && string.Compare(macro, i, "$M=", 0, 3, StringComparison.OrdinalIgnoreCase) == 0
                )
                {
                    refused = "$M=";
                    return false;
                }
                else
                {
                    word.Append(c);
                }
            }

            if (word.Length > 0)
            {
                made.Add(new MacroToken(MacroKind.Text, word.ToString()));
            }

            return true;
        }

        /// <summary>Where the expression that starts at an opening parenthesis ends: after its closing one, parentheses inside strings left out; the end of the macro where it never closes.</summary>
        private static int EndOfExpression(string macro, int open)
        {
            int depth = 0;
            bool inString = false;

            for (int i = open; i < macro.Length; i++)
            {
                char c = macro[i];

                if (inString)
                {
                    if (c == '\\' && i + 1 < macro.Length)
                    {
                        i++;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                }
                else if (c == '"')
                {
                    inString = true;
                }
                else if (c == '(')
                {
                    depth++;
                }
                else if (c == ')' && --depth == 0)
                {
                    return i + 1;
                }
            }

            return macro.Length;
        }
    }
}
