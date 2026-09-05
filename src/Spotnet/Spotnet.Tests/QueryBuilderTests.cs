using System;
using Spotnet.DAL;
using Spotnet.Properties;
using Xunit;

namespace Spotnet.Tests
{
    /// <summary>
    /// Golden-output tests for the SQL that <see cref="SpotProvider"/> generates.
    /// </summary>
    /// <remarks>
    /// These four builders assemble SQL by string concatenation across roughly eighty
    /// lines, branching on sort column, sort direction, the erotica toggle, whether the
    /// filter is a search, and whether a minimum row id was supplied. That is impossible
    /// to refactor safely by inspection, so this records what each combination produces
    /// today.
    ///
    /// They are change detectors, not a specification. A failure means the generated SQL
    /// moved - confirm the new SQL is what you intended, then update the expectation.
    /// The FTS5 rowid query shape is deliberately pinned here.
    /// </remarks>
    public class QueryBuilderTests : IDisposable
    {
        private readonly string _sortColumn;
        private readonly string _sortDirection;
        private readonly bool _showErotica;
        private readonly long _databaseMax;

        public QueryBuilderTests()
        {
            // The builders read global settings, so snapshot and restore them.
            _sortColumn = Settings.Default.SortColumn;
            _sortDirection = Settings.Default.SortDirection;
            _showErotica = Settings.Default.ShowEroticaInSearchResults;
            _databaseMax = Settings.Default.DatabaseMax;

            Settings.Default.DatabaseMax = 5000;
            Settings.Default.ShowEroticaInSearchResults = false;
        }

        public void Dispose()
        {
            Settings.Default.SortColumn = _sortColumn;
            Settings.Default.SortDirection = _sortDirection;
            Settings.Default.ShowEroticaInSearchResults = _showErotica;
            Settings.Default.DatabaseMax = _databaseMax;
            GC.SuppressFinalize(this);
        }

        private static SpotProvider Provider(string sortColumn = "date", string sortDirection = "desc", long rowNew = -1)
        {
            var provider = new SpotProvider();
            // Assign after construction: the constructor forces rowid/desc on first run.
            Settings.Default.SortColumn = sortColumn;
            Settings.Default.SortDirection = sortDirection;
            provider.RowNew = rowNew;
            return provider;
        }

        // --- CreateQuery ---------------------------------------------------------

        [Fact]
        public void CreateQuery_DateDescendingUsesTheRowidSubqueryForm()
        {
            string sql = Provider().CreateQuery("cat<9", startIndex: 0, countRequested: 250, minRowId: -1, out string countQuery);

            // Sorting by date descending goes through a rowid subquery so the LIMIT can
            // be applied before the join.
            Assert.Contains("WHERE rowid IN (SELECT rowid FROM spots", sql);
            Assert.Contains("ORDER BY rowid DESC LIMIT 250 OFFSET 0", sql);
            Assert.Contains("LEFT JOIN spamgroup s USING (msgid)", sql);
            Assert.Contains("key != 2 AND key != 5", sql);
            Assert.Equal("SELECT COUNT(1) FROM spots LEFT JOIN spamgroup s USING (msgid) WHERE (cat<@filter0 AND key != 2 AND key != 5)", countQuery);
        }

        [Fact]
        public void CreateQuery_NonDateSortAppendsASecondaryDateOrder()
        {
            string sql = Provider(sortColumn: "subject").CreateQuery("cat<9", 0, 100, -1, out _);

            Assert.Contains("ORDER BY subject desc , date DESC", sql);
            Assert.Contains("LIMIT 100 OFFSET 0", sql);
            // The subquery form is only used for date-descending.
            Assert.DoesNotContain("WHERE rowid IN (SELECT rowid FROM spots", sql);
        }

        [Fact]
        public void CreateQuery_InjectsTheEroticaGuardUnlessTheFilterMentionsCat()
        {
            // A filter that says nothing about cat gets the guard...
            string guarded = Provider().CreateQuery("subject MATCH 'ubuntu'", 0, 50, -1, out _);
            Assert.Contains("cat<9 AND", guarded);

            // ...but one that constrains cat itself is left alone.
            string explicitCat = Provider().CreateQuery("cat=5", 0, 50, -1, out _);
            Assert.DoesNotContain("cat<9 AND", explicitCat);
        }

        [Fact]
        public void CreateQuery_OmitsTheEroticaGuardWhenTheSettingIsOn()
        {
            Settings.Default.ShowEroticaInSearchResults = true;

            string sql = Provider().CreateQuery("subject MATCH 'ubuntu'", 0, 50, -1, out _);

            Assert.DoesNotContain("cat<9 AND", sql);
        }

        [Fact]
        public void CreateQuery_AppliesAMinimumRowIdWhenGiven()
        {
            string sql = Provider().CreateQuery("cat<9", 0, 50, minRowId: 4242, out _);

            Assert.Contains("rowid>=4242 AND", sql);
        }

        [Fact]
        public void CreateQuery_HintsTheDateIndexForDateFilters()
        {
            string sql = Provider().CreateQuery("date>[SN:DATE]", 0, 50, -1, out _);

            Assert.Contains("INDEXED BY dateidx", sql);
        }

