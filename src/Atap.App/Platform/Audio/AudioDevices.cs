using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Atap.Platform.Audio;

/// <summary>An output such as "Speakers (Realtek)" or "Headphones".</summary>
internal sealed record AudioOutput(string Id, string Name, bool IsDefault);

/// <summary>One app making sound, with its own volume.</summary>
internal sealed record AppAudio(string Id, string Name, int VolumePercent, bool Muted);

/// <summary>Outputs and per-app volume. Read on demand, with nothing held open between calls. Faked in tests.</summary>
internal interface IAudioDevices
{
    IReadOnlyList<AudioOutput> Outputs();

    /// <summary>Makes an output the default for every role. False when Windows refused, so the caller offers Sound settings.</summary>
    bool TrySetDefault(string id);

    void OpenSoundSettings();

    /// <summary>Apps with an active audio session on the default output.</summary>
    IReadOnlyList<AppAudio> Apps();

    void SetAppVolume(string id, int percent);

    void SetAppMuted(string id, bool muted);
}

/// <summary>
/// Core Audio through NAudio. Switching the default output has no public API, so it uses the undocumented
/// IPolicyConfig interface in this one file. On any failure the widget opens the Sound settings page instead.
/// </summary>
internal sealed class WindowsAudioDevices : IAudioDevices
{
    public IReadOnlyList<AudioOutput> Outputs()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultId = enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID
                : null;
            return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(d => new AudioOutput(d.ID, d.FriendlyName, d.ID == defaultId))
                .ToList();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return [];
        }
    }

    public bool TrySetDefault(string id)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                {
                    if (policy.SetDefaultEndpoint(id, role) != 0)
                        return false;
                }
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(policy);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return false;
        }
    }

    public void OpenSoundSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing more to try.
        }
    }

    public IReadOnlyList<AppAudio> Apps()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                return [];
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            var apps = new List<AppAudio>();
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.IsSystemSoundsSession || session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired)
                    continue;
                apps.Add(new AppAudio(
                    session.GetSessionInstanceIdentifier,
                    NameOf(session),
                    (int)Math.Round(session.SimpleAudioVolume.Volume * 100),
                    session.SimpleAudioVolume.Mute));
            }
            return apps.GroupBy(a => a.Name).Select(g => g.First()).ToList();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            return [];
        }
    }

    public void SetAppVolume(string id, int percent) =>
        WithSession(id, s => s.SimpleAudioVolume.Volume = Math.Clamp(percent, 0, 100) / 100f);

    public void SetAppMuted(string id, bool muted) => WithSession(id, s => s.SimpleAudioVolume.Mute = muted);

    private static void WithSession(string id, Action<AudioSessionControl> change)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var sessions = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).AudioSessionManager.Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                if (sessions[i].GetSessionInstanceIdentifier == id)
                {
                    change(sessions[i]);
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            // The app went away. The next read drops it.
        }
    }

    /// <summary>The session's display name, else the process's file description or name.</summary>
    private static string NameOf(AudioSessionControl session)
    {
        if (!string.IsNullOrWhiteSpace(session.DisplayName) && !session.DisplayName.StartsWith('@'))
            return session.DisplayName;
        try
        {
            using var process = Process.GetProcessById((int)session.GetProcessID);
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            return string.IsNullOrWhiteSpace(description) ? process.ProcessName : description;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return session.GetSessionIdentifier;
        }
    }

    private enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2,
    }

    [ComImport]
    [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient;

    // Undocumented. Only SetDefaultEndpoint is called. The methods before it keep the order of the vtable.
    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device, int defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string device, int defaultPeriod, IntPtr defaultTime, IntPtr minimumTime);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string device, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string device, int visible);
    }
}
