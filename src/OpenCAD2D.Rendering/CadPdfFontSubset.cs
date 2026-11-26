using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Cuts the TrueType fonts of a PDF as SkiaSharp writes it down to the glyphs its pages show. SkiaSharp embeds each font
    /// whole, so a plot of 100 lines and one text in Arial was 543 KB, and one with Chinese text in Microsoft YaHei 20 MB; it
    /// already names the font as a subset (<c>/AAAAAA+ArialMT</c>) but does not cut it. The same plots are now about 10 KB.
    /// </summary>
    /// <remarks>
    /// The glyphs keep their numbers, so that the pages, the widths and the map to letters stay as SkiaSharp wrote them and
    /// the text is found and copied as before: a glyph the pages do not show is an empty outline, and the font is cut short
    /// after the last glyph that is kept. Of the tables only those a PDF needs are kept (head, hhea, maxp, loca, glyf, hmtx,
    /// cvt, fpgm, prep). Each font is given a tag of six capitals of its own, made from the glyphs it keeps. A font whose OS/2
    /// table forbids subsetting (fsType bit 8), one of CFF outlines, or any laid out otherwise than as expected stays whole.
    /// A font of a collection (.ttc: MS Gothic, SimSun, Microsoft YaHei) is cut from the face whose PostScript name the PDF's
    /// font carries, and written as a font of its own; where no face, or two, have the name it stays whole.
    /// A PDF not laid out as SkiaSharp writes one is given back as it is.
    /// </remarks>
    internal static class CadPdfFontSubset
    {
        private static readonly Encoding Latin = Encoding.GetEncoding(28591);

        /// <summary>One entry of the cross-reference table: the object it is for and where its line and its object lie.</summary>
        private sealed class Entry
        {
            public int Number;
            public int Line;
            public int Offset;
            public int End;
        }

        /// <summary>The objects that make up one font of the file: its Type0 font, its CID font, its descriptor and its file.</summary>
        private sealed class Face
        {
            public int Type0;
            public int CidFont;
            public int Descriptor;
            public int File;
            public int ToUnicode;
            public string Tag;
            public readonly SortedSet<int> Glyphs = new SortedSet<int> { 0 };
        }

        /// <summary>
        /// The PDF with each TrueType font cut to the glyphs the pages show, the cross-reference table made again for the bytes
        /// that moved.
        /// </summary>
        /// <param name="pdf">The PDF, with a classic cross-reference table, as SkiaSharp writes one.</param>
        /// <returns>The PDF as it is now; <paramref name="pdf"/> itself where there is no font to cut or the file is not laid out as expected.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the PDF is null.</exception>
        internal static byte[] Apply(byte[] pdf)
        {
            if (pdf == null)
            {
                throw new ArgumentNullException(nameof(pdf));
            }

            try
            {
                return Cut(pdf) ?? pdf;
            }
            catch (InvalidDataException)
            {
                return pdf;
            }
        }

        private static byte[] Cut(byte[] pdf)
        {
            string text = Latin.GetString(pdf);
            int startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);

            if (startxref < 0 || !Table(text, startxref, out int xref, out List<Entry> entries, out int numberEnd))
            {
                return null;
            }

            var bodies = new Dictionary<int, string>();

            foreach (Entry entry in entries)
            {
                bodies[entry.Number] = text.Substring(entry.Offset, entry.End - entry.Offset);
            }

            List<Face> faces = Faces(bodies);

            if (faces.Count == 0)
            {
                return null;
            }

            Dictionary<string, int> names = FontNames(bodies);

            if (names == null)
            {
                return null;
            }

            foreach (Face face in faces)
            {
                if (face.ToUnicode != 0 && Stream(bodies, face.ToUnicode, pdf, entries) is byte[] cmap)
                {
                    foreach (int code in Codes(Latin.GetString(cmap)))
                    {
                        face.Glyphs.Add(code);
                    }
                }
            }

            if (!Shown(bodies, entries, pdf, faces, names))
            {
                return null;
            }

            var replaced = new Dictionary<int, byte[]>();
            var tags = new HashSet<string>(StringComparer.Ordinal);

            foreach (Face face in faces)
            {
                byte[] file = Stream(bodies, face.File, pdf, entries);
                Match name = Regex.Match(bodies[face.Descriptor], @"/FontName /([A-Z]{6})\+(\S+)");
                byte[] part =
                    file == null ? null : TrueType(file, face.Glyphs.Select(g => (ushort)g), name.Groups[2].Value);

                if (part == null)
                {
                    continue;
                }

                face.Tag = TagOf(part, tags);
                Match was = name;

                foreach (int number in new[] { face.Type0, face.CidFont, face.Descriptor })
                {
                    replaced[number] = Latin.GetBytes(
                        bodies[number].Replace("/" + was.Groups[1].Value + "+", "/" + face.Tag + "+")
                    );
                }

                replaced[face.File] = FontObject(bodies[face.File], part);
            }

            if (replaced.Count == 0)
            {
                return null;
            }

            return Rebuilt(pdf, text, xref, startxref, numberEnd, entries, replaced);
        }

        /// <summary>The PDF with the objects in <paramref name="replaced"/> written as they are given, and the table of the offsets made again.</summary>
        private static byte[] Rebuilt(
            byte[] pdf,
            string text,
            int xref,
            int startxref,
            int numberEnd,
            List<Entry> entries,
            Dictionary<int, byte[]> replaced
        )
        {
            var table = new StringBuilder(text.Substring(xref, startxref - xref));
            var offsets = new Dictionary<int, int>();

            using (var output = new MemoryStream(pdf.Length))
            {
                output.Write(pdf, 0, entries[0].Offset);

                foreach (Entry entry in entries)
                {
                    offsets[entry.Number] = (int)output.Length;

                    if (replaced.TryGetValue(entry.Number, out byte[] bytes))
                    {
                        output.Write(bytes, 0, bytes.Length);
                    }
                    else
                    {
                        output.Write(pdf, entry.Offset, entry.End - entry.Offset);
                    }
                }

                foreach (Entry entry in entries)
                {
                    table
                        .Remove(entry.Line - xref, 10)
                        .Insert(entry.Line - xref, offsets[entry.Number].ToString("D10", CultureInfo.InvariantCulture));
                }

                int at = (int)output.Length;
                byte[] tail = Latin.GetBytes(
                    table.ToString()
                        + "startxref\n"
                        + at.ToString(CultureInfo.InvariantCulture)
                        + text.Substring(numberEnd)
                );
                output.Write(tail, 0, tail.Length);
                return output.ToArray();
            }
        }

        /// <summary>
        /// Reads the cross-reference table that <c>startxref</c> points at: one section, as SkiaSharp writes it, each entry 20
        /// bytes. The entries returned are those in use, in the order their objects lie in the file, each object ending where the
        /// next begins.
        /// </summary>
        private static bool Table(string text, int startxref, out int xref, out List<Entry> entries, out int numberEnd)
        {
            entries = new List<Entry>();
            xref = 0;
            numberEnd = 0;
            int numberStart = startxref + "startxref".Length;
            numberEnd = numberStart;

            while (numberEnd < text.Length && char.IsWhiteSpace(text[numberEnd]))
            {
                numberEnd++;
            }

            while (numberEnd < text.Length && char.IsDigit(text[numberEnd]))
            {
                numberEnd++;
            }

            if (
                !int.TryParse(
                    text.Substring(numberStart, numberEnd - numberStart).Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out xref
                )
                || xref <= 0
                || xref >= text.Length
                || string.CompareOrdinal(text, xref, "xref\n", 0, 5) != 0
            )
            {
                return false;
            }

            int at = xref + 5;
            int lineEnd = text.IndexOf('\n', at);
            string[] head = lineEnd < 0 ? new string[0] : text.Substring(at, lineEnd - at).Split(' ');

            if (
                head.Length != 2
                || !int.TryParse(head[0], NumberStyles.None, CultureInfo.InvariantCulture, out int first)
                || !int.TryParse(head[1], NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            )
            {
                return false;
            }

            at = lineEnd + 1;

            for (int i = 0; i < count; i++, at += 20)
            {
                if (
                    at + 20 > text.Length
                    || !int.TryParse(
                        text.Substring(at, 10),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int offset
                    )
                )
                {
                    return false;
                }

                if (text[at + 17] == 'n')
                {
                    entries.Add(
                        new Entry
                        {
                            Number = first + i,
                            Line = at,
                            Offset = offset,
                        }
                    );
                }
            }

            if (string.CompareOrdinal(text, at, "trailer", 0, 7) != 0 || entries.Count == 0)
            {
                return false;
            }

            entries.Sort((a, b) => a.Offset.CompareTo(b.Offset));

            for (int i = 0; i < entries.Count; i++)
            {
                entries[i].End = i + 1 < entries.Count ? entries[i + 1].Offset : xref;

                string opening = entries[i].Number.ToString(CultureInfo.InvariantCulture) + " 0 obj";

                if (
                    entries[i].Offset <= 0
                    || entries[i].End <= entries[i].Offset
                    || entries[i].End > xref
                    || string.CompareOrdinal(text, entries[i].Offset, opening, 0, opening.Length) != 0
                )
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The TrueType fonts of the file, each as the four objects SkiaSharp writes for it.</summary>
        private static List<Face> Faces(Dictionary<int, string> bodies)
        {
            var faces = new List<Face>();
            var descriptors = new Dictionary<int, int>();
            var cids = new Dictionary<int, int>();

            foreach (KeyValuePair<int, string> body in bodies.OrderBy(b => b.Key))
            {
                string text = body.Value;
                Match file = Regex.Match(text, @"/Type /FontDescriptor[^>]*?/FontFile2 (\d+) 0 R");

                if (file.Success && Regex.IsMatch(text, @"/FontName /[A-Z]{6}\+"))
                {
                    descriptors[body.Key] = int.Parse(file.Groups[1].Value, CultureInfo.InvariantCulture);
                }

                Match cid = Regex.Match(text, @"/FontDescriptor (\d+) 0 R");

                if (
                    cid.Success
                    && text.Contains("/Subtype /CIDFontType2")
                    && text.Contains("/CIDToGIDMap /Identity")
                    && text.Contains("/BaseFont /")
                )
                {
                    cids[body.Key] = int.Parse(cid.Groups[1].Value, CultureInfo.InvariantCulture);
                }
            }

            foreach (KeyValuePair<int, string> body in bodies.OrderBy(b => b.Key))
            {
                Match type0 = Regex.Match(body.Value, @"/DescendantFonts \[(\d+) 0 R\]");

                if (
                    !type0.Success
                    || !body.Value.Contains("/Subtype /Type0")
                    || !body.Value.Contains("/Encoding /Identity-H")
                    || !cids.TryGetValue(int.Parse(type0.Groups[1].Value, CultureInfo.InvariantCulture), out int d)
                    || !descriptors.TryGetValue(d, out int file)
                    || !bodies.ContainsKey(file)
                )
                {
                    continue;
                }

                Match unicode = Regex.Match(body.Value, @"/ToUnicode (\d+) 0 R");
                faces.Add(
                    new Face
                    {
                        Type0 = body.Key,
                        CidFont = int.Parse(type0.Groups[1].Value, CultureInfo.InvariantCulture),
                        Descriptor = d,
                        File = file,
                        ToUnicode = unicode.Success
                            ? int.Parse(unicode.Groups[1].Value, CultureInfo.InvariantCulture)
                            : 0,
                    }
                );
            }

            return faces;
        }

        /// <summary>The object each font resource name of the file stands for; null where one name stands for two objects.</summary>
        private static Dictionary<string, int> FontNames(Dictionary<int, string> bodies)
        {
            var names = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (KeyValuePair<int, string> body in bodies.OrderBy(b => b.Key))
            {
                foreach (
                    Match fonts in Regex.Matches(
                        dictionaryOf(body.Value),
                        @"/Font\s*<<(.*?)>>",
                        RegexOptions.Singleline
                    )
                )
                {
                    foreach (Match name in Regex.Matches(fonts.Groups[1].Value, @"/([^\s/<>\[\]()]+)\s+(\d+) 0 R"))
                    {
                        int number = int.Parse(name.Groups[2].Value, CultureInfo.InvariantCulture);

                        if (names.TryGetValue(name.Groups[1].Value, out int was) && was != number)
                        {
                            return null;
                        }

                        names[name.Groups[1].Value] = number;
                    }
                }
            }

            return names;

            string dictionaryOf(string body)
            {
                int stream = body.IndexOf("stream", StringComparison.Ordinal);
                return stream < 0 ? body : body.Substring(0, stream);
            }
        }

        /// <summary>
        /// Adds to each face the glyphs the content of the file shows in it: the strings of <c>Tj</c> and <c>TJ</c>, each of
        /// two-byte codes, shown in the font the last <c>Tf</c> before it sets. A string shown before any font is set is added to
        /// every face. False where a stream cannot be read or a string is not of two-byte codes.
        /// </summary>
        private static bool Shown(
            Dictionary<int, string> bodies,
            List<Entry> entries,
            byte[] pdf,
            List<Face> faces,
            Dictionary<string, int> names
        )
        {
            var byType0 = faces.ToDictionary(f => f.Type0);
            var token = new Regex(
                @"/([^\s/<>\[\]()]+)\s+[-+0-9.eE]+\s+Tf|<([0-9A-Fa-f\s]*)>\s*Tj|\[([^\]]*)\]\s*TJ",
                RegexOptions.CultureInvariant
            );

            foreach (Entry entry in entries)
            {
                string body = bodies[entry.Number];
                int streamAt = body.IndexOf("stream", StringComparison.Ordinal);

                if (streamAt < 0 || body.Contains("/Length1") || body.Contains("/Subtype /Image"))
                {
                    continue;
                }

                if (!body.Contains("/FlateDecode") && body.Contains("/Filter"))
                {
                    return false;
                }

                byte[] data = Stream(bodies, entry.Number, pdf, entries);

                if (data == null)
                {
                    return false;
                }

                string content = Latin.GetString(data);

                if (
                    content.IndexOf("Tf", StringComparison.Ordinal) < 0
                    && content.IndexOf("Tj", StringComparison.Ordinal) < 0
                    && content.IndexOf("TJ", StringComparison.Ordinal) < 0
                )
                {
                    continue;
                }

                Face current = null;
                bool set = false;

                foreach (Match match in token.Matches(content))
                {
                    if (match.Groups[1].Success)
                    {
                        set = true;
                        current =
                            names.TryGetValue(match.Groups[1].Value, out int number)
                            && byType0.TryGetValue(number, out Face face)
                                ? face
                                : null;
                        continue;
                    }

                    IEnumerable<string> strings = match.Groups[2].Success
                        ? new[] { match.Groups[2].Value }
                        : Regex
                            .Matches(match.Groups[3].Value, @"<([0-9A-Fa-f\s]*)>")
                            .Cast<Match>()
                            .Select(m => m.Groups[1].Value);

                    foreach (string hex in strings)
                    {
                        string digits = Regex.Replace(hex, @"\s+", string.Empty);

                        if (digits.Length % 4 != 0)
                        {
                            return false;
                        }

                        for (int i = 0; i < digits.Length; i += 4)
                        {
                            int glyph = int.Parse(
                                digits.Substring(i, 4),
                                NumberStyles.AllowHexSpecifier,
                                CultureInfo.InvariantCulture
                            );

                            if (!set)
                            {
                                foreach (Face every in faces)
                                {
                                    every.Glyphs.Add(glyph);
                                }
                            }
                            else
                            {
                                current?.Glyphs.Add(glyph);
                            }
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>The codes a ToUnicode map reads: the sources of its <c>bfchar</c> pairs and the whole of its <c>bfrange</c> ranges.</summary>
        private static IEnumerable<int> Codes(string cmap)
        {
            foreach (Match section in Regex.Matches(cmap, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
            {
                foreach (Match pair in Regex.Matches(section.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<[0-9A-Fa-f]+>"))
                {
                    yield return Convert.ToInt32(pair.Groups[1].Value, 16);
                }
            }

            foreach (Match section in Regex.Matches(cmap, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
            {
                foreach (Match range in Regex.Matches(section.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                {
                    int low = Convert.ToInt32(range.Groups[1].Value, 16);
                    int high = Math.Min(Convert.ToInt32(range.Groups[2].Value, 16), low + 0xFFFF);

                    for (int code = low; code <= high; code++)
                    {
                        yield return code;
                    }
                }
            }
        }

        /// <summary>The data of the stream of an object, inflated where its filter is FlateDecode; null where it cannot be read.</summary>
        private static byte[] Stream(Dictionary<int, string> bodies, int number, byte[] pdf, List<Entry> entries)
        {
            if (!bodies.TryGetValue(number, out string body))
            {
                return null;
            }

            int marker = body.IndexOf("stream", StringComparison.Ordinal);
            Match length = Regex.Match(body, @"/Length (\d+)(?! \d+ R)");

            if (marker < 0 || !length.Success || length.Index > marker)
            {
                return null;
            }

            int start = marker + "stream".Length;
            start += body[start] == '\r' ? 2 : 1;
            int size = int.Parse(length.Groups[1].Value, CultureInfo.InvariantCulture);
            Entry entry = entries.First(e => e.Number == number);

            if (start + size > body.Length)
            {
                return null;
            }

            byte[] data = new byte[size];
            Array.Copy(pdf, entry.Offset + start, data, 0, size);

            if (!body.Contains("/FlateDecode"))
            {
                return data;
            }

            if (data.Length < 3 || (data[0] & 0x0F) != 8)
            {
                return null;
            }

            using (var input = new MemoryStream(data, 2, data.Length - 2))
            using (var inflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                try
                {
                    inflate.CopyTo(output);
                }
                catch (InvalidDataException)
                {
                    return null;
                }

                return output.ToArray();
            }
        }

        /// <summary>The object of a font file with the data given, in the layout SkiaSharp writes it, the data deflated.</summary>
        private static byte[] FontObject(string was, byte[] font)
        {
            byte[] packed = Deflated(font);
            string head = Regex.Replace(
                Regex.Replace(
                    was.Substring(0, was.IndexOf("stream", StringComparison.Ordinal)),
                    @"/Length1 \d+",
                    "/Length1 " + font.Length.ToString(CultureInfo.InvariantCulture)
                ),
                @"/Length \d+",
                "/Length " + packed.Length.ToString(CultureInfo.InvariantCulture)
            );

            using (var output = new MemoryStream())
            {
                byte[] first = Latin.GetBytes(head + "stream\n");
                output.Write(first, 0, first.Length);
                output.Write(packed, 0, packed.Length);
                byte[] last = Latin.GetBytes("\nendstream\nendobj\n");
                output.Write(last, 0, last.Length);
                return output.ToArray();
            }
        }

        /// <summary>The data as a zlib stream: its two-byte header, the deflated bytes, and the Adler-32 sum.</summary>
        private static byte[] Deflated(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                output.WriteByte(0x78);
                output.WriteByte(0xDA);

                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
                {
                    deflate.Write(data, 0, data.Length);
                }

                uint a = 1;
                uint b = 0;

                foreach (byte value in data)
                {
                    a = (a + value) % 65521;
                    b = (b + a) % 65521;
                }

                uint adler = (b << 16) | a;
                output.WriteByte((byte)(adler >> 24));
                output.WriteByte((byte)(adler >> 16));
                output.WriteByte((byte)(adler >> 8));
                output.WriteByte((byte)adler);
                return output.ToArray();
            }
        }

        /// <summary>
        /// Six capitals made from the font that is kept, the tag of a subset; one not yet in <paramref name="taken"/>, which
        /// records it.
        /// </summary>
        private static string TagOf(byte[] font, HashSet<string> taken)
        {
            uint hash = 2166136261;

            foreach (byte value in font)
            {
                hash = (hash ^ value) * 16777619;
            }

            while (true)
            {
                var tag = new char[6];
                uint rest = hash % 308915776u;

                for (int i = 5; i >= 0; i--)
                {
                    tag[i] = (char)('A' + rest % 26);
                    rest /= 26;
                }

                string text = new string(tag);

                if (taken.Add(text))
                {
                    return text;
                }

                hash++;
            }
        }

        /// <summary>
        /// A TrueType font cut to the glyphs given, with those that the composite glyphs among them are made of. The glyphs keep
        /// their numbers: the others are empty, and the font ends after the last that is kept. Of a collection (a .ttc, as
        /// SkiaSharp embeds the faces of MS Gothic, SimSun and Microsoft YaHei) the face named is cut and written as a font of
        /// its own.
        /// </summary>
        /// <param name="font">The font file, of glyf outlines (<c>0x00010000</c> or <c>true</c>), or a collection of them (<c>ttcf</c>).</param>
        /// <param name="glyphs">The numbers of the glyphs to keep; glyph 0 is always kept, a number the font has not is ignored.</param>
        /// <param name="name">The PostScript name of the face to cut from a collection (<c>MicrosoftYaHei</c>); not looked at for a font that is not one.</param>
        /// <returns>The font cut; null where it is not a TrueType font of outlines, its OS/2 table forbids subsetting (fsType bit 8), it is a collection without exactly one face of the name, or it is malformed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the font or the glyphs are null.</exception>
        internal static byte[] TrueType(byte[] font, IEnumerable<ushort> glyphs, string name = null)
        {
            if (font == null)
            {
                throw new ArgumentNullException(nameof(font));
            }

            if (glyphs == null)
            {
                throw new ArgumentNullException(nameof(glyphs));
            }

            try
            {
                return Cut(font, glyphs, name);
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        /// <summary>The place of the table directory of the face a collection holds under a PostScript name; -1 where no face or more than one has it.</summary>
        private static int FaceOf(byte[] font, string name)
        {
            int found = -1;

            if (string.IsNullOrEmpty(name) || font.Length < 16)
            {
                return -1;
            }

            uint faces = U32(font, 8);

            for (int i = 0; i < faces && i < 64; i++)
            {
                int face = (int)U32(font, 12 + 4 * i);
                int count = U16(font, face + 4);

                for (int t = 0; t < count; t++)
                {
                    int record = face + 12 + 16 * t;

                    if (Latin.GetString(font, record, 4) != "name")
                    {
                        continue;
                    }

                    int table = (int)U32(font, record + 8);
                    int strings = table + U16(font, table + 4);
                    int records = U16(font, table + 2);

                    for (int r = 0; r < records; r++)
                    {
                        int at = table + 6 + 12 * r;

                        if (U16(font, at + 6) != 6)
                        {
                            continue;
                        }

                        int length = U16(font, at + 8);
                        int from = strings + U16(font, at + 10);

                        if (from < 0 || from + length > font.Length)
                        {
                            throw new InvalidDataException("A name lies beyond the font.");
                        }

                        string text = (U16(font, at) == 1 ? Latin : Encoding.BigEndianUnicode).GetString(
                            font,
                            from,
                            length
                        );

                        if (text.Replace(" ", string.Empty) == name)
                        {
                            if (found >= 0 && found != face)
                            {
                                return -1;
                            }

                            found = face;
                        }
                    }
                }
            }

            return found;
        }

        private static byte[] Cut(byte[] font, IEnumerable<ushort> wanted, string name)
        {
            if (font.Length < 12)
            {
                return null;
            }

            int directory = 0;

            if (U32(font, 0) == 0x74746366)
            {
                directory = FaceOf(font, name);

                if (directory < 0)
                {
                    return null;
                }
            }
            else if (U32(font, 0) != 0x00010000 && U32(font, 0) != 0x74727565)
            {
                return null;
            }

            var tables = new Dictionary<string, int[]>(StringComparer.Ordinal);
            int count = U16(font, directory + 4);

            for (int i = 0; i < count; i++)
            {
                int record = directory + 12 + 16 * i;
                long offset = U32(font, record + 8);
                long length = U32(font, record + 12);

                if (offset + length > font.Length)
                {
                    throw new InvalidDataException("A table lies beyond the font.");
                }

                tables[Latin.GetString(font, record, 4)] = new[] { (int)offset, (int)length };
            }

            foreach (string required in new[] { "head", "hhea", "maxp", "loca", "glyf", "hmtx" })
            {
                if (!tables.ContainsKey(required))
                {
                    return null;
                }
            }

            if (tables.TryGetValue("OS/2", out int[] os2) && os2[1] >= 10 && (U16(font, os2[0] + 8) & 0x0100) != 0)
            {
                return null;
            }

            int[] head = tables["head"];
            int[] hhea = tables["hhea"];
            int[] maxp = tables["maxp"];
            int[] loca = tables["loca"];
            int[] glyf = tables["glyf"];
            int[] hmtx = tables["hmtx"];

            if (head[1] < 54 || hhea[1] < 36 || maxp[1] < 6)
            {
                throw new InvalidDataException("A table is short.");
            }

            int format = U16(font, head[0] + 50);
            int numGlyphs = U16(font, maxp[0] + 4);
            int metrics = U16(font, hhea[0] + 34);

            if (
                format > 1
                || numGlyphs == 0
                || metrics == 0
                || metrics > numGlyphs
                || loca[1] < (numGlyphs + 1) * (format == 0 ? 2 : 4)
                || hmtx[1] < metrics * 4 + (numGlyphs - metrics) * 2
            )
            {
                throw new InvalidDataException("The font's tables do not agree.");
            }

            long Place(int glyph) => format == 0 ? U16(font, loca[0] + 2 * glyph) * 2L : U32(font, loca[0] + 4 * glyph);

            void Bounds(int glyph, out int start, out int end)
            {
                long from = Place(glyph);
                long to = Place(glyph + 1);

                if (from > to || to > glyf[1])
                {
                    throw new InvalidDataException("A glyph lies beyond the glyf table.");
                }

                start = (int)from;
                end = (int)to;
            }

            // The glyphs kept: those asked for, and the parts of each composite among them.
            var kept = new bool[numGlyphs];
            var pending = new Stack<int>();
            kept[0] = true;
            pending.Push(0);

            foreach (ushort glyph in wanted)
            {
                if (glyph < numGlyphs && !kept[glyph])
                {
                    kept[glyph] = true;
                    pending.Push(glyph);
                }
            }

            while (pending.Count > 0)
            {
                Bounds(pending.Pop(), out int start, out int end);

                if (end == start)
                {
                    continue;
                }

                if (end - start < 10)
                {
                    throw new InvalidDataException("A glyph is short.");
                }

                if ((short)U16(font, glyf[0] + start) >= 0)
                {
                    continue;
                }

                int at = start + 10;
                int flags;

                do
                {
                    if (at + 4 > end)
                    {
                        throw new InvalidDataException("A composite glyph ends early.");
                    }

                    flags = U16(font, glyf[0] + at);
                    int part = U16(font, glyf[0] + at + 2);

                    if (part >= numGlyphs)
                    {
                        throw new InvalidDataException("A composite glyph names a glyph the font has not.");
                    }

                    if (!kept[part])
                    {
                        kept[part] = true;
                        pending.Push(part);
                    }

                    at += 4 + ((flags & 0x0001) != 0 ? 4 : 2);
                    at +=
                        (flags & 0x0008) != 0 ? 2
                        : (flags & 0x0040) != 0 ? 4
                        : (flags & 0x0080) != 0 ? 8
                        : 0;
                } while ((flags & 0x0020) != 0);
            }

            int last = Array.LastIndexOf(kept, true);
            int newCount = last + 1;

            // glyf and loca (long form) of the glyphs kept, each on a four-byte boundary.
            var newGlyf = new MemoryStream();
            var newLoca = new byte[(newCount + 1) * 4];

            for (int g = 0; g < newCount; g++)
            {
                Put32(newLoca, 4 * g, (uint)newGlyf.Length);

                if (kept[g])
                {
                    Bounds(g, out int start, out int end);
                    newGlyf.Write(font, glyf[0] + start, end - start);

                    while (newGlyf.Length % 4 != 0)
                    {
                        newGlyf.WriteByte(0);
                    }
                }
            }

            Put32(newLoca, 4 * newCount, (uint)newGlyf.Length);

            // hmtx: the metrics of the glyphs kept, the others zero, the last long metric whole where glyphs follow it that
            // share its advance.
            int newMetrics = Math.Min(metrics, newCount);
            var newHmtx = new byte[newMetrics * 4 + (newCount - newMetrics) * 2];

            for (int g = 0; g < newMetrics; g++)
            {
                if (kept[g] || (g == newMetrics - 1 && newCount > newMetrics))
                {
                    Array.Copy(font, hmtx[0] + 4 * g, newHmtx, 4 * g, 4);
                }
            }

            for (int g = newMetrics; g < newCount; g++)
            {
                if (kept[g])
                {
                    Array.Copy(
                        font,
                        hmtx[0] + metrics * 4 + (g - metrics) * 2,
                        newHmtx,
                        newMetrics * 4 + (g - newMetrics) * 2,
                        2
                    );
                }
            }

            byte[] Copy(int[] table) => font.Skip(table[0]).Take(table[1]).ToArray();

            byte[] newHead = Copy(head);
            newHead[8] = newHead[9] = newHead[10] = newHead[11] = 0;
            newHead[50] = 0;
            newHead[51] = 1;
            byte[] newHhea = Copy(hhea);
            newHhea[34] = (byte)(newMetrics >> 8);
            newHhea[35] = (byte)newMetrics;
            byte[] newMaxp = Copy(maxp);
            newMaxp[4] = (byte)(newCount >> 8);
            newMaxp[5] = (byte)newCount;

            var parts = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["glyf"] = newGlyf.ToArray(),
                ["head"] = newHead,
                ["hhea"] = newHhea,
                ["hmtx"] = newHmtx,
                ["loca"] = newLoca,
                ["maxp"] = newMaxp,
            };

            foreach (string optional in new[] { "cvt ", "fpgm", "prep" })
            {
                if (tables.TryGetValue(optional, out int[] table))
                {
                    parts[optional] = Copy(table);
                }
            }

            return Assembled(parts);
        }

        /// <summary>A font file of the tables given: the directory, each table on a four-byte boundary, the checksums, the file's own in the head table.</summary>
        private static byte[] Assembled(SortedDictionary<string, byte[]> parts)
        {
            int n = parts.Count;
            int power = 1;
            int selector = 0;

            while (power * 2 <= n)
            {
                power *= 2;
                selector++;
            }

            int size = 12 + 16 * n;

            foreach (byte[] data in parts.Values)
            {
                size += (data.Length + 3) & ~3;
            }

            var file = new byte[size];
            Put32(file, 0, 0x00010000);
            file[4] = (byte)(n >> 8);
            file[5] = (byte)n;
            file[6] = (byte)((power * 16) >> 8);
            file[7] = (byte)(power * 16);
            file[8] = (byte)(selector >> 8);
            file[9] = (byte)selector;
            file[10] = (byte)((n * 16 - power * 16) >> 8);
            file[11] = (byte)(n * 16 - power * 16);

            int record = 12;
            int at = 12 + 16 * n;
            int headAt = 0;

            foreach (KeyValuePair<string, byte[]> part in parts)
            {
                Latin.GetBytes(part.Key).CopyTo(file, record);
                Put32(file, record + 4, Sum(part.Value));
                Put32(file, record + 8, (uint)at);
                Put32(file, record + 12, (uint)part.Value.Length);
                part.Value.CopyTo(file, at);

                if (part.Key == "head")
                {
                    headAt = at;
                }

                record += 16;
                at += (part.Value.Length + 3) & ~3;
            }

            Put32(file, headAt + 8, 0xB1B0AFBA - Sum(file));
            return file;
        }

        private static uint Sum(byte[] data)
        {
            uint sum = 0;

            for (int i = 0; i < data.Length; i += 4)
            {
                uint word = 0;

                for (int k = 0; k < 4; k++)
                {
                    word = (word << 8) | (i + k < data.Length ? data[i + k] : (uint)0);
                }

                sum += word;
            }

            return sum;
        }

        private static int U16(byte[] data, int at)
        {
            if (at < 0 || at + 2 > data.Length)
            {
                throw new InvalidDataException("A read lies beyond the font.");
            }

            return (data[at] << 8) | data[at + 1];
        }

        private static uint U32(byte[] data, int at)
        {
            if (at < 0 || at + 4 > data.Length)
            {
                throw new InvalidDataException("A read lies beyond the font.");
            }

            return ((uint)data[at] << 24) | ((uint)data[at + 1] << 16) | ((uint)data[at + 2] << 8) | data[at + 3];
        }

        private static void Put32(byte[] data, int at, uint value)
        {
            data[at] = (byte)(value >> 24);
            data[at + 1] = (byte)(value >> 16);
            data[at + 2] = (byte)(value >> 8);
            data[at + 3] = (byte)value;
        }
    }
}
