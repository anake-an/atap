namespace Atap.Features.Alerts;

/// <summary>Whether Windows asks apps to stay quiet right now. Read only when an alert is about to show, never polled.</summary>
internal interface IQuietHours
{
    bool IsOn();
}

/// <summary>
/// Do not disturb on Windows 11, Focus assist on Windows 10, and the quiet first hour after a new sign-in.
/// Two cheap calls, made only when an alert is about to show.
/// </summary>
internal sealed class WindowsQuietHours : IQuietHours
{
    public bool IsOn() => IsQuietTime() || IsFocusOn();

    private static bool IsQuietTime() =>
        AlertsNative.SHQueryUserNotificationState(out var state) == 0 && state == AlertsNative.QuietTime;

    // Windows has no documented call for this, so a missing or changed state counts as off.
    private static bool IsFocusOn()
    {
        try
        {
            var name = AlertsNative.QuietHoursProfileState;
            var size = (uint)sizeof(int);
            var status = AlertsNative.NtQueryWnfStateData(ref name, IntPtr.Zero, IntPtr.Zero, out _, out var profile, ref size);
            return status == 0 && size == sizeof(int) && profile > 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
