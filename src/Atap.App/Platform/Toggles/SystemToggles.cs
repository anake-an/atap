using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using Windows.Devices.Radios;

namespace Atap.Platform.Toggles;

/// <summary>One on/off switch. Null state means the PC has no such thing, and the toggle hides.</summary>
internal interface ISystemToggle
{
    string Id { get; }

    /// <summary>Reads the state now. Null when unavailable.</summary>
    Task<bool?> ReadAsync();

    /// <summary>True when Windows took the change. False is shown as a failure, never as success.</summary>
    Task<bool> SetAsync(bool on);
}

/// <summary>Dark mode for apps and Windows, in the user's Personalize key, then a settings broadcast.</summary>
internal sealed partial class DarkModeToggle : ISystemToggle
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int HwndBroadcast = 0xffff;
    private const int WmSettingChange = 0x001A;
    private const int SmtoAbortIfHung = 0x0002;

    public string Id => "darkMode";

    public Task<bool?> ReadAsync()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return Task.FromResult(key?.GetValue("AppsUseLightTheme") is int light ? light == 0 : (bool?)null);
    }

    public Task<bool> SetAsync(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key is null)
                return Task.FromResult(false);
            var light = on ? 0 : 1;
            key.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", light, RegistryValueKind.DWord);
            SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, "ImmersiveColorSet", SmtoAbortIfHung, 1000, out _);
            return Task.FromResult(true);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return Task.FromResult(false);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);

    private static IntPtr SendMessageTimeout(int hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result) =>
        SendMessageTimeout(new IntPtr(hWnd), msg, wParam, lParam, flags, timeout, out result);
}

/// <summary>The Bluetooth radio through Windows.Devices.Radios. Hidden on PCs without one.</summary>
internal sealed class BluetoothToggle : ISystemToggle
{
    public string Id => "bluetooth";

    public async Task<bool?> ReadAsync() => await FindAsync() is { } radio ? radio.State == RadioState.On : null;

    public async Task<bool> SetAsync(bool on)
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed || await FindAsync() is not { } radio)
                return false;
            return await radio.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<Radio?> FindAsync()
    {
        try
        {
            return (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Mutes the default microphone. Hidden when there is no microphone.</summary>
internal sealed class MicMuteToggle : ISystemToggle
{
    public string Id => "micMute";

    public Task<bool?> ReadAsync() => Task.FromResult(With(d => (bool?)d.AudioEndpointVolume.Mute, null));

    public Task<bool> SetAsync(bool on) => Task.FromResult(With(d =>
    {
        d.AudioEndpointVolume.Mute = on;
        return d.AudioEndpointVolume.Mute == on;
    }, false));

    private static T With<T>(Func<MMDevice, T> use, T fallback)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications))
                return fallback;
            return use(enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications));
        }
        catch (Exception e) when (e is COMException or InvalidOperationException)
        {
            return fallback;
        }
    }
}
