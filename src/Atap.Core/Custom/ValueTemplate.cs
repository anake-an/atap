using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Atap.Core.Custom;

/// <summary>
/// A checked <see cref="ValueTemplateSpec"/> with its paths parsed once. Rendering is pure: the payload,
/// the last view and the UI culture go in, a <see cref="ValueView"/> comes out.
/// </summary>
public sealed class ValueTemplate
{
    private readonly JsonPathLite _value;
    private readonly JsonPathLite? _max;
    private readonly JsonPathLite? _itemLabel;
    private readonly JsonPathLite? _itemValue;
    private readonly StatusMatcher _status;
    private readonly HashSet<string>? _noValue;
    private readonly string? _unit;
    private readonly int? _decimals;
    private readonly int _maxItems;

    private ValueTemplate(ValueTemplateSpec spec, JsonPathLite value, JsonPathLite? max, JsonPathLite? itemLabel, JsonPathLite? itemValue)
    {
        Spec = spec;
        _value = value;
        _max = max;
        _itemLabel = itemLabel;
        _itemValue = itemValue;
        _status = new StatusMatcher(
            spec.OkValues ?? StatusWords.DefaultOk,
            spec.WarningValues ?? StatusWords.DefaultWarning,
            spec.CriticalValues ?? StatusWords.DefaultCritical);
        _noValue = BuildNoValue(spec.NoValueWords);
        _unit = ValueText.CleanUnit(spec.Unit);
        _decimals = spec.Decimals is int d ? Math.Clamp(d, 0, ValueTemplateSpec.MaxDecimals) : null;
        _maxItems = Math.Clamp(spec.MaxItems, 1, ValueTemplateSpec.MaxItemsLimit);
    }

    public ValueTemplateSpec Spec { get; }

    public ValueTemplateKind Kind => Spec.Kind;

    /// <summary>
    /// Checks a spec and parses its paths. Only the settings that apply to the template are checked.
    /// An empty value path means <c>$</c>. Bad settings give an error, never an exception.
    /// </summary>
    public static bool TryCreate(ValueTemplateSpec spec, [NotNullWhen(true)] out ValueTemplate? template, [NotNullWhen(false)] out ValueTemplateError? error)
    {
        ArgumentNullException.ThrowIfNull(spec);
        template = null;

        JsonPathLite? value = JsonPathLite.Root;
        if (!string.IsNullOrWhiteSpace(spec.ValuePath) && !JsonPathLite.TryParse(spec.ValuePath, out value, out var valueError))
        {
            error = new ValueTemplateError(ValueTemplateField.ValuePath, valueError);
            return false;
        }

        JsonPathLite? max = null, itemLabel = null, itemValue = null;
        if (spec.Kind == ValueTemplateKind.Progress)
        {
            if (!TryParseOptional(spec.MaxPath, ValueTemplateField.MaxPath, out max, out error))
                return false;
            var top = spec.Max ?? 100;
            if (!double.IsFinite(spec.Min) || (max is null && !(double.IsFinite(top) && top > spec.Min)))
            {
                error = new ValueTemplateError(ValueTemplateField.Range);
                return false;
            }
        }
        else if (spec.Kind == ValueTemplateKind.List)
        {
            if (!TryParseOptional(spec.ItemLabelPath, ValueTemplateField.ItemLabelPath, out itemLabel, out error)
                || !TryParseOptional(spec.ItemValuePath, ValueTemplateField.ItemValuePath, out itemValue, out error))
                return false;
        }

        if (spec.Thresholds is { } thresholds && thresholds.Validate() is var problem and not ThresholdProblem.None)
        {
            error = new ValueTemplateError(ValueTemplateField.Thresholds, Threshold: problem);
            return false;
        }

        template = new ValueTemplate(spec, value, max, itemLabel, itemValue);
        error = null;
        return true;
    }

    /// <summary>Renders a read payload. A payload that could not be read gives a view with no value.</summary>
    /// <param name="previous">
    /// The view this template returned last time, even one with no value, or null at first. It carries the levels for hysteresis.
    /// A view with no value keeps the levels from before it, so a blip does not reset the band.
    /// </param>
    /// <param name="format">The UI culture for numbers. Null uses the current culture.</param>
    public ValueView Render(PayloadReadResult payload, ValueView? previous = null, IFormatProvider? format = null) => payload.Status switch
    {
        PayloadStatus.Ok => Render(payload.Root, previous, format),
        PayloadStatus.Empty => Keep(ValueView.Empty(Kind, ValueProblem.NoValue), previous),
        PayloadStatus.InvalidJson => Keep(ValueView.Empty(Kind, ValueProblem.InvalidJson), previous),
        PayloadStatus.InvalidText => Keep(ValueView.Empty(Kind, ValueProblem.InvalidText), previous),
        _ => Keep(ValueView.Empty(Kind, ValueProblem.TooLarge), previous),
    };

