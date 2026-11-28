using System.Collections.Generic;

namespace OpenCAD2D.Remote
{
    /// <summary>
    /// A linetype of a drawing of a running OpenCAD2D, as <see cref="CadRemoteDocument.GetLinetypes"/> lists it: its name,
    /// description, pattern length and dash lengths in millimetres.
    /// </summary>
    public sealed class CadRemoteLinetype
    {
        internal static CadRemoteLinetype FromJson(JsonObject json)
        {
            var dashes = new List<double>();

            foreach (object item in json.GetArray("dashes"))
            {
                if (item is double real)
                {
                    dashes.Add(real);
                }
                else if (item is long whole)
                {
                    dashes.Add(whole);
                }
            }

            return new CadRemoteLinetype
            {
                Name = json.GetString("name") ?? string.Empty,
                Description = json.GetString("description"),
                PatternLength = json.GetDouble("patternLength"),
                Dashes = dashes,
            };
        }

        /// <summary>Gets the name of the linetype.</summary>
        public string Name { get; private set; }

        /// <summary>Gets the text description of the pattern, such as <c>Dash dot __ . __ . __</c>.</summary>
        public string Description { get; private set; }

        /// <summary>Gets the total length of one repeat of the pattern in millimetres; 0 for a continuous line.</summary>
        public double PatternLength { get; private set; }

        /// <summary>Gets the dash lengths of one repeat: a positive number is a dash, a negative a gap, 0 a dot.</summary>
        public IReadOnlyList<double> Dashes { get; private set; }
    }
}
