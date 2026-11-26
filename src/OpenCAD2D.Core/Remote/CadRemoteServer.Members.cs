using System.Collections.Generic;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>
    /// The members of a request read strictly: one that is there and of the wrong kind is refused in words that name it, where
    /// the lenient <c>GetDouble</c> and <c>GetString</c> would take it for the fallback.
    /// </summary>
    public sealed partial class CadRemoteServer
    {
        /// <summary>A text member; null where the request has none, refused where it is not text.</summary>
        private static string TextMember(JsonObject request, string member)
        {
            object value = request.Get(member);
            return value == null || value is string
                ? (string)value
                : throw new Refusal("The member " + member + " has to be text.");
        }

        /// <summary>A number member, whole or not; the fallback where the request has none, refused where it is not a number.</summary>
        private static double NumberMember(JsonObject request, string member, double fallback)
        {
            object value = request.Get(member);
            return value == null ? fallback
                : value is double || value is long ? request.GetDouble(member)
                : throw new Refusal("The member " + member + " has to be a number.");
        }

        /// <summary>A list of names; null where the request has none, refused where it is not a list of text.</summary>
        private static List<string> NamesMember(JsonObject request, string member)
        {
            object value = request.Get(member);

            if (value == null)
            {
                return null;
            }

            var names = new List<string>();

            if (!(value is JsonArray items))
            {
                throw new Refusal("The member " + member + " has to be a list of text.");
            }

            foreach (object item in items)
            {
                names.Add(item as string ?? throw new Refusal("The member " + member + " has to be a list of text."));
            }

            return names;
        }

        /// <summary>The numbers of a list member of a given length; refused where it is no list, has another length, or holds what is no number.</summary>
        private static double[] NumbersMember(JsonObject request, string member, int count, string rule)
        {
            if (!(request.Get(member) is JsonArray items) || items.Count != count)
            {
                throw new Refusal(rule);
            }

            var numbers = new double[count];

            for (int i = 0; i < count; i++)
            {
                numbers[i] =
                    items[i] is double real ? real
                    : items[i] is long whole ? whole
                    : throw new Refusal(
                        "The member " + member + " has to be " + count + " numbers: item " + (i + 1) + " is not one."
                    );
            }

            return numbers;
        }
    }
}
