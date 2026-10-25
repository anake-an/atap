using Atap.Composition;
using Atap.Core.Widgets;
using Atap.Platform.Bluetooth;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Bluetooth;

namespace Atap.Tests.SystemWidgets;

public sealed class BluetoothTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly OwnerLifetime _lifetime = new("bluetooth");
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Hidden);
    private readonly FakeDevices _devices = new();

    public void Dispose()
    {
        _lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task ReadsOnlyWhileOpen_AndSaysUnknownWhenNotReported()
    {
        var descriptor = new BluetoothDescriptor();
        var widget = new BluetoothWidget(_services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility), _devices);
        await widget.StartAsync(SettingValues.Empty);
        var view = Assert.IsType<BluetoothViewModel>(widget.ViewModel);
        Assert.Equal(0, _devices.Reads);

        _visibility.Value = WidgetVisibility.Panel;

        Assert.Equal(1, _devices.Reads);
        Assert.Equal(["80%", "Battery unknown"], view.Rows.Select(r => r.Battery));
        Assert.False(view.Rows[1].HasLevel);

        _visibility.Value = WidgetVisibility.Hidden;
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
        await widget.StopAsync();
    }

    private sealed class FakeDevices : IBluetoothDevices
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<BluetoothGadget>> ConnectedAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<BluetoothGadget>>([new("1", "Earbuds", 80), new("2", "Keyboard", null)]);
        }
    }
}
