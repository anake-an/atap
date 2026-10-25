using System.Runtime.InteropServices;

namespace Atap.Features.Alerts;

/// <summary>The Windows notification sound and the Windows quiet hours state.</summary>
internal static class AlertsNative
{
    public const uint SND_ASYNC = 0x0001;
    public const uint SND_NODEFAULT = 0x0002;
    public const uint SND_ALIAS = 0x00010000;

    /// <summary>Plays in the system sounds session, so the "System sounds" volume applies.</summary>
    public const uint SND_SYSTEM = 0x00200000;

    /// <summary>The sound Windows plays for a notification, as set in the Sound control panel.</summary>
    public const string NotificationSound = "Notification.Default";

    /// <summary>QUNS_QUIET_TIME from SHQueryUserNotificationState.</summary>
    public const int QuietTime = 6;

    /// <summary>
    /// WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED: the Focus assist or Do not disturb profile, 0 when off.
    /// Not documented, so it is read only when an alert is about to show and any failure counts as off.
    /// </summary>
    public const ulong QuietHoursProfileState = 0x0D83063EA3BF1C75;

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PlaySound(string? sound, IntPtr module, uint flags);

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryWnfStateData(
        ref ulong stateName, IntPtr typeId, IntPtr explicitScope, out uint changeStamp, out int buffer, ref uint bufferSize);
}
