using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GTranslate.Results;
using GTranslate.Translators;
using NLog;
using Spotnet.Extensions;

namespace Spotnet.Services;

public class TranslationService : ITranslationService
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly Lazy<TranslationService> LazyInstance =
		new Lazy<TranslationService>(() => new TranslationService());

	public static TranslationService Instance => LazyInstance.Value;

	private readonly AggregateTranslator _translator;
	private readonly ConcurrentDictionary<string, string> _cache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
	private static readonly Regex HtmlTagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);
	private static readonly Regex TagPlaceholderRegex = new Regex(@"\[\[T_(\d+)\]\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	public TranslationService()
	{
		_translator = new AggregateTranslator();
	}

	/// <summary>
	/// Translates HTML or text to target language, preserving HTML tags.
	/// </summary>
	public async Task<string> TranslateAsync(
		string text,
		string targetLanguage,
		string sourceLanguage = "auto",
		CancellationToken cancellationToken = default)
	{
		if (text.IsNullOrWhiteSpace())
		{
			return text ?? string.Empty;
		}

		targetLanguage = (targetLanguage ?? "en").Trim().ToLowerInvariant();
		sourceLanguage = (sourceLanguage ?? "auto").Trim().ToLowerInvariant();

		string cacheKey = $"{sourceLanguage}->{targetLanguage}:{text}";
		if (_cache.TryGetValue(cacheKey, out string cached))
		{
			return cached;
		}

		// Mask HTML tags to protect markup, URLs, and image tags
		var (maskedText, tags) = MaskHtml(text);

		try
		{
			ITranslationResult result;
			if (sourceLanguage == "auto" || sourceLanguage.IsNullOrEmpty())
			{
				result = await _translator.TranslateAsync(maskedText, targetLanguage).ConfigureAwait(false);
			}
			else
			{
				result = await _translator.TranslateAsync(maskedText, targetLanguage, sourceLanguage).ConfigureAwait(false);
			}

			string translated = result?.Translation ?? maskedText;

			// Restore original HTML tags
			string unmasked = UnmaskHtml(translated, tags);

			_cache[cacheKey] = unmasked;
			return unmasked;
		}
		catch (Exception ex)
		{
			Log.Warn("Translation failed for text of length {0}: {1}", text.Length, ex.Message);
			throw;
		}
	}

	/// <summary>
	/// Translates multiple texts concurrently (concurrency capped to protect against rate limits).
	/// </summary>
	public async Task<IReadOnlyDictionary<string, string>> TranslateBatchAsync(
		IDictionary<string, string> idToTextMap,
		string targetLanguage,
		string sourceLanguage = "auto",
		CancellationToken cancellationToken = default)
	{
		var results = new ConcurrentDictionary<string, string>();
		if (idToTextMap == null || idToTextMap.Count == 0)
		{
			return results;
		}

		using var semaphore = new SemaphoreSlim(3, 3);
		var tasks = idToTextMap.Select(async kvp =>
		{
			await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				string translated = await TranslateAsync(kvp.Value, targetLanguage, sourceLanguage, cancellationToken).ConfigureAwait(false);
				results[kvp.Key] = translated;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				Log.Warn("Batch translation failed for item {0}: {1}", kvp.Key, ex.Message);
				results[kvp.Key] = kvp.Value; // Fallback to original
			}
			finally
			{
				semaphore.Release();
			}
		});

		await Task.WhenAll(tasks).ConfigureAwait(false);
		return results;
	}

	/// <summary>
	/// Replaces HTML tags with [[T_0]], [[T_1]], etc. so the machine translator doesn't modify tags.
	/// </summary>
	internal static (string Masked, List<string> Tags) MaskHtml(string html)
	{
		if (html.IsNullOrEmpty())
		{
			return (html ?? string.Empty, new List<string>());
		}

		var tags = new List<string>();
		string masked = HtmlTagRegex.Replace(html, match =>
		{
			int index = tags.Count;
			tags.Add(match.Value);
			return $"[[T_{index}]]";
		});

		return (masked, tags);
	}

	/// <summary>
	/// Restores the HTML tags by substituting the indexed placeholders.
	/// </summary>
	internal static string UnmaskHtml(string translatedText, IReadOnlyList<string> tags)
	{
		if (translatedText.IsNullOrEmpty() || tags == null || tags.Count == 0)
		{
			return translatedText ?? string.Empty;
		}

		return TagPlaceholderRegex.Replace(translatedText, match =>
		{
			if (match.Groups.Count > 1 && int.TryParse(match.Groups[1].Value, out int index) && index >= 0 && index < tags.Count)
			{
				return tags[index];
			}
			return match.Value;
		});
	}
}
