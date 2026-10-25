using Atap.Composition;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Features.LocalApi;
using Atap.Platform.EnvironmentPath;

[assembly: AtapFeature(typeof(CliPathDescriptor))]

namespace Atap.Features.LocalApi;

/// <summary>
/// "Add atap to PATH": puts the folder with <c>atap.exe</c> on the user's own PATH, so a terminal can run
/// <c>atap push</c>. Opt-in and a Machine setting. It changes PATH only when the setting changes, never when Atap
/// starts or quits, and uninstalling Atap takes the entry out. Store builds leave PATH alone.
/// </summary>
internal sealed class CliPathDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "cliPath";
    public const string OnPathKey = "localApi.onPath";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
#if STORE
        [];
#else
    [
        new(OnPathKey, "localapi.onPath", SettingType.Bool, false)
        {
            SectionId = SettingsSections.Advanced,
            DescriptionKey = "localapi.onPathHint",
            Scope = SettingScope.Machine,
            VisibleWhen = (LocalApiDescriptor.EnabledKey, true),
        },
    ];
#endif

    public IReadOnlyCollection<string> EnabledBy { get; } = [];

#if STORE
    public bool IsEnabled(SettingValues settings, AppMode mode) => false;
#else
    public bool IsEnabled(SettingValues settings, AppMode mode) => true;
#endif

    public IAppFeature Create(FeatureContext context) => new CliPathFeature(context.Settings, new RegistryUserPath(), RegistryUserPath.CliFolder);
}

/// <summary>Adds or removes exactly one PATH entry when the setting changes. Holds no timer.</summary>
internal sealed class CliPathFeature(IAppSettingsView settings, IUserPath path, string folder) : IAppFeature
{
    public Task StartAsync()
    {
        settings.Changed += OnSettingsChanged;
        // Repairs a missing entry, for example after a PATH clean-up, but never removes one at start.
        if (settings.Values.GetBool(CliPathDescriptor.OnPathKey))
            Apply(on: true);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        settings.Changed -= OnSettingsChanged;
        return ValueTask.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(CliPathDescriptor.OnPathKey))
            Apply(settings.Values.GetBool(CliPathDescriptor.OnPathKey));
    }

    private void Apply(bool on)
    {
        var current = path.Read();
        var next = on ? PathList.Add(current, folder) : PathList.Remove(current, folder);
        if (!string.Equals(next, current ?? string.Empty, StringComparison.Ordinal))
            path.Write(next);
    }
}
