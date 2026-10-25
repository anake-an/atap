using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Atap.Core.Input;
using Atap.Core.Settings;
using Atap.Core.Widgets;

namespace Atap.Settings.Form;

/// <summary>
/// Secret: a password box. The value goes to the secret store and the settings file holds only its
/// "secret:&lt;id&gt;" reference, so it never reaches a JSON file or a sync bundle.
/// </summary>
internal sealed class SecretField : SettingField
{
    public const string UnavailableKey = "settingsform.secret.unavailable";

    private bool _isSaved;
    private bool _isFromOtherPc;

    public SecretField(SettingDefinition definition, SettingsFormContext form)
        : base(definition, form)
    {
        SaveCommand = new RelayCommand<object?>(parameter =>
        {
            if (parameter is not PasswordBox box)
                return;
            if (Save(box.Password))
                box.Clear();
        });
        RemoveCommand = new RelayCommand(Remove, () => _isSaved || _isFromOtherPc);
        Refresh(form.Target.Values);
    }

    public bool IsSaved => _isSaved;

    public string Note => Strings["settingsform.secret.note"];

    public string StatusText => Strings[
        !Form.Secrets.IsAvailable ? UnavailableKey
        : _isSaved ? "settingsform.secret.saved"
        : _isFromOtherPc ? "settingsform.secret.otherPc"
        : "settingsform.secret.notSaved"];

    public int MaxLength => Definition.MaxLength ?? 0;

    /// <summary>Takes the password box, reads it once and clears it after a save.</summary>
    public IRelayCommand<object?> SaveCommand { get; }

    public IRelayCommand RemoveCommand { get; }

    /// <summary>Stores the value with the secret store and writes only the reference. False when nothing was saved.</summary>
    public bool Save(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            SetError(SettingValidator.RequiredKey);
            return false;
        }
        var check = SettingValidator.Check(Definition, value);
        if (!check.IsValid)
        {
            SetError(check.MessageKey);
            return false;
        }
        if (!Form.Secrets.IsAvailable)
        {
            SetError(UnavailableKey);
            return false;
        }

        var old = Form.Target.Values.GetSecretRef(Key);
        string reference;
        try
        {
            reference = Form.Secrets.Put(value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            SetError(UnavailableKey);
            return false;
        }

        switch (WriteChecked(SettingCheck.Valid(reference)))
        {
            case SettingWriteResult.Applied or SettingWriteResult.Unchanged:
                // A write only queues the change. The old secret goes once the file on disk names the new one.
                if (old is not null && old != reference && Form.Target.Flush() == SettingWriteResult.Applied)
                    Form.Secrets.Delete(old);
                return true;
            case SettingWriteResult.Invalid:
                Form.Secrets.Delete(reference);
                return false;
            default:
                // Not saved to disk: this session uses the new secret and the file still names the old one,
                // so both are kept. Deleting the old one would lose it after a restart.
                return false;
        }
    }

    public override void Refresh(SettingValues values)
    {
        var reference = values.GetSecretRef(Key);
        _isSaved = reference is not null && Form.Secrets.IsAvailable && Form.Secrets.Get(reference) is not null;
        _isFromOtherPc = reference is not null && !_isSaved;
        OnPropertyChanged(nameof(IsSaved));
        OnPropertyChanged(nameof(StatusText));
        RemoveCommand?.NotifyCanExecuteChanged();
    }

    private void Remove()
    {
        var old = Form.Target.Values.GetSecretRef(Key);
        // Only once the file on disk no longer names it, or a failed write would leave the file pointing at nothing.
        if (WriteChecked(SettingCheck.Valid(null)) is SettingWriteResult.Applied && old is not null
            && Form.Target.Flush() == SettingWriteResult.Applied)
            Form.Secrets.Delete(old);
    }
}

/// <summary>Hotkey: a box that captures the keys pressed in it. A key another field in the form uses is refused.</summary>
internal sealed class HotkeyField : SettingField
{
    public const string TakenKey = "settingsform.error.hotkeyTaken";

    private string _chord = string.Empty;

    public HotkeyField(SettingDefinition definition, SettingsFormContext form)
        : base(definition, form)
    {
        ClearCommand = new RelayCommand(() => Commit(string.Empty));
        Refresh(form.Target.Values);
    }

    /// <summary>The stored chord, such as "Ctrl+Alt+K", or empty for none.</summary>
    public string Chord => _chord;

    public string DisplayText => _chord.Length > 0 ? _chord : Strings["settingsform.hotkey.none"];

    public string Prompt => Strings["settingsform.hotkey.prompt"];

    public IRelayCommand ClearCommand { get; }

    /// <summary>Set by the form: the label of another field that uses a chord, or null.</summary>
    internal Func<HotkeyField, string, string?>? FindConflict { get; set; }

    /// <summary>Called by the capture box with the keys held down. False when they were refused.</summary>
    public bool Capture(HotkeyModifiers modifiers, int virtualKey)
    {
        if (!HotkeyChord.TryCreate(modifiers, virtualKey, out var chord, out var error))
        {
            SetError(error switch
            {
                HotkeyError.MissingModifier => SettingValidator.HotkeyModifierKey,
                HotkeyError.ShiftOnly => SettingValidator.HotkeyShiftKey,
                _ => SettingValidator.HotkeyKey,
            });
            return false;
        }
        var text = chord.ToString();
        if (FindConflict?.Invoke(this, text) is { } owner)
        {
            SetError(TakenKey, ("name", owner));
            return false;
        }
        return Commit(text);
    }

