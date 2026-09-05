using System;
using Spotnet.Model;
using Xunit;

namespace Spotnet.Tests
{
    /// <summary>
    /// Covers the search that decides where a first synchronisation starts.
    /// </summary>
    /// <remarks>
    /// This is what turns a first run from hours into minutes, so it has to be right in both
    /// directions: starting too high silently loses spots the user asked for, and failing to
    /// find an answer has to leave the caller with the full range rather than a guess. The
    /// probe is supplied by the caller, so a whole newsgroup can be modelled here without a
    /// server.
    /// </remarks>
    public class ArticleWatermarkTests
    {
        private static readonly DateTime GroupStart = new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>A group where article n was posted n hours after the group started.</summary>
        private static ArticleWatermark.ProbeRange LinearGroup(long first, long last, Action<long> onProbe = null)
        {
            return (from, to) =>
            {
                onProbe?.Invoke(from);
                long article = Math.Max(from, first);
                if (article > to || article > last)
                {
                    return null;
                }
                return new ArticleWatermark.ArticleStamp(article, GroupStart.AddHours(article));
            };
        }

        [Fact]
        public void FindsTheArticleWhereTheCutoffFalls()
        {
            DateTime cutoff = GroupStart.AddHours(750_000);

            long found = ArticleWatermark.FindFirstArticleOnOrAfter(1, 1_000_000, cutoff, LinearGroup(1, 1_000_000));

            Assert.Equal(750_000, found);
        }

        [Fact]
        public void ReachesTheAnswerInAHandfulOfProbes()
        {
            // The point of the search is that it costs a few round trips rather than a full
            // header download, so the budget is part of the contract.
            int probes = 0;
            DateTime cutoff = GroupStart.AddHours(9_000_000);

            ArticleWatermark.FindFirstArticleOnOrAfter(
                1, 10_000_000, cutoff, LinearGroup(1, 10_000_000, delegate { probes++; }));

            Assert.InRange(probes, 1, 32);
        }

        [Fact]
        public void EverythingNewerThanTheCutoffMeansStartingAtTheBeginning()
        {
            long found = ArticleWatermark.FindFirstArticleOnOrAfter(
                1, 100_000, GroupStart.AddHours(-1), LinearGroup(1, 100_000));

            Assert.Equal(1, found);
        }

        [Fact]
        public void AGroupEntirelyOlderThanTheCutoffStartsAtItsEnd()
        {
            long found = ArticleWatermark.FindFirstArticleOnOrAfter(
                1, 100_000, GroupStart.AddYears(50), LinearGroup(1, 100_000));

            Assert.Equal(100_000, found);
        }

        [Fact]
        public void HolesInTheNumberingAreSteppedOver()
        {
            // Cancelled and expired articles leave gaps; a probe landing in one answers
            // nothing and the search has to continue rather than stop.
            const long gapFrom = 400_000;
            const long gapTo = 600_000;
            ArticleWatermark.ProbeRange probe = (from, to) =>
            {
                for (long n = from; n <= to; n++)
                {
                    if (n < gapFrom || n > gapTo)
                    {
                        return new ArticleWatermark.ArticleStamp(n, GroupStart.AddHours(n));
                    }
                }
                return null;
            };

            long found = ArticleWatermark.FindFirstArticleOnOrAfter(1, 1_000_000, GroupStart.AddHours(700_000), probe);

            Assert.Equal(700_000, found);
        }

        [Fact]
        public void AServerThatAnswersNothingLeavesTheRangeAlone()
        {
            long found = ArticleWatermark.FindFirstArticleOnOrAfter(
                1, 1_000_000, GroupStart, (from, to) => null);

            Assert.Equal(ArticleWatermark.Undetermined, found);
        }

        [Theory]
        [InlineData(5, 4)]
        [InlineData(0, -1)]
        [InlineData(-1, 100)]
        public void ANonsensicalRangeIsUndetermined(long first, long last)
        {
            Assert.Equal(
                ArticleWatermark.Undetermined,
                ArticleWatermark.FindFirstArticleOnOrAfter(first, last, GroupStart, LinearGroup(first, last)));
        }

        [Fact]
        public void AMissingProbeIsUndetermined()
        {
            Assert.Equal(
                ArticleWatermark.Undetermined,
                ArticleWatermark.FindFirstArticleOnOrAfter(1, 100, GroupStart, null));
        }

        // --- reading the overview response -----------------------------------

        [Fact]
        public void ReadsTheFirstArticleAndDateOutOfAnXoverResponse()
        {
            string response =
                "224 Overview information follows\r\n"
                + "9001\tA subject\tposter@example.com\tTue, 12 Aug 2025 10:33:21 +0000\t<a@b>\t\t4096\t32\r\n"
                + "9002\tAnother\tposter@example.com\tTue, 12 Aug 2025 10:34:00 +0000\t<c@d>\t\t4096\t32\r\n"
                + ".\r\n";

            ArticleWatermark.ArticleStamp? stamp = ArticleWatermark.FirstStampIn(response);

            Assert.True(stamp.HasValue);
            Assert.Equal(9001, stamp.Value.Article);
            Assert.Equal(new DateTime(2025, 8, 12, 10, 33, 21, DateTimeKind.Utc), stamp.Value.PostedUtc);
        }

        [Fact]
        public void SkipsALineWhoseDateCannotBeRead()
        {
            // Usenet carries plenty of malformed Date headers; one of them must not push the
            // search off course.
            string response =
                "224 Overview information follows\r\n"
                + "9001\tA subject\tposter@example.com\tnot a date at all\t<a@b>\t\t4096\t32\r\n"
                + "9002\tAnother\tposter@example.com\t12 Aug 2025 10:34:00 -0000\t<c@d>\t\t4096\t32\r\n"
                + ".\r\n";

            ArticleWatermark.ArticleStamp? stamp = ArticleWatermark.FirstStampIn(response);

            Assert.True(stamp.HasValue);
            Assert.Equal(9002, stamp.Value.Article);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("224 Overview information follows\r\n.\r\n")]
        [InlineData("430 No such article\r\n")]
        public void AnEmptyOrRefusedResponseReadsAsNothing(string response)
        {
            Assert.Null(ArticleWatermark.FirstStampIn(response));
        }

        [Theory]
        [InlineData("Tue, 12 Aug 2025 10:33:21 +0000", "2025-08-12T10:33:21Z")]
        [InlineData("Tue, 12 Aug 2025 12:33:21 +0200", "2025-08-12T10:33:21Z")]
        [InlineData("12 Aug 2025 10:33:21 GMT", "2025-08-12T10:33:21Z")]
        [InlineData("Tue, 12 Aug 2025 10:33:21 +0000 (UTC)", "2025-08-12T10:33:21Z")]
        public void ParsesTheDateHeaderShapesThatArriveInPractice(string header, string expected)
        {
            DateTime? parsed = ArticleWatermark.ParseOverviewDate(header);

            Assert.True(parsed.HasValue);
            Assert.Equal(DateTime.Parse(expected).ToUniversalTime(), parsed.Value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("yesterday")]
        public void AnUnreadableDateIsNull(string header)
        {
            Assert.Null(ArticleWatermark.ParseOverviewDate(header));
        }
    }
}
