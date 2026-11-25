using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// Reads and writes the XML of a sheet set data file as AutoCAD writes it: one line with no indentation, an element with
    /// nothing in it as <c>&lt;a/&gt;</c>, the text of a value escaped for <c>&amp;</c>, <c>&lt;</c> and <c>&gt;</c> alone, a
    /// line end inside a value as CR LF and a tab as it is, and the XML declaration and the closing tag each followed by CR
    /// LF. Written by hand rather than by <see cref="XmlWriter"/>, which writes <c>&lt;a /&gt;</c> and would not give the
    /// bytes of the file back.
    /// </summary>
    internal static class CadDstXml
    {
        private const string Declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";

        /// <summary>Reads the XML text of a sheet set data file, every character of every value kept.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The document.</returns>
        /// <exception cref="FormatException">Thrown when the text is no XML: the file is not a sheet set data file.</exception>
        internal static XDocument Parse(string text)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreWhitespace = false,
            };
            try
            {
                using (XmlReader reader = XmlReader.Create(new StringReader(text), settings))
                {
                    XDocument document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                    document.Nodes().OfType<XText>().ToList().ForEach(t => t.Remove());
                    return document;
                }
            }
            catch (XmlException e)
            {
                throw new FormatException("the file is not a sheet set data file (" + e.Message + ")", e);
            }
        }

        /// <summary>Writes a document as AutoCAD writes a sheet set data file.</summary>
        /// <param name="document">The document, with its root element.</param>
        /// <returns>The text.</returns>
        /// <exception cref="ArgumentException">Thrown when a value holds a character XML cannot hold (a control character other than tab, line feed and carriage return, or a lone surrogate).</exception>
        internal static string Write(XDocument document)
        {
            var text = new StringBuilder(Declaration).Append("\r\n");
            WriteElement(text, document.Root);
            return text.Append("\r\n").ToString();
        }

        private static void WriteElement(StringBuilder text, XElement element)
        {
            text.Append('<').Append(element.Name.LocalName);
            foreach (XAttribute attribute in element.Attributes())
            {
                text.Append(' ').Append(attribute.Name.LocalName).Append("=\"");
                Escape(text, attribute.Value, true);
                text.Append('"');
            }

            if (element.IsEmpty)
            {
                text.Append("/>");
                return;
            }

            text.Append('>');
            foreach (XNode node in element.Nodes())
            {
                switch (node)
                {
                    case XElement child:
                        WriteElement(text, child);
                        break;
                    case XText content:
                        Escape(text, content.Value, false);
                        break;
                    case XComment comment:
                        text.Append("<!--").Append(comment.Value).Append("-->");
                        break;
                    case XProcessingInstruction instruction:
                        text.Append("<?").Append(instruction.Target).Append(' ').Append(instruction.Data).Append("?>");
                        break;
                }
            }

            text.Append("</").Append(element.Name.LocalName).Append('>');
        }

        private static void Escape(StringBuilder text, string value, bool attribute)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '&':
                        text.Append("&amp;");
                        break;
                    case '<':
                        text.Append("&lt;");
                        break;
                    case '>':
                        text.Append("&gt;");
                        break;
                    case '"' when attribute:
                        text.Append("&quot;");
                        break;
                    case '\t' when attribute:
                        text.Append("&#x9;");
                        break;
                    case '\t':
                        text.Append('\t');
                        break;
                    case '\r':
                    case '\n':
                        // Any line end, a CR LF pair, a CR or an LF, is written as the pair AutoCAD writes.
                        if (c == '\r' && i + 1 < value.Length && value[i + 1] == '\n')
                        {
                            i++;
                        }

                        text.Append(attribute ? "&#xA;" : "\r\n");
                        break;
                    default:
                        if (XmlConvert.IsXmlChar(c))
                        {
                            text.Append(c);
                        }
                        else if (i + 1 < value.Length && XmlConvert.IsXmlSurrogatePair(value[i + 1], c))
                        {
                            text.Append(c).Append(value[++i]);
                        }
                        else
                        {
                            throw new ArgumentException(
                                "A value holds a control character (U+"
                                    + ((int)c).ToString("X4")
                                    + ") that a sheet set data file cannot hold.",
                                nameof(value)
                            );
                        }

                        break;
                }
            }
        }
    }
}
