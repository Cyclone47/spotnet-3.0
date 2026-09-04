using System;
using System.Collections.Generic;
using Spotnet.Model;
using Xunit;

namespace Spotnet.Mac.Tests;

public class MacArticleWatermarkTests
{
    [Fact]
    public void BinarySearch_FindsCorrectArticleForCutoff()
    {
        // 1000 articles posted 1 per hour over 1000 hours
        var baseDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var articles = new Dictionary<long, DateTime>();
        for (long i = 1; i <= 1000; i++)
        {
            articles[i] = baseDate.AddHours(i);
        }

        // Cutoff at article 500
        DateTime cutoff = baseDate.AddHours(500);

        long result = ArticleWatermark.FindFirstArticleOnOrAfter(1, 1000, cutoff, (from, to) =>
        {
            for (long a = from; a <= to; a++)
            {
                if (articles.TryGetValue(a, out var date))
                {
                    return new ArticleWatermark.ArticleStamp(a, date);
                }
            }
            return null;
        });

        Assert.Equal(500, result);
    }

    [Fact]
    public void FirstStampIn_ParsesXoverLineCorrectly()
    {
        string xover = "12345\tTest Subject\tposter@test.com\tFri, 04 Sep 2026 12:00:00 +0000\t<msg@test>\r\n";
        var stamp = ArticleWatermark.FirstStampIn(xover);

        Assert.NotNull(stamp);
        Assert.Equal(12345, stamp!.Value.Article);
        Assert.Equal(2026, stamp.Value.PostedUtc.Year);
        Assert.Equal(9, stamp.Value.PostedUtc.Month);
        Assert.Equal(4, stamp.Value.PostedUtc.Day);
    }

    [Fact]
    public void ParseOverviewDate_HandlesCommentsAndVariations()
    {
        var d1 = ArticleWatermark.ParseOverviewDate("04 Sep 2026 10:00:00 GMT (UTC)");
        Assert.NotNull(d1);
        Assert.Equal(10, d1!.Value.Hour);

        var d2 = ArticleWatermark.ParseOverviewDate("Invalid date format");
        Assert.Null(d2);
    }
}
