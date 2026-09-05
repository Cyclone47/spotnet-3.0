using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Spotnet.Model;
using Spotnet.Properties;
using Xunit;
using Xunit.Abstractions;

namespace Spotnet.Tests
{
    /// <summary>
    /// Pins the observable behaviour of the header parser.
    /// </summary>
    /// <remarks>
    /// <see cref="Worker.DoWork"/> is the hottest and least readable code in the
    /// application - a decompiled goto-lattice that turns raw NNTP XOVER lines into
    /// <see cref="Spot"/> objects. It has no unit tests and no specification, so before
    /// anything in it can be parallelized or refactored, its current behaviour has to be
    /// written down. These tests are that record: they assert what the parser does today,
    /// not what it ought to do.
    ///
    /// A failure here after a refactor means the refactor changed behaviour. Decide
    /// deliberately whether that change is correct, then update the assertion.
    /// </remarks>
    public class WorkerCharacterizationTests
    {
        private readonly ITestOutputHelper _output;

        public WorkerCharacterizationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // --- header construction -------------------------------------------------
        //
        // An XOVER line is five tab-separated fields:
        //     articleNumber \t subject \t from \t date \t <messageId>
        //
        // The `from` field carries the whole spot encoding in its address:
        //     Poster <modulus@meta0.meta1.meta2.meta3.meta4.meta5.signature>
        // where meta0 packs category (char 0), key id (char 1) and subcategories
        // (the rest), meta1 is the file size and meta3 is the unix timestamp.

        private const long ValidStamp = 1700000000L;

        private static string From(
            string poster = "TestPoster",
            int category = 3,
            int keyId = 3,
            string subCats = "a01b02c03",
            long fileSize = 1048576L,
            long stamp = ValidStamp,
            string modulus = "MODULUS",
            string signature = "SIG")
        {
            string meta0 = category.ToString() + keyId.ToString() + subCats;
            string address = string.Join(".", meta0, fileSize.ToString(), "0", stamp.ToString(), "x", "y", signature);
            return $"{poster} <{modulus}@{address}>";
        }

        private static string Line(
            long articleNumber = 1000L,
            string subject = "Ubuntu 24.04 LTS",
            string from = null,
            string date = "Mon, 01 Jan 2024 00:00:00 +0000",
            string messageId = "<abc123@spot.net>")
        {
            return string.Join("\t", articleNumber.ToString(), subject, from ?? From(), date, messageId);
        }

        /// <summary>
        /// Wraps data lines the way DoWork expects: it skips index 0 and the final two
        /// entries, so the payload has to sit between a preamble and a terminator.
        /// </summary>
        private static string HeaderBlock(params string[] lines)
        {
            return "224 overview follows\n" + string.Join("\n", lines) + "\n.\n";
        }

        private static Worker MakeWorker(string headerData, bool checkSignatures = false)
        {
            return new Worker
            {
                HeaderData = headerData,
                InstanceCount = 1,
                Rsa = new RSACryptoServiceProvider[10],
                XSettings = new NntpSettings
                {
                    BlackList = new HashSet<string>(),
                    WhiteList = new HashSet<string>(),
                    CheckSignatures = checkSignatures,
                    GroupName = "free.pt",
                    TrustedKeys = new string[10]
                }
            };
        }

        /// <summary>Runs the parser and returns the spots it produced.</summary>
        private static List<Spot> Parse(string headerData, bool checkSignatures = false)
        {
            Settings.Default.HideBlacklistedSpots = false;
            List<Spot> captured = null;
            bool failed = true;

            MakeWorker(headerData, checkSignatures).ParseHeaders(
                (bool isError, int a, int b, string error, List<Spot> spots, long newCount, bool g) =>
                {
                    failed = isError;
                    captured = spots;
                    return true;
                });

            Assert.False(failed, "the parser reported an error");
            return captured ?? new List<Spot>();
        }

        // --- the tests -----------------------------------------------------------

        [Fact]
        public void ParsesAWellFormedHeaderIntoASpot()
        {
            List<Spot> spots = Parse(HeaderBlock(Line()));

            Spot spot = Assert.Single(spots);
            Assert.Equal(1000L, spot.Article);
            Assert.Equal("abc123@spot.net", spot.MessageId);
            Assert.Equal("TestPoster", spot.Poster);
            Assert.Equal(3, spot.Category);
            Assert.Equal(3, spot.KeyID);
            Assert.Equal(1048576L, spot.Filesize);
            Assert.Equal(ValidStamp, spot.Stamp);
        }

