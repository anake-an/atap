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

    /// <summary>Atap is closing to install the update, and opens again by itself.</summary>
    Installing,
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

    /// <summary>
    /// The user asked to check. An update is downloaded, then Atap closes, installs it and opens again by itself.
    /// An update that is already downloaded installs at once. Does nothing while a check is running.
    /// </summary>
    Task CheckNowAsync();

    /// <summary>Hands a downloaded update to the updater, which installs it once this process has exited.</summary>
    void ApplyPendingOnExit();
}
