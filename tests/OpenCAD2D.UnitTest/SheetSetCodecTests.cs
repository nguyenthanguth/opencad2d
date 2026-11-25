using System;
using System.Linq;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The cipher of a sheet set data file (.dst), AutoCAD 2021's: UTF-8 XML under a one-byte substitution. The text byte
    /// is <c>(BLOCK_BASE[high nibble] + PERMUTE[low nibble]) mod 256</c>, and the expected bytes below are worked out from
    /// that formula by hand, not read from any Autodesk file.
    /// </summary>
    public class SheetSetCodecTests
    {
        [Fact]
        public void EveryByteValue_IsTakenToATextByteAndBack_AndNoTwoShareOne()
        {
            byte[] all = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

            byte[] text = CadDstCodec.DecodeBytes(all);

            Assert.Equal(256, text.Distinct().Count());
            Assert.Equal(all, CadDstCodec.EncodeBytes(text));
            Assert.Equal(all, CadDstCodec.DecodeBytes(CadDstCodec.EncodeBytes(all)));
        }

        [Fact]
        public void AShortText_EncodesToTheBytesWorkedOutByHandFromTheFormula()
        {
            // '<' is 60 = 47 + 13: block D (base 47), PERMUTE[0] = 13, so 0xD0. '/' is 47 = 47 + 0: PERMUTE[13] = 0, 0xDD.
            // '>' is 62 = 47 + 15: PERMUTE[2] = 15, 0xD2. 'a' is 97 = 95 + 2: block E (base 95), PERMUTE[15] = 2, 0xEF.
            // CR 13 = (255 + 14) mod 256: block 8 (base 255), PERMUTE[3] = 14, 0x83. LF 10 = (255 + 11) mod 256: 0x86.
            // TAB 9 = (255 + 10) mod 256: 0x87. ' ' is 32 = 31 + 1: block A (base 31), PERMUTE[12] = 1, 0xAC.
            // '=' is 61 = 47 + 14: PERMUTE[3] = 14, 0xD3.
            byte[] expected = { 0xD0, 0xEF, 0xDD, 0xD2, 0x83, 0x86, 0x87, 0xAC, 0xD3 };

            Assert.Equal(expected, CadDstCodec.Encode("<a/>\r\n\t ="));
            Assert.Equal("<a/>\r\n\t =", CadDstCodec.Decode(expected));
        }

        [Fact]
        public void TheXmlDeclaration_EncodesToTheBytesWorkedOutByHand()
        {
            // '<' 0xD0; '?' is 63 = 63 + 0: block C, PERMUTE[13] = 0, 0xCD; 'x' is 120 = 111 + 9: block 1, PERMUTE[4] = 9,
            // 0x14; 'm' is 109 = 95 + 14: block E, PERMUTE[3] = 14, 0xE3; 'l' is 108 = 95 + 13: PERMUTE[0] = 13, 0xE0.
            byte[] expected = { 0xD0, 0xCD, 0x14, 0xE3, 0xE0 };

            Assert.Equal(expected, CadDstCodec.Encode("<?xml"));
        }

        [Theory]
        [InlineData("Phòng họp tầng 3 — Bản vẽ kiến trúc, đã kiểm tra")]
        [InlineData("图纸集 图纸 シート")]
        [InlineData("Plan \U0001F600 \U0001F4D0 end")]
        [InlineData("")]
        public void Text_InVietnameseCjkAndEmoji_ReadsBackWhole(string text)
        {
            byte[] file = CadDstCodec.Encode(text);

            Assert.Equal(text, CadDstCodec.Decode(file));
        }

        [Fact]
        public void ALineEnd_IsCarriedAsCrLf_AndATabAsATab()
        {
            // AutoCAD writes a line end as the pair 131 134 (CR LF) and a TAB as 135, which the sheet set probe of 3a showed
            // by saving a description holding both; the first notes took 131 for LF and 134 for TAB.
            Assert.Equal(new byte[] { 0x83, 0x86 }, CadDstCodec.Encode("\r\n"));
            Assert.Equal(new byte[] { 0x87 }, CadDstCodec.Encode("\t"));
            Assert.Equal("\r\n\t", CadDstCodec.Decode(new byte[] { 0x83, 0x86, 0x87 }));
        }

        [Fact]
        public void BytesThatDecodeToNoUtf8_AreRefused_AsNotASheetSetFile()
        {
            // Text byte 255 is block 8 with PERMUTE[13] = 0, the file byte 0x8D; no UTF-8 text holds a 255.
            FormatException error = Assert.Throws<FormatException>(() => CadDstCodec.Decode(new byte[] { 0x8D }));

            Assert.StartsWith("the file is not a sheet set data file", error.Message);
        }

        [Fact]
        public void ATextHoldingALoneSurrogate_IsRefusedAtTheWriter()
        {
            ArgumentException error = Assert.Throws<ArgumentException>(() => CadDstCodec.Encode("a\uD800b"));

            Assert.Contains("text", error.Message);
        }

        [Fact]
        public void ANullFileOrText_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => CadDstCodec.Decode(null));
            Assert.Throws<ArgumentNullException>(() => CadDstCodec.Encode(null));
            Assert.Throws<ArgumentNullException>(() => CadDstCodec.DecodeBytes(null));
            Assert.Throws<ArgumentNullException>(() => CadDstCodec.EncodeBytes(null));
        }
    }
}
