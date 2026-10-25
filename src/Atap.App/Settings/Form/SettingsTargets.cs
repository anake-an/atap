using System.Text.Json;
using Atap.Core.Layout;
using Atap.Core.Settings;
using Atap.Core.Widgets;

namespace Atap.Settings.Form;

/// <summary>Where a settings form reads and writes its values. Use it on the UI thread.</summary>
internal interface ISettingsTarget
{
    /// <summary>The current values, with defaults and limits from the definitions.</summary>
    SettingValues Values { get; }

    /// <summary>Writes a checked value. Null removes it, so the default applies again.</summary>
    SettingWriteResult Write(string key, object? value);

    /// <summary>Writes queued changes to disk now. A write only queues them.</summary>
    SettingWriteResult Flush();

    /// <summary>Raised after values changed, from this form or from anywhere else.</summary>
    event EventHandler? Changed;
}

/// <summary>App settings in settings.json, written through <see cref="IAppSettings.Set"/>.</summary>
internal sealed class AppSettingsTarget : ISettingsTarget, IDisposable
{
    private readonly IAppSettings _settings;

    public AppSettingsTarget(IAppSettings settings)
    {
        _settings = settings;
        _settings.Changed += OnChanged;
    }

    public event EventHandler? Changed;

    public SettingValues Values => _settings.Values;

    public SettingWriteResult Write(string key, object? value) => _settings.Set(key, value);

    public SettingWriteResult Flush() => _settings.Flush();

    public void Dispose() => _settings.Changed -= OnChanged;

    private void OnChanged(object? sender, SettingsChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// One widget instance's settings in layout.json, written through <see cref="ILayoutService.Update"/>.
/// The app restarts only that instance, once the writes settle.
/// </summary>
internal sealed class WidgetSettingsTarget : ISettingsTarget, IDisposable
{
    private readonly ILayoutService _layout;
    private readonly IReadOnlyList<SettingDefinition> _definitions;

    public WidgetSettingsTarget(ILayoutService layout, string instanceId, IReadOnlyList<SettingDefinition> definitions)
    {
        _layout = layout;
        InstanceId = instanceId;
        _definitions = definitions;
        _layout.Changed += OnChanged;
    }

    public event EventHandler? Changed;

    public string InstanceId { get; }

    public SettingValues Values => new(_definitions, _layout.Document.GetWidgetSettings(InstanceId));

    public SettingWriteResult Write(string key, object? value)
    {
        var current = _layout.Document.GetWidgetSettings(InstanceId);
        var had = current.TryGetValue(key, out var old);
        var element = value is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(value, value.GetType());
        if (element is null ? !had : had && old.GetRawText() == element.Value.GetRawText())
            return SettingWriteResult.Unchanged;

        var result = _layout.Update(document =>
        {
            if (element is { } stored)
            {
                document.SetWidgetSetting(InstanceId, key, stored);
            }
            else if (document.WidgetSettings.TryGetValue(InstanceId, out var values))
            {
                values.Remove(key);
                if (values.Count == 0)
                    document.WidgetSettings.Remove(InstanceId);
            }
        });
        return result switch
        {
            LayoutWriteResult.Applied => SettingWriteResult.Applied,
            LayoutWriteResult.ReadOnly => SettingWriteResult.ReadOnly,
            _ => SettingWriteResult.Failed,
        };
    }

    public SettingWriteResult Flush() => _layout.Flush() switch
    {
        LayoutWriteResult.Applied => SettingWriteResult.Applied,
        LayoutWriteResult.ReadOnly => SettingWriteResult.ReadOnly,
        _ => SettingWriteResult.Failed,
    };

    public void Dispose() => _layout.Changed -= OnChanged;

    private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
