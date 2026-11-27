using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper.Dxf;

namespace OpenCAD2D
{
    /// <summary>The edits of <see cref="CadMake"/> that add and change multileader styles and table styles.</summary>
    public static partial class CadMake
    {
        /// <summary>
        /// Adds a multileader style, as New of the Multileader Style Manager does and then OK of its Modify dialog: a copy of the
        /// style <c>from</c> (the presenter's metric Standard where left out) with the settings given. One undo step.
        /// </summary>
        private static IReadOnlyList<string> AddMLeaderStyle(CadDocument document, CadMakeArgs args)
        {
            string name = Required(args, "name");
            return InOneStep(document, "MLEADERSTYLE", () =>
            {
                Check(CadMLeaderStyleEditor.Add(document, name, args.Text("from")));
                ChangeMLeaderStyle(document, name, args);
                return One(null, name.Trim());
            });
        }

        /// <summary>Changes the multileader style <c>name</c>: only the settings given, as Modify of the manager does. One undo step.</summary>
        private static IReadOnlyList<string> SetMLeaderStyle(CadDocument document, CadMakeArgs args)
        {
            string name = Required(args, "name");
            DxfMLeaderStyle found = CadMLeaderStyleEditor.Find(document.Database, name)
                ?? throw new InvalidOperationException("The drawing has no multileader style \"" + name + "\".");
            return InOneStep(document, "MLEADERSTYLE", () =>
            {
                ChangeMLeaderStyle(document, found.Name, args);
                return One(null, found.Name);
            });
        }

        private static void ChangeMLeaderStyle(CadDocument document, string name, CadMakeArgs args)
        {
            DxfMLeaderStyle style = CadMLeaderStyleEditor.Find(document.Database, name);
            CadMLeaderStyleSettings settings = CadMLeaderStyleSettings.Of(style);
            string textStyle = args.Text("text_style");
            string arrow = args.Text("arrow");

            if (!string.IsNullOrWhiteSpace(textStyle))
            {
                settings.TextStyle = textStyle.Trim();
            }

            if (!string.IsNullOrWhiteSpace(arrow))
            {
                settings.Arrowhead = CadArrowheads.Names.FirstOrDefault(n =>
                    string.Equals(n, arrow.Trim(), StringComparison.OrdinalIgnoreCase)
                ) ?? throw new ArgumentException("The arrowheads are " + string.Join(", ", CadArrowheads.Names) + ".");
            }

            settings.TextHeight = Above(args, "text_height") ?? settings.TextHeight;
            settings.ArrowSize = NotBelow(args, "arrow_size") ?? settings.ArrowSize;
            settings.LandingGap = NotBelow(args, "landing_gap") ?? settings.LandingGap;
            double? length = NotBelow(args, "landing_length");

            if (length.HasValue)
            {
                settings.LandingDistance = length.Value;
                settings.SetLandingDistance = true;
            }

            if (args.Values.TryGetValue("landing", out object landing) && landing is bool include)
            {
                settings.IncludeLanding = include;
            }

            double? scale = Above(args, "scale");
            bool annotative = args.Values.TryGetValue("annotative", out object kind) && kind is bool flag && flag;

            if (scale.HasValue && annotative)
            {
                throw new ArgumentException(
                    "A multileader style has a scale or is annotative, which takes its scale from the annotation scale."
                );
            }

            if (scale.HasValue)
            {
                settings.Scale = scale.Value;
                settings.Annotative = false;
            }
            else if (annotative)
            {
                settings.Annotative = true;
            }

            Check(CadMLeaderStyleEditor.Apply(document, name, settings));
        }

        /// <summary>
        /// Adds a table style, as New of the Table Style dialog does and then OK of its Modify dialog: a copy of the style
        /// <c>from</c> (AutoCAD's Standard where left out) with the settings given. One undo step.
        /// </summary>
        private static IReadOnlyList<string> AddTableStyle(CadDocument document, CadMakeArgs args)
        {
            string name = Required(args, "name");
            return InOneStep(document, "TABLESTYLE", () =>
            {
                Check(CadTableStyleEditor.Add(document, name, args.Text("from")));
                ChangeTableStyle(document, name, args);
                return One(null, name.Trim());
            });
        }

        /// <summary>Changes the table style <c>name</c>: only the settings given, as Modify of the dialog does. One undo step.</summary>
        private static IReadOnlyList<string> SetTableStyle(CadDocument document, CadMakeArgs args)
        {
            string name = Required(args, "name");
            DxfTableStyle found = CadTableStyleEditor.Find(document.Database, name)
                ?? throw new InvalidOperationException("The drawing has no table style \"" + name + "\".");
            return InOneStep(document, "TABLESTYLE", () =>
            {
                ChangeTableStyle(document, found.Name, args);
                return One(null, found.Name);
            });
        }

        private static void ChangeTableStyle(CadDocument document, string name, CadMakeArgs args)
        {
            CadTableStyleSettings settings = CadTableStyleSettings.Of(
                CadTableStyleEditor.Find(document.Database, name)
            );
            string textStyle = args.Text("text_style");
            string description = args.Text("description");

            if (description != null)
            {
                settings.Description = description;
            }

            settings.HorizontalMargin = NotBelow(args, "horizontal_margin") ?? settings.HorizontalMargin;
            settings.VerticalMargin = NotBelow(args, "vertical_margin") ?? settings.VerticalMargin;
            settings.Title.TextHeight = Above(args, "title_height") ?? settings.Title.TextHeight;
            settings.Header.TextHeight = Above(args, "header_height") ?? settings.Header.TextHeight;
            settings.Data.TextHeight = Above(args, "data_height") ?? settings.Data.TextHeight;

            if (!string.IsNullOrWhiteSpace(textStyle))
            {
                foreach (DxfRowType kind in CadTableStyleSettings.Kinds)
                {
                    settings.Cell(kind).TextStyle = textStyle.Trim();
                }
            }

            Check(CadTableStyleEditor.Apply(document, name, settings));
        }

        /// <summary>A number argument above nought; null where it is not given.</summary>
        private static double? Above(CadMakeArgs args, string name)
        {
            double? value = args.Number(name);

            if (value.HasValue && (!(value.Value > 0.0) || double.IsInfinity(value.Value)))
            {
                throw new ArgumentException(
                    name + " is a number above nought, not " + value.Value.ToString(CultureInfo.InvariantCulture) + "."
                );
            }

            return value;
        }

        /// <summary>A number argument of nought or more; null where it is not given.</summary>
        private static double? NotBelow(CadMakeArgs args, string name)
        {
            double? value = args.Number(name);

            if (value.HasValue && (!(value.Value >= 0.0) || double.IsInfinity(value.Value)))
            {
                throw new ArgumentException(
                    name
                        + " is a number of nought or more, not "
                        + value.Value.ToString(CultureInfo.InvariantCulture)
                        + "."
                );
            }

            return value;
        }

        /// <summary>Runs edits as one undo step; a refusal part way takes back what was made, as the caller's own edits stay.</summary>
        private static IReadOnlyList<string> InOneStep(
            CadDocument document,
            string name,
            Func<IReadOnlyList<string>> work
        )
        {
            bool own = !document.IsInGroup;
            CadGroupMark mark = own ? null : document.MarkGroup();
            document.BeginGroup(name);

            try
            {
                IReadOnlyList<string> made = work();
                document.EndGroup();
                return made;
            }
            catch
            {
                if (own)
                {
                    document.CancelGroup();
                }
                else
                {
                    document.BackTo(mark);
                    document.EndGroup();
                }

                throw;
            }
        }
    }
}