    /// <summary>Renders a JSON value. Never throws.</summary>
    /// <param name="root">The payload. Read the view before disposing the document it comes from.</param>
    /// <param name="previous">
    /// The view this template returned last time, even one with no value, or null at first. It carries the levels for hysteresis.
    /// A view with no value keeps the levels from before it, so a blip does not reset the band.
    /// </param>
    /// <param name="format">The UI culture for numbers. Null uses the current culture.</param>
    public ValueView Render(JsonElement root, ValueView? previous = null, IFormatProvider? format = null)
    {
        format ??= CultureInfo.CurrentCulture;
        var last = SameKind(previous);
        var previousLevel = last?.HysteresisLevel ?? ValueLevel.Unknown;

        var view = Kind switch
        {
            ValueTemplateKind.Number => RenderNumber(root, previousLevel, format),
            ValueTemplateKind.Text => RenderText(root, previousLevel, format),
            ValueTemplateKind.Progress => RenderProgress(root, previousLevel, format),
            ValueTemplateKind.List => RenderList(root, last, format),
            _ => RenderStatusDot(root, previousLevel, format),
        };
        return Keep(view, last);
    }

    private ValueView? SameKind(ValueView? view) => view is not null && view.Kind == Kind ? view : null;

    /// <summary>A view with no value takes the last levels along, so the next good value continues the hysteresis.</summary>
    private ValueView Keep(ValueView view, ValueView? previous)
    {
        if (view.HasValue || SameKind(previous) is not { } last)
            return view;
        return view with { HeldLevel = last.HysteresisLevel, ItemLevels = last.ItemLevels };
    }

    private ValueView RenderNumber(JsonElement root, ValueLevel previous, IFormatProvider format)
    {
        if (!TryValue(_value, root, out var element, out var problem))
            return ValueView.Empty(Kind, problem);
        if (!ValueText.TryGetNumber(element, out var number))
            return ValueView.Empty(Kind, ValueProblem.NotANumber);

        return new ValueView
        {
            Kind = Kind,
            Level = Thresholds.Evaluate(Spec.Thresholds, number, previous),
            Text = ValueText.FormatNumber(number, _decimals, format),
            Unit = _unit,
            Number = number,
        };
    }

    private ValueView RenderText(JsonElement root, ValueLevel previous, IFormatProvider format)
    {
        if (!TryValue(_value, root, out var element, out var problem))
            return ValueView.Empty(Kind, problem);
        var text = ValueText.Display(element, _decimals, format);
        if (text is null)
            return ValueView.Empty(Kind, ValueProblem.NotText);
        if (text.Length == 0)
            return ValueView.Empty(Kind, ValueProblem.NoValue);

        var isNumber = ValueText.TryGetNumber(element, out var number);
        return new ValueView
        {
            Kind = Kind,
            Level = isNumber && Spec.Thresholds is { IsEmpty: false }
                ? Thresholds.Evaluate(Spec.Thresholds, number, previous)
                : ValueLevel.Normal,
            Text = text,
            Unit = isNumber ? _unit : null,
            Number = isNumber ? number : null,
        };
    }

    private ValueView RenderProgress(JsonElement root, ValueLevel previous, IFormatProvider format)
    {
        if (!TryValue(_value, root, out var element, out var problem))
            return ValueView.Empty(Kind, problem);
        if (!ValueText.TryGetNumber(element, out var number))
            return ValueView.Empty(Kind, ValueProblem.NotANumber);

        var max = Spec.Max ?? 100;
        if (_max is not null && !(TryFirst(_max, root, out var maxElement, out _) && ValueText.TryGetNumber(maxElement, out max)))
            return ValueView.Empty(Kind, ValueProblem.BadMaximum);
        if (!(max > Spec.Min))
            return ValueView.Empty(Kind, ValueProblem.BadMaximum);

        var fraction = Math.Clamp((number - Spec.Min) / (max - Spec.Min), 0, 1);
        if (double.IsNaN(fraction))
            fraction = 0;
        var percent = fraction * 100;
        return new ValueView
        {
            Kind = Kind,
            Level = Thresholds.Evaluate(Spec.Thresholds, percent, previous),
            Text = ValueText.FormatNumber(percent, _decimals ?? 0, format),
            Unit = "%",
            Detail = ValueText.FormatNumber(number, _decimals, format) + " / "
                + ValueText.JoinUnit(ValueText.FormatNumber(max, _decimals, format), _unit),
            Number = number,
            Fraction = fraction,
        };
    }

