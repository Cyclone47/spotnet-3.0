using System.Collections.Generic;
using System.Threading.Tasks;
using Spotnet.Services;
using Xunit;

namespace Spotnet.Tests;

public class TranslationServiceTests
{
	[Fact]
	public void MaskHtml_And_UnmaskHtml_PreservesMarkup()
	{
		string original = "Dit is <b>vetgedrukt</b> en heeft een <a href=\"https://spotnet.example/test\">link</a> en een break.<br>";
		var (masked, tags) = TranslationService.MaskHtml(original);

		Assert.Contains("[[T_0]]", masked);
		Assert.Contains("[[T_1]]", masked);
		Assert.Equal(5, tags.Count);
		Assert.Equal("<b>", tags[0]);
		Assert.Equal("</b>", tags[1]);
		Assert.Equal("<a href=\"https://spotnet.example/test\">", tags[2]);
		Assert.Equal("</a>", tags[3]);
		Assert.Equal("<br>", tags[4]);

		// Simulate translation of the text parts while keeping placeholders
		string simulatedTranslated = masked.Replace("Dit is", "This is")
		                                   .Replace("vetgedrukt", "bold")
		                                   .Replace("en heeft een", "and has a")
		                                   .Replace("link", "link")
		                                   .Replace("en een break.", "and a break.");

		string unmasked = TranslationService.UnmaskHtml(simulatedTranslated, tags);

		Assert.Contains("<b>bold</b>", unmasked);
		Assert.Contains("<a href=\"https://spotnet.example/test\">link</a>", unmasked);
		Assert.EndsWith("<br>", unmasked.Trim());
	}

	[Fact]
	public async Task TranslateAsync_TranslatesDutchToEnglish()
	{
		var service = new TranslationService();
		string dutchText = "Hallo wereld, dit is een testbericht.";

		string result = await service.TranslateAsync(dutchText, "en", "nl");

		Assert.NotNull(result);
		Assert.NotEmpty(result);
		Assert.Contains("Hello", result, System.StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task TranslateAsync_CachesSubsequentCalls()
	{
		var service = new TranslationService();
		string input = "Goedemorgen allemaal";

		string result1 = await service.TranslateAsync(input, "en", "nl");
		string result2 = await service.TranslateAsync(input, "en", "nl");

		Assert.Equal(result1, result2);
	}

	[Fact]
	public async Task TranslateBatchAsync_TranslatesMultipleEntries()
	{
		var service = new TranslationService();
		var batch = new Dictionary<string, string>
		{
			{ "c1", "Bedankt voor de post!" },
			{ "c2", "Werkt perfect hier." }
		};

		var results = await service.TranslateBatchAsync(batch, "en", "nl");

		Assert.Equal(2, results.Count);
		Assert.Contains("Thanks", results["c1"], System.StringComparison.OrdinalIgnoreCase);
	}
}
