using System;
using System.Collections.Generic;

namespace OpenCAD2D
{
    /// <summary>
    /// Checks a drawing against a company standard (<see cref="CadStandard"/>) and scores it, for a program that lays a drawing out
    /// and wants a number to climb. The rules read the same display lists the canvas draws, so what is measured is what is on the
    /// screen and on the paper: the height of a word is the height it is drawn at, times the scale of the viewport that shows
    /// it. A drawing of 10 000 objects is checked in well under a second after its display lists are made.
    /// <para>
    /// The rules, in the order the result lists them: <c>layer-name</c> (an object on a layer the standard does not allow),
    /// <c>layer-properties</c> (an allowed layer whose colour, linetype or lineweight differs from the standard's),
    /// <c>layer-zero</c> (an object on layer 0), <c>by-layer-color</c>, <c>by-layer-linetype</c>, <c>by-layer-lineweight</c> (an
    /// object with a look of its own), <c>text-style</c> and <c>dim-style</c> (an object in a style the standard does not allow),
    /// <c>text-font</c> and <c>dim-font</c> (an allowed style in another font), <c>text-height</c> (a word on paper at a height
    /// the standard does not list), <c>gap-word-line</c> and <c>gap-word-word</c> (a word nearer a line or another word than
    /// the standard allows) and <c>dim-chain</c> (dimensions of one chain whose dimension lines do not line up).
    /// </para>
    /// </summary>
    public static class CadStandards
    {
        private static readonly string[] Ids =
        {
            "layer-name",
            "layer-properties",
            "layer-zero",
            "by-layer-color",
            "by-layer-linetype",
            "by-layer-lineweight",
            "text-style",
            "text-font",
            "dim-style",
            "dim-font",
            "text-height",
            "gap-word-line",
            "gap-word-word",
            "dim-chain",
        };

        /// <summary>Gets the ids of the rules, in the order a result lists them.</summary>
        public static IReadOnlyList<string> RuleIds => Ids;

        internal static int IndexOf(string rule)
        {
            int index = Array.IndexOf(Ids, rule);

            if (index < 0)
            {
                throw new ArgumentException(
                    "There is no rule " + rule + "; the rules are " + string.Join(", ", Ids) + ".",
                    nameof(rule)
                );
            }

            return index;
        }

        /// <summary>
        /// Checks every space of a drawing against a standard. A rule scores 1 minus the share of the things it looked at that offend
        /// (see <see cref="CadRuleScore"/>); the total is the weighted mean of the rules the standard runs. Words are measured on
        /// paper: in a layout by the layout's own units, in model space through each viewport that shows them at the viewport's
        /// scale (annotative objects at the annotation scale the viewport draws them at); where no layout has a viewport, or a
        /// scale is given, model space is measured at that scale, a drawing of millimetres plotted 1:1 where none is given. A
        /// word seen through two viewports of two scales is checked at both and reported at the worse.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="standard">The standard.</param>
        /// <param name="modelScale">The paper length in millimetres of one unit of model space, where model space is checked on its own
        /// (0.01 for 1:100 in millimetres); 0 for none, which leaves model space to its viewports, or to 1:1 where it has none.</param>
        /// <param name="tolerance">The tolerance of the comparisons, in millimetres on paper (<see cref="GeometryHelper.Tolerance.EqualPoint"/>): a gap
        /// within it of the allowed one is not a violation. Pass the tolerance, never a global.</param>
        /// <returns>The scores and the violations.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document or the standard is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the model scale is below nought or not a number.</exception>
        public static CadStandardResult Check(
            CadDocument document,
            CadStandard standard,
            double modelScale,
            GeometryHelper.Tolerance tolerance
        )
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (standard == null)
            {
                throw new ArgumentNullException(nameof(standard));
            }

            if (!(modelScale >= 0.0) || double.IsInfinity(modelScale))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(modelScale),
                    "The model scale is a finite number of nought or more."
                );
            }

            return new CadStandardRun(document, standard, modelScale, Math.Max(tolerance.EqualPoint, 0.0)).Run();
        }
    }
}
