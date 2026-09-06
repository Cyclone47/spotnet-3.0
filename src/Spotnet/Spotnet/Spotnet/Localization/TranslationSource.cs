using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows;

namespace Spotnet.Localization;

/// <summary>
/// Binding source that hands out resx strings by key and can announce that every one of
/// them changed at once.
///
/// XAML used to reach the resources through <c>{x:Static p:Words.Something}</c>, which is
/// resolved once while the tree is built and never looked at again - which is why
/// switching the language used to need a restart. Markup goes through
/// <see cref="LocExtension"/> instead, which binds to the indexer below, so raising
/// <c>Item[]</c> makes every live window re-read its labels.
/// </summary>
public sealed class TranslationSource : INotifyPropertyChanged
{
    /// <summary>The path WPF listens on for "every indexer value changed".</summary>
    private static readonly PropertyChangedEventArgs AllKeys = new PropertyChangedEventArgs("Item[]");

    /// <summary>Backs <c>{loc:Loc Key}</c>.</summary>
    public static TranslationSource Words { get; } = new TranslationSource(
        () => global::Spotnet.Properties.Words.ResourceManager,
        () => global::Spotnet.Properties.Words.Culture);

    /// <summary>Backs <c>{loc:Cat Key}</c>.</summary>
    public static TranslationSource Categories { get; } = new TranslationSource(
        () => global::Spotnet.Properties.Categories.ResourceManager,
        () => global::Spotnet.Properties.Categories.Culture);

    private readonly Func<ResourceManager> _resourceManager;

    private readonly Func<CultureInfo> _culture;

    private TranslationSource(Func<ResourceManager> resourceManager, Func<CultureInfo> culture)
    {
        _resourceManager = resourceManager;
        _culture = culture;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>
    /// The string for <paramref name="key"/> in the current culture. Missing keys return
    /// the key itself: a designer typo should show up as visible text rather than take a
    /// window down with a binding exception.
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            try
            {
                return _resourceManager().GetString(key, _culture()) ?? key;
            }
            catch (Exception)
            {
                return key;
            }
        }
    }

    /// <summary>Tells both sets to re-read, on the UI thread. Call after the culture changes.</summary>
    public static void InvalidateAll()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(InvalidateAll);
            return;
        }

        Words.Invalidate();
        Categories.Invalidate();
    }

    /// <summary>Re-reads every string bound against this set.</summary>
    public void Invalidate()
    {
        PropertyChanged?.Invoke(this, AllKeys);
    }
}
