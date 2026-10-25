using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Atap.Platform.Audio;

/// <summary>An output such as "Speakers (Realtek)" or "Headphones".</summary>
internal sealed record AudioOutput(string Id, string Name, bool IsDefault);

/// <summary>One app making sound, with its own volume. The id stands for every session of the app. The name is empty for the system sounds or when nothing usable is known.</summary>
internal sealed record AppAudio(string Id, string Name, int VolumePercent, bool Muted, bool IsSystemSounds = false);

/// <summary>Outputs and per-app volume. Read on demand, with nothing held open between calls. Faked in tests.</summary>
internal interface IAudioDevices
{
    IReadOnlyList<AudioOutput> Outputs();

    /// <summary>Makes an output the default for every role. False when Windows refused, so the caller offers Sound settings.</summary>
    bool TrySetDefault(string id);

    void OpenSoundSettings();

    /// <summary>Apps with an audio session on the default output, one row per app, with the system sounds.</summary>
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
            var found = new List<AudioSessionInfo>();
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired)
                    continue;
                found.Add(Read(session));
            }
            return AudioSessionRules.Merge(found);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            return [];
        }
    }

    public void SetAppVolume(string id, int percent) =>
        WithSessions(id, s => s.SimpleAudioVolume.Volume = Math.Clamp(percent, 0, 100) / 100f);

    public void SetAppMuted(string id, bool muted) => WithSessions(id, s => s.SimpleAudioVolume.Mute = muted);

    /// <summary>Changes every session a mixer row stands for.</summary>
    private static void WithSessions(string rowId, Action<AudioSessionControl> change)
    {
        var ids = AudioSessionRules.SplitIds(rowId).ToHashSet(StringComparer.Ordinal);
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var sessions = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).AudioSessionManager.Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                if (ids.Contains(sessions[i].GetSessionInstanceIdentifier))
                    change(sessions[i]);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            // The app went away. The next read drops it.
        }
    }

    /// <summary>The session and what its program says about itself. The system sounds session has no program.</summary>
    private static AudioSessionInfo Read(AudioSessionControl session)
    {
        var processId = (int)session.GetProcessID;
        var system = session.IsSystemSoundsSession || processId == 0;
        string? description = null, product = null, processName = null, path = null;
        if (!system)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                processName = process.ProcessName;
                var module = process.MainModule;
                path = module?.FileName;
                description = module?.FileVersionInfo.FileDescription;
                product = module?.FileVersionInfo.ProductName;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // A protected or finished process. What was read so far is used.
            }
        }
        return new AudioSessionInfo(
            session.GetSessionInstanceIdentifier,
            system,
            session.DisplayName,
            description,
            product,
            processName,
            path,
            (int)Math.Round(session.SimpleAudioVolume.Volume * 100),
            session.SimpleAudioVolume.Mute);
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
