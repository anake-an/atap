using System.Text.Json;
using Atap.Composition;
using Atap.Core.Placement;
using Atap.Core.Presets;
using Atap.Core.Sync;
using Atap.Core.Widgets;
using Atap.Features.Language;
using Atap.Features.Malaysia;
using Atap.Features.Onboarding;
using Atap.Features.Startup;
using Atap.Features.Sync;
using Atap.Platform.Location;
using Atap.Settings.Cards.Malaysia.PrayerArea;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Onboarding;

public sealed class OnboardingTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly List<SettingDefinition> _definitions = [];

    public OnboardingTests()
    {
        _services.Layout.Load();
        IFeatureDescriptor[] features = [new OnboardingDescriptor(), new StartupDescriptor(), new LanguageFeatureDescriptor(), new MalaysiaDescriptor(), new SyncDescriptor()];
        foreach (var feature in features)
        {
            _services.Settings.Register(feature.Settings);
            _definitions.AddRange(feature.Settings);
        }
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public void Steps_IncludeTheAreaOnlyForTheMalaysiaPreset()
    {
        using var setup = NewSetup(startup: true);
        Assert.Equal("Step 1 of 5", setup.StepText);

        setup.Presets.Single(p => p.Preset == LayoutPreset.Malaysia).PickCommand.Execute(null);

        Assert.Equal("Step 1 of 6", setup.StepText);
        setup.NextCommand.Execute(null);
        setup.NextCommand.Execute(null);
        setup.NextCommand.Execute(null);
        Assert.True(setup.IsArea);
    }

    [Fact]
    public void Steps_LeaveOutStartWithWindows_WhereItIsNotSupported()
    {
        using var setup = NewSetup(startup: false);

        Assert.Equal("Step 1 of 4", setup.StepText);
    }

    [Fact]
    public void LanguageAndPosition_ApplyAtOnce()
    {
        using var setup = NewSetup(startup: true);

        setup.SelectedLanguage = setup.Languages.Single(l => l.Value == "ms");
        setup.SelectedAnchor = setup.Anchors.Single(a => a.Value == NotchAnchor.RightCenter);

        Assert.Equal("ms", _services.Settings.Values.GetString(LanguageFeatureDescriptor.ChoiceKey));
        Assert.Equal(NotchAnchor.RightCenter, _services.Layout.Document.Placement.Anchor);
    }

    [Fact]
    public void Finish_AppliesThePreset_AndStartWithWindows_AndNeverShowsAgain()
    {
        using var setup = NewSetup(startup: true);
        var closed = false;
        setup.CloseRequested += (_, _) => closed = true;
        setup.Presets.Single(p => p.Preset == LayoutPreset.Work).PickCommand.Execute(null);

        setup.FinishCommand.Execute(null);

        Assert.True(closed);
        Assert.Contains("world-clock", _services.Layout.Document.EnabledWidgets);
        Assert.True(_services.Settings.Values.GetBool(StartupDescriptor.EnabledKey));
        Assert.True(_services.Settings.Values.GetBool(OnboardingDescriptor.DoneKey));
    }

    [Fact]
    public void Skip_KeepsTheLayout_AndNeverShowsAgain()
    {
        using var setup = NewSetup(startup: true);
        var before = _services.Layout.Document.EnabledWidgets.ToList();

        setup.SkipCommand.Execute(null);

        Assert.Equal(before, _services.Layout.Document.EnabledWidgets);
        Assert.False(_services.Settings.Values.GetBool(StartupDescriptor.EnabledKey));
        Assert.True(_services.Settings.Values.GetBool(OnboardingDescriptor.DoneKey));
    }

    [Fact]
    public async Task UpgradedUsers_SkipTheWizard()
    {
        var opened = 0;
        var feature = new OnboardingFeature(_services.Settings, freshInstall: false, () =>
        {
            opened++;
            return null!;
        });

        await feature.StartAsync();

        Assert.Equal(0, opened);
        Assert.True(_services.Settings.Values.GetBool(OnboardingDescriptor.DoneKey));
        Assert.False(new OnboardingDescriptor().IsEnabled(_services.Settings.Values, AppMode.Normal));
    }

    [Fact]
    public async Task ClosingSetupWithX_LetsGoOfTheWindow()
    {
        var window = new FakeSetupWindow();
        var feature = new OnboardingFeature(_services.Settings, freshInstall: true, () => window);
        await feature.StartAsync();
        Assert.Same(window, feature.Window);

        // Setup is not done, so the feature keeps running. The closed window must not stay with it.
        window.Close();
        Assert.Null(feature.Window);
        Assert.False(_services.Settings.Values.GetBool(OnboardingDescriptor.DoneKey));

        await feature.DisposeAsync();
        Assert.Equal(1, window.Closes);
    }

    [Fact]
    public void SafeMode_NeverShowsSetup() =>
        Assert.False(new OnboardingDescriptor().IsEnabled(_services.Settings.Values, AppMode.Safe));

    [Fact]
    public void WithoutOneDrive_TheSyncStepIsSkipped()
    {
        using var setup = NewSetup(startup: true);

        Assert.Equal("Step 1 of 5", setup.StepText);
        setup.NextCommand.Execute(null);
        Assert.True(setup.IsPosition);
    }

    [Fact]
    public void UsingTheOneDriveFile_BacksUpFirst_ThenTurnsSyncOn_AndSkipsThePreset()
    {
        WriteBundle();
        using var setup = NewSetup(startup: true, oneDrive: true);
        Assert.Equal("Step 1 of 6", setup.StepText);
        setup.NextCommand.Execute(null);
        Assert.True(setup.IsSync);
        Assert.True(setup.SyncAsking);
        Assert.False(setup.ShowNext);

        setup.UseOneDriveSettingsCommand.Execute(null);

        var backup = Assert.Single(Directory.GetDirectories(_services.Paths.Roaming, "backup-*"));
        Assert.Contains(backup, setup.SyncMessage, StringComparison.Ordinal);
        Assert.True(File.Exists(new SyncLocalFiles(_services.Paths).StatePath));
        Assert.True(_services.Settings.Values.GetBool(SyncDescriptor.EnabledKey));
        Assert.True(setup.SyncUsed);
        Assert.True(setup.ShowNext);
        // The preset step is left out, since the layout comes from OneDrive.
        Assert.Equal("Step 2 of 5", setup.StepText);
        setup.NextCommand.Execute(null);
        setup.NextCommand.Execute(null);
        Assert.True(setup.IsStartup);
    }

    [Fact]
    public void NotNow_ChangesNothing()
    {
        WriteBundle();
        using var setup = NewSetup(startup: true, oneDrive: true);
        setup.NextCommand.Execute(null);

        setup.NotNowCommand.Execute(null);

        Assert.True(setup.IsPosition);
        Assert.False(_services.Settings.Values.GetBool(SyncDescriptor.EnabledKey));
        Assert.False(Directory.Exists(_services.Paths.Roaming) && Directory.GetDirectories(_services.Paths.Roaming, "backup-*").Length > 0);
        Assert.False(File.Exists(new SyncLocalFiles(_services.Paths).StatePath));

        setup.FinishCommand.Execute(null);
        Assert.False(_services.Settings.Values.GetBool(SyncDescriptor.EnabledKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithOneDriveButNoFile_TheToggleIsOffByDefault_AndAppliesOnFinish(bool keep)
    {
        using var setup = NewSetup(startup: true, oneDrive: true);
        setup.NextCommand.Execute(null);

        Assert.True(setup.IsSync);
        Assert.True(setup.SyncOffer);
        Assert.False(setup.SyncHasFile);
        Assert.False(setup.KeepInSync);
        Assert.True(setup.ShowNext);

        setup.KeepInSync = keep;
        setup.FinishCommand.Execute(null);

        Assert.Equal(keep, _services.Settings.Values.GetBool(SyncDescriptor.EnabledKey));
    }

    private string SyncFolder => Path.Combine(Path.GetDirectoryName(_services.Paths.Roaming)!, "onedrive", SyncFileNames.FolderUnderOneDrive);

    /// <summary>A sync file from another PC.</summary>
    private void WriteBundle()
    {
        const string device = "9f8c3a52-7d1e-4c6b-a0e2-5b3f1d2c4e6a";
        var version = new SyncVersion(SyncVersion.Truncate(_services.Time.GetUtcNow()), device);
        var bundle = new SyncBundle(device, new Dictionary<string, SyncBundleUnit>(StringComparer.Ordinal)
        {
            ["settings:" + LanguageFeatureDescriptor.ChoiceKey] = new(version, JsonElement.Parse("\"ms\"")),
        });
        Directory.CreateDirectory(SyncFolder);
        File.WriteAllText(Path.Combine(SyncFolder, SyncFileNames.Bundle), SyncBundleWriter.Write(bundle));
    }

    private OnboardingViewModel NewSetup(bool startup, bool oneDrive = false)
    {
        var sync = new SyncSetup(
            new SyncStores(_services.Settings, _services.Layout, _definitions, _ => null),
            SyncDefinitions.Scope(_definitions, []),
            new SyncLocalFiles(_services.Paths),
            _services.Time);
        return new(
            _services.Settings,
            _services.Layout,
            Discovery.Widgets(typeof(App).Assembly),
            _services.Text,
            new PrayerAreaViewModel(_services.Settings, _services.Text, new NoLocation()),
            startup,
            oneDrive ? sync : null,
            oneDrive ? SyncFolder : null);
    }

    private sealed class FakeSetupWindow : IOnboardingWindow
    {
        public int Closes { get; private set; }

        public event EventHandler? Closed;

        public void Close()
        {
            Closes++;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class NoLocation : ILocationSource
    {
        public Task<LocationFix> FindAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocationFix(LocationOutcome.Failed, null));

        public void OpenWindowsSettings()
        {
        }
    }
}
