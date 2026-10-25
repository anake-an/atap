using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Atap.Core.Settings;
using Atap.Core.Widgets;

namespace Atap.Settings.Form;

/// <summary>Bool: a toggle switch with "On" or "Off" next to it.</summary>
internal sealed class ToggleField : SettingField
{
    private bool _isOn;

    public ToggleField(SettingDefinition definition, SettingsFormContext form)
        : base(definition, form) => Refresh(form.Target.Values);

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn != value)
                Commit(value);
        }
    }

    public string StateText => Strings[_isOn ? "settingsform.on" : "settingsform.off"];

    public override void Refresh(SettingValues values)
    {
        _isOn = values.GetBool(Key);
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(StateText));
    }
}

/// <summary>Int and Double: a number box, plus a slider when Min and Max are both set.</summary>
internal sealed class NumberField : SettingField
{
    private string _text = string.Empty;
    private double _number;

    public NumberField(SettingDefinition definition, SettingsFormContext form)
        : base(definition, form) => Refresh(form.Target.Values);

    /// <summary>What the number box shows. Written when the box loses focus or on Enter.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? string.Empty))
                CommitText(_text);
        }
    }

    public bool HasSlider => Definition is { Min: not null, Max: not null };

    public double Minimum => Definition.Min ?? 0;

    public double Maximum => Definition.Max ?? 100;

    public double Step => Definition.Step ?? (Definition.Type == SettingType.Int ? 1 : (Maximum - Minimum) / 100);

    /// <summary>The slider's value. The view delays this binding, so a drag writes once it settles.</summary>
    public double SliderValue
    {
        get => _number;
        set
        {
            // A hidden slider still has a 0 to 100 range, and a value it clamped must never be written back.
            if (!HasSlider)
                return;
            var number = Definition.Type == SettingType.Int ? Math.Round(value) : value;
            if (number != _number)
                Commit(number);
        }
    }

    public string? UnitText => Definition.UnitKey is { } key ? Strings[key] : null;

    public bool HasUnit => Definition.UnitKey is not null;

    public override void Refresh(SettingValues values)
    {
        _number = Definition.Type == SettingType.Int ? values.GetInt(Key) : values.GetDouble(Key);
        _text = Definition.Type == SettingType.Int
            ? ((int)_number).ToString(Strings.Culture)
            : _number.ToString("0.###", Strings.Culture);
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(SliderValue));
    }
}

/// <summary>One choice with its label, "&lt;Label&gt;.&lt;choice&gt;" in the strings.</summary>
internal sealed record ChoiceOption(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Choice: a segmented control for three choices or fewer, otherwise a drop-down list.</summary>
internal sealed class ChoiceField : SettingField
{
    private IReadOnlyList<ChoiceOption> _options = [];
    private ChoiceOption? _selected;

    public ChoiceField(SettingDefinition definition, SettingsFormContext form)
        : base(definition, form)
    {
        RebuildText();
        Refresh(form.Target.Values);
    }

    public IReadOnlyList<ChoiceOption> Options
    {
        get => _options;
        private set => SetProperty(ref _options, value);
    }

    public bool IsSegmented => Options.Count <= 3;

    public ChoiceOption? Selected
    {
        get => _selected;
        set
        {
            if (value is not null && value != _selected)
                Commit(value.Value);
        }
    }

    public override void Refresh(SettingValues values)
    {
        var current = values.GetString(Key);
        _selected = Options.FirstOrDefault(o => o.Value == current);
        OnPropertyChanged(nameof(Selected));
    }

    protected override void RebuildText() =>
        Options = (Definition.Choices ?? []).Select(c => new ChoiceOption(c, Strings[$"{Definition.Label}.{c}"])).ToList();
}

/// <summary>String, Url, Time, Folder and File: a text box. Folder and File add a Browse button.</summary>
internal sealed class TextField : SettingField
{
    private string _text = string.Empty;

    public TextField(SettingDefinition definition, SettingsFormContext form)
        : base(definition, form)
    {
        BrowseCommand = new RelayCommand(Browse, () => CanBrowse);
        Refresh(form.Target.Values);
    }

    /// <summary>Written when the box loses focus or on Enter.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? string.Empty))
                CommitText(_text);
        }
    }

    /// <summary>A short example under the box, for addresses and times.</summary>
    public string? Example => Definition.Type switch
    {
        SettingType.Url => Strings["settingsform.url.example"],
        SettingType.Time => Strings["settingsform.time.example"],
        _ => null,
    };

    public bool HasExample => Definition.Type is SettingType.Url or SettingType.Time;

    public int MaxLength => Definition.MaxLength ?? 0;

    public bool CanBrowse => Definition.Type is SettingType.Folder or SettingType.File && Form.Picker is not null;

    public IRelayCommand BrowseCommand { get; }

    public override void Refresh(SettingValues values)
    {
        _text = Definition.Type switch
        {
            SettingType.Time => HasStoredValue(values) || Definition.DefaultValue is not null
                ? values.GetTime(Key).ToString("HH:mm", CultureInfo.InvariantCulture)
                : string.Empty,
            _ => values.GetString(Key),
        };
        OnPropertyChanged(nameof(Text));
    }

    private void Browse()
    {
        var start = string.IsNullOrWhiteSpace(_text) ? null : _text;
        var picked = Definition.Type == SettingType.Folder ? Form.Picker?.PickFolder(start) : Form.Picker?.PickFile(start);
        if (picked is not null)
            Text = picked;
    }
}