        [Fact]
        public void CreateQuery_SubstitutesTheNewRowMarker()
        {
            // With RowNew unset the marker resolves to DatabaseMax + 1.
            string unset = Provider(rowNew: -1).CreateQuery("rowid>[SN:NEW]", 0, 50, -1, out _);
            Assert.Contains("rowid>@filter0", unset);
            Assert.DoesNotContain("[SN:NEW]", unset);

            // With RowNew set it wins.
            string set = Provider(rowNew: 1234).CreateQuery("rowid>[SN:NEW]", 0, 50, -1, out _);
            Assert.Contains("rowid>@filter0", set);
        }

        [Fact]
        public void CreateQuery_StripsAPosterIdentPrefix()
        {
            // PosterIdent is handled in the view model, not in SQL, so the builder peels
            // it off and falls back to a plain category bound.
            string sql = Provider().CreateQuery("PosterIdent IN (W,B) ", 0, 50, -1, out _);

            Assert.DoesNotContain("PosterIdent", sql);
            Assert.Contains("cat<@filter0", sql);
        }

        [Fact]
        public void CreateQuery_WithNoFilterHasNoWhereClause()
        {
            string sql = Provider().CreateQuery("", 0, 50, -1, out string countQuery);

            Assert.DoesNotContain("WHERE (", sql);
            Assert.Equal("SELECT COUNT(1) FROM spots LEFT JOIN spamgroup s USING (msgid)", countQuery);
        }

        // --- CreateSearchQuery ---------------------------------------------------

        [Fact]
        public void CreateSearchQuery_GoesThroughTheFts5RowidIndex()
        {
            string sql = Provider().CreateSearchQuery("subject MATCH 'ubuntu'", 0, 250, -1, out string countQuery);

            Assert.Contains("SELECT rowid FROM search", sql);
            Assert.Contains("cats NOT LIKE '9 %' AND", sql);
            Assert.Contains("key != 2 AND key != 5", sql);
            Assert.Equal("SELECT COUNT(1) FROM search WHERE (cats NOT LIKE '9 %' AND subject MATCH @filter0)", countQuery);
        }

        [Fact]
        public void CreateSearchQuery_SkipsTheEroticaGuardForAnExplicitCatsMatch()
        {
            string sql = Provider().CreateSearchQuery("cats match 'a01'", 0, 50, -1, out _);

            Assert.DoesNotContain("cats NOT LIKE '9 %'", sql);
        }

        [Fact]
        public void CreateSearchQuery_AppliesAMinimumRowIdWhenGiven()
        {
            string sql = Provider().CreateSearchQuery("subject MATCH 'ubuntu'", 0, 50, minRowId: 99, out _);

            Assert.Contains("rowid>=99 AND", sql);
        }

        [Fact]
        public void CreateSearchQuery_MovesTheLimitInsideForDateDescending()
        {
            string dateDesc = Provider(sortColumn: "date", sortDirection: "desc")
                .CreateSearchQuery("subject MATCH 'ubuntu'", 0, 250, -1, out _);
            // The limit is applied inside the FTS rowid subquery.
            Assert.Contains("ORDER BY rowid DESC  LIMIT 250 OFFSET 0", dateDesc);

            string bySubject = Provider(sortColumn: "subject").CreateSearchQuery("subject MATCH 'ubuntu'", 0, 250, -1, out _);
            // Otherwise it trails the outer query.
            Assert.EndsWith(" LIMIT 250 OFFSET 0", bySubject);
        }

        [Fact]
        public void CreateSearchQuery_TreatsRowidSortAsDate()
        {
            string sql = Provider(sortColumn: "rowid", sortDirection: "desc")
                .CreateSearchQuery("subject MATCH 'ubuntu'", 0, 50, -1, out _);

            Assert.Contains("ORDER BY date desc", sql);
        }

        // --- the "new items" counters --------------------------------------------

        [Fact]
        public void CreateQueryCountNew_CountsAboveTheNewWatermark()
        {
            string sql = Provider(rowNew: 900).CreateQueryCountNew("cat<9");

            Assert.Equal("SELECT COUNT(1) FROM spots WHERE rowid>900 AND (cat<@filter0 AND key != 2 AND key != 5)", sql);
        }

        [Fact]
        public void CreateSearchQueryCountNew_CountsAboveTheNewWatermark()
        {
            string sql = Provider(rowNew: 900).CreateSearchQueryCountNew("subject MATCH 'ubuntu'");

            Assert.Equal("SELECT COUNT(1) FROM search WHERE rowid>900 AND (cats NOT LIKE '9 %' AND subject MATCH @filter0)", sql);
        }

        [Fact]
        public void CountNewBuildersReturnNullForAnEmptyFilter()
        {
            Assert.Null(Provider().CreateQueryCountNew(""));
            Assert.Null(Provider().CreateSearchQueryCountNew("   "));
        }

        [Fact]
        public void EveryBuilderResolvesTheDatePlaceholder()
        {
            // [SN:DATE] must never survive into executed SQL.
            Assert.DoesNotContain("[SN:DATE]", Provider().CreateQuery("date>[SN:DATE]", 0, 50, -1, out _));
            Assert.DoesNotContain("[SN:DATE]", Provider().CreateSearchQuery("date>[SN:DATE]", 0, 50, -1, out _));
            Assert.DoesNotContain("[SN:DATE]", Provider(rowNew: 5).CreateQueryCountNew("date>[SN:DATE]"));
            Assert.DoesNotContain("[SN:DATE]", Provider(rowNew: 5).CreateSearchQueryCountNew("date>[SN:DATE]"));
        }
    }
}
