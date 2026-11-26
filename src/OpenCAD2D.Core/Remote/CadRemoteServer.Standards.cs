using System;
using GeometryHelper;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>A drawing checked against a drawing standard, asked from outside.</summary>
    public sealed partial class CadRemoteServer
    {
        /// <summary>Answers a <c>check</c>: the scores and the violations of <see cref="CadStandards.Check"/>.</summary>
        private static JsonObject CheckStandard(CadDocument document, JsonObject request)
        {
            CadStandardResult result;

            try
            {
                CadStandard standard = CadStandard.Parse(TextMember(request, "standard") ?? string.Empty);
                Tolerance tolerance = new Tolerance(
                    NumberMember(request, "tolerance", Tolerance.DefaultEqualPoint),
                    Tolerance.DefaultEqualVector
                );
                result = CadStandards.Check(document, standard, NumberMember(request, "modelScale", 0.0), tolerance);
            }
            catch (ArgumentException exception)
            {
                throw new Refusal(exception.Message.Split('\n')[0].TrimEnd('\r'));
            }

            var rules = new JsonArray();

            foreach (CadRuleScore rule in result.Rules)
            {
                rules.Add(
                    new JsonObject()
                        .Set("rule", rule.Rule)
                        .Set("score", rule.Score)
                        .Set("subjects", (long)rule.Subjects)
                        .Set("offenders", (long)rule.Offenders)
                        .Set("weight", rule.Weight)
                );
            }

            var violations = new JsonArray();

            foreach (CadViolation each in result.Violations)
            {
                JsonArray box = null;

                if (each.Box != null)
                {
                    box = Numbers(each.Box);
                }

                violations.Add(
                    new JsonObject()
                        .Set("rule", each.Rule)
                        .Set("handle", each.Handle)
                        .Set("other", each.Other)
                        .Set("space", each.Space)
                        .Set("through", each.Through)
                        .Set("box", box)
                        .Set("measured", each.Measured)
                        .Set("allowed", each.Allowed)
                        .Set("value", double.IsNaN(each.Value) ? null : (object)each.Value)
                        .Set("limit", double.IsNaN(each.Limit) ? null : (object)each.Limit)
                );
            }

            return new JsonObject()
                .Set("name", result.Name)
                .Set("total", result.Total)
                .Set("rules", rules)
                .Set("violations", violations);
        }
    }
}
