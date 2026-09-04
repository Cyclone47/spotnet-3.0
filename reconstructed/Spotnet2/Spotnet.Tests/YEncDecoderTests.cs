using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Spotnet.Downloader;
using SpotnetEnc;
using Xunit;

namespace Spotnet.Tests
{
    public class YEncDecoderTests
    {
        [Fact]
        public void SpotnetDecoder_DecodesSimpleYEncData()
        {
            // Simple yEnc encoding test:
            // Original byte 0 -> encoded (0 + 42) = 42 ('*')
            // Original byte 1 -> encoded (1 + 42) = 43 ('+')
            string yencBody = "*+\r\n";
            byte[] inputBytes = Encoding.ASCII.GetBytes(yencBody);
            byte[] outputBuffer = new byte[10];

            var decoder = new SpotnetDecoder();
            uint decodedBytes = decoder.Decode(inputBytes, outputBuffer, 0, (uint)inputBytes.Length);

            Assert.Equal(2u, decodedBytes);
            Assert.Equal(0, outputBuffer[0]);
            Assert.Equal(1, outputBuffer[1]);
        }

        [Fact]
        public void SpotnetDecoder_HandlesEscapedCharacters()
        {
            // Escaped character: '=j' -> (= followed by byte = original + 42 + 64)
            // Original byte 0x00 escaped -> '=' + (0 + 42 + 64) = '=' + 106 ('j')
            string yencBody = "=j\r\n";
            byte[] inputBytes = Encoding.ASCII.GetBytes(yencBody);
            byte[] outputBuffer = new byte[10];

            var decoder = new SpotnetDecoder();
            uint decodedBytes = decoder.Decode(inputBytes, outputBuffer, 0, (uint)inputBytes.Length);

            Assert.Equal(1u, decodedBytes);
            Assert.Equal(0, outputBuffer[0]);
        }

        [Fact]
        public void SpotnetDecoder_RemovesNntpDotStuffing()
        {
            // Byte 0x04 encodes to '.' (4 + 42 = 46). When it lands at the start of a
            // line the server doubles it, so the wire carries "..". Only one of the two
            // dots is payload; keeping both inserts a byte and shifts the rest of the file.
            byte[] inputBytes = Encoding.ASCII.GetBytes("*\r\n..*\r\n");
            byte[] outputBuffer = new byte[10];

            var decoder = new SpotnetDecoder();
            uint decodedBytes = decoder.Decode(inputBytes, outputBuffer, 0, (uint)inputBytes.Length);

            Assert.Equal(3u, decodedBytes);
            Assert.Equal(0, outputBuffer[0]);
            Assert.Equal(4, outputBuffer[1]);
            Assert.Equal(0, outputBuffer[2]);
        }

        [Fact]
        public void SpotnetDecoder_KeepsDotThatIsNotAtLineStart()
        {
            byte[] inputBytes = Encoding.ASCII.GetBytes("*.*\r\n");
            byte[] outputBuffer = new byte[10];

            var decoder = new SpotnetDecoder();
            uint decodedBytes = decoder.Decode(inputBytes, outputBuffer, 0, (uint)inputBytes.Length);

            Assert.Equal(3u, decodedBytes);
            Assert.Equal(0, outputBuffer[0]);
            Assert.Equal(4, outputBuffer[1]);
            Assert.Equal(0, outputBuffer[2]);
        }

        [Fact]
        public void FastDecoder_DecodesDotStuffedArticleByteForByte()
        {
            byte[] payload = BuildPayloadWithDotsAtLineStarts();
            using MemoryStream article = BuildArticle(payload);

            using MemoryStream decoded = DownloaderDataDecoderCpuOptimized.NewDecodeBinary(article);

            Assert.Equal(payload, decoded.ToArray());
        }

        [Fact]
        public void SlowDecoder_DecodesDotStuffedArticleByteForByte()
        {
            byte[] payload = BuildPayloadWithDotsAtLineStarts();
            using MemoryStream article = BuildArticle(payload);

            using MemoryStream decoded = DownloaderDataDecoder.DecodeBinary(article);

            Assert.Equal(payload, decoded.ToArray());
        }

        [Fact]
        public void BothDecoders_AgreeOnRandomBinaryArticle()
        {
            var random = new Random(20260903);
            byte[] payload = new byte[64 * 1024];
            random.NextBytes(payload);
            using MemoryStream article = BuildArticle(payload);
            using MemoryStream fast = DownloaderDataDecoderCpuOptimized.NewDecodeBinary(article);
            using MemoryStream slow = DownloaderDataDecoder.DecodeBinary(article);

            Assert.Equal(payload, fast.ToArray());
            Assert.Equal(payload, slow.ToArray());
        }

        /// <summary>
        /// 0x04 encodes to '.', so placing it on a line boundary forces the server to
        /// dot-stuff that line. Every other byte is chosen so it never needs escaping,
        /// which keeps one encoded character per payload byte and the line starts at
        /// exact multiples of the line length.
        /// </summary>
        private static byte[] BuildPayloadWithDotsAtLineStarts()
        {
            const int lineLength = 128;
            byte[] payload = new byte[lineLength * 8];
            for (int i = 0; i < payload.Length; i++)
            {
                byte b = (byte)(i % 256);
                payload[i] = NeedsEscape(b) ? (byte)0x41 : b;
            }
            for (int i = 0; i < payload.Length; i += lineLength)
            {
                payload[i] = 0x04;
            }
            return payload;
        }

        private static bool NeedsEscape(byte b)
        {
            byte encoded = (byte)(b + 42);
            return encoded == 0x00 || encoded == 0x0A || encoded == 0x0D || encoded == 0x3D;
        }

        /// <summary>
        /// Builds the article body exactly as it arrives from a news server: yEnc
        /// headers, CRLF-terminated encoded lines with NNTP dot-stuffing applied, and
        /// the terminating "." line.
        /// </summary>
        private static MemoryStream BuildArticle(byte[] payload, int lineLength = 128)
        {
            var lines = new List<string>();
            var line = new StringBuilder();
            foreach (byte b in payload)
            {
                byte encoded = (byte)(b + 42);
                if (NeedsEscape(b))
                {
                    line.Append('=');
                    line.Append((char)(byte)(encoded + 64));
                }
                else
                {
                    line.Append((char)encoded);
                }
                if (line.Length >= lineLength)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }
            }
            if (line.Length > 0)
            {
                lines.Add(line.ToString());
            }

            var article = new StringBuilder();
            article.Append("222 0 <test@spotnet> body\r\n");
            article.Append($"=ybegin part=1 line={lineLength} size={payload.Length} name=test.bin\r\n");
            article.Append($"=ypart begin=1 end={payload.Length}\r\n");
            foreach (string l in lines)
            {
                if (l.StartsWith(".", StringComparison.Ordinal))
                {
                    article.Append('.');
                }
                article.Append(l);
                article.Append("\r\n");
            }
            article.Append($"=yend size={payload.Length} part=1 pcrc32=00000000\r\n");
            article.Append(".\r\n");

            byte[] bytes = Encoding.GetEncoding("iso-8859-1").GetBytes(article.ToString());
            var stream = new MemoryStream();
            stream.Write(bytes, 0, bytes.Length);
            stream.Position = 0;
            return stream;
        }
    }
}
