using System;
using System.Collections.Generic;

namespace OpenCAD2D
{
    /// <summary>
    /// How one rule of a standard fared (<see cref="CadStandardResult.Rules"/>): the score, from 0 to 1, and the numbers it is made
    /// of. The score is <c>1 - offenders / subjects</c>, and 1 where the rule found nothing to look at: a rule about the text
    /// heights of 40 words, 10 of them set at a height the standard does not list, scores 0.75.
    /// </summary>
    public sealed class CadRuleScore
    {
        internal CadRuleScore(string rule, double score, int subjects, int offenders, double weight)
        {
            Rule = rule;
            Score = score;
            Subjects = subjects;
            Offenders = offenders;
            Weight = weight;
        }

        /// <summary>Gets the id of the rule, one of <see cref="CadStandards.RuleIds"/>.</summary>
        public string Rule { get; }

        /// <summary>Gets the score, from 0 (every subject offends) to 1 (none does, or there was nothing to check).</summary>
        public double Score { get; }

        /// <summary>Gets how many things the rule looked at: objects, or layers and styles for the rules about the tables.</summary>
        public int Subjects { get; }

        /// <summary>Gets how many of them offend, each counted once however many violations it has.</summary>
        public int Offenders { get; }

        /// <summary>Gets the weight the standard gives the rule in the total score.</summary>
        public double Weight { get; }
    }

    /// <summary>One place a drawing breaks a rule of a standard: which rule, which objects, where, and the number found against the number allowed.</summary>
    public sealed class CadViolation
    {
        internal CadViolation(
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

        /// <summary>Gets the id of the rule broken (<see cref="CadStandards.RuleIds"/>).</summary>
        public string Rule { get; }

        /// <summary>Gets the handle of the object (or, for a rule about a layer or a style, of its record) that breaks the rule.</summary>
        public string Handle { get; }

        /// <summary>
        /// Gets the handle of the second object where the rule is about two (the line a word is too near, the dimension a chain
        /// follows); null where it is about one.
        /// </summary>
        public string Other { get; }

        /// <summary>Gets the name of the space the object is in: <c>Model</c>, a layout, or an empty text for a record of a table.</summary>
        public string Space { get; }

        /// <summary>
        /// Gets the handle of the viewport through which a model-space object was measured on paper, where it was; null where the
        /// object was measured in its own space.
        /// </summary>
        public string Through { get; }

        /// <summary>
        /// Gets the place, (min X, min Y, max X, max Y), in the coordinates of <see cref="Space"/>: the box of the object, or of the gap
        /// or the overlap for a rule about two. Null for a record of a table, which has no place.
        /// </summary>
        public double[] Box { get; }

        /// <summary>Gets what was found, as a sentence: <c>3.2 mm</c>, <c>WALL-OLD</c>, <c>color 1</c>.</summary>
        public string Measured { get; }

        /// <summary>Gets what the standard allows, as a sentence: <c>2.5, 3.5, 5, 7 mm</c>, <c>at least 0.5 mm</c>.</summary>
        public string Allowed { get; }

        /// <summary>Gets the number found where the rule is about a number (millimetres on paper; the offset of a dimension line from its chain), NaN where it is about a name.</summary>
        public double Value { get; }

        /// <summary>Gets the number allowed against <see cref="Value"/>: the nearest listed height, the least gap, the tolerance of a chain; NaN for a name.</summary>
        public double Limit { get; }
    }

    /// <summary>
    /// What <see cref="CadStandards.Check"/> found: a score for each rule the standard runs, the total, and every violation in an order
    /// that does not depend on how the drawing was walked (by rule, then handle, then the other handle, space, value and
    /// sentence).
    /// </summary>
    public sealed class CadStandardResult
    {
        internal CadStandardResult(
            string name,
            IReadOnlyList<CadRuleScore> rules,
            IReadOnlyList<CadViolation> violations
        )
        {
            Name = name;
            Rules = rules;
            Violations = violations;
            double weights = 0.0;
            double sum = 0.0;

            foreach (CadRuleScore rule in rules)
            {
                weights += rule.Weight;
                sum += rule.Weight * rule.Score;
            }

            Total = weights > 0.0 ? sum / weights : 1.0;
            Boxes = new double[4 * violations.Count];

            for (int i = 0; i < violations.Count; i++)
            {
                double[] box = violations[i].Box;

                for (int j = 0; j < 4; j++)
                {
                    Boxes[4 * i + j] = box == null ? double.NaN : box[j];
                }
            }
        }

        /// <summary>Gets the name of the standard that was checked.</summary>
        public string Name { get; }

        /// <summary>
        /// Gets the total score, from 0 to 1: the sum of each rule's weight times its score over the sum of the weights of the rules run;
        /// 1 where no rule ran or every weight is nought.
        /// </summary>
        public double Total { get; }

        /// <summary>Gets the score of each rule the standard runs, in the order of <see cref="CadStandards.RuleIds"/>.</summary>
        public IReadOnlyList<CadRuleScore> Rules { get; }

        /// <summary>Gets the violations, sorted.</summary>
        public IReadOnlyList<CadViolation> Violations { get; }

        /// <summary>
        /// Gets the places of the violations, four numbers each in the order of <see cref="Violations"/> (min X, min Y, max X, max Y);
        /// NaN for a violation with no place. The array is the result's own: read it, do not change it.
        /// </summary>
        public double[] Boxes { get; }

        /// <summary>Gets the score of a rule.</summary>
        /// <param name="rule">The id of the rule.</param>
        /// <returns>The score; 1 for a rule the standard does not run.</returns>
        /// <exception cref="ArgumentException">Thrown when there is no rule of the id.</exception>
        public double ScoreOf(string rule)
        {
            CadStandards.IndexOf(rule);

            foreach (CadRuleScore score in Rules)
            {
                if (score.Rule == rule)
                {
                    return score.Score;
                }
            }

            return 1.0;
        }
    }
}
