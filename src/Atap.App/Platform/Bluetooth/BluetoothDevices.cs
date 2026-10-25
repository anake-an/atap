using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace Atap.Platform.Bluetooth;

/// <param name="Battery">0 to 100, or null when the device does not report it.</param>
internal sealed record BluetoothGadget(string Id, string Name, int? Battery);

/// <summary>Connected Bluetooth devices. Read on demand, nothing kept open. Faked in tests.</summary>
internal interface IBluetoothDevices
{
    Task<IReadOnlyList<BluetoothGadget>> ConnectedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Connected classic and Bluetooth LE devices. Battery comes from the standard GATT Battery Level characteristic
/// for LE devices. Anything else, or any failure, reads as unknown rather than a guess.
/// </summary>
internal sealed class WindowsBluetoothDevices : IBluetoothDevices
{
    public async Task<IReadOnlyList<BluetoothGadget>> ConnectedAsync(CancellationToken cancellationToken = default)
    {
        var found = new List<BluetoothGadget>();
        try
        {
            foreach (var info in await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected)).AsTask(cancellationToken))
                found.Add(new BluetoothGadget(info.Id, Name(info), await LeBatteryAsync(info.Id, cancellationToken)));
            foreach (var info in await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected)).AsTask(cancellationToken))
            {
                if (!found.Any(f => string.Equals(f.Name, Name(info), StringComparison.OrdinalIgnoreCase)))
                    found.Add(new BluetoothGadget(info.Id, Name(info), null));
            }
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Bluetooth is off or missing: what was found so far stands.
        }
        return found;
    }

    private static string Name(DeviceInformation info) => string.IsNullOrWhiteSpace(info.Name) ? info.Id : info.Name;

    private static async Task<int?> LeBatteryAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            using var device = await BluetoothLEDevice.FromIdAsync(id).AsTask(cancellationToken);
            if (device is null)
                return null;
            var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached).AsTask(cancellationToken);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
                return null;
            using var service = services.Services[0];
            var characteristics = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached).AsTask(cancellationToken);
            if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0)
                return null;
            var value = await characteristics.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            if (value.Status != GattCommunicationStatus.Success || value.Value.Length < 1)
                return null;
            using var reader = DataReader.FromBuffer(value.Value);
            var level = reader.ReadByte();
            return level <= 100 ? level : null;
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException)
        {
            return null;
        }
    }
}
