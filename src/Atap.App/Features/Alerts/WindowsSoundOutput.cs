using Atap.Core.Alerts;

namespace Atap.Features.Alerts;

/// <summary>The real sounds: the Windows notification sound, and files through Media Foundation.</summary>
internal sealed class WindowsSoundOutput : ISoundOutput
{
    public void PlayNotification() =>
        AlertsNative.PlaySound(
            AlertsNative.NotificationSound,
            IntPtr.Zero,
            AlertsNative.SND_ALIAS | AlertsNative.SND_ASYNC | AlertsNative.SND_NODEFAULT | AlertsNative.SND_SYSTEM);

    public void StopNotification() => AlertsNative.PlaySound(null, IntPtr.Zero, 0);

    // NAudio loads here, the first time a file or bundled sound plays, and not before.
    public ISoundHandle Start(AlertSound sound, Action<Exception?> ended) => MediaFoundationPlayback.Start(sound, ended);
}
