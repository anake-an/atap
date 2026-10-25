#if !STORE
using System.Windows.Threading;
using Atap.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace Atap.Updates;

/// <summary>
/// Velopack updates from GitHub Releases. Stable builds get stable releases; prerelease builds
/// also get later prereleases. Checks 30 s after start and then every 24 h on one timer,
/// downloads in the background, and installs when Atap exits. Inactive unless the app came from
/// a Velopack release (the installer or the portable zip). Failures are logged, never shown.
/// </summary>
internal sealed class UpdateService : IUpdateService
{
    private const string RepositoryUrl = "https://github.com/anake-an/atap";

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly Dispatcher _dispatcher;
    private readonly UpdateManager? _manager;
    private Timer? _timer;
    private VelopackAsset? _pending;
    private UpdateStatus _status;
    private int _checking;

    public UpdateService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        if (AppInfo.InstallSource == Core.Updates.InstallSource.Scoop)
        {
            // Scoop owns the files and updates them. Atap replacing them would confuse Scoop.
            Log.Info("Updates are off: Scoop installed Atap and keeps it up to date.");
            _status = UpdateStatus.Idle;
            return;
        }

        try
        {
            // A preview such as 0.1.0-preview.1 must see later previews, or it is stuck until the next stable release.
            var prerelease = AppInfo.Version.Contains('-', StringComparison.Ordinal);
            var manager = new UpdateManager(new GithubSource(RepositoryUrl, null, prerelease));
            if (manager.IsInstalled)
            {
                _manager = manager;
                _pending = manager.UpdatePendingRestart;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Updates are off: the update manager could not start.", ex);
        }

        _status = _pending is null ? UpdateStatus.Idle : UpdateStatus.ReadyToInstall;
    }

    public bool IsSupported => _manager is not null;

    public UpdateStatus Status => _status;

    public event EventHandler? StatusChanged;

    public void Start()
    {
        if (_manager is null || _timer is not null)
            return;
        _timer = new Timer(_ => _ = CheckNowAsync(), null, FirstCheckDelay, CheckInterval);
    }

    public async Task CheckNowAsync()
    {
        // One check at a time; the timer and the menu can race.
        if (_manager is null || _pending is not null || Interlocked.Exchange(ref _checking, 1) == 1)
            return;

        try
        {
            SetStatus(UpdateStatus.Checking);
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                SetStatus(UpdateStatus.UpToDate);
                return;
            }

            SetStatus(UpdateStatus.Downloading);
            await _manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            _pending = update.TargetFullRelease;
            Log.Info($"Update {update.TargetFullRelease.Version} downloaded; it installs when Atap exits.");
            SetStatus(UpdateStatus.ReadyToInstall);
        }
        catch (Exception ex)
        {
            Log.Warn("Update check failed.", ex);
            SetStatus(UpdateStatus.Idle);
        }
        finally
        {
            Volatile.Write(ref _checking, 0);
        }
    }

    public void ApplyPendingOnExit()
    {
        if (_manager is null || _pending is null)
            return;
        try
        {
            // The updater waits for this process to exit, installs silently and does not relaunch.
            _manager.WaitExitThenApplyUpdates(_pending, silent: true, restart: false);
        }
        catch (Exception ex)
        {
            // Velopack also applies a downloaded update on the next start.
            Log.Warn("Could not hand the update to the updater.", ex);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void SetStatus(UpdateStatus status) =>
        _dispatcher.InvokeAsync(() =>
        {
            if (_status == status)
                return;
            _status = status;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        });
}
#endif
