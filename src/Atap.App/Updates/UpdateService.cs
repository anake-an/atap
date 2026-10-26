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
    private bool _restart;

    // Long enough to read "Installing the update" before Atap closes.
    private static readonly TimeSpan InstallNotice = TimeSpan.FromSeconds(2);

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
        // The schedule never closes Atap. Its update waits until Atap exits.
        _timer = new Timer(_ => _ = CheckAsync(userAsked: false), null, FirstCheckDelay, CheckInterval);
    }

    public Task CheckNowAsync() => CheckAsync(userAsked: true);

    private async Task CheckAsync(bool userAsked)
    {
        if (_manager is null || _restart)
            return;
        if (_pending is not null)
        {
            if (userAsked)
                InstallNow();
            return;
        }
        // One check at a time. The timer and the menu can race.
        if (Interlocked.Exchange(ref _checking, 1) == 1)
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
            if (userAsked)
            {
                Log.Info($"Update {update.TargetFullRelease.Version} downloaded. Atap restarts to install it.");
                InstallNow();
            }
            else
            {
                Log.Info($"Update {update.TargetFullRelease.Version} downloaded. It installs when Atap exits.");
                SetStatus(UpdateStatus.ReadyToInstall);
            }
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

    /// <summary>Shows that the update installs, then closes Atap the normal way, so settings are saved first.</summary>
    private void InstallNow()
    {
        _restart = true;
        SetStatus(UpdateStatus.Installing);
        _ = Task.Delay(InstallNotice).ContinueWith(
            _ => _dispatcher.BeginInvoke(() => System.Windows.Application.Current?.Shutdown()),
            TaskScheduler.Default);
    }

    public void ApplyPendingOnExit()
    {
        if (_manager is null || _pending is null)
            return;
        try
        {
            // The updater waits for this process to exit and installs silently. It opens Atap again only when the
            // user asked for the update. An update found by the schedule waits for the next start.
            _manager.WaitExitThenApplyUpdates(_pending, silent: true, restart: _restart);
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