/// <summary>One unit of a duration field.</summary>
internal sealed record DurationUnit(int Seconds, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Duration: a number and a unit (seconds, minutes or hours). Stored as whole seconds.</summary>
internal sealed class DurationField : SettingField
{
    private IReadOnlyList<DurationUnit> _units = [];
    private DurationUnit? _unit;
    private string _text = string.Empty;

    public DurationField(SettingDefinition definition, SettingsFormContext form)
        : base(definition, form)
    {
        RebuildText();
        Refresh(form.Target.Values);
    }

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? string.Empty))
                CommitAmount();
        }
    }

    public IReadOnlyList<DurationUnit> Units
    {
        get => _units;
        private set => SetProperty(ref _units, value);
    }

    /// <summary>Changing the unit keeps the number, so "5 minutes" becomes "5 hours".</summary>
    public DurationUnit? Unit
    {
        get => _unit;
        set
        {
            if (value is not null && SetProperty(ref _unit, value))
                CommitAmount();
        }
    }

    public string UnitName => Strings["settingsform.duration.unit"];

    public override void Refresh(SettingValues values)
    {
        var seconds = (long)values.GetDuration(Key).TotalSeconds;
        // The largest unit that fits evenly, so 300 seconds shows as 5 minutes. Zero keeps the unit in use.
        var unit = seconds > 0
            ? _units.Last(u => seconds % u.Seconds == 0)
            : _units.FirstOrDefault(u => u.Seconds == _unit?.Seconds) ?? _units[0];
        _unit = unit;
        _text = (seconds / unit.Seconds).ToString(Strings.Culture);
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Unit));
    }

    protected override void RebuildText()
    {
        Units =
        [
            new(1, Strings["settingsform.unit.seconds"]),
            new(60, Strings["settingsform.unit.minutes"]),
            new(3600, Strings["settingsform.unit.hours"]),
        ];
        _unit = _units.FirstOrDefault(u => u.Seconds == _unit?.Seconds);
    }

    private void CommitAmount()
    {
        var unit = _unit ?? _units[0];
        var text = _text.Trim();
        if (text.Length == 0)
        {
            CommitText(text);
            return;
        }
        if (!TryParse(text, Strings.Culture, out var amount) && !TryParse(text, CultureInfo.InvariantCulture, out amount))
        {
            SetError(SettingValidator.NumberKey);
            return;
        }
        Commit(Math.Round(amount * unit.Seconds, 6));
    }

    private static bool TryParse(string text, IFormatProvider culture, out double amount) =>
        double.TryParse(text, NumberStyles.Float, culture, out amount) && double.IsFinite(amount);
}
