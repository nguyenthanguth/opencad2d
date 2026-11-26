using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>Reads the words a PDF written by the app's plotter shows, as a reader's search does: the glyph codes of its <c>Tj</c> strings through the <c>ToUnicode</c> maps of its fonts.</summary>
    internal static class PdfWords
    {
        public static string Of(byte[] pdf)
        {
            var codes = new Dictionary<string, string>();
            var pages = new List<string>();
            Encoding latin = Encoding.GetEncoding(28591);
            string text = latin.GetString(pdf);

            foreach (Match stream in Regex.Matches(text, "stream\r?\n(.*?)endstream", RegexOptions.Singleline))
            {
                byte[] bytes = latin.GetBytes(stream.Groups[1].Value);
                string inflated;

                try
                {
                    using (var from = new MemoryStream(bytes, 2, bytes.Length - 2))
                    using (var deflate = new DeflateStream(from, CompressionMode.Decompress))
                    using (var to = new MemoryStream())
                    {
                        deflate.CopyTo(to);
                        inflated = latin.GetString(to.ToArray());
                    }
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                if (inflated.Contains("begincmap"))
                {
                    foreach (
                        Match section in Regex.Matches(inflated, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline)
                    )
                    {
                        foreach (
                            Match pair in Regex.Matches(section.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>")
                        )
                        {
                            codes[pair.Groups[1].Value.ToUpperInvariant()] = (
                                (char)Convert.ToInt32(pair.Groups[2].Value, 16)
                            ).ToString();
                        }
                    }

                    foreach (
                        Match section in Regex.Matches(
                            inflated,
                            @"beginbfrange(.*?)endbfrange",
                            RegexOptions.Singleline
                        )
                    )
                    {
                        foreach (
                            Match range in Regex.Matches(
                                section.Groups[1].Value,
                                @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"
                            )
                        )
                        {
                            int low = Convert.ToInt32(range.Groups[1].Value, 16);
                            int high = Convert.ToInt32(range.Groups[2].Value, 16);
                            int first = Convert.ToInt32(range.Groups[3].Value, 16);

                            for (int code = low; code <= high; code++)
                            {
                                codes[code.ToString("X4")] = ((char)(first + code - low)).ToString();
                            }
                        }
                    }
                }
                else if (inflated.Contains(" Tj"))
                {
                    pages.Add(inflated);
                }
            }

            var words = new StringBuilder();

            foreach (string page in pages)
            {
                foreach (Match shown in Regex.Matches(page, @"<([0-9A-Fa-f]+)>\s*Tj"))
                {
                    string hex = shown.Groups[1].Value.ToUpperInvariant();

                    for (int i = 0; i + 4 <= hex.Length; i += 4)
                    {
                        words.Append(codes.TryGetValue(hex.Substring(i, 4), out string letter) ? letter : "?");
                    }
                }
            }

            return words.ToString();
        }
    }
}
