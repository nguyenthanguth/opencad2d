using System;
using System.Text;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// The cipher of a sheet set data file (.dst), AutoCAD 2021's: UTF-8 XML under a fixed substitution of every byte. The
    /// text byte is <c>(BLOCK_BASE[high nibble] + PERMUTE[low nibble]) mod 256</c>, with <c>PERMUTE</c> the sixteen values
    /// 13, 12, 15, 14, 9, 8, 11, 10, 5, 4, 7, 6, 1, 0, 3, 2 and <c>BLOCK_BASE</c> the sixteen 127, 111, 159, 143, 191, 175,
    /// 223, 207, 255, 239, 31, 15, 63, 47, 95, 79 (the formula of the MIT-licensed dst-codec, checked here against the ten
    /// files AutoCAD 2021 installs: each decodes to XML and encodes back to the same bytes).
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is a bijection of all 256 byte values, so no byte has "no mapping". The carriage return (13) is the file byte 131,
    /// the line feed (10) is 134 and the tab (9) is 135: the pair 131 134 that ends a line is CR LF, and the XML declaration
    /// and the closing tag are each followed by it. (The first notes took 131 for LF and 134 for TAB; a description holding
    /// a CR LF pair and a tab, saved by AutoCAD's own sheet set API, settled it.) The text bytes 0 to 14 and 255 are the file
    /// bytes 0x80 to 0x8F, and 15 to 30 are 0xB0 to 0xBF (the block of 15 to 30 follows the pattern of the others and has
    /// not been seen in a file); XML cannot hold the controls among them, and AutoCAD refuses to save a value that does
    /// (its save failed on a value holding U+0001).
    /// </para>
    /// </remarks>
    internal static class CadDstCodec
    {
        private static readonly int[] Permute = { 13, 12, 15, 14, 9, 8, 11, 10, 5, 4, 7, 6, 1, 0, 3, 2 };

        private static readonly int[] BlockBase =
        {
            127,
            111,
            159,
            143,
            191,
            175,
            223,
            207,
            255,
            239,
            31,
            15,
            63,
            47,
            95,
            79,
        };

        private static readonly byte[] ToText = new byte[256];
        private static readonly byte[] ToFile = new byte[256];

        static CadDstCodec()
        {
            for (int file = 0; file < 256; file++)
            {
                byte text = (byte)((BlockBase[file >> 4] + Permute[file & 15]) & 0xFF);
                ToText[file] = text;
                ToFile[text] = (byte)file;
            }
        }

        /// <summary>Takes the cipher off the bytes of a file.</summary>
        /// <param name="file">The bytes of a .dst file.</param>
        /// <returns>The bytes of its XML text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the bytes are null.</exception>
        internal static byte[] DecodeBytes(byte[] file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            var text = new byte[file.Length];
            for (int i = 0; i < file.Length; i++)
            {
                text[i] = ToText[file[i]];
            }

            return text;
        }

        /// <summary>Puts the cipher on the bytes of XML text.</summary>
        /// <param name="text">The bytes of the text.</param>
        /// <returns>The bytes of the file.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the bytes are null.</exception>
        internal static byte[] EncodeBytes(byte[] text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var file = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                file[i] = ToFile[text[i]];
            }

            return file;
        }

        /// <summary>Takes the cipher off a file and reads what is under it as UTF-8.</summary>
        /// <param name="file">The bytes of a .dst file.</param>
        /// <returns>The XML text, the line ends as the file has them (CR LF).</returns>
        /// <exception cref="ArgumentNullException">Thrown when the bytes are null.</exception>
        /// <exception cref="FormatException">Thrown when what is under the cipher is not UTF-8: the file is not a sheet set data file. The message names the byte and its place.</exception>
        internal static string Decode(byte[] file)
        {
            byte[] text = DecodeBytes(file);
            try
            {
                return new UTF8Encoding(false, true).GetString(text);
            }
            catch (DecoderFallbackException e)
            {
                int at = Math.Max(0, Math.Min(e.Index, file.Length - 1));
                throw new FormatException(
                    "the file is not a sheet set data file (byte 0x"
                        + file[at].ToString("X2")
                        + " at offset "
                        + at
                        + ")",
                    e
                );
            }
        }

        /// <summary>Writes XML text as UTF-8 and puts the cipher on it.</summary>
        /// <param name="text">The XML text, with the line ends it is to have in the file.</param>
        /// <returns>The bytes of the file.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the text is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the text holds a lone surrogate, which UTF-8 cannot write.</exception>
        internal static byte[] Encode(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            try
            {
                return EncodeBytes(new UTF8Encoding(false, true).GetBytes(text));
            }
            catch (EncoderFallbackException e)
            {
                throw new ArgumentException(
                    "The text holds a lone surrogate (at index " + e.Index + "), which UTF-8 cannot write.",
                    nameof(text),
                    e
                );
            }
        }
    }
}
