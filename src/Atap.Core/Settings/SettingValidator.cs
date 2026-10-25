using System.Globalization;
using Atap.Core.Input;
using Atap.Core.Widgets;

namespace Atap.Core.Settings;

/// <summary>The outcome of checking one value against its definition.</summary>
/// <param name="IsValid">False when the value cannot be stored. The last valid value then stays.</param>
/// <param name="Value">What to store: parsed, trimmed and clamped. Null removes the value, so the default applies.</param>
/// <param name="MessageKey">String key of the message shown under the field, or null when the value is fine.</param>
public readonly record struct SettingCheck(bool IsValid, object? Value, string? MessageKey)
{
    public static SettingCheck Valid(object? value) => new(true, value, null);

    public static SettingCheck Invalid(string messageKey) => new(false, null, messageKey);
}

/// <summary>
/// Checks a value from the settings UI against its <see cref="SettingDefinition"/> before it is written.
/// Pure: no files, no clock. The message keys are strings in the settings form's text.
/// </summary>
public static class SettingValidator
{
    /// <summary>Lists hold at most this many items when the definition sets no MaxItems.</summary>
    public const int DefaultMaxItems = 50;

    public const string RequiredKey = "settingsform.error.required";
    public const string NumberKey = "settingsform.error.number";
    public const string WholeNumberKey = "settingsform.error.wholeNumber";
    public const string UrlKey = "settingsform.error.url";
    public const string TimeKey = "settingsform.error.time";
    public const string ChoiceKey = "settingsform.error.choice";
    public const string TooLongKey = "settingsform.error.tooLong";
    public const string TooManyKey = "settingsform.error.tooMany";
    public const string HotkeyKey = "settingsform.error.hotkey";
    public const string HotkeyModifierKey = "settingsform.error.hotkeyModifier";
    public const string HotkeyShiftKey = "settingsform.error.hotkeyShift";
    public const string TimeZoneKey = "settingsform.error.timeZone";
    public const string PathKey = "settingsform.error.path";
    public const string InvalidKey = "settingsform.error.invalid";

    /// <summary>
    /// Checks a typed value: bool, a number, a string, a <see cref="TimeOnly"/>, a <see cref="TimeSpan"/> or a
    /// list of strings. Null always passes and means "back to the default".
    /// </summary>
    public static SettingCheck Check(SettingDefinition definition, object? value)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (value is null)
            return SettingCheck.Valid(null);

