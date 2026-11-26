using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// A small JSON writer for the exports: objects and arrays opened and closed, a comma put where one is wanted, numbers
    /// rounded to six decimals (a nanometre of a millimetre) and written the same on .NET Framework and .NET.
    /// </summary>
    internal sealed class CadJsonWriter
    {
        private readonly StringBuilder _text;
        private readonly Stack<bool> _first = new Stack<bool>();
        private bool _afterName;

        public CadJsonWriter(int capacity = 1024)
        {
            _text = new StringBuilder(capacity);
            _first.Push(true);
        }

        public override string ToString() => _text.ToString();

        /// <summary>Where the writer stands, to be given back to <see cref="Rewind"/>: the length of the text, the depth and the comma state of the open value.</summary>
        public struct Mark
        {
            internal int Length;
            internal int Depth;
            internal bool First;
            internal bool AfterName;
        }

        public Mark Here() =>
            new Mark
            {
                Length = _text.Length,
                Depth = _first.Count,
                First = _first.Peek(),
                AfterName = _afterName,
            };

        /// <summary>Takes back everything written since the mark, the objects and arrays opened since closed over.</summary>
        public void Rewind(Mark mark)
        {
            _text.Length = mark.Length;

            while (_first.Count > mark.Depth)
            {
                _first.Pop();
            }

            _first.Pop();
            _first.Push(mark.First);
            _afterName = mark.AfterName;
        }

        private void Separate()
        {
            if (_afterName)
            {
                _afterName = false;
                return;
            }

            if (_first.Peek())
            {
                _first.Pop();
                _first.Push(false);
            }
            else
            {
                _text.Append(',');
            }
        }

        public CadJsonWriter Begin()
        {
            Separate();
            _text.Append('{');
            _first.Push(true);
            return this;
        }

        public CadJsonWriter End()
        {
            _first.Pop();
            _text.Append('}');
            return this;
        }

        public CadJsonWriter BeginArray()
        {
            Separate();
            _text.Append('[');
            _first.Push(true);
            return this;
        }

        public CadJsonWriter EndArray()
        {
            _first.Pop();
            _text.Append(']');
            return this;
        }

        public CadJsonWriter Name(string name)
        {
            Separate();
            Quote(name);
            _text.Append(':');
            _afterName = true;
            return this;
        }

        public CadJsonWriter Value(string value)
        {
            Separate();

            if (value == null)
            {
                _text.Append("null");
            }
            else
            {
                Quote(value);
            }

            return this;
        }

        public CadJsonWriter Value(bool value)
        {
            Separate();
            _text.Append(value ? "true" : "false");
            return this;
        }

        public CadJsonWriter Value(int value)
        {
            Separate();
            _text.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public CadJsonWriter Value(double value)
        {
            Separate();
            Number(value);
            return this;
        }

        public CadJsonWriter Null()
        {
            Separate();
            _text.Append("null");
            return this;
        }

        /// <summary>A point of the plane, [x, y], in millimetres.</summary>
        public CadJsonWriter Point(GeoPoint3 point, double scale) => Pair(point.X * scale, point.Y * scale);

        /// <summary>A point of the plane, [x, y], in millimetres.</summary>
        public CadJsonWriter Point(GeoPoint2 point, double scale) => Pair(point.X * scale, point.Y * scale);

        /// <summary>A vector of the plane, [x, y], in millimetres.</summary>
        public CadJsonWriter Vector(GeoVector3 vector, double scale) => Pair(vector.X * scale, vector.Y * scale);

        public CadJsonWriter Pair(double x, double y)
        {
            Separate();
            _text.Append('[');
            Number(x);
            _text.Append(',');
            Number(y);
            _text.Append(']');
            return this;
        }

        /// <summary>A number rounded to six decimals; none for NaN or infinity, which JSON has no word for.</summary>
        private void Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                _text.Append("null");
                return;
            }

            double rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
            _text.Append(rounded == 0.0 ? "0" : rounded.ToString("0.######", CultureInfo.InvariantCulture));
        }

        private void Quote(string text)
        {
            _text.Append('"');

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                switch (c)
                {
                    case '"':
                        _text.Append("\\\"");
                        break;
                    case '\\':
                        _text.Append("\\\\");
                        break;
                    case '\n':
                        _text.Append("\\n");
                        break;
                    case '\r':
                        _text.Append("\\r");
                        break;
                    case '\t':
                        _text.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            _text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                        {
                            _text.Append(c).Append(text[++i]);
                        }
                        else if (char.IsSurrogate(c))
                        {
                            // A half of a pair cannot be written in UTF-8; it is written as its number.
                            _text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            _text.Append(c);
                        }

                        break;
                }
            }

            _text.Append('"');
        }
    }
}
