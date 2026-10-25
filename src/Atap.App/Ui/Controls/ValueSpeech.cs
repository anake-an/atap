using System.Globalization;
using System.Windows.Data;
using Atap.Core.Custom;
using Atap.Core.Localization;

namespace Atap.Ui.Controls;

/// <summary>What a <see cref="ValueSpeech"/> converter makes.</summary>
public enum ValueSpeechPart
{
    /// <summary>The screen reader name for a whole <see cref="ValueView"/>.</summary>
    View,

    /// <summary>The screen reader name for one <see cref="ValueListItem"/>.</summary>
    Item,

    /// <summary>"3 more" under a List that shows only its first items, or empty.</summary>
    More,
}

/// <summary>
/// Text for the value templates, in the current language. Bound as a MultiBinding of the value and the
/// localizer's Language, so the text changes with a language switch.
/// </summary>
public sealed class ValueSpeech : IMultiValueConverter
{
    private const string Separator = ". ";

    public ValueSpeechPart Part { get; set; }

    /// <summary>
    /// For example "72.5%. Warning", "72%. 36.2 / 50 GB" or "No value right now".
    /// The level is said only for Warning and Critical, and "Out of date" is added to a stale view.
    /// </summary>
    public static string Describe(ValueView view, ILocalizer text)
    {
        if (!view.HasValue)
            return KitText.Problem(view.Problem, text);

        var main = view.Kind == ValueTemplateKind.List ? text.Format(KitText.Items, ("count", view.ItemCount)) : view.DisplayText;
        var detail = view.Kind == ValueTemplateKind.Progress ? view.Detail : null;
        // A status word that matches nothing is worth saying. Elsewhere Unknown only means no value.
        var status = view.Level == ValueLevel.Unknown && view.Kind == ValueTemplateKind.StatusDot
            ? KitText.Level(ValueLevel.Unknown, text)
            : KitText.Status(view.Level, text);
        return Join(main, detail, status, view.Stale ? text[KitText.Stale] : null);
    }

    /// <summary>For example "Disk C. 91%. Critical".</summary>
    public static string Describe(ValueListItem item, ILocalizer text) =>
        Join(item.Label, item.Value, item.Value is null ? null : KitText.Status(item.Level, text));

    /// <summary>"3 more" when a List has more items than it shows, otherwise empty.</summary>
    public static string More(ValueView view, ILocalizer text)
    {
        var hidden = view.ItemCount - view.Items.Count;
        return view.HasValue && view.Kind == ValueTemplateKind.List && hidden > 0
            ? text.Format(KitText.More, ("count", hidden))
            : string.Empty;
    }

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ILocalizer text = Localizer.Instance;
        return (Part, values.Length > 0 ? values[0] : null) switch
        {
            (ValueSpeechPart.View, ValueView view) => Describe(view, text),
            (ValueSpeechPart.Item, ValueListItem item) => Describe(item, text),
            (ValueSpeechPart.More, ValueView view) => More(view, text),
            _ => string.Empty,
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string Join(params string?[] parts) =>
        string.Join(Separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