        return definition.Type switch
        {
            SettingType.Bool => value is bool ? SettingCheck.Valid(value) : SettingCheck.Invalid(InvalidKey),
            SettingType.Int => CheckInt(definition, value),
            SettingType.Double => CheckDouble(definition, value),
            SettingType.Duration => CheckDuration(definition, value),
            SettingType.Choice => CheckChoice(definition, value),
            SettingType.Time => CheckTime(definition, value),
            SettingType.Url => CheckUrl(definition, value),
            SettingType.Folder or SettingType.File => CheckPath(definition, value),
            SettingType.Hotkey => CheckHotkey(definition, value),
            SettingType.TimeZone => CheckTimeZone(definition, value),
            SettingType.StringList => CheckList(definition, value),
            SettingType.String or SettingType.Secret => CheckText(definition, value),
            SettingType.Custom => SettingCheck.Valid(value),
            _ => SettingCheck.Invalid(InvalidKey),
        };
    }

    /// <summary>
    /// Checks what the user typed. Numbers are read in <paramref name="culture"/> and then the invariant
    /// culture, without thousands separators. A Duration is typed in seconds.
    /// </summary>
    public static SettingCheck CheckText(SettingDefinition definition, string? text, IFormatProvider culture)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var trimmed = text?.Trim() ?? string.Empty;
        switch (definition.Type)
        {
            case SettingType.Int or SettingType.Duration:
                if (trimmed.Length == 0)
                    return definition.Required ? SettingCheck.Invalid(RequiredKey) : SettingCheck.Valid(null);
                if (TryParseNumber(trimmed, culture, out var whole))
                    return Check(definition, whole);
                return SettingCheck.Invalid(WholeNumberKey);
            case SettingType.Double:
                if (trimmed.Length == 0)
                    return definition.Required ? SettingCheck.Invalid(RequiredKey) : SettingCheck.Valid(null);
                return TryParseNumber(trimmed, culture, out var number) ? Check(definition, number) : SettingCheck.Invalid(NumberKey);
            case SettingType.Bool:
                return bool.TryParse(trimmed, out var flag) ? SettingCheck.Valid(flag) : SettingCheck.Invalid(InvalidKey);
            case SettingType.StringList:
                return Check(definition, (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.None));
            default:
                return Check(definition, text ?? string.Empty);
        }
    }

    private static bool TryParseNumber(string text, IFormatProvider culture, out double number) =>
        (double.TryParse(text, NumberStyles.Float, culture, out number)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        && double.IsFinite(number);

    private static bool TryGetNumber(object value, out double number)
    {
        number = value switch
        {
            int i => i,
            long l => l,
            short s => s,
            byte b => b,
            float f => f,
            double d => d,
            decimal m => (double)m,
            _ => double.NaN,
        };
        return double.IsFinite(number);
    }

    private static double Clamp(SettingDefinition definition, double number)
    {
        if (definition.Min is { } min && number < min)
            number = min;
        if (definition.Max is { } max && number > max)
            number = max;
        return number;
    }

    private static SettingCheck CheckInt(SettingDefinition definition, object value)
    {
        if (!TryGetNumber(value, out var number))
            return SettingCheck.Invalid(WholeNumberKey);
        if (number != Math.Floor(number))
            return SettingCheck.Invalid(WholeNumberKey);
        var clamped = Math.Clamp(Clamp(definition, number), int.MinValue, int.MaxValue);
        return SettingCheck.Valid((int)clamped);
    }

    private static SettingCheck CheckDouble(SettingDefinition definition, object value) =>
        TryGetNumber(value, out var number) ? SettingCheck.Valid(Clamp(definition, number)) : SettingCheck.Invalid(NumberKey);

    /// <summary>Whole seconds, never negative. Min and Max are in seconds too.</summary>
    private static SettingCheck CheckDuration(SettingDefinition definition, object value)
    {
        double seconds;
        if (value is TimeSpan span)
            seconds = Math.Round(span.TotalSeconds);
        else if (TryGetNumber(value, out var number) && number == Math.Floor(number))
            seconds = number;
        else
            return SettingCheck.Invalid(WholeNumberKey);
        var clamped = Math.Clamp(Clamp(definition, seconds), 0, int.MaxValue);
        return SettingCheck.Valid((long)clamped);
    }

    private static SettingCheck CheckChoice(SettingDefinition definition, object value) =>
        value is string choice && (definition.Choices is null || definition.Choices.Contains(choice, StringComparer.Ordinal))
            ? SettingCheck.Valid(choice)
            : SettingCheck.Invalid(ChoiceKey);

    /// <summary>"HH:mm". "7:30" is read too and stored as "07:30".</summary>
    private static SettingCheck CheckTime(SettingDefinition definition, object value)
    {
        if (value is TimeOnly time)
            return SettingCheck.Valid(time.ToString("HH:mm", CultureInfo.InvariantCulture));
        if (value is not string text)
            return SettingCheck.Invalid(TimeKey);
        text = text.Trim();
        if (text.Length == 0)
            return definition.Required ? SettingCheck.Invalid(RequiredKey) : SettingCheck.Valid(null);
        return TimeOnly.TryParseExact(text, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? SettingCheck.Valid(parsed.ToString("HH:mm", CultureInfo.InvariantCulture))
            : SettingCheck.Invalid(TimeKey);
    }

    /// <summary>An absolute http or https address with a host. Blank means "none" unless the field is required.</summary>
    private static SettingCheck CheckUrl(SettingDefinition definition, object value)
    {
        if (value is not string text)
            return SettingCheck.Invalid(UrlKey);
        text = text.Trim();
        if (text.Length == 0)
            return definition.Required ? SettingCheck.Invalid(RequiredKey) : SettingCheck.Valid(string.Empty);
        if (definition.MaxLength is { } maxLength && text.Length > maxLength)
            return SettingCheck.Invalid(TooLongKey);
        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(uri.Host)
            // A login inside the address would sit in plain text in the settings file and travel with sync.
            && string.IsNullOrEmpty(uri.UserInfo)
            ? SettingCheck.Valid(text)
            : SettingCheck.Invalid(UrlKey);
    }

    /// <summary>A full path such as C:\Music. Whether it exists is not checked here.</summary>
    private static SettingCheck CheckPath(SettingDefinition definition, object value)
    {
        if (value is not string text)
            return SettingCheck.Invalid(PathKey);
        text = text.Trim().Trim('"');
        if (text.Length == 0)
            return definition.Required ? SettingCheck.Invalid(RequiredKey) : SettingCheck.Valid(string.Empty);
        if (definition.MaxLength is { } maxLength && text.Length > maxLength)
            return SettingCheck.Invalid(TooLongKey);
        return text.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathFullyQualified(text)
            ? SettingCheck.Valid(text)
            : SettingCheck.Invalid(PathKey);
    }

    /// <summary>Stored as the canonical text, for example "Ctrl+Alt+K". Blank means no hotkey.</summary>
    private static SettingCheck CheckHotkey(SettingDefinition definition, object value)
    {
        var text = value switch
        {
            HotkeyChord chord => chord.ToString(),
            string s => s.Trim(),
            _ => null,
        };
        if (text is null)
            return SettingCheck.Invalid(HotkeyKey);
        if (text.Length == 0)
            return definition.Required ? SettingCheck.Invalid(RequiredKey) : SettingCheck.Valid(string.Empty);
        if (HotkeyChord.TryParse(text, out var parsed, out var error))
            return SettingCheck.Valid(parsed.ToString());
        return SettingCheck.Invalid(error switch
        {
            HotkeyError.MissingModifier => HotkeyModifierKey,
            HotkeyError.ShiftOnly => HotkeyShiftKey,
            _ => HotkeyKey,
        });
    }

    /// <summary>A time zone id this PC knows, Windows ("Singapore Standard Time") or IANA ("Asia/Kuala_Lumpur").</summary>
    private static SettingCheck CheckTimeZone(SettingDefinition definition, object value)
    {
        if (value is TimeZoneInfo zone)
            return SettingCheck.Valid(zone.Id);
        if (value is not string text)
            return SettingCheck.Invalid(TimeZoneKey);
        text = text.Trim();
        if (text.Length == 0)
            return definition.Required ? SettingCheck.Invalid(RequiredKey) : SettingCheck.Valid(string.Empty);
        return TimeZoneInfo.TryFindSystemTimeZoneById(text, out _) ? SettingCheck.Valid(text) : SettingCheck.Invalid(TimeZoneKey);
    }

    /// <summary>Items are trimmed and blank ones dropped. At most MaxItems (50 when not set).</summary>
    private static SettingCheck CheckList(SettingDefinition definition, object value)
    {
        if (value is string || value is not IEnumerable<string> items)
            return SettingCheck.Invalid(InvalidKey);
        var list = items.Select(item => item?.Trim() ?? string.Empty).Where(item => item.Length > 0).ToList();
        if (list.Count > (definition.MaxItems ?? DefaultMaxItems))
            return SettingCheck.Invalid(TooManyKey);
        if (definition.MaxLength is { } maxLength && list.Any(item => item.Length > maxLength))
            return SettingCheck.Invalid(TooLongKey);
        if (definition.Required && list.Count == 0)
            return SettingCheck.Invalid(RequiredKey);
        return SettingCheck.Valid(list);
    }

    /// <summary>Text is kept as typed. A required field cannot be blank.</summary>
    private static SettingCheck CheckText(SettingDefinition definition, object value)
    {
        if (value is not string text)
            return SettingCheck.Invalid(InvalidKey);
        if (definition.Required && string.IsNullOrWhiteSpace(text))
            return SettingCheck.Invalid(RequiredKey);
        if (definition.MaxLength is { } maxLength && text.Length > maxLength)
            return SettingCheck.Invalid(TooLongKey);
        return SettingCheck.Valid(text);
    }
}
