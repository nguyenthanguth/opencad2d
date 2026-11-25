using System;
using System.Globalization;

namespace OpenCAD2D
{
    /// <summary>One format code of an MTEXT's words, as <see cref="CadMTextCodes.TryRead"/> reads it.</summary>
    internal readonly struct CadMTextCode
    {
        internal CadMTextCode(char letter, int end, string value, double number, bool hasNumber, bool times)
        {
            Letter = letter;
            End = end;
            Value = value;
            Number = number;
            HasNumber = hasNumber;
            Times = times;
        }

        /// <summary>Gets the letter of the code: H, W, Q, T, C, c, A, f, F, S or p.</summary>
        internal char Letter { get; }

        /// <summary>Gets where the code ends: the index of the first character after it.</summary>
        internal int End { get; }

        /// <summary>Gets what f, F, S and p carry: the characters up to the semicolon that ends them, or to the end of the text; null for the others.</summary>
        internal string Value { get; }

        /// <summary>
        /// Gets the number of H, W, Q and T (a fraction as 2 1/2 worked out), or the whole number of C, c and A as AutoCAD takes
        /// it: C and A in the 16 low bits, c in the 32; 0 where there is none.
        /// </summary>
        internal double Number { get; }

        /// <summary>Gets whether the code carries a number (H, W, Q, T) or digits (C, c, A).</summary>
        internal bool HasNumber { get; }

        /// <summary>Gets whether an x follows the number of H, W, Q and T: its value is a factor of the one before.</summary>
        internal bool Times { get; }
    }

    /// <summary>
    /// Where an MTEXT's format codes end, as AutoCAD 2021 reads them (measured with accoreconsole, the MTEXT's size and its
    /// exploded pieces): the one tokenizer the content parser, the display, FIND and SPELL share, so that they cannot disagree.
    /// <list type="bullet">
    /// <item>H, W, Q, T: spaces, then a number (a sign, digits with a point, an exponent), an x, a semicolon, each of them
    /// optional and the semicolon not needed to end the number. An integer straight after the letter followed by a space, a
    /// hyphen or a slash is a fraction, <c>2 1/2</c>, <c>2-1/2</c> or <c>2/3</c>, its denominator not 0, or no number at all:
    /// the code is the letter alone, <c>\H2 B</c> shows 2 B. After spaces, or after a point or an exponent, no fraction is read.</item>
    /// <item>C, c, A: an optional minus sign, digits (none is 0) and an optional semicolon; nothing else belongs to them.</item>
    /// <item>f, F, S, p: everything up to the next semicolon, and to the end of the text where there is none.</item>
    /// </list>
    /// </summary>
    internal static class CadMTextCodes
    {
        /// <summary>Gets the letters of the codes <see cref="TryRead"/> reads.</summary>
        private const string Letters = "HWQTCcAfFSp";

        /// <summary>Reads the format code that starts at a back slash.</summary>
        /// <param name="text">The words of the MTEXT with their codes.</param>
        /// <param name="at">The index of the back slash.</param>
        /// <param name="code">The code.</param>
        /// <returns>True for one of H, W, Q, T, C, c, A, f, F, S and p; false for any other letter or no letter after the slash.</returns>
        internal static bool TryRead(string text, int at, out CadMTextCode code)
        {
            code = default;

            if (at < 0 || at + 1 >= text.Length || text[at] != '\\' || Letters.IndexOf(text[at + 1]) < 0)
            {
                return false;
            }

            char letter = text[at + 1];
            int i = at + 2;

            if (letter == 'f' || letter == 'F' || letter == 'S' || letter == 'p')
            {
                int semicolon = text.IndexOf(';', i);
                int end = semicolon < 0 ? text.Length : semicolon;
                code = new CadMTextCode(
                    letter,
                    semicolon < 0 ? end : end + 1,
                    text.Substring(i, end - i),
                    0.0,
                    false,
                    false
                );
                return true;
            }

            if (letter == 'C' || letter == 'c' || letter == 'A')
            {
                bool negative = i < text.Length && text[i] == '-';
                i += negative ? 1 : 0;
                int digits = i;
                long value = 0;

                while (i < text.Length && text[i] >= '0' && text[i] <= '9')
                {
                    value = unchecked((value * 10) + (text[i] - '0'));
                    i++;
                }

                value = unchecked(negative ? -value : value);
                double number = letter == 'c' ? unchecked((int)value) : unchecked((ushort)value);
                i += i < text.Length && text[i] == ';' ? 1 : 0;
                code = new CadMTextCode(letter, i, null, number, i > digits, false);
                return true;
            }

            return ReadNumber(text, at, letter, out code);
        }