        [Fact]
        public void ReturnsSpotsInAscendingArticleOrder()
        {
            // DoWork walks the block backwards and reverses at the end, so the output
            // order is not the order the lines were read in.
            List<Spot> spots = Parse(HeaderBlock(
                Line(articleNumber: 10, messageId: "<a@spot.net>"),
                Line(articleNumber: 20, messageId: "<b@spot.net>"),
                Line(articleNumber: 30, messageId: "<c@spot.net>")));

            Assert.Equal(3, spots.Count);
            Assert.Equal(new[] { 10L, 20L, 30L }, spots.Select(s => s.Article).ToArray());
        }

        [Fact]
        public void SkipsMalformedLinesWithoutFailingTheBatch()
        {
            List<Spot> spots = Parse(HeaderBlock(
                Line(articleNumber: 10, messageId: "<a@spot.net>"),
                "not\ta\tvalid\tline",
                "",
                Line(articleNumber: 30, messageId: "<c@spot.net>")));

            // One bad line must not cost the whole batch.
            Assert.Equal(new[] { 10L, 30L }, spots.Select(s => s.Article).ToArray());
        }

        [Fact]
        public void RejectsTimestampsBeforeTheSpotnetEpoch()
        {
            // 1218171600 is the floor the parser enforces; anything earlier is not a spot.
            List<Spot> spots = Parse(HeaderBlock(Line(from: From(stamp: 1218171599L))));

            Assert.Empty(spots);
        }

        [Fact]
        public void AcceptsTheOldestValidTimestamp()
        {
            List<Spot> spots = Parse(HeaderBlock(Line(from: From(stamp: 1218171600L))));

            Assert.Equal(1218171600L, Assert.Single(spots).Stamp);
        }

        [Fact]
        public void ClampsTimestampsFromTheFuture()
        {
            // A spot claiming a date beyond now+25000s is pulled back rather than dropped.
            long farFuture = (long)Math.Round((DateTime.UtcNow - Spotnet.Helpers.SpotHelper.Epoch).TotalSeconds) + 500000L;

            Spot spot = Assert.Single(Parse(HeaderBlock(Line(from: From(stamp: farFuture)))));

            Assert.True(spot.Stamp < farFuture, "a future timestamp should be clamped");
        }

        [Fact]
        public void RejectsAnEmptySubject()
        {
            Assert.Empty(Parse(HeaderBlock(Line(subject: ""))));
        }

        [Fact]
        public void RejectsAnAddressWithTooFewMetadataSegments()
        {
            // The address needs at least seven dot-separated segments.
            string from = "TestPoster <MODULUS@33a01b02c03.1048576.0.1700000000>";

            Assert.Empty(Parse(HeaderBlock(Line(from: from))));
        }

        [Fact]
        public void RejectsAKeyIdOfZero()
        {
            Assert.Empty(Parse(HeaderBlock(Line(from: From(keyId: 0)))));
        }

        [Fact]
        public void SignedKeyIdsAreDroppedWhenSignatureCheckingIsOn()
        {
            // With CheckSignatures on, a key id above 1 has to carry a verifiable
            // signature. This one does not, so it must not reach the database.
            List<Spot> spots = Parse(HeaderBlock(Line()), checkSignatures: true);

            Assert.Empty(spots);
        }

        [Fact]
        public void SignedKeyIdsAreAcceptedUnverifiedWhenCheckingIsOff()
        {
            List<Spot> spots = Parse(HeaderBlock(Line()), checkSignatures: false);

            Assert.Single(spots);
        }

        [Fact]
        public void FilesizeSentinelIsExcluded()
        {
            // 94165742 is treated as a marker rather than a real size and the spot is
            // dropped. Documented here because it is otherwise a bare magic number.
            Assert.Empty(Parse(HeaderBlock(Line(from: From(fileSize: 94165742L)))));
        }

        [Fact]
        public void NegativeFilesizeIsNormalizedToZero()
        {
            Spot spot = Assert.Single(Parse(HeaderBlock(Line(from: From(fileSize: -5L)))));

            Assert.Equal(0L, spot.Filesize);
        }

        [Fact]
        public void AnEmptyBlockProducesNoSpots()
        {
            Assert.Empty(Parse("224 overview follows\n.\n"));
        }

        /// <summary>
        /// Not an assertion - prints what the parser produced so the shape of a parsed
        /// spot is visible when one of the tests above starts failing.
        /// </summary>
        [Fact]
        public void DumpParsedShapeForDiagnostics()
        {
            foreach (Spot spot in Parse(HeaderBlock(Line())))
            {
                _output.WriteLine(
                    $"Article={spot.Article} MsgId={spot.MessageId} Poster={spot.Poster} " +
                    $"Cat={spot.Category} SubCat={spot.SubCat} SubCats={spot.SubCats} " +
                    $"KeyID={spot.KeyID} Size={spot.Filesize} Stamp={spot.Stamp} Title={spot.Title}");
            }
        }
    }
}
