using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace Spotnet.Localization;

/// <summary>
/// <c>{loc:Loc SomeKey}</c> - the live replacement for <c>{x:Static p:Words.SomeKey}</c>.
/// Produces a one-way binding against <see cref="TranslationSource.Words"/>, so the label
/// follows the language the user picks instead of the one the window was built with.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    /// <summary>The resx key, without the resource class prefix.</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; }

    /// <summary>Optional, for the few places that post-process the text (upper-casing, for one).</summary>
    public IValueConverter Converter { get; set; }

    public object ConverterParameter { get; set; }

    public string StringFormat { get; set; }

    /// <summary>Which resource set the key is looked up in.</summary>
    protected virtual TranslationSource Source => TranslationSource.Words;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key)) return string.Empty;

        var binding = new Binding("[" + Key + "]")
        {
            Source = Source,
            Mode = BindingMode.OneWay,
            Converter = Converter,
            ConverterParameter = ConverterParameter,
            StringFormat = StringFormat
        };

        // Hand back the BindingExpression for a normal property target, or the Binding
        // itself inside a template or setter - Binding.ProvideValue decides which.
        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>
/// <c>{loc:Cat SomeKey}</c> - the same thing against the category names.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class CatExtension : LocExtension
{
    public CatExtension()
    {
    }

    public CatExtension(string key)
        : base(key)
    {
    }

    protected override TranslationSource Source => TranslationSource.Categories;
}