    private ValueView RenderStatusDot(JsonElement root, ValueLevel previous, IFormatProvider format)
    {
        if (!TryValue(_value, root, out var element, out var problem))
            return ValueView.Empty(Kind, problem);
        var text = ValueText.Display(element, _decimals, format);
        if (text is null)
            return ValueView.Empty(Kind, ValueProblem.NotText);
        if (text.Length == 0)
            return ValueView.Empty(Kind, ValueProblem.NoValue);

        var isNumber = ValueText.TryGetNumber(element, out var number);
        return new ValueView
        {
            Kind = Kind,
            Level = LevelOf(element, previous),
            Text = text,
            Unit = isNumber ? _unit : null,
            Number = isNumber ? number : null,
        };
    }

    private ValueView RenderList(JsonElement root, ValueView? last, IFormatProvider format)
    {
        var result = _value.Evaluate(root);
        if (result.Outcome == JsonPathOutcome.Missing)
            return ValueView.Empty(Kind, ValueProblem.NotFound);

        var entries = new List<(JsonElement Item, string? Name)>();
        if (result.Found && _value.IsDefinite)
        {
            var single = result.Matches[0];
            switch (single.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in single.EnumerateArray())
                    {
                        if (entries.Count == JsonPathLite.MaxMatches)
                            break;
                        entries.Add((item, null));
                    }
                    break;
                case JsonValueKind.Object:
                    foreach (var property in single.EnumerateObject())
                    {
                        if (entries.Count == JsonPathLite.MaxMatches)
                            break;
                        entries.Add((property.Value, JsonText.GetName(property)));
                    }
                    break;
                case JsonValueKind.Null:
                    return ValueView.Empty(Kind, ValueProblem.NoValue);
                default:
                    return ValueView.Empty(Kind, ValueProblem.NotAList);
            }
        }
        else
        {
            foreach (var match in result.Matches)
                entries.Add((match, null));
        }

        // Hysteresis per item needs the item's last level, so each item gets a key that stays the same between refreshes.
        // Object members use their name, other items their label when ItemLabelPath is set, and otherwise their position.
        var previousLevels = last?.ItemLevels;
        var levels = new Dictionary<string, ValueLevel>(entries.Count, StringComparer.Ordinal);
        Dictionary<string, int>? seen = null;
        var items = new List<ValueListItem>(Math.Min(entries.Count, _maxItems));
        var worst = ValueLevel.Normal;

        for (var index = 0; index < entries.Count; index++)
        {
            var (item, name) = entries[index];
            var label = _itemLabel is not null
                ? (TryFirst(_itemLabel, item, out var labelElement, out _) ? ValueText.Display(labelElement, _decimals, format) : null)
                : name is not null ? ValueText.Clean(name) : ValueText.Display(item, _decimals, format);
            label ??= string.Empty;

            var key = name is not null ? ItemKey('m', name, ref seen)
                : _itemLabel is not null ? ItemKey('l', label, ref seen)
                : "i" + index.ToString(CultureInfo.InvariantCulture);

            string? valueText = null;
            var level = ValueLevel.Normal;
            var kept = level;
            // An object member is its own value unless it is an object or array, which needs ItemValuePath.
            var hasValueSource = _itemValue is not null
                || (name is not null && item.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array));
            if (hasValueSource)
            {
                var valueElement = item;
                var found = _itemValue is null ? !IsNoValueWord(item) : TryValue(_itemValue, item, out valueElement, out _);
                valueText = found ? ValueText.Display(valueElement, _decimals, format) : null;
                var previousLevel = previousLevels is not null && previousLevels.TryGetValue(key, out var known) ? known : ValueLevel.Unknown;
                level = valueText is null ? ValueLevel.Unknown : LevelOf(valueElement, previousLevel);
                // An item with no value keeps its last level, so a blip does not reset its band.
                kept = valueText is null ? previousLevel : level;
            }

