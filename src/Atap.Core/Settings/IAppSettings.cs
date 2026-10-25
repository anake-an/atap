using System.Text.Json;
using Atap.Core.Widgets;

namespace Atap.Core.Settings;

/// <summary>Read access to app settings (settings.json "values"). Keys are "&lt;featureId&gt;.&lt;name&gt;".</summary>
public interface IAppSettingsView
{
    SettingValues Values { get; }

    /// <summary>Raised on the UI thread with the keys that changed.</summary>
    event EventHandler<SettingsChangedEventArgs>? Changed;
}

/// <summary>Read and write access to app settings. Widgets get the read-only view.</summary>
public interface IAppSettings : IAppSettingsView
{
    /// <summary>Sets a value. Null removes it, so the default applies again.</summary>
    SettingWriteResult Set(string key, object? value);

    /// <summary>Writes queued changes to disk now. Applied once everything is on disk.</summary>
    SettingWriteResult Flush() => SettingWriteResult.Applied;

    /// <summary>
    /// settings.json "rules" as stored: a JSON array in order, empty when there are none. The rules
    /// feature parses it into its own model, so this contract does not depend on that model.
    /// </summary>
    JsonElement Rules { get; }

    /// <summary>
    /// Replaces the rules. Anything but a JSON array is <see cref="SettingWriteResult.Invalid"/>.
    /// Raises <see cref="IAppSettingsView.Changed"/> with <see cref="SettingsChangedEventArgs.RulesKey"/>.
    /// </summary>
    SettingWriteResult SetRules(JsonElement rules);

    /// <summary>True when the file could not be read or is from a newer Atap. Nothing is written then.</summary>
    bool IsReadOnly { get; }
}

public sealed class SettingsChangedEventArgs(IReadOnlyCollection<string> keys) : EventArgs
{
    /// <summary>The key in <see cref="Keys"/> when the rules changed. Setting keys are "&lt;featureId&gt;.&lt;name&gt;", so it cannot clash with one.</summary>
    public const string RulesKey = "rules";

    public IReadOnlyCollection<string> Keys { get; } = keys;
}

public enum SettingWriteResult
{
    /// <summary>The value changed and is saved (or queued to save).</summary>
    Applied,
    Unchanged,

    /// <summary>The value does not fit the definition. The old value stays.</summary>
    Invalid,

    /// <summary>The session is read-only. The value applies in memory only.</summary>
    ReadOnly,

    /// <summary>The file could not be written. The value applies in memory only.</summary>
    Failed,
}
