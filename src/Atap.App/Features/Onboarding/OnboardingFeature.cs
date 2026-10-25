using Atap.Composition;
using Atap.Core.Settings;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Features.Onboarding;
using Atap.Features.Sync;
using Atap.Platform.OneDrive;
using Atap.Platform.Startup;
using Atap.Platform.Location;
using Atap.Settings.Cards.Malaysia.PrayerArea;

[assembly: AtapFeature(typeof(OnboardingDescriptor))]

namespace Atap.Features.Onboarding;

/// <summary>First-run setup. Runs once on a new install. People upgrading from an earlier Atap skip it.</summary>
internal sealed class OnboardingDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "onboarding";
    public const string DoneKey = "onboarding.done";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
        [new(DoneKey, DoneKey, SettingType.Bool, false) { Scope = SettingScope.Machine }];

    public IReadOnlyCollection<string> EnabledBy => [];

    public bool IsEnabled(SettingValues settings, AppMode mode) => mode == AppMode.Normal && !settings.GetBool(DoneKey);

    public IAppFeature Create(FeatureContext context)
    {
        var services = context.Services;
        // A new install has neither file yet. Anyone with either one has used Atap before.
        var fresh = services.Settings.LastLoadOutcome == FileLoadOutcome.CreatedDefaults
            && services.Layout.LastLoadOutcome == FileLoadOutcome.CreatedDefaults;
        return new OnboardingFeature(context.Settings, fresh, () =>
        {
            // The same first turn on as the Sync card, so the backup and the choice work the same way.
            var catalog = services.Catalog;
            var sync = new SyncSetup(
                SyncParts.Stores(context.Settings, context.Layout, services.Features, catalog),
                SyncParts.Scope(services.Features, catalog),
                new SyncLocalFiles(context.Paths),
                context.Time);
            var folder = OneDriveLocator.SyncFolder(context.Settings.Values.GetString(SyncDescriptor.FolderKey), new WindowsOneDriveEnvironment());
            return OnboardingWindow.Open(new OnboardingViewModel(
                context.Settings,
                context.Layout,
                catalog,
                context.Text,
                new PrayerAreaViewModel(context.Settings, context.Text, new WindowsLocation()),
                WindowsStartup.IsSupported,
                sync,
                folder));
        });
    }
}

internal sealed class OnboardingFeature(IAppSettings settings, bool freshInstall, Func<IOnboardingWindow> open) : IAppFeature
{
    private IOnboardingWindow? _window;

    /// <summary>The open setup window, or null.</summary>
    internal IOnboardingWindow? Window => _window;

    public Task StartAsync()
    {
        if (!freshInstall)
        {
            settings.Set(OnboardingDescriptor.DoneKey, true);
            return Task.CompletedTask;
        }
        var window = open();
        // Closed with X, setup is not done, so the feature keeps running until Atap restarts. It lets go of the window all the same.
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
                _window = null;
        };
        _window = window;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _window?.Close();
        _window = null;
        return ValueTask.CompletedTask;
    }
}

internal interface IOnboardingWindow
{
    event EventHandler? Closed;

    void Close();
}