    /// <summary>
    /// "Used by another app" when Windows refused these keys, which the hotkeys feature finds as it registers
    /// them. It runs before the form refreshes on a settings change, so a new chord shows its own result.
    /// </summary>
    public string? UsedElsewhere =>
        _chord.Length > 0 && Platform.Input.HotkeyConflicts.Keys.Contains(Key) ? Strings["settingsform.hotkey.usedElsewhere"] : null;

    public bool IsUsedElsewhere => UsedElsewhere is not null;

    public override void Refresh(SettingValues values)
    {
        _chord = values.GetString(Key);
        OnPropertyChanged(nameof(Chord));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(UsedElsewhere));
        OnPropertyChanged(nameof(IsUsedElsewhere));
    }
}

/// <summary>A time zone in the searchable list.</summary>
internal sealed record TimeZoneOption(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>TimeZone: a search box over the time zones this PC knows.</summary>
internal sealed class TimeZoneField : SettingField
{
    private const int MaxShown = 200;

    private IReadOnlyList<TimeZoneOption>? _zones;
    private IReadOnlyList<TimeZoneOption> _matches = [];
    private TimeZoneOption? _selected;
    private string _query = string.Empty;
    private string _current = string.Empty;

    public TimeZoneField(SettingDefinition definition, SettingsFormContext form)
        : base(definition, form)
    {
        Refresh(form.Target.Values);
        Filter();
    }

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? string.Empty))
                Filter();
        }
    }

    public string SearchName => Strings["settingsform.timeZone.search"];

    public IReadOnlyList<TimeZoneOption> Matches
    {
        get => _matches;
        private set => SetProperty(ref _matches, value);
    }

    public TimeZoneOption? Selected
    {
        get => _selected;
        set
        {
            if (value is not null && value.Id != _current)
                Commit(value.Id);
        }
    }

    /// <summary>The stored zone's name, or "None".</summary>
    public string CurrentName => Zones.FirstOrDefault(z => z.Id == _current)?.Name
        ?? (_current.Length > 0 ? _current : Strings["settingsform.hotkey.none"]);

    private IReadOnlyList<TimeZoneOption> Zones => _zones ??= TimeZoneInfo.GetSystemTimeZones()
        .Select(z => new TimeZoneOption(z.Id, z.DisplayName))
        .ToList();

    public override void Refresh(SettingValues values)
    {
        _current = values.GetString(Key);
        _selected = _matches.FirstOrDefault(z => z.Id == _current);
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(CurrentName));
    }

    private void Filter()
    {
        var words = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Matches = Zones
            .Where(z => words.All(w => z.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase) || z.Id.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Take(MaxShown)
            .ToList();
        _selected = _matches.FirstOrDefault(z => z.Id == _current);
        OnPropertyChanged(nameof(Selected));
    }
}

/// <summary>One item in a list field, with its own Remove button.</summary>
internal sealed record StringListItem(string Text, string RemoveName, ICommand Remove);

/// <summary>StringList: the items with a Remove button each, and a box with Add below.</summary>
internal sealed class StringListField : SettingField
{
    private string _newItem = string.Empty;

    public StringListField(SettingDefinition definition, SettingsFormContext form)
        : base(definition, form)
    {
        AddCommand = new RelayCommand(Add, () => !IsFull);
        Refresh(form.Target.Values);
    }

    public ObservableCollection<StringListItem> Items { get; } = [];

    public string NewItem
    {
        get => _newItem;
        set => SetProperty(ref _newItem, value ?? string.Empty);
    }

    public string NewItemName => Strings["settingsform.list.new"];

    public string EmptyText => Strings["settingsform.list.empty"];

    public bool IsEmpty => Items.Count == 0;

    public bool IsFull => Items.Count >= (Definition.MaxItems ?? SettingValidator.DefaultMaxItems);

    public int MaxLength => Definition.MaxLength ?? 0;

    public IRelayCommand AddCommand { get; }

    public override void Refresh(SettingValues values)
    {
        Items.Clear();
        var list = values.GetStringList(Key);
        for (var i = 0; i < list.Count; i++)
        {
            var index = i;
            Items.Add(new StringListItem(list[i], Strings.Format("settingsform.remove", ("item", list[i])), new RelayCommand(() => RemoveAt(index))));
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsFull));
        AddCommand?.NotifyCanExecuteChanged();
    }

    private void Add()
    {
        if (string.IsNullOrWhiteSpace(_newItem))
            return;
        if (Commit(Items.Select(i => i.Text).Append(_newItem).ToList()))
            NewItem = string.Empty;
    }

    private void RemoveAt(int index)
    {
        var list = Items.Select(i => i.Text).ToList();
        if (index < list.Count)
        {
            list.RemoveAt(index);
            Commit(list);
        }
    }
}

/// <summary>
/// Custom: shown with the template whose resource key is <see cref="SettingDefinition.EditorKey"/>, from the
/// owner's views. The template gets this field and writes through <see cref="Write"/>.
/// </summary>
internal sealed class CustomField : SettingField
{
    private JsonElement? _raw;

    public CustomField(SettingDefinition definition, SettingsFormContext form)
        : base(definition, form) => Refresh(form.Target.Values);

    public string? EditorKey => Definition.EditorKey;

    /// <summary>The stored value as JSON, or null when the default applies.</summary>
    public JsonElement? Raw => _raw;

    public string MissingText => Strings["settingsform.custom.missing"];

    /// <summary>The whole form's values, text, secrets and HTTP access, for an editor that needs more than its own value.</summary>
    internal SettingsFormContext Context => Form;

    public bool Write(object? value) => Commit(value);

    public override void Refresh(SettingValues values)
    {
        _raw = values.Raw.TryGetValue(Key, out var value) ? value : null;
        OnPropertyChanged(nameof(Raw));
    }
}
