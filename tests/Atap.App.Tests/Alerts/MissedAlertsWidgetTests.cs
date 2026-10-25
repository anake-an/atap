using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Widgets;
using Atap.Tests.Infrastructure;
using Atap.Widgets.MissedAlerts;

namespace Atap.Tests.Alerts;

public sealed class MissedAlertsWidgetTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly OwnerLifetime _lifetime = new(MissedAlertsDescriptor.TypeIdValue);

    public void Dispose()
    {
        _lifetime.Dispose();
        _services.Dispose();
    }

    private MissedAlertsWidget Create()
    {
        var descriptor = new MissedAlertsDescriptor();
        var context = _services.CreateContext(
            descriptor.TypeId, descriptor, _lifetime, new ObservableValue<WidgetVisibility>(WidgetVisibility.Panel));
        return Assert.IsType<MissedAlertsWidget>(descriptor.Create(context));
    }

    private void Miss(string key, string title)
    {
        _services.Alerts.SetQuiet("test", true);
        _services.Alerts.For("battery").Show(new Alert(key, AlertPriority.Normal, title) { Subtitle = "From the battery" });
    }

    [Fact]
    public async Task Widget_ListsWhatWasMissed_NewestFirst()
    {
        var widget = Create();
        var viewModel = Assert.IsType<MissedAlertsViewModel>(widget.ViewModel);
        await widget.StartAsync(SettingValues.Empty);
        Assert.True(viewModel.IsEmpty);
        Assert.Equal("Missed alerts, nothing missed", viewModel.AccessibleSummary);

        Miss("low", "Battery low");
        Miss("full", "Battery full");

        Assert.Equal(["Battery full", "Battery low"], viewModel.Items.Select(i => i.Title));
        Assert.Equal("Missed alerts, 2", viewModel.AccessibleSummary);
        Assert.StartsWith("Battery full, at ", viewModel.Items[0].AccessibleName, StringComparison.Ordinal);
        Assert.True(viewModel.Items[0].HasSubtitle);
    }

    [Fact]
    public async Task Clear_EmptiesTheListAndTheIndicator()
    {
        var widget = Create();
        var viewModel = (MissedAlertsViewModel)widget.ViewModel;
        var indicatorChanges = 0;
        widget.IndicatorChanged += (_, _) => indicatorChanges++;
        await widget.StartAsync(SettingValues.Empty);

        Miss("low", "Battery low");
        Assert.True(widget.IndicatorOn);

        viewModel.ClearCommand.Execute(null);

        Assert.Empty(_services.Alerts.Missed);
        Assert.True(viewModel.IsEmpty);
        Assert.False(widget.IndicatorOn);
        Assert.Equal(2, indicatorChanges);
    }

    [Fact]
    public async Task TheCard_ShowsOnlyWhileSomethingWasMissed()
    {
        var widget = Create();
        var changes = 0;
        widget.HasContentChanged += (_, _) => changes++;
        await widget.StartAsync(SettingValues.Empty);
        Assert.False(widget.HasContent);

        Miss("low", "Battery low");
        Assert.True(widget.HasContent);

        ((MissedAlertsViewModel)widget.ViewModel).ClearCommand.Execute(null);
        Assert.False(widget.HasContent);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task Stopped_ItNoLongerFollowsTheList()
    {
        var widget = Create();
        var viewModel = (MissedAlertsViewModel)widget.ViewModel;
        await widget.StartAsync(SettingValues.Empty);
        await widget.StopAsync();

        Miss("low", "Battery low");

        Assert.True(viewModel.IsEmpty);
        Assert.False(widget.IsLive);
        await widget.DisposeAsync();
    }

    [Fact]
    public void Descriptor_IsAPanelWidget()
    {
        var descriptor = new MissedAlertsDescriptor();

        Assert.True(WidgetInstanceId.IsValidTypeId(descriptor.TypeId));
        Assert.Equal([Atap.Core.NotchState.Panel], descriptor.SupportedStates);
        Assert.Empty(descriptor.Capabilities.NetworkHosts);
    }
}
