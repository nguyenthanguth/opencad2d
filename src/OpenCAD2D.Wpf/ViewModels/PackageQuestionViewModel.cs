using System;
using System.Collections.Generic;

namespace OpenCAD2D.Wpf
{
    /// <summary>One line of the table of <see cref="PackageQuestionWindow"/>: a package, its version and a note.</summary>
    public sealed class PackageQuestionRow
    {
        /// <summary>Initializes a row.</summary>
        /// <param name="name">The package's name, as <c>Newtonsoft.Json</c>.</param>
        /// <param name="version">The version, as <c>13.0.3</c>.</param>
        /// <param name="isDirect">Whether the tool names the package itself, which the table shows in bold; false where another package needs it.</param>
        /// <param name="note">The third column: <c>download</c> or <c>in the cache</c> for NuGet.</param>
        /// <param name="isNoteHighlighted">Whether the note is drawn in the accent colour, as <c>download</c> is.</param>
        public PackageQuestionRow(string name, string version, bool isDirect, string note, bool isNoteHighlighted)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            IsDirect = isDirect;
            Note = note ?? string.Empty;
            IsNoteHighlighted = isNoteHighlighted;
        }

        /// <summary>Gets the package's name.</summary>
        public string Name { get; }

        /// <summary>Gets the version.</summary>
        public string Version { get; }

        /// <summary>Gets whether the tool names the package itself, which the table shows in bold.</summary>
        public bool IsDirect { get; }

        /// <summary>Gets the third column's text.</summary>
        public string Note { get; }

        /// <summary>Gets whether the note is drawn in the accent colour.</summary>
        public bool IsNoteHighlighted { get; }
    }

    /// <summary>
    /// What <see cref="PackageQuestionWindow"/> says: the question put before a tool's packages are downloaded or installed,
    /// as the PackageQuestion board draws it, with every text given by the caller so that one window asks about the NuGet
    /// packages of a C# plugin and, later, the pip packages of a Python tool. The table lists the packages the tool names
    /// first, in bold, then those they pull in.
    /// </summary>
    public sealed class PackageQuestionViewModel
    {
        /// <summary>Initializes the question.</summary>
        /// <param name="title">The title, as <c>Packages for Stairs</c>.</param>
        /// <param name="lead">The sentence above the table, which names the tool and its file.</param>
        /// <param name="thirdHead">The heading of the third column, as <c>State</c>.</param>
        /// <param name="rows">The packages, in the order to show them.</param>
        /// <param name="foot">The sentence below the table: what loading runs, and where the packages are kept.</param>
        /// <param name="source">Where the packages come from, shown at the bottom left, as <c>From nuget.org</c>.</param>
        /// <param name="noLabel">The label of the button that answers No.</param>
        /// <param name="yesLabel">The label of the button that answers Yes.</param>
        public PackageQuestionViewModel(
            string title,
            string lead,
            string thirdHead,
            IReadOnlyList<PackageQuestionRow> rows,
            string foot,
            string source,
            string noLabel,
            string yesLabel
        )
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Lead = lead ?? throw new ArgumentNullException(nameof(lead));
            ThirdHead = thirdHead ?? throw new ArgumentNullException(nameof(thirdHead));
            Rows = rows ?? throw new ArgumentNullException(nameof(rows));
            Foot = foot ?? throw new ArgumentNullException(nameof(foot));
            Source = source ?? throw new ArgumentNullException(nameof(source));
            NoLabel = noLabel ?? throw new ArgumentNullException(nameof(noLabel));
            YesLabel = yesLabel ?? throw new ArgumentNullException(nameof(yesLabel));
        }

        /// <summary>Gets the title.</summary>
        public string Title { get; }

        /// <summary>Gets the sentence above the table.</summary>
        public string Lead { get; }

        /// <summary>Gets the heading of the third column.</summary>
        public string ThirdHead { get; }

        /// <summary>Gets the packages, in the order shown.</summary>
        public IReadOnlyList<PackageQuestionRow> Rows { get; }

        /// <summary>Gets the sentence below the table.</summary>
        public string Foot { get; }

        /// <summary>Gets where the packages come from.</summary>
        public string Source { get; }

        /// <summary>Gets the label of the button that answers No.</summary>
        public string NoLabel { get; }

        /// <summary>Gets the label of the button that answers Yes.</summary>
        public string YesLabel { get; }
    }
}
