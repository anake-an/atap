namespace Atap.Core.Widgets;

public enum SettingType
{
    Bool,
    Int,
    String,

    /// <summary>A string limited to <see cref="SettingDefinition.Choices"/>.</summary>
    Choice,
    Double,

    /// <summary>"HH:mm".</summary>
    Time,

    /// <summary>Whole seconds.</summary>
    Duration,

    /// <summary>An absolute http or https URL.</summary>
    Url,

    /// <summary>Stored as "secret:&lt;id&gt;". The value lives in secrets.json with Windows data protection.</summary>
    Secret,
    Folder,
    File,
    Hotkey,
    TimeZone,
    StringList,

    /// <summary>Edited with the template named by <see cref="SettingDefinition.EditorKey"/>.</summary>
    Custom,
}

/// <summary>Whether a value follows the user to other PCs through sync.</summary>
public enum SettingScope
{
    Synced,

    /// <summary>Never leaves this PC.</summary>
    Machine,
}

/// <summary>
/// Describes one setting so the app can generate a settings UI for it.
/// </summary>
/// <param name="Key">Key used in the settings or layout file.</param>
/// <param name="Label">String key of the label. Choice labels use "&lt;Label&gt;.&lt;choice&gt;".</param>
/// <param name="Type">Value type.</param>
/// <param name="DefaultValue">Used when the file has no valid value. Must match <paramref name="Type"/>.</param>
/// <param name="Choices">Allowed values for <see cref="SettingType.Choice"/>.</param>
public sealed record SettingDefinition(
    string Key,
    string Label,
    SettingType Type,
    object? DefaultValue,
    IReadOnlyList<string>? Choices = null)
{
    public string? DescriptionKey { get; init; }

    /// <summary>A <c>SettingsSections</c> id. Feature settings only.</summary>
    public string? SectionId { get; init; }

    public string? GroupKey { get; init; }

    public double? Min { get; init; }

    public double? Max { get; init; }

    public double? Step { get; init; }

    public string? UnitKey { get; init; }

    public int? MaxLength { get; init; }

    /// <summary>For <see cref="SettingType.StringList"/>. 50 when not set.</summary>
    public int? MaxItems { get; init; }

    /// <summary>Shown only while the setting named by Key has this value.</summary>
    public (string Key, object Value)? VisibleWhen { get; init; }

    public bool Required { get; init; }

    /// <summary>Shown under "More options".</summary>
    public bool Advanced { get; init; }

    public SettingScope Scope { get; init; } = SettingScope.Synced;

    public string? EditorKey { get; init; }
}
