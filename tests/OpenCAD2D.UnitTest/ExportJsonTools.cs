using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// A small JSON reader and a validator of the part of JSON Schema (draft 2020-12) that the schema of the drawing export
    /// uses, so that the test project needs no package: objects are <c>Dictionary&lt;string, object&gt;</c>, arrays
    /// <c>List&lt;object&gt;</c>, numbers <c>double</c>, and strings, booleans and null as they are.
    /// </summary>
    internal static class ExportJsonTools
    {
        /// <summary>Reads JSON text; throws <see cref="FormatException"/> for text that is none.</summary>
        public static object Parse(string text)
        {
            int at = 0;
            object value = Read(text, ref at);
            Skip(text, ref at);

            if (at != text.Length)
            {
                throw new FormatException("Text after the value at " + at + ".");
            }

            return value;
        }

        private static void Skip(string text, ref int at)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }
        }

        private static object Read(string text, ref int at)
        {
            Skip(text, ref at);

            if (at >= text.Length)
            {
                throw new FormatException("The text ends inside a value.");
            }

            char c = text[at];

            if (c == '{')
            {
                at++;
                var members = new Dictionary<string, object>();
                Skip(text, ref at);

                if (text[at] == '}')
                {
                    at++;
                    return members;
                }

                while (true)
                {
                    Skip(text, ref at);
                    string name = (string)Read(text, ref at);
                    Skip(text, ref at);
                    Expect(text, ref at, ':');
                    members[name] = Read(text, ref at);
                    Skip(text, ref at);

                    if (text[at] == ',')
                    {
                        at++;
                        continue;
                    }

                    Expect(text, ref at, '}');
                    return members;
                }
            }

            if (c == '[')
            {
                at++;
                var items = new List<object>();
                Skip(text, ref at);

                if (text[at] == ']')
                {
                    at++;
                    return items;
                }

                while (true)
                {
                    items.Add(Read(text, ref at));
                    Skip(text, ref at);

                    if (text[at] == ',')
                    {
                        at++;
                        continue;
                    }

                    Expect(text, ref at, ']');
                    return items;
                }
            }

            if (c == '"')
            {
                at++;
                var s = new StringBuilder();

                while (text[at] != '"')
                {
                    if (text[at] == '\\')
                    {
                        at++;
                        char e = text[at++];

                        switch (e)
                        {
                            case 'n':
                                s.Append('\n');
                                break;
                            case 'r':
                                s.Append('\r');
                                break;
                            case 't':
                                s.Append('\t');
                                break;
                            case 'u':
                                s.Append((char)int.Parse(text.Substring(at, 4), NumberStyles.HexNumber));
                                at += 4;
                                break;
                            default:
                                s.Append(e);
                                break;
                        }
                    }
                    else
                    {
                        s.Append(text[at++]);
                    }
                }

                at++;
                return s.ToString();
            }

            if (string.CompareOrdinal(text, at, "true", 0, 4) == 0)
            {
                at += 4;
                return true;
            }

            if (string.CompareOrdinal(text, at, "false", 0, 5) == 0)
            {
                at += 5;
                return false;
            }

            if (string.CompareOrdinal(text, at, "null", 0, 4) == 0)
            {
                at += 4;
                return null;
            }

            int start = at;

            while (at < text.Length && "+-0123456789.eE".IndexOf(text[at]) >= 0)
            {
                at++;
            }

            if (start == at)
            {
                throw new FormatException("A value was expected at " + at + ".");
            }

            return double.Parse(text.Substring(start, at - start), CultureInfo.InvariantCulture);
        }

        private static void Expect(string text, ref int at, char c)
        {
            if (at >= text.Length || text[at] != c)
            {
                throw new FormatException("'" + c + "' was expected at " + at + ".");
            }

            at++;
        }

        /// <summary>The members of an object read by <see cref="Parse"/>.</summary>
        public static Dictionary<string, object> Obj(object value) => (Dictionary<string, object>)value;

        /// <summary>The items of an array read by <see cref="Parse"/>.</summary>
        public static List<object> Arr(object value) => (List<object>)value;

        /// <summary>
        /// Validates a value against a schema, both read by <see cref="Parse"/>, and gives what is wrong with it, each as a path
        /// and a sentence; empty for a value that fits. Keywords: type, enum, const, properties, required, additionalProperties,
        /// items, minItems, maxItems, minimum, maximum, exclusiveMinimum, pattern, $ref to #/$defs, allOf, if/then, oneOf.
        /// </summary>
        public static List<string> Validate(object value, object schema)
        {
            var problems = new List<string>();
            Check(value, Obj(schema), Obj(schema), "$", problems);
            return problems;
        }

        private static string TypeOf(object value) =>
            value == null ? "null"
            : value is bool ? "boolean"
            : value is string ? "string"
            : value is double ? "number"
            : value is List<object> ? "array"
            : "object";

        private static bool IsType(object value, string type) =>
            TypeOf(value) == type
            || (type == "integer" && value is double d && Math.Abs(d - Math.Round(d)) < 1E-12)
            || (type == "number" && value is double);

        private static void Check(
            object value,
            Dictionary<string, object> schema,
            Dictionary<string, object> root,
            string path,
            List<string> problems
        )
        {
            if (schema.TryGetValue("$ref", out object reference))
            {
                string[] parts = ((string)reference).Split('/');
                object target = root;

                foreach (string part in parts.Skip(1))
                {
                    target = Obj(target)[part];
                }

                Check(value, Obj(target), root, path, problems);
            }

            if (schema.TryGetValue("type", out object type))
            {
                IEnumerable<string> types = type is List<object> list ? list.Cast<string>() : new[] { (string)type };

                if (!types.Any(t => IsType(value, t)))
                {
                    problems.Add(path + ": expected " + string.Join("|", types) + ", found " + TypeOf(value) + ".");
                    return;
                }
            }

            if (schema.TryGetValue("const", out object constant) && !Same(constant, value))
            {
                problems.Add(path + ": expected the value " + constant + ".");
            }

            if (schema.TryGetValue("enum", out object options) && !Arr(options).Any(o => Same(o, value)))
            {
                problems.Add(path + ": " + value + " is not one of the listed values.");
            }

            if (schema.TryGetValue("pattern", out object pattern) && value is string text)
            {
                if (!Regex.IsMatch(text, (string)pattern))
                {
                    problems.Add(path + ": '" + text + "' does not match " + pattern + ".");
                }
            }

            if (value is double number)
            {
                if (schema.TryGetValue("minimum", out object minimum) && number < (double)minimum)
                {
                    problems.Add(path + ": " + number + " is below " + minimum + ".");
                }

                if (schema.TryGetValue("maximum", out object maximum) && number > (double)maximum)
                {
                    problems.Add(path + ": " + number + " is above " + maximum + ".");
                }

                if (schema.TryGetValue("exclusiveMinimum", out object least) && number <= (double)least)
                {
                    problems.Add(path + ": " + number + " is not above " + least + ".");
                }
            }

            if (value is List<object> array)
            {
                if (schema.TryGetValue("minItems", out object minItems) && array.Count < (double)minItems)
                {
                    problems.Add(path + ": fewer than " + minItems + " items.");
                }

                if (schema.TryGetValue("maxItems", out object maxItems) && array.Count > (double)maxItems)
                {
                    problems.Add(path + ": more than " + maxItems + " items.");
                }

                if (schema.TryGetValue("items", out object items))
                {
                    for (int i = 0; i < array.Count; i++)
                    {
                        Check(array[i], Obj(items), root, path + "[" + i + "]", problems);
                    }
                }
            }

            if (value is Dictionary<string, object> members)
            {
                if (schema.TryGetValue("required", out object required))
                {
                    foreach (string name in Arr(required).Cast<string>().Where(n => !members.ContainsKey(n)))
                    {
                        problems.Add(path + ": the member '" + name + "' is missing.");
                    }
                }

                schema.TryGetValue("properties", out object properties);
                schema.TryGetValue("additionalProperties", out object additional);

                foreach (KeyValuePair<string, object> member in members)
                {
                    if (properties != null && Obj(properties).TryGetValue(member.Key, out object memberSchema))
                    {
                        Check(member.Value, Obj(memberSchema), root, path + "." + member.Key, problems);
                    }
                    else if (additional is bool allowed && !allowed)
                    {
                        problems.Add(path + ": the member '" + member.Key + "' is not in the schema.");
                    }
                    else if (additional is Dictionary<string, object> additionalSchema)
                    {
                        Check(member.Value, additionalSchema, root, path + "." + member.Key, problems);
                    }
                }
            }

            if (schema.TryGetValue("allOf", out object all))
            {
                foreach (object part in Arr(all))
                {
                    Check(value, Obj(part), root, path, problems);
                }
            }

            if (schema.TryGetValue("if", out object condition))
            {
                var test = new List<string>();
                Check(value, Obj(condition), root, path, test);

                if (test.Count == 0 && schema.TryGetValue("then", out object then))
                {
                    Check(value, Obj(then), root, path, problems);
                }
            }

            if (schema.TryGetValue("oneOf", out object one))
            {
                int fitting = 0;

                foreach (object option in Arr(one))
                {
                    var test = new List<string>();
                    Check(value, Obj(option), root, path, test);
                    fitting += test.Count == 0 ? 1 : 0;
                }

                if (fitting != 1)
                {
                    problems.Add(path + ": fits " + fitting + " of the alternatives, not one.");
                }
            }
        }

        private static bool Same(object a, object b) =>
            a == null ? b == null : b != null && a.GetType() == b.GetType() && a.Equals(b);
    }
}
