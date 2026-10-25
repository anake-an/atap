using System.IO;
using Atap.Composition;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Diagnostics;
using Atap.Features.Startup;
using Atap.Platform.Startup;

[assembly: AtapFeature(typeof(StartupDescriptor))]

namespace Atap.Features.Startup;

/// <summary>
/// Start with Windows. The setting is the user's choice, and the feature makes Windows follow it: at start
/// (so a moved or updated install is picked up) and whenever the setting changes. Hidden in Store builds.
/// </summary>
internal sealed class StartupDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "startup";
    public const string EnabledKey = "startup.enabled";

    public string Id => FeatureId;

    /// <summary>Off until onboarding ticks it. Machine scope, and sync never carries it either.</summary>
    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(EnabledKey, "startup.enabled", SettingType.Bool, false)
        {
            SectionId = WindowsStartup.IsSupported ? SettingsSections.General : null,
            DescriptionKey = "startup.enabledHint",
            Scope = SettingScope.Machine,
        },
    ];

    public IReadOnlyCollection<string> EnabledBy => [];

    public bool IsEnabled(SettingValues settings, AppMode mode) => WindowsStartup.IsSupported;

    public IAppFeature Create(FeatureContext context) =>
        new StartupFeature(context.Settings, WindowsStartup.CreateDefault(), Log.Warn);
}

/// <summary>Keeps the Windows startup entry in line with the setting. Holds no timer, only a settings subscription.</summary>
internal sealed class StartupFeature : IAppFeature
{
    private readonly IAppSettings _settings;
    private readonly IStartupRegistration _registration;
    private readonly Action<string, Exception?> _log;

    public StartupFeature(IAppSettings settings, IStartupRegistration registration, Action<string, Exception?> log)
    {
        _settings = settings;
        _registration = registration;
        _log = log;
    }

    public Task StartAsync()
    {
        _settings.Changed += OnSettingsChanged;
        // Only a choice the user saved is applied at start. Defaults loaded from an unreadable or recovered
        // settings file must not remove the entry.
        if (!_settings.IsReadOnly && _settings.Values.Raw.ContainsKey(StartupDescriptor.EnabledKey))
            Apply(atStart: true);
        return Task.CompletedTask;
    }

    /// <summary>Leaves the entry as it is: quitting Atap must not change whether it starts with Windows.</summary>
    public ValueTask DisposeAsync()
    {
        _settings.Changed -= OnSettingsChanged;
        return ValueTask.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(StartupDescriptor.EnabledKey))
            Apply();
    }

    private void Apply(bool atStart = false)
    {
        var wanted = _settings.Values.GetBool(StartupDescriptor.EnabledKey);
        try
        {
            if (!wanted)
                _registration.Unregister();
            // At start the entry is only repaired, so a portable or test copy opened once does not take it over.
            else if (atStart)
                _registration.Repair();
            else
                _registration.Register();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _log(wanted ? "Atap could not add itself to Windows startup." : "Atap could not remove itself from Windows startup.", ex);
        }
    }
}
