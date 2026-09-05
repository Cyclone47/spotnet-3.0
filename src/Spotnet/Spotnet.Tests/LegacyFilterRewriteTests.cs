using System.Reflection;
using Spotnet.DAL;
using Spotnet.Model;
using Xunit;

namespace Spotnet.Tests;

/// <summary>
/// Filters saved before the FTS5 migration address rows by FTS4's `docid`. FTS5 has no
/// such column and the filter compiler no longer accepts the name, so those queries have
/// to be rewritten while filters.xml is read.
/// </summary>
public class LegacyFilterRewriteTests
{
	private static string Rewrite(string query)
	{
		MethodInfo method = typeof(Filters).GetMethod("RewriteLegacyDocId",
			BindingFlags.Static | BindingFlags.NonPublic);
		Assert.NotNull(method);
		return (string)method.Invoke(null, new object[] { query });
	}

	[Fact]
	public void ASavedFilterKeepsWorkingAfterTheFts5Migration()
	{
		const string saved = "cats MATCH '1b3 OR 1b7' AND docid IN (SELECT docid FROM search WHERE subject LIKE '%kerst%')";

		string rewritten = Rewrite(saved);

		Assert.Equal("cats MATCH '1b3 OR 1b7' AND rowid IN (SELECT rowid FROM search WHERE subject LIKE '%kerst%')", rewritten);
		// The point of the rewrite: the filter has to survive the compiler, which knows
		// `rowid` and no longer knows `docid`.
		Assert.Equal(2, FilterExpressionCompiler.Compile(rewritten).Values.Count);
	}

	[Theory]
	[InlineData("DocID IN (SELECT DOCID FROM search)", "rowid IN (SELECT rowid FROM search)")]
	[InlineData("cats MATCH '1'", "cats MATCH '1'")]
	[InlineData("", "")]
	[InlineData(null, null)]
	public void TheRewriteIsCaseInsensitiveAndLeavesEverythingElseAlone(string query, string expected)
	{
		Assert.Equal(expected, Rewrite(query));
	}

	[Fact]
	public void AWordMerelyContainingDocidIsNotTouched()
	{
		Assert.Equal("subject LIKE '%nodocidx%'", Rewrite("subject LIKE '%nodocidx%'"));
	}
}