            levels[key] = kept;
            if (level > worst)
                worst = level;
            if (items.Count < _maxItems)
                items.Add(new ValueListItem(label, valueText, level));
        }

        return new ValueView
        {
            Kind = Kind,
            Level = worst,
            Text = ValueText.FormatNumber(entries.Count, 0, format),
            Number = entries.Count,
            Items = items,
            ItemCount = entries.Count,
            ItemLevels = levels,
        };
    }

    /// <summary>
    /// The hysteresis key for an item with a name or label. The kind and the count of earlier items with the same text
    /// go first, so a repeated name or label gets its own key and keys of different kinds never match.
    /// </summary>
    private static string ItemKey(char kind, string text, ref Dictionary<string, int>? seen)
    {
        seen ??= new Dictionary<string, int>(StringComparer.Ordinal);
        ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(seen, text, out _);
        var key = string.Create(CultureInfo.InvariantCulture, $"{kind}{count}:{text}");
        count++;
        return key;
    }

    /// <summary>A level from the thresholds for a number when thresholds are set, otherwise from the status words.</summary>
    private ValueLevel LevelOf(JsonElement element, ValueLevel previous)
    {
        var isNumber = ValueText.TryGetNumber(element, out var number);
        if (isNumber && Spec.Thresholds is { IsEmpty: false })
            return Thresholds.Evaluate(Spec.Thresholds, number, previous);
        return _status.Match(ValueText.StatusText(element), isNumber ? number : null);
    }

    /// <summary>The first value a path finds. JSON null counts as no value.</summary>
    private static bool TryFirst(JsonPathLite path, JsonElement root, out JsonElement element, out ValueProblem problem)
    {
        if (!path.Evaluate(root).TryGetFirst(out element))
        {
            problem = ValueProblem.NotFound;
            return false;
        }
        if (element.ValueKind == JsonValueKind.Null)
        {
            problem = ValueProblem.NoValue;
            return false;
        }
        problem = ValueProblem.None;
        return true;
    }

    /// <summary><see cref="TryFirst"/> for a value, where a word from <see cref="ValueTemplateSpec.NoValueWords"/> also counts as no value.</summary>
    private bool TryValue(JsonPathLite path, JsonElement root, out JsonElement element, out ValueProblem problem)
    {
        if (!TryFirst(path, root, out element, out problem))
            return false;
        if (!IsNoValueWord(element))
            return true;
        problem = ValueProblem.NoValue;
        return false;
    }

    private bool IsNoValueWord(JsonElement element) =>
        _noValue is not null
        && element.ValueKind == JsonValueKind.String
        && _noValue.Contains(ValueText.Clean(JsonText.GetString(element)));

    private static HashSet<string>? BuildNoValue(IReadOnlyList<string>? source)
    {
        if (source is null)
            return null;
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source)
        {
            var word = ValueText.Clean(entry);
            if (word.Length > 0)
                words.Add(word);
        }
        return words.Count == 0 ? null : words;
    }

    private static bool TryParseOptional(string? path, ValueTemplateField field, out JsonPathLite? parsed, [NotNullWhen(false)] out ValueTemplateError? error)
    {
        parsed = null;
        error = null;
        if (string.IsNullOrWhiteSpace(path))
            return true;
        if (JsonPathLite.TryParse(path, out parsed, out var pathError))
            return true;
        error = new ValueTemplateError(field, pathError);
        return false;
    }

    /// <summary>Status words, matched without regard to case. Numeric words also match numbers by value.</summary>
    private sealed class StatusMatcher
    {
        private readonly (HashSet<string> Words, HashSet<double> Numbers)[] _levels;

        public StatusMatcher(IReadOnlyList<string> ok, IReadOnlyList<string> warning, IReadOnlyList<string> critical)
        {
            // Worst first, so a word in two lists takes the worse level.
            _levels = [Build(critical), Build(warning), Build(ok)];
        }

        public ValueLevel Match(string? text, double? number)
        {
            ReadOnlySpan<ValueLevel> order = [ValueLevel.Critical, ValueLevel.Warning, ValueLevel.Normal];
            for (var i = 0; i < _levels.Length; i++)
            {
                var (words, numbers) = _levels[i];
                if ((text is not null && words.Contains(text)) || (number is double n && numbers.Contains(n == 0 ? 0 : n)))
                    return order[i];
            }
            return ValueLevel.Unknown;
        }

        private static (HashSet<string>, HashSet<double>) Build(IReadOnlyList<string> source)
        {
            var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var numbers = new HashSet<double>();
            foreach (var entry in source)
            {
                var word = ValueText.Clean(entry);
                if (word.Length == 0)
                    continue;
                words.Add(word);
                if (ValueText.TryParseNumber(word, out var number))
                    numbers.Add(number == 0 ? 0 : number);
            }
            return (words, numbers);
        }
    }
}
