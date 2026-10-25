using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core.Localization;
using Atap.Core.Net;
using Atap.Core.Settings;
using Atap.Core.Storage;
using Atap.Core.Widgets;

namespace Atap.Settings.Form;

/// <summary>Picks files and folders for path fields. Null in the context hides the Browse button.</summary>
internal interface IPathPicker
{
    /// <summary>The chosen folder, or null when the user cancelled.</summary>
    string? PickFolder(string? start);

    /// <summary>The chosen file, or null when the user cancelled.</summary>
    string? PickFile(string? start);
}

/// <summary>What every field in one form shares.</summary>
/// <param name="Target">Where the values live.</param>
/// <param name="Text">UI text. Fields follow its language.</param>
/// <param name="Secrets">Where Secret fields keep their values. Settings files hold only the reference.</param>
/// <param name="Picker">File and folder dialogs, or null when there are none (tests).</param>
/// <param name="Http">The owner's own HTTP access, for a custom editor that tests a link. Null when the form has none.</param>
internal sealed record SettingsFormContext(ISettingsTarget Target, ILocalizer Text, ISecretStore Secrets, IPathPicker? Picker = null, IHttpDataService? Http = null);

/// <summary>
/// One generated field. It shows the stored value, checks new input with <see cref="SettingValidator"/>,
/// writes valid input to the target, and keeps the last valid value with a message under the field when
/// the input is not valid.
/// </summary>
internal abstract class SettingField : ObservableObject
{
    public const string NotSavedKey = "settingsform.error.notSaved";
    public const string ReadOnlyKey = "settingsform.error.readOnly";

    private string? _errorKey;
    private (string Name, object? Value)[] _errorArgs = [];
    private bool _isVisible = true;

    protected SettingField(SettingDefinition definition, SettingsFormContext form)
    {
        Definition = definition;
        Form = form;
    }

    public SettingDefinition Definition { get; }

    public string Key => Definition.Key;

    public string Label => Strings[Definition.Label];

    public string? Description => Definition.DescriptionKey is { } key ? Strings[key] : null;

    public bool HasDescription => Definition.DescriptionKey is not null;

    /// <summary>The message under the field, or null.</summary>
    public string? Error => _errorKey is null ? null : Strings.Format(_errorKey, _errorArgs);

    public bool HasError => _errorKey is not null;

    /// <summary>What a screen reader adds after the label: the description, then any message.</summary>
    public string HelpText => string.Join(" ", new[] { Description, Error }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>False while <see cref="SettingDefinition.VisibleWhen"/> does not match.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        internal set => SetProperty(ref _isVisible, value);
    }

    /// <summary>The message key shown right now, for tests.</summary>
    internal string? ErrorKey => _errorKey;

    protected SettingsFormContext Form { get; }

    protected ILocalizer Strings => Form.Text;

    /// <summary>Builds the field for a definition's type.</summary>
    public static SettingField Create(SettingDefinition definition, SettingsFormContext form) => definition.Type switch
    {
        SettingType.Bool => new ToggleField(definition, form),
        SettingType.Int or SettingType.Double => new NumberField(definition, form),
        SettingType.Choice => new ChoiceField(definition, form),
        SettingType.Secret => new SecretField(definition, form),
        SettingType.Duration => new DurationField(definition, form),
        SettingType.Hotkey => new HotkeyField(definition, form),
        SettingType.TimeZone => new TimeZoneField(definition, form),
        SettingType.StringList => new StringListField(definition, form),
        SettingType.Custom => new CustomField(definition, form),
        _ => new TextField(definition, form),
    };

    /// <summary>Shows the stored value again. Messages stay until the next input.</summary>
    public abstract void Refresh(SettingValues values);

    internal void OnLanguageChanged()
    {
        RebuildText();
        OnPropertyChanged(string.Empty);
        Refresh(Form.Target.Values);
    }

    /// <summary>Rebuilds lists of labels after a language switch.</summary>
    protected virtual void RebuildText()
    {
    }

    /// <summary>True when the target stores a value for this key, so the default does not apply.</summary>
    protected bool HasStoredValue(SettingValues values) => values.Raw.ContainsKey(Key);

    /// <summary>Checks and writes a typed value. False when it was not valid.</summary>
    protected bool Commit(object? value) => Commit(SettingValidator.Check(Definition, value));

    /// <summary>Checks and writes typed text. False when it was not valid.</summary>
    protected bool CommitText(string? text) => Commit(SettingValidator.CheckText(Definition, text, Strings.Culture));

    protected bool Commit(SettingCheck check) => WriteChecked(check) is not SettingWriteResult.Invalid;

    /// <summary>Checks and writes, and says what happened. Invalid when the check or the target refused the value.</summary>
    protected SettingWriteResult WriteChecked(SettingCheck check)
    {
        if (!check.IsValid)
        {
            SetError(check.MessageKey ?? SettingValidator.InvalidKey);
            return SettingWriteResult.Invalid;
        }
        var result = Form.Target.Write(Key, check.Value);
        Refresh(Form.Target.Values);
        SetError(result switch
        {
            SettingWriteResult.Invalid => SettingValidator.InvalidKey,
            SettingWriteResult.Failed => NotSavedKey,
            SettingWriteResult.ReadOnly => ReadOnlyKey,
            _ => null,
        });
        return result;
    }

    protected void SetError(string? key, params (string Name, object? Value)[] args)
    {
        if (_errorKey == key && args.Length == 0 && _errorArgs.Length == 0)
            return;
        _errorKey = key;
        _errorArgs = args;
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HelpText));
    }
}
