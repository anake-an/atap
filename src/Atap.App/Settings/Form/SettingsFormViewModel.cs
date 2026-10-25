using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core.Localization;
using Atap.Core.Widgets;

namespace Atap.Settings.Form;

/// <summary>Fields that share a <see cref="SettingDefinition.GroupKey"/>, under its title.</summary>
internal sealed class SettingGroup : ObservableObject
{
    private readonly ILocalizer _text;

    public SettingGroup(string? titleKey, IReadOnlyList<SettingField> fields, ILocalizer text)
    {
        TitleKey = titleKey;
        Fields = fields;
        _text = text;
    }

    public string? TitleKey { get; }

    public string? Title => TitleKey is null ? null : _text[TitleKey];

    public bool HasTitle => TitleKey is not null;

    public IReadOnlyList<SettingField> Fields { get; }

    internal void OnLanguageChanged() => OnPropertyChanged(nameof(Title));
}

/// <summary>
/// The generated form for a list of definitions: app settings for a page, or one widget instance's settings.
/// Fields are grouped by GroupKey in the order the groups first appear. Fields marked Advanced go under
/// "More options". Fields follow the target and the UI language until the form is disposed.
/// </summary>
internal sealed class SettingsFormViewModel : ObservableObject, IDisposable
{
    private readonly SettingsFormContext _context;
    private readonly List<SettingField> _fields;
    private readonly List<SettingGroup> _groups;
    private bool _disposed;

    /// <param name="definitions">What to show. Duplicate keys after the first are left out.</param>
    /// <param name="context">The target, text and secrets. A disposable target is disposed with the form.</param>
    /// <param name="splitAdvanced">False shows Advanced fields with the rest, for a form that is already under "More options".</param>
    /// <param name="keyOrder">Keys to show first, in this order. The rest keep their order.</param>
    public SettingsFormViewModel(
        IEnumerable<SettingDefinition> definitions,
        SettingsFormContext context,
        bool splitAdvanced = true,
        IReadOnlyList<string>? keyOrder = null)
    {
        _context = context;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = definitions
            .Where(d => seen.Add(d.Key))
            .Select((d, index) => (Definition: d, Index: index))
            .OrderBy(p => Rank(keyOrder, p.Definition.Key, p.Index))
            .Select(p => p.Definition)
            .ToList();

        _fields = ordered.Select(d => SettingField.Create(d, context)).ToList();
        foreach (var hotkey in _fields.OfType<HotkeyField>())
            hotkey.FindConflict = FindHotkeyConflict;

        var main = _fields.Where(f => !splitAdvanced || !f.Definition.Advanced).ToList();
        _groups = main
            .GroupBy(f => f.Definition.GroupKey ?? string.Empty, StringComparer.Ordinal)
            .Select(g => new SettingGroup(g.Key.Length == 0 ? null : g.Key, g.ToList(), context.Text))
            .ToList();
        AdvancedFields = splitAdvanced ? _fields.Where(f => f.Definition.Advanced).ToList() : [];

        UpdateVisibility();
        _context.Target.Changed += OnTargetChanged;
        _context.Text.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<SettingGroup> Groups => _groups;

    /// <summary>Shown under "More options".</summary>
    public IReadOnlyList<SettingField> AdvancedFields { get; }

    public bool HasAdvanced => AdvancedFields.Count > 0;

    public IReadOnlyList<SettingField> Fields => _fields;

    public bool IsEmpty => _fields.Count == 0;

    public string MoreOptions => _context.Text["settingsform.moreOptions"];

    public string MoreOptionsHint => _context.Text["settingsform.moreOptionsHint"];

    public SettingField? Find(string key) => _fields.Find(f => f.Key == key);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _context.Target.Changed -= OnTargetChanged;
        _context.Text.LanguageChanged -= OnLanguageChanged;
        (_context.Target as IDisposable)?.Dispose();
    }

    private static int Rank(IReadOnlyList<string>? keyOrder, string key, int index)
    {
        var position = keyOrder is null ? -1 : IndexOf(keyOrder, key);
        return position >= 0 ? position : (keyOrder?.Count ?? 0) + index;
    }

    private static int IndexOf(IReadOnlyList<string> keys, string key)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (string.Equals(keys[i], key, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    private string? FindHotkeyConflict(HotkeyField field, string chord) =>
        _fields.OfType<HotkeyField>()
            .FirstOrDefault(other => other != field && string.Equals(other.Chord, chord, StringComparison.OrdinalIgnoreCase))
            ?.Label;

    private void OnTargetChanged(object? sender, EventArgs e)
    {
        var values = _context.Target.Values;
        foreach (var field in _fields)
            field.Refresh(values);
        UpdateVisibility();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var field in _fields)
            field.OnLanguageChanged();
        foreach (var group in _groups)
            group.OnLanguageChanged();
        OnPropertyChanged(nameof(MoreOptions));
        OnPropertyChanged(nameof(MoreOptionsHint));
    }

    /// <summary>A field with VisibleWhen shows only while the other setting has that value.</summary>
    private void UpdateVisibility()
    {
        var values = _context.Target.Values;
        foreach (var field in _fields)
        {
            if (field.Definition.VisibleWhen is not { } condition)
                continue;
            field.IsVisible = condition.Value switch
            {
                bool flag => values.GetBool(condition.Key) == flag,
                int number => values.GetInt(condition.Key) == number,
                double number => values.GetDouble(condition.Key) == number,
                string text => string.Equals(values.GetString(condition.Key), text, StringComparison.Ordinal),
                _ => values.Raw.TryGetValue(condition.Key, out var raw) && raw.ToString() == Convert.ToString(condition.Value, System.Globalization.CultureInfo.InvariantCulture),
            };
        }
    }
}
