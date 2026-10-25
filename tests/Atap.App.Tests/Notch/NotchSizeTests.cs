using Atap.Core.Notch;
using Atap.Core.Widgets;
using Atap.Features.NotchSize;
using Atap.Notch;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Notch;

/// <summary>Normal, Slim and Tucked away, Hide for 1 hour, and the notch resizing when an alert is replaced.</summary>
public sealed class NotchSizeTests : IAsyncLifetime
{
    private readonly TestServices _services = new();
    private readonly NotchControl _notch = new();
    private NotchSizeFeature _feature = null!;

    public async Task InitializeAsync()
    {
        _services.Settings.Load();
        _services.Settings.Register(new NotchSizeDescriptor().Settings);
        _feature = new NotchSizeFeature(_services.Settings, _notch, _services.Commands, _services.Scheduler, _services.Time, NotchSizeDescriptor.FeatureId);
        await _feature.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _feature.DisposeAsync();
        _services.Dispose();
    }

    [Fact]
    public void Normal_SetsNoFlags()
    {
        Assert.False(_notch.IsSet(NotchFlag.Slim));
        Assert.False(_notch.IsSet(NotchFlag.Tucked));
        Assert.False(_notch.IsSet(NotchFlag.QuietAlerts));
    }

    [Fact]
    public void Slim_IsAThinBar_AndAlertsStillShow()
    {
        _services.Settings.Set(NotchSizeDescriptor.SizeKey, NotchSizeDescriptor.Slim);

        Assert.True(_notch.IsSet(NotchFlag.Slim));
        Assert.False(_notch.IsSet(NotchFlag.Tucked));
        Assert.False(_notch.IsSet(NotchFlag.QuietAlerts));
    }

    [Fact]
    public void TuckedAway_IsAHairline_AndOnlyCriticalAlertsShow()
    {
        _services.Settings.Set(NotchSizeDescriptor.SizeKey, NotchSizeDescriptor.Tucked);

        Assert.True(_notch.IsSet(NotchFlag.Slim));
        Assert.True(_notch.IsSet(NotchFlag.Tucked));
        Assert.True(_notch.IsSet(NotchFlag.QuietAlerts));

        _services.Settings.Set(NotchSizeDescriptor.SizeKey, NotchSizeDescriptor.Normal);
        Assert.False(_notch.IsSet(NotchFlag.Slim));
        Assert.False(_notch.IsSet(NotchFlag.QuietAlerts));
    }

    [Fact]
    public async Task MenuItems_ChangeTheSize_AndTickTheCurrentOne()
    {
        var slim = _services.Commands.All.Single(c => c.Id == "notch.size.slim");
        Assert.Equal(NotchSizeFeature.MenuGroup, slim.MenuGroup);
        Assert.False(slim.IsChecked!());

        Assert.True(await _services.Commands.TryRunAsync("notch.size.slim"));

        Assert.Equal(NotchSizeDescriptor.Slim, _services.Settings.Values.GetString(NotchSizeDescriptor.SizeKey));
        Assert.True(slim.IsChecked!());
        Assert.True(_notch.IsSet(NotchFlag.Slim));
        Assert.Contains(_services.Commands.All, c => c.Id == NotchSizeFeature.HideForAnHourCommand && c.MenuGroup == NotchSizeFeature.MenuGroup);
    }

    [Fact]
    public async Task HideForAnHour_ComesBackByItself()
    {
        Assert.True(await _services.Commands.TryRunAsync(NotchSizeFeature.HideForAnHourCommand));
        Assert.True(_notch.IsSet(NotchFlag.Hide));

        _services.Time.Advance(TimeSpan.FromMinutes(59));
        Assert.True(_notch.IsSet(NotchFlag.Hide));

        _services.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(_notch.IsSet(NotchFlag.Hide));
        Assert.False(_feature.IsHiddenForAnHour);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task ShowNow_EndsTheHourEarly_AndLeavesNoTimer()
    {
        _feature.HideForAnHour();

        Assert.True(await _services.Commands.TryRunAsync(NotchSizeFeature.ShowNowCommand));

        Assert.False(_notch.IsSet(NotchFlag.Hide));
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void HidingAgain_StartsTheHourOver()
    {
        _feature.HideForAnHour();
        _services.Time.Advance(TimeSpan.FromMinutes(50));
        _feature.HideForAnHour();

        _services.Time.Advance(TimeSpan.FromMinutes(20));

        Assert.True(_notch.IsSet(NotchFlag.Hide));
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void SafeMode_KeepsTheNotchFullSize()
    {
        var descriptor = new NotchSizeDescriptor();

        Assert.False(descriptor.IsEnabled(SettingValues.Empty, Atap.Composition.AppMode.Safe));
        Assert.True(descriptor.IsEnabled(SettingValues.Empty, Atap.Composition.AppMode.Normal));
    }

    [Fact]
    public async Task ViewModel_FollowsTucked_AndResizesWhenAnAlertIsReplaced()
    {
        await using var widgets = _services.CreateWidgetHost(WidgetCatalog.FromDescriptors([]));
        _services.Layout.Load();
        using var model = new NotchViewModel(_services.Layout, widgets, _notch, new ForegroundStub());
        var changes = 0;
        model.ContentChanged += (_, _) => changes++;

        _services.Settings.Set(NotchSizeDescriptor.SizeKey, NotchSizeDescriptor.Tucked);
        Assert.Equal(NotchSurface.Slim, model.Surface);
        Assert.True(model.IsTucked);

        _notch.SetSurfaceContent(NotchSurface.Alert, "first", "alerts");
        Assert.Equal(NotchSurface.Alert, model.Surface);
        var before = changes;

        _notch.SetSurfaceContent(NotchSurface.Alert, "second, longer alert", "alerts");

        Assert.Equal(before + 1, changes);
    }
}
