using System;
using System.Collections.Generic;

namespace OpenCAD2D.Remote
{
    /// <summary>A drawing checked against a drawing standard, in the app.</summary>
    public sealed partial class CadRemoteDocument
    {
        /// <summary>
        /// Checks the drawing against a drawing standard and scores it, as the app does: a score for each rule the standard runs, a
        /// total and the violations with their places. The standard is the text of a file of the schema
        /// <c>docs/schemas/opencad2d-standard-v1.schema.json</c>.
        /// </summary>
        /// <param name="standard">The JSON text of the standard.</param>
        /// <param name="modelScale">The paper length in millimetres of one unit of model space where model space is checked on its own (0.01 for 1:100 in millimetres); 0 for none.</param>
        /// <param name="tolerance">The tolerance of the comparisons in millimetres on paper; 0.001 is the library's.</param>
        /// <returns>The scores and the violations.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the standard is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed, or the standard is not one the app reads.</exception>
        public CadRemoteStandardResult CheckStandard(string standard, double modelScale, double tolerance)
        {
            if (standard == null)
            {
                throw new ArgumentNullException(nameof(standard));
            }

            JsonObject answer = Ask(RemoteOps.CheckStandard, request =>
                request.Set("standard", standard).Set("modelScale", modelScale).Set("tolerance", tolerance)
            );
            var rules = new List<CadRemoteRuleScore>();

            foreach (object each in answer.GetArray("rules"))
            {
                var rule = (JsonObject)each;
                rules.Add(
                    new CadRemoteRuleScore(
                        rule.GetString("rule"),
                        rule.GetDouble("score"),
                        (int)rule.GetLong("subjects"),
                        (int)rule.GetLong("offenders"),
                        rule.GetDouble("weight")
                    )
                );
            }

            var violations = new List<CadRemoteViolation>();

            foreach (object each in answer.GetArray("violations"))
            {
                var violation = (JsonObject)each;
                violations.Add(
                    new CadRemoteViolation(
                        violation.GetString("rule"),
                        violation.GetString("handle"),
                        violation.GetString("other"),
                        violation.GetString("space"),
                        violation.GetString("through"),
                        violation.Get("box") is JsonArray box ? NumbersOf(box) : null,
                        violation.GetString("measured"),
                        violation.GetString("allowed"),
                        violation.GetDouble("value", double.NaN),
                        violation.GetDouble("limit", double.NaN)
                    )
                );
            }

            return new CadRemoteStandardResult(answer.GetString("name"), answer.GetDouble("total"), rules, violations);
        }
    }
}
