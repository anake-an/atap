using Atap.Core.Commands;
using Atap.Core.Placement;
using Atap.Core.Widgets;
using Atap.Settings.Cards.About.Licenses;
using Atap.Settings.Cards.About.Version;
using Atap.Settings.Cards.Advanced.ProblemLog;
using Atap.Settings.Cards.Position;
using Atap.Settings.Cards.Privacy.NetworkFeatures;
using Atap.Settings.Cards.Widgets;
using Atap.Settings.Form;
using Atap.Tests.Infrastructure;
using Atap.Updates;

namespace Atap.Tests.Settings;

public sealed class SettingsCardsTests : IDisposable
{
    private static readonly DisplayMonitor Main = new(@"\\?\DISPLAY#MAIN", "Main", new PixelRect(0, 0, 1920, 1080), IsPrimary: true);
    private static readonly DisplayMonitor Side = new(@"\\?\DISPLAY#SIDE", "Side", new PixelRect(1920, 0, 2560, 1440), IsPrimary: false);

    private readonly TestServices _services = new();

    public SettingsCardsTests()
    {
        _services.Settings.Load();
        _services.Layout.Load();
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public void Widgets_FailedShowsRestart_AndOnlyThere()
    {
        var host = Host(("clock", WidgetStatus.Running), ("clock#second", WidgetStatus.Failed), ("clock#third", WidgetStatus.NeedsSetup));
        using var card = new WidgetsCardViewModel(host, _services.Layout, _services.Text, new FakeSecretStore(), null);

        Assert.Equal(new[] { false, true, false }, card.Rows.Select(r => r.CanRestart));
        Assert.Equal(new[] { "Working", "Stopped after a problem", "Needs setting up" }, card.Rows.Select(r => r.StatusText));
        Assert.Equal("Clock (second)", card.Rows[1].Name);

        card.Rows[1].RestartCommand.Execute(null);
        Assert.Equal(new[] { "clock#second" }, host.Restarted);

        host.SetStatus("clock#second", WidgetStatus.Running);
        Assert.False(card.Rows[1].CanRestart);
        Assert.False(card.Rows[1].RestartCommand.CanExecute(null));
    }

    [Fact]
    public void Widgets_KeepRowsAcrossChanges_AndBuildTheFormOnlyWhenOpened()
    {
        var host = Host(("clock", WidgetStatus.Running), ("clock#second", WidgetStatus.Running));
        using var card = new WidgetsCardViewModel(host, _services.Layout, _services.Text, new FakeSecretStore(), null);
        var row = card.Rows[0];
        Assert.True(row.HasSettings);
        Assert.Null(row.Form);

        row.IsExpanded = true;
        var form = Assert.IsType<SettingsFormViewModel>(row.Form);
        Assert.IsType<ToggleField>(Assert.Single(form.Fields)).IsOn = false;
        Assert.False(_services.Layout.Document.GetWidgetSettings("clock")["use24Hour"].GetBoolean());
        Assert.Empty(_services.Layout.Document.GetWidgetSettings("clock#second"));

        // A restart or a layout change keeps the row and its open form.
        host.RaiseWidgetsChanged();
        Assert.Same(row, card.Rows[0]);
        Assert.Same(form, row.Form);

        host.Ids.Remove("clock");
        host.RaiseWidgetsChanged();
        Assert.Equal("clock#second", Assert.Single(card.Rows).InstanceId);
        Assert.Null(row.Form);
    }

    [Fact]
    public void Widgets_EmptyLayout_SaysSo()
    {
        using var card = new WidgetsCardViewModel(Host(), _services.Layout, _services.Text, new FakeSecretStore(), null);

        Assert.True(card.IsEmpty);
        Assert.Equal("Nothing is on your notch yet.", card.EmptyText);
    }

    [Fact]
    public void Network_IsBuiltFromCapabilities_AndFollowsTheLayout()
    {
        var catalog = WidgetCatalog.FromDescriptors(
        [
            new SettingsTestDescriptor("clock"),
            new SettingsTestDescriptor("weather", capabilities: new WidgetCapabilities(WidgetPermissions.Network, ["api.data.gov.my"], "clock.description")),
            new SettingsTestDescriptor("json-url", capabilities: new WidgetCapabilities(WidgetPermissions.Network | WidgetPermissions.UserUrls, [])),
        ]);
        _services.Layout.Update(d => d.EnabledWidgets = ["clock", "json-url#nas"]);
        using var card = new NetworkFeaturesViewModel(catalog, _services.Layout, updateChecks: null, _services.Text);

        Assert.Equal(2, card.Items.Count);
        var weather = card.Items.Single(i => i.Detail.Contains("api.data.gov.my", StringComparison.Ordinal));
        Assert.False(weather.IsOn);
        Assert.Equal("Uses the internet: api.data.gov.my", weather.Detail);
        Assert.True(weather.HasNote);
        Assert.Equal("Off", weather.StateText);
        Assert.Equal("Turn on in What it shows", weather.TurnOnHint);
        var json = card.Items.Single(i => i.Detail == "Uses the internet: addresses you type in");
        Assert.True(json.IsOn);
        Assert.Equal("On", json.StateText);
        Assert.False(json.HasTurnOnHint);

        _services.Layout.Update(d => d.EnabledWidgets = ["weather"]);
        Assert.True(card.Items.Single(i => i.Detail.Contains("api.data.gov.my", StringComparison.Ordinal)).IsOn);
        Assert.False(card.Items.Single(i => i.Detail == "Uses the internet: addresses you type in").IsOn);
    }

    [Fact]
    public void Network_NamesSitesAndTypedAddresses_OnOneLine()
    {
        var catalog = WidgetCatalog.FromDescriptors(
        [
            new SettingsTestDescriptor("lyrics", capabilities: new WidgetCapabilities(WidgetPermissions.Network | WidgetPermissions.UserUrls, ["lrclib.net"])),
            new SettingsTestDescriptor("plain", capabilities: new WidgetCapabilities(WidgetPermissions.Network, [])),
        ]);
        using var card = new NetworkFeaturesViewModel(catalog, _services.Layout, updateChecks: null, _services.Text);

        Assert.Equal(
            new[] { "Uses the internet: lrclib.net and addresses you type in", "Uses the internet" },
            card.Items.Select(i => i.Detail));
    }

    [Fact]
    public void Network_ListsUpdateChecks_OnlyWhereAtapChecksItself()
    {
        var catalog = WidgetCatalog.FromDescriptors([new SettingsTestDescriptor("clock")]);

        using (var store = new NetworkFeaturesViewModel(catalog, _services.Layout, updateChecks: null, _services.Text))
        {
            Assert.True(store.IsEmpty);
            Assert.Equal("Nothing in Atap uses the internet right now.", store.EmptyText);
        }
        using var installed = new NetworkFeaturesViewModel(catalog, _services.Layout, updateChecks: true, _services.Text);
        Assert.Equal("Update checks", Assert.Single(installed.Items).Name);
    }

    [Fact]
    public void Position_PickingASpot_MovesTheNotch_AndResetsTheOffset()
    {
        _services.Layout.Update(d => d.Placement.Offset = 120);
        using var card = new PositionCardViewModel(_services.Layout, new TestMonitors(Main, Side), _services.Commands, _services.Text);
        Assert.Equal(5, card.Anchors.Count);
        Assert.Equal(NotchAnchor.TopCenter, card.SelectedAnchor!.Anchor);

        card.SelectedAnchor = card.Anchors.Single(a => a.Anchor == NotchAnchor.RightCenter);

        Assert.Equal(NotchAnchor.RightCenter, _services.Layout.Document.Placement.Anchor);
        Assert.Equal(0, _services.Layout.Document.Placement.Offset);
        Assert.Equal(NotchAnchor.RightCenter, card.SelectedAnchor!.Anchor);
        Assert.True(card.SelectedAnchor.IsVertical);
        Assert.False(card.HasMessage);
    }

    [Fact]
    public void Position_WhichScreen_ListsEveryScreen_AndWritesTheChoice()
    {
        using var card = new PositionCardViewModel(_services.Layout, new TestMonitors(Main, Side), _services.Commands, _services.Text);
        Assert.Equal(new[] { "Main screen", "Main", "Side", "Every screen" }, card.Screens.Select(s => s.Label));
        Assert.Equal("primary", card.SelectedScreen!.Id);

        card.SelectedScreen = card.Screens.Single(s => s.Label == "Side");

        var selection = _services.Layout.Document.Placement.Monitor;
        Assert.Equal(MonitorMode.Specific, selection.Mode);
        Assert.Equal(Side.DeviceId, selection.DeviceId);
        Assert.Equal("Side", card.SelectedScreen!.Label);
    }

    [Fact]
    public void Position_AnUnpluggedScreen_StaysVisible()
    {
        _services.Layout.Update(d => d.Placement.Monitor = MonitorSelection.Specific(Side));
        using var card = new PositionCardViewModel(_services.Layout, new TestMonitors(Main), _services.Commands, _services.Text);

        Assert.Equal("Side (not connected)", card.SelectedScreen!.Label);
        Assert.Null(card.SelectedScreen.Selection);
    }

    [Fact]
    public void Position_FollowsChangesFromTheNotchMenu_AndShowsEditLayoutOnceItExists()
    {
        using var card = new PositionCardViewModel(_services.Layout, new TestMonitors(Main), _services.Commands, _services.Text);
        var changes = new List<string?>();
        card.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.False(card.CanEditLayout);

        _services.Layout.Update(d => d.Placement.Anchor = NotchAnchor.TopLeft);
        Assert.Equal(NotchAnchor.TopLeft, card.SelectedAnchor!.Anchor);

        var ran = 0;
        using (_services.Commands.Register(new AtapCommand(PositionCardViewModel.EditLayoutCommandId, "position.editLayout", _ =>
        {
            ran++;
            return Task.CompletedTask;
        })))
        {
            Assert.True(card.CanEditLayout);
            Assert.Contains(nameof(PositionCardViewModel.CanEditLayout), changes);
            card.EditLayoutCommand.Execute(null);
            Assert.Equal(1, ran);
        }
        Assert.False(card.CanEditLayout);
    }

    [Fact]
    public void Version_ChecksThroughTheCommand_WhileItIsAvailable()
    {
        var updates = new FakeUpdates();
        var ran = 0;
        using var registration = _services.Commands.Register(new AtapCommand(Atap.Composition.AppCommands.CheckForUpdates, "app.command.checkUpdates", _ =>
        {
            ran++;
            return Task.CompletedTask;
        })
        {
            IsAvailable = () => updates.Status is UpdateStatus.Idle or UpdateStatus.UpToDate,
        });
        using var card = new VersionCardViewModel("1.2.3", updates, _services.Commands, _services.Text);

        Assert.Equal("Atap 1.2.3", card.VersionText);
        Assert.True(card.ShowCheck);
        Assert.True(card.CanCheck);
        card.CheckCommand.Execute(null);
        Assert.Equal(1, ran);

        updates.Set(UpdateStatus.Downloading);
        Assert.False(card.CanCheck);
        Assert.False(card.CheckCommand.CanExecute(null));
        Assert.Equal("Downloading the update…", card.StatusText);
    }

    [Fact]
    public void Version_InTheStoreBuild_HasNoCheckButton()
    {
        using var card = new VersionCardViewModel("1.2.3", updates: null, _services.Commands, _services.Text);

        Assert.False(card.ShowCheck);
        Assert.False(card.CanCheck);
        Assert.Equal("The Microsoft Store keeps Atap up to date.", card.StatusText);
    }

    [Fact]
    public void Version_FromABuildFolder_SaysUpdatesNeedTheInstalledApp()
    {
        using var card = new VersionCardViewModel("1.2.3", new FakeUpdates { IsSupported = false }, _services.Commands, _services.Text);

        Assert.Equal("Updates work in the installed app only.", card.StatusText);
    }

    [Fact]
    public void Licenses_OpenTheShippedFiles_AndSayWhenOneIsMissing()
    {
        var opened = new List<string>();
        var card = new LicensesCardViewModel(@"C:\Atap", path =>
        {
            opened.Add(path);
            return path.EndsWith(LicensesCardViewModel.LicenseFile, StringComparison.Ordinal);
        });

        card.OpenLicenseCommand.Execute(null);
        Assert.False(card.IsMissing);
        card.OpenNoticesCommand.Execute(null);
        Assert.True(card.IsMissing);
        Assert.Equal(new[] { @"C:\Atap\LICENSE.txt", @"C:\Atap\THIRD-PARTY-NOTICES.md" }, opened);
    }

    [Fact]
    public void Logs_SayWhenThereIsNoLogYet()
    {
        var card = new LogsCardViewModel(@"C:\nowhere", _ => false);

        card.OpenCommand.Execute(null);

        Assert.True(card.NoLog);
    }

    private static FakeHostView Host(params (string Id, WidgetStatus Status)[] instances)
    {
        var catalog = WidgetCatalog.FromDescriptors(
            [new SettingsTestDescriptor("clock", [new SettingDefinition("use24Hour", "clock.use24Hour", SettingType.Bool, true)])]);
        var host = new FakeHostView(catalog);
        foreach (var (id, status) in instances)
        {
            host.Ids.Add(id);
            host.Statuses[id] = status;
        }
        return host;
    }

    private sealed class TestMonitors(params DisplayMonitor[] monitors) : IMonitorList
    {
        public IReadOnlyList<DisplayMonitor> Monitors { get; } = monitors;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class FakeUpdates : IUpdateService
    {
        public bool IsSupported { get; init; } = true;

        public UpdateStatus Status { get; private set; } = UpdateStatus.Idle;

        public event EventHandler? StatusChanged;

        public void Set(UpdateStatus status)
        {
            Status = status;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Start()
        {
        }

        public Task CheckNowAsync() => Task.CompletedTask;

        public void ApplyPendingOnExit()
        {
        }

        public void Dispose()
        {
        }
    }
}
