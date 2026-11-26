using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Gives a PDF as SkiaSharp writes it an outline, the tree of bookmarks a reader shows beside the pages. It is done as a
    /// reader does it, by an incremental update appended to the file: the catalog gets a new object (a copy of the old with
    /// <c>/Outlines</c> and <c>/PageMode /UseOutlines</c> added), the outline's objects follow it, and a cross-reference
    /// section lists just these. The bytes SkiaSharp wrote are not touched, so every offset in its table stays true.
    /// </summary>
    internal static class CadPdfOutline
    {
        private static readonly Encoding Latin = Encoding.GetEncoding(28591);

        /// <summary>One bookmark with the number of the object it is written as and the nodes around it.</summary>
        private sealed class Node
        {
            public CadPdfBookmark Bookmark;
            public Node Parent;
            public int Number;
            public readonly List<Node> Kids = new List<Node>();

            /// <summary>The nodes under this one, at any depth.</summary>
            public int Descendants()
            {
                int count = 0;

                foreach (Node kid in Kids)
                {
                    count += 1 + kid.Descendants();
                }

                return count;
            }
        }

        /// <summary>
        /// The PDF with an outline of the bookmarks, each going to its page by the page's place in the page tree.
        /// </summary>
        /// <param name="pdf">The PDF, with a classic cross-reference table and a page tree, as SkiaSharp writes one.</param>
        /// <param name="bookmarks">The top bookmarks, each with those under it.</param>
        /// <param name="pageCount">The number of pages the PDF is to have.</param>
        /// <returns>The PDF and the update.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the PDF is not laid out as expected, or has not <paramref name="pageCount"/> pages.</exception>
        /// <exception cref="ArgumentException">Thrown when a bookmark goes to a page the PDF has not.</exception>
        internal static byte[] Add(byte[] pdf, IReadOnlyList<CadPdfBookmark> bookmarks, int pageCount)
        {
            string text = Latin.GetString(pdf);
            int startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
            int trailer = text.LastIndexOf("trailer", StringComparison.Ordinal);

            if (startxref < 0 || trailer < 0 || trailer > startxref)
            {
                throw new InvalidOperationException("The PDF has no cross-reference table to add an outline to.");
            }

            string trailerText = text.Substring(trailer + "trailer".Length, startxref - trailer - "trailer".Length);
            Match root = Regex.Match(trailerText, @"/Root (\d+) 0 R");
            Match size = Regex.Match(trailerText, @"/Size (\d+)");
            Match previous = Regex.Match(text.Substring(startxref), @"startxref\s+(\d+)");

            if (!root.Success || !size.Success || !previous.Success)
            {
                throw new InvalidOperationException("The PDF has no catalog to add an outline to.");
            }

            Dictionary<int, int> offsets = Offsets(text, Number(previous));
            string catalog = Body(text, offsets, Number(root));
            Match pages = Regex.Match(catalog, @"/Pages (\d+) 0 R");

            if (!pages.Success || !catalog.TrimEnd().EndsWith(">>", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The PDF's catalog has no page tree.");
            }

            var pageObjects = new List<int>();
            PageObjects(text, offsets, Number(pages), pageObjects);

            if (pageObjects.Count != pageCount)
            {
                throw new InvalidOperationException(
                    "The PDF has " + pageObjects.Count + " pages, not " + pageCount + "."
                );
            }

            // The new catalog is object first, the outline's root first + 1 and each bookmark the next numbers, parents first.
            int first = Number(size);
            int next = first + 2;
            var tops = new List<Node>();

            foreach (CadPdfBookmark bookmark in bookmarks)
            {
                tops.Add(Build(bookmark, null, ref next));
            }

            var bodies = new List<string>();
            string kept = catalog.TrimEnd();
            bodies.Add(
                kept.Substring(0, kept.Length - 2) + "/Outlines " + Reference(first + 1) + " /PageMode /UseOutlines >>"
            );
            int total = 0;

            foreach (Node top in tops)
            {
                total += 1 + top.Descendants();
            }

            bodies.Add(
                "<< /Type /Outlines /First "
                    + Reference(tops[0].Number)
                    + " /Last "
                    + Reference(tops[tops.Count - 1].Number)
                    + " /Count "
                    + total.ToString(CultureInfo.InvariantCulture)
                    + " >>"
            );
            Write(tops, first + 1, pageObjects, bodies);

            var added = new StringBuilder();
            var table = new StringBuilder();
            int at = pdf.Length;

            if (pdf[pdf.Length - 1] != (byte)'\n')
            {
                added.Append('\n');
            }

            for (int i = 0; i < bodies.Count; i++)
            {
                table.Append((at + added.Length).ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
                added
                    .Append((first + i).ToString(CultureInfo.InvariantCulture))
                    .Append(" 0 obj\n")
                    .Append(bodies[i])
                    .Append("\nendobj\n");
            }

            int xref = at + added.Length;
            string rest = Regex.Replace(trailerText.Trim(), @"/(Size|Prev) \d+|/Root \d+ 0 R", string.Empty);
            rest = rest.StartsWith("<<", StringComparison.Ordinal) ? rest.Substring(2) : rest;
            rest = rest.EndsWith(">>", StringComparison.Ordinal) ? rest.Substring(0, rest.Length - 2) : rest;
            added
                .Append("xref\n")
                .Append(first.ToString(CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(bodies.Count.ToString(CultureInfo.InvariantCulture))
                .Append('\n')
                .Append(table)
                .Append("trailer\n<< /Size ")
                .Append((first + bodies.Count).ToString(CultureInfo.InvariantCulture))
                .Append(" /Root ")
                .Append(Reference(first))
                .Append(' ')
                .Append(rest.Trim())
                .Append(" /Prev ")
                .Append(Number(previous).ToString(CultureInfo.InvariantCulture))
                .Append(" >>\nstartxref\n")
                .Append(xref.ToString(CultureInfo.InvariantCulture))
                .Append("\n%%EOF\n");

            byte[] update = Latin.GetBytes(added.ToString());
            var result = new byte[pdf.Length + update.Length];
            Buffer.BlockCopy(pdf, 0, result, 0, pdf.Length);
            Buffer.BlockCopy(update, 0, result, pdf.Length, update.Length);
            return result;
        }

        private static int Number(Match match) => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

        private static string Reference(int number) => number.ToString(CultureInfo.InvariantCulture) + " 0 R";

        private static Node Build(CadPdfBookmark bookmark, Node parent, ref int next)
        {
            var node = new Node
            {
                Bookmark = bookmark,
                Parent = parent,
                Number = next++,
            };

            foreach (CadPdfBookmark child in bookmark.Children)
            {
                node.Kids.Add(Build(child, node, ref next));
            }

            return node;
        }

        /// <summary>Writes the bodies of the nodes in the order of their numbers (the parent, then what is under it).</summary>
        private static void Write(
            IReadOnlyList<Node> siblings,
            int parent,
            IReadOnlyList<int> pageObjects,
            List<string> bodies
        )
        {
            for (int i = 0; i < siblings.Count; i++)
            {
                Node node = siblings[i];
                CadPdfBookmark bookmark = node.Bookmark;
                var body = new StringBuilder("<< /Title ").Append(Title(bookmark.Title));
                body.Append(" /Parent ").Append(Reference(parent));

                if (i > 0)
                {
                    body.Append(" /Prev ").Append(Reference(siblings[i - 1].Number));
                }

                if (i + 1 < siblings.Count)
                {
                    body.Append(" /Next ").Append(Reference(siblings[i + 1].Number));
                }

                if (node.Kids.Count > 0)
                {
                    body.Append(" /First ")
                        .Append(Reference(node.Kids[0].Number))
                        .Append(" /Last ")
                        .Append(Reference(node.Kids[node.Kids.Count - 1].Number))
                        .Append(" /Count ")
                        .Append(node.Descendants().ToString(CultureInfo.InvariantCulture));
                }

                if (bookmark.PageIndex >= 0)
                {
                    if (bookmark.PageIndex >= pageObjects.Count)
                    {
                        throw new ArgumentException(
                            "The bookmark \""
                                + bookmark.Title
                                + "\" goes to page "
                                + bookmark.PageIndex
                                + ", and the PDF has "
                                + pageObjects.Count
                                + ".",
                            nameof(siblings)
                        );
                    }

                    body.Append(" /Dest [").Append(Reference(pageObjects[bookmark.PageIndex])).Append(" /Fit]");
                }

                bodies.Add(body.Append(" >>").ToString());
                Write(node.Kids, node.Number, pageObjects, bodies);
            }
        }

        /// <summary>A title as a PDF text string: UTF-16, big endian, after its byte order mark, in hexadecimal.</summary>
        private static string Title(string title)
        {
            var hex = new StringBuilder("<FEFF");

            foreach (char letter in title)
            {
                hex.Append(((int)letter).ToString("X4", CultureInfo.InvariantCulture));
            }

            return hex.Append('>').ToString();
        }

        /// <summary>The offsets of the objects of the table at a place: each entry is 20 bytes, an offset of 10 digits, a generation of 5, n or f.</summary>
        private static Dictionary<int, int> Offsets(string text, int xref)
        {
            if (xref <= 0 || xref >= text.Length || string.CompareOrdinal(text, xref, "xref", 0, 4) != 0)
            {
                throw new InvalidOperationException("The PDF's cross-reference table is not where startxref says.");
            }

            var offsets = new Dictionary<int, int>();
            int at = text.IndexOf('\n', xref) + 1;

            while (at > 0 && string.CompareOrdinal(text, at, "trailer", 0, 7) != 0)
            {
                int lineEnd = text.IndexOf('\n', at);
                string[] head = text.Substring(at, lineEnd - at).Trim().Split(' ');

                if (
                    head.Length != 2
                    || !int.TryParse(head[0], NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                    || !int.TryParse(head[1], NumberStyles.None, CultureInfo.InvariantCulture, out int count)
                )
                {
                    throw new InvalidOperationException("The PDF's cross-reference table is not a classic table.");
                }

                at = lineEnd + 1;

                for (int i = 0; i < count; i++, at += 20, number++)
                {
                    if (text[at + 17] == 'n')
                    {
                        offsets[number] = int.Parse(
                            text.Substring(at, 10),
                            NumberStyles.None,
                            CultureInfo.InvariantCulture
                        );
                    }
                }
            }

            return offsets;
        }

        /// <summary>What an object holds between <c>obj</c> and <c>endobj</c>.</summary>
        private static string Body(string text, Dictionary<int, int> offsets, int number)
        {
            if (!offsets.TryGetValue(number, out int offset))
            {
                throw new InvalidOperationException("The PDF has no object " + number + ".");
            }

            int start = text.IndexOf("obj", offset, StringComparison.Ordinal) + 3;
            int end = text.IndexOf("endobj", start, StringComparison.Ordinal);
            return text.Substring(start, end - start).Trim();
        }

        /// <summary>The page objects under a node of the page tree, in the order of the pages.</summary>
        private static void PageObjects(string text, Dictionary<int, int> offsets, int node, List<int> pages)
        {
            string body = Body(text, offsets, node);
            Match kids = Regex.Match(body, @"/Kids\s*\[([^\]]*)\]");

            if (!Regex.IsMatch(body, @"/Type\s*/Pages\b") || !kids.Success)
            {
                pages.Add(node);
                return;
            }

            foreach (Match kid in Regex.Matches(kids.Groups[1].Value, @"(\d+) 0 R"))
            {
                PageObjects(text, offsets, Number(kid), pages);
            }
        }
    }
}
