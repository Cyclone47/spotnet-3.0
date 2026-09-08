using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Spotnet.Services;

public interface ITranslationService
{
	/// <summary>
	/// Translates the given text (or HTML snippet) to the target language.
	/// </summary>
	/// <param name="text">Text or HTML markup to translate.</param>
	/// <param name="targetLanguage">Target language code (e.g. "en", "nl", "de").</param>
	/// <param name="sourceLanguage">Source language code or "auto".</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>Translated text with HTML structure preserved.</returns>
	Task<string> TranslateAsync(string text, string targetLanguage, string sourceLanguage = "auto", CancellationToken cancellationToken = default);

	/// <summary>
	/// Translates multiple texts or HTML snippets in parallel with controlled concurrency.
	/// </summary>
	Task<IReadOnlyDictionary<string, string>> TranslateBatchAsync(
		IDictionary<string, string> idToTextMap,
		string targetLanguage,
		string sourceLanguage = "auto",
		CancellationToken cancellationToken = default);
}
