using System;
using System.Collections.Generic;

namespace OpenCAD2D.Remote
{
    /// <summary>How one rule of a standard fared in the app's check (<see cref="CadRemoteDocument.CheckStandard"/>).</summary>
    public sealed class CadRemoteRuleScore
    {
        internal CadRemoteRuleScore(string rule, double score, int subjects, int offenders, double weight)
        {
            Rule = rule;
            Score = score;
            Subjects = subjects;
            Offenders = offenders;
            Weight = weight;
        }

        /// <summary>Gets the id of the rule: <c>text-height</c>, <c>gap-word-line</c>, <c>layer-name</c> and the others.</summary>
        public string Rule { get; }

        /// <summary>Gets the score, from 0 (every subject offends) to 1 (none does, or there was nothing to check).</summary>
        public double Score { get; }

        /// <summary>Gets how many things the rule looked at.</summary>
        public int Subjects { get; }

        /// <summary>Gets how many of them offend.</summary>
        public int Offenders { get; }

        /// <summary>Gets the weight of the rule in the total score.</summary>
        public double Weight { get; }
    }

    /// <summary>One place a drawing breaks a rule of a standard, as the app found it.</summary>
    public sealed class CadRemoteViolation
    {
        internal CadRemoteViolation(
            string rule,
            string handle,
            string other,
            string space,
            string through,
            double[] box,
            string measured,
            string allowed,
            double value,
            double limit
        )
        {
            Rule = rule;
            Handle = handle;
            Other = other;
            Space = space;
            Through = through;
            Box = box;
            Measured = measured;
            Allowed = allowed;
            Value = value;
            Limit = limit;
        }

        /// <summary>Gets the id of the rule broken.</summary>
        public string Rule { get; }

        /// <summary>Gets the handle of the object, or of the record of the layer or style, that breaks the rule.</summary>
        public string Handle { get; }

        /// <summary>Gets the handle of the second object where the rule is about two; null where it is about one.</summary>
        public string Other { get; }

        /// <summary>Gets the name of the space the object is in; an empty text for a record of a table.</summary>
        public string Space { get; }

        /// <summary>Gets the handle of the viewport the object was measured through, or null.</summary>
        public string Through { get; }

        /// <summary>Gets the place (min X, min Y, max X, max Y), or null where the violation has none.</summary>
        public double[] Box { get; }

        /// <summary>Gets what was found, as a sentence.</summary>
        public string Measured { get; }

        /// <summary>Gets what the standard allows, as a sentence.</summary>
        public string Allowed { get; }

        /// <summary>Gets the number found, or NaN where the rule is about a name.</summary>
        public double Value { get; }

        /// <summary>Gets the number allowed against <see cref="Value"/>, or NaN.</summary>
        public double Limit { get; }
    }

    /// <summary>
    /// What the app found in a drawing checked against a standard (<see cref="CadRemoteDocument.CheckStandard"/>): the scores, the total
    /// and the violations, as the app's own check gives them.
    /// </summary>
    public sealed class CadRemoteStandardResult
    {
        internal CadRemoteStandardResult(
            string name,
            double total,
            IReadOnlyList<CadRemoteRuleScore> rules,
            IReadOnlyList<CadRemoteViolation> violations
        )
        {
            Name = name;
            Total = total;
            Rules = rules;
            Violations = violations;
            Boxes = new double[4 * violations.Count];

            for (int i = 0; i < violations.Count; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    Boxes[4 * i + j] = violations[i].Box == null ? double.NaN : violations[i].Box[j];
                }
            }
        }

        /// <summary>Gets the name of the standard.</summary>
        public string Name { get; }

        /// <summary>Gets the total score, from 0 to 1.</summary>
        public double Total { get; }

        /// <summary>Gets the score of each rule the standard runs.</summary>
        public IReadOnlyList<CadRemoteRuleScore> Rules { get; }

        /// <summary>Gets the violations, in the app's order.</summary>
        public IReadOnlyList<CadRemoteViolation> Violations { get; }

        /// <summary>Gets the places of the violations, four numbers each; NaN for a violation with no place.</summary>
        public double[] Boxes { get; }
    }
}
