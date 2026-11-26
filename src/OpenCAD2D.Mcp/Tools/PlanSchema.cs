using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// What a plan checks before it runs anything: the arguments of a step against the JSON schema of its tool, and the
    /// references (<c>$id.path</c>) a step takes from the results of the steps before it.
    /// </summary>
    internal static class PlanSchema
    {
        /// <summary>A whole string that is a reference: <c>$id</c>, <c>$id.name</c>, <c>$id.list[0].name</c>, <c>$id.list.0</c>.</summary>
        private static readonly Regex Reference = new Regex(
            @"^\$(?<id>[A-Za-z_][A-Za-z0-9_-]*)(?<path>(\.[^.\[\]\s]+|\[[0-9]+\])*)$",
            RegexOptions.CultureInvariant
        );

        /// <summary>A string that starts like a reference, so that one that is not quite right is an error and not a text.</summary>
        private static readonly Regex Looks = new Regex(@"^\$[A-Za-z_]", RegexOptions.CultureInvariant);

        /// <summary>The ids a step may have.</summary>
        public static readonly Regex Id = new Regex(@"^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant);

        /// <summary>Reads a string as a reference.</summary>
        /// <param name="text">The string.</param>
        /// <param name="id">Gets the id of the step it names.</param>
        /// <param name="path">Gets the path into that step's result, with its leading dot or bracket; empty for the whole result.</param>
        /// <returns>True for a reference; false for a text, a <c>$$</c> text or a number such as <c>$5</c>.</returns>
        /// <exception cref="ArgumentException">Thrown for a string that starts like a reference and is not one.</exception>
        public static bool TryReference(string text, out string id, out string path)
        {
            id = null;
            path = null;

            if (text == null || text.Length < 2 || text[0] != '$' || !Looks.IsMatch(text))
            {
                return false;
            }

            Match match = Reference.Match(text);

            if (!match.Success)
            {
                throw new ArgumentException(
                    "\""
                        + text
                        + "\" starts like a reference but is not one: a reference is $id or $id.name or $id.list[0].name. Write $$ for a text that starts with a dollar."
                );
            }

            id = match.Groups["id"].Value;
            path = match.Groups["path"].Value;
            return true;
        }

        /// <summary>Whether a value is a string that is a reference (or starts like one: that is an error found when it is read).</summary>
        public static bool IsReference(JsonNode value) =>
            value is JsonValue leaf
            && leaf.GetValueKind() == JsonValueKind.String
            && leaf.GetValue<string>().Length > 1
            && leaf.GetValue<string>()[0] == '$'
            && Looks.IsMatch(leaf.GetValue<string>());

        /// <summary>
        /// Takes the value a path names out of a step's result: <c>.name</c> for a member, <c>[n]</c> or <c>.n</c> for the
        /// n-th of a list, counted from 0. A name is found as written, else ignoring case.
        /// </summary>
        /// <param name="result">The result of the step.</param>
        /// <param name="path">The path, as <see cref="TryReference"/> gives it.</param>
        /// <returns>A copy of the value.</returns>
        /// <exception cref="ArgumentException">Thrown where the result has no such member or item, naming what it has.</exception>
        public static JsonNode Follow(JsonNode result, string path)
        {
            JsonNode node = result;
            int i = 0;
            string walked = string.Empty;

            while (i < path.Length)
            {
                string name;
                int index = -1;

                if (path[i] == '[')
                {
                    int close = path.IndexOf(']', i);
                    index = int.Parse(path.Substring(i + 1, close - i - 1), CultureInfo.InvariantCulture);
                    name = path.Substring(i, close - i + 1);
                    i = close + 1;
                }
                else
                {
                    int next = path.IndexOfAny(new[] { '.', '[' }, i + 1);
                    next = next < 0 ? path.Length : next;
                    name = path.Substring(i + 1, next - i - 1);
                    i = next;

                    if (
                        node is JsonArray
                        && int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                    )
                    {
                        index = number;
                    }
                }

                if (index >= 0)
                {
                    if (!(node is JsonArray list))
                    {
                        throw new ArgumentException(
                            "the value "
                                + Where(walked)
                                + " is not a list, so it has no item "
                                + index
                                + Has(node)
                                + "."
                        );
                    }

                    if (index >= list.Count)
                    {
                        throw new ArgumentException(
                            "the list "
                                + Where(walked)
                                + " has "
                                + list.Count
                                + " items, so it has no item "
                                + index
                                + "."
                        );
                    }

                    node = list[index];
                    walked += "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                    continue;
                }

                if (!(node is JsonObject members))
                {
                    throw new ArgumentException(
                        "the value " + Where(walked) + " has no member " + name + Has(node) + "."
                    );
                }

                List<string> keys = members.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
                string key = keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.Ordinal))
                    ?? keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

                if (key == null)
                {
                    throw new ArgumentException(
                        "the result " + Where(walked) + " has no member " + name + Has(node) + "."
                    );
                }

                node = members[key];
                walked += "." + key;
            }

            return node?.DeepClone();
        }

        private static string Where(string walked) => walked.Length == 0 ? "of the step" : "at " + walked;

        /// <summary>What a value has, for the message of a path that found nothing.</summary>
        private static string Has(JsonNode node)
        {
            if (node is JsonObject members)
            {
                var keys = members.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
                return " (it has " + (keys.Count == 0 ? "no members" : string.Join(", ", keys)) + ")";
            }

            if (node is JsonArray list)
            {
                return " (it is a list of " + list.Count + ")";
            }

            return node == null
                ? " (it is null)"
                : " (it is " + node.GetValueKind().ToString().ToLowerInvariant() + ")";
        }

        /// <summary>
        /// Checks the arguments of a step against the input schema of its tool: the type of each value, the names that are
        /// required, the names the tool does not take, enums, items and the limits of numbers. A value that is a reference is
        /// not looked at, as it is not known until the plan runs.
        /// </summary>
        /// <param name="schema">The input schema of the tool.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>What is wrong, one text each, in the order of the arguments; none where they are right.</returns>
        public static List<string> Validate(JsonElement schema, JsonObject arguments)
        {
            var errors = new List<string>();
            Check(schema, arguments, string.Empty, errors);
            return errors;
        }

        private static void Check(JsonElement schema, JsonNode value, string path, List<string> errors)
        {
            if (schema.ValueKind != JsonValueKind.Object || IsReference(value) || schema.TryGetProperty("$ref", out _))
            {
                return;
            }

            if (
                TryAlternatives(schema, "anyOf", value, path, errors)
                || TryAlternatives(schema, "oneOf", value, path, errors)
            )
            {
                return;
            }

            if (schema.TryGetProperty("type", out JsonElement type) && !TypeMatches(type, value))
            {
                errors.Add(Name(path) + " is " + Describe(value) + " but the tool takes " + TypeWords(type) + ".");
                return;
            }

            if (schema.TryGetProperty("enum", out JsonElement allowed) && allowed.ValueKind == JsonValueKind.Array)
            {
                if (!allowed.EnumerateArray().Any(a => Same(a, value)))
                {
                    errors.Add(
                        Name(path)
                            + " is "
                            + (value?.ToJsonString() ?? "null")
                            + " but the tool takes one of "
                            + string.Join(", ", allowed.EnumerateArray().Select(a => a.GetRawText()))
                            + "."
                    );
                }
            }

            if (value is JsonObject members)
            {
                CheckMembers(schema, members, path, errors);
            }
            else if (value is JsonArray items)
            {
                CheckItems(schema, items, path, errors);
            }
            else if (value is JsonValue leaf && leaf.GetValueKind() == JsonValueKind.Number)
            {
                double number = leaf.GetValue<double>();

                if (schema.TryGetProperty("minimum", out JsonElement min) && number < min.GetDouble())
                {
                    errors.Add(
                        Name(path)
                            + " is "
                            + number.ToString(CultureInfo.InvariantCulture)
                            + ", below the least "
                            + min.GetRawText()
                            + "."
                    );
                }

                if (schema.TryGetProperty("maximum", out JsonElement max) && number > max.GetDouble())
                {
                    errors.Add(
                        Name(path)
                            + " is "
                            + number.ToString(CultureInfo.InvariantCulture)
                            + ", above the most "
                            + max.GetRawText()
                            + "."
                    );
                }
            }
        }

        private static void CheckMembers(JsonElement schema, JsonObject members, string path, List<string> errors)
        {
            bool has =
                schema.TryGetProperty("properties", out JsonElement properties)
                && properties.ValueKind == JsonValueKind.Object;

            if (
                schema.TryGetProperty("required", out JsonElement required)
                && required.ValueKind == JsonValueKind.Array
            )
            {
                foreach (string name in required.EnumerateArray().Select(r => r.GetString()))
                {
                    if (!members.ContainsKey(name))
                    {
                        errors.Add(Name(Join(path, name)) + " is required and is missing.");
                    }
                }
            }

            foreach (KeyValuePair<string, JsonNode> member in members)
            {
                if (has && properties.TryGetProperty(member.Key, out JsonElement known))
                {
                    Check(known, member.Value, Join(path, member.Key), errors);
                }
                else if (has && IsClosed(schema))
                {
                    errors.Add(
                        Name(Join(path, member.Key))
                            + " is not an argument of the tool, which takes "
                            + string.Join(
                                ", ",
                                properties.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
                            )
                            + "."
                    );
                }
            }
        }

        /// <summary>Whether a schema names every member it allows: the tools' schemas do, a schema that says nothing does not.</summary>
        private static bool IsClosed(JsonElement schema) =>
            !schema.TryGetProperty("additionalProperties", out JsonElement more)
            || more.ValueKind == JsonValueKind.False;

        private static void CheckItems(JsonElement schema, JsonArray items, string path, List<string> errors)
        {
            if (schema.TryGetProperty("minItems", out JsonElement least) && items.Count < least.GetInt32())
            {
                errors.Add(Name(path) + " has " + items.Count + " items but needs at least " + least.GetInt32() + ".");
            }

            if (schema.TryGetProperty("maxItems", out JsonElement most) && items.Count > most.GetInt32())
            {
                errors.Add(Name(path) + " has " + items.Count + " items but takes at most " + most.GetInt32() + ".");
            }

            if (schema.TryGetProperty("items", out JsonElement each))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    Check(each, items[i], path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", errors);
                }
            }
        }

        private static bool TryAlternatives(
            JsonElement schema,
            string word,
            JsonNode value,
            string path,
            List<string> errors
        )
        {
            if (
                !schema.TryGetProperty(word, out JsonElement alternatives)
                || alternatives.ValueKind != JsonValueKind.Array
            )
            {
                return false;
            }

            var found = new List<string>();

            foreach (JsonElement alternative in alternatives.EnumerateArray())
            {
                var mine = new List<string>();
                Check(alternative, value, path, mine);

                if (mine.Count == 0)
                {
                    return true;
                }

                found.AddRange(mine);
            }

            errors.Add(Name(path) + " fits none of the forms the tool takes: " + string.Join(" ", found.Take(2)));
            return true;
        }

        private static bool TypeMatches(JsonElement type, JsonNode value)
        {
            if (type.ValueKind == JsonValueKind.Array)
            {
                return type.EnumerateArray().Any(t => TypeMatches(t, value));
            }

            if (type.ValueKind != JsonValueKind.String)
            {
                return true;
            }

            JsonValueKind kind = value == null ? JsonValueKind.Null : value.GetValueKind();

            switch (type.GetString())
            {
                case "string":
                    return kind == JsonValueKind.String;
                case "number":
                    return kind == JsonValueKind.Number;
                case "integer":
                    if (kind != JsonValueKind.Number)
                    {
                        return false;
                    }

                    double number = value.GetValue<double>();
                    return Math.Abs(number - Math.Round(number)) < 1e-9;
                case "boolean":
                    return kind == JsonValueKind.True || kind == JsonValueKind.False;
                case "array":
                    return kind == JsonValueKind.Array;
                case "object":
                    return kind == JsonValueKind.Object;
                case "null":
                    return kind == JsonValueKind.Null;
                default:
                    return true;
            }
        }

        private static string TypeWords(JsonElement type) =>
            type.ValueKind == JsonValueKind.Array
                ? string.Join(" or ", type.EnumerateArray().Select(t => t.GetString()))
            : type.ValueKind == JsonValueKind.String ? type.GetString()
            : "another type";

        private static string Describe(JsonNode value)
        {
            if (value == null)
            {
                return "null";
            }

            switch (value.GetValueKind())
            {
                case JsonValueKind.String:
                    return "a string";
                case JsonValueKind.Number:
                    return "a number";
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return "a boolean";
                case JsonValueKind.Array:
                    return "a list";
                case JsonValueKind.Object:
                    return "an object";
                default:
                    return "null";
            }
        }

        private static bool Same(JsonElement allowed, JsonNode value)
        {
            if (value == null)
            {
                return allowed.ValueKind == JsonValueKind.Null;
            }

            switch (allowed.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetValueKind() == JsonValueKind.String
                        && string.Equals(allowed.GetString(), value.GetValue<string>(), StringComparison.Ordinal);
                case JsonValueKind.Number:
                    return value.GetValueKind() == JsonValueKind.Number
                        && Math.Abs(allowed.GetDouble() - value.GetValue<double>()) < 1e-12;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return value.GetValueKind() == allowed.ValueKind;
                default:
                    return false;
            }
        }

        private static string Join(string path, string name) => path.Length == 0 ? name : path + "." + name;

        private static string Name(string path) => path.Length == 0 ? "The arguments" : "The argument " + path;
    }
}
