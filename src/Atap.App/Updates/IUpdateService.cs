namespace Atap.Updates;

public enum UpdateStatus
{
    /// <summary>Waiting for the next check.</summary>
    Idle,

    Checking,

    /// <summary>The last check found nothing newer.</summary>
    UpToDate,

    Downloading,

    /// <summary>An update is downloaded and installs when Atap exits.</summary>
    ReadyToInstall,
}

/// <summary>Self-updates for installed builds. Store builds have none (the Store updates the app).</summary>
public interface IUpdateService : IDisposable
{
    /// <summary>False when the app did not come from a Velopack release, the installer or the portable zip (for example, run from a build folder).</summary>
    bool IsSupported { get; }

    UpdateStatus Status { get; }

    /// <summary>Raised on the UI thread when <see cref="Status"/> changes.</summary>
    event EventHandler? StatusChanged;

    /// <summary>Starts the background schedule.</summary>
    void Start();

    /// <summary>Checks now (and downloads if there is an update). Does nothing while a check is running.</summary>
    Task CheckNowAsync();

    /// <summary>Hands a downloaded update to the updater, which installs it once this process has exited.</summary>
    void ApplyPendingOnExit();
}