        // H, W, Q and T. A rejected fraction is the letter alone, its number left as text.
        private static bool ReadNumber(string text, int at, char letter, out CadMTextCode code)
        {
            int start = at + 2;
            int i = start;
            double number = 0.0;
            bool hasNumber = false;
            int whole = i + (i < text.Length && (text[i] == '+' || text[i] == '-') ? 1 : 0);
            int wholeEnd = Digits(text, whole);
            bool isInteger =
                wholeEnd > whole
                && wholeEnd < text.Length
                && (text[wholeEnd] == ' ' || text[wholeEnd] == '-' || text[wholeEnd] == '/');

            if (isInteger)
            {
                if (!Fraction(text, start, whole, wholeEnd, out number, out i))
                {
                    code = new CadMTextCode(letter, start, null, 0.0, false, false);
                    return true;
                }

                hasNumber = true;
            }
            else
            {
                int spaces = start;

                while (spaces < text.Length && text[spaces] == ' ')
                {
                    spaces++;
                }

                int numberStart = spaces;
                int at1 = spaces + (spaces < text.Length && (text[spaces] == '+' || text[spaces] == '-') ? 1 : 0);
                int intEnd = Digits(text, at1);
                int end = intEnd;

                if (intEnd > at1)
                {
                    end += end < text.Length && text[end] == '.' ? 1 : 0;
                    end = Digits(text, end);
                }
                else if (at1 < text.Length && text[at1] == '.' && Digits(text, at1 + 1) > at1 + 1)
                {
                    end = Digits(text, at1 + 1);
                }

                if (end > at1)
                {
                    hasNumber = true;
                    int exponentEnd = end;

                    if (end < text.Length && (text[end] == 'e' || text[end] == 'E'))
                    {
                        int digits =
                            end + 1 + (end + 1 < text.Length && (text[end + 1] == '+' || text[end + 1] == '-') ? 1 : 0);
                        int digitsEnd = Digits(text, digits);
                        i = digitsEnd > digits ? digitsEnd : end + 1;
                        exponentEnd = digitsEnd > digits ? digitsEnd : end;
                    }
                    else
                    {
                        i = end;
                    }

                    double.TryParse(
                        text.Substring(numberStart, exponentEnd - numberStart),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out number
                    );
                }
                else
                {
                    i = start;
                }
            }

            bool times = i < text.Length && (text[i] == 'x' || text[i] == 'X');
            i += times ? 1 : 0;
            i += i < text.Length && text[i] == ';' ? 1 : 0;
            code = new CadMTextCode(letter, i, null, number, hasNumber, times);
            return true;
        }

        private static int Digits(string text, int at)
        {
            while (at < text.Length && text[at] >= '0' && text[at] <= '9')
            {
                at++;
            }

            return at;
        }

        // The fraction after an integer: /3, " 1/2" or "-1/2", the denominator not 0; false for anything else.
        private static bool Fraction(string text, int start, int whole, int wholeEnd, out double number, out int end)
        {
            number = 0.0;
            end = wholeEnd;
            double result;

            if (text[wholeEnd] == '/')
            {
                int denominator = Digits(text, wholeEnd + 1);

                if (denominator == wholeEnd + 1 || Value(text, wholeEnd + 1, denominator) == 0.0)
                {
                    return false;
                }

                result = Value(text, whole, wholeEnd) / Value(text, wholeEnd + 1, denominator);
                end = denominator;
            }
            else
            {
                int numerator = Digits(text, wholeEnd + 1);

                if (numerator == wholeEnd + 1 || numerator >= text.Length || text[numerator] != '/')
                {
                    return false;
                }

                int denominator = Digits(text, numerator + 1);

                if (denominator == numerator + 1 || Value(text, numerator + 1, denominator) == 0.0)
                {
                    return false;
                }

                result =
                    Value(text, whole, wholeEnd)
                    + (Value(text, wholeEnd + 1, numerator) / Value(text, numerator + 1, denominator));
                end = denominator;
            }

            number = (text[start] == '-' ? -1.0 : 1.0) * result;
            return true;
        }

        private static double Value(string text, int from, int to)
        {
            double.TryParse(
                text.Substring(from, to - from),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value
            );
            return value;
        }
    }
}
