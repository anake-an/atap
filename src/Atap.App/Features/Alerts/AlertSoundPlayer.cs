using Atap.Core.Alerts;

namespace Atap.Features.Alerts;

/// <summary>Makes the sounds. The app uses <see cref="WindowsSoundOutput"/>. Tests use a fake that plays nothing.</summary>
internal interface ISoundOutput
{
    /// <summary>The short Windows notification sound. It is not followed: it ends on its own.</summary>
    void PlayNotification();

    /// <summary>Stops the notification sound if it still plays.</summary>
    void StopNotification();

    /// <summary>
    /// Starts a file or bundled sound, once. Returns at once: opening and playing happen off the UI thread.
    /// <paramref name="ended"/> is called once, from any thread: with null when the sound finished or was
    /// stopped, with the exception when it could not play.
    /// </summary>
    ISoundHandle Start(AlertSound sound, Action<Exception?> ended);
}

/// <summary>A file or bundled sound that is playing.</summary>
internal interface ISoundHandle
{
    /// <summary>Stops it. The end is still reported once.</summary>
    void Stop();
}

/// <summary>A file or bundled sound the player follows until it ends.</summary>
internal sealed class PlayingSound(AlertSound sound, AlertPriority priority, ShownAlert? alert)
{
    public AlertSound Sound { get; } = sound;

    public AlertPriority Priority { get; } = priority;

    /// <summary>The alert it plays for, if any.</summary>
    public ShownAlert? Alert { get; } = alert;

    public bool IsEnded { get; internal set; }

    internal ISoundHandle? Handle { get; set; }

    internal bool IsFor(string source, string key) =>
        Alert is { } alert
        && string.Equals(alert.Source, source, StringComparison.Ordinal)
        && string.Equals(alert.Alert.Key, key, StringComparison.Ordinal);
}

/// <summary>
/// Plays alert sounds, and later the azan. Every sound plays once and never loops. A sound that plays is
/// never cut by another: a new Normal or High sound is skipped while one plays, and a Critical one plays
/// alongside. Only <see cref="Stop"/> ends a sound early. Sounds other than Critical respect quiet hours
/// and full screen. Use it on the UI thread.
/// </summary>
internal sealed class AlertSoundPlayer : IDisposable
{
    private readonly ISoundOutput _output;
    private readonly SynchronizationContext _ui;
    private readonly Func<bool> _quiet;
    private readonly Func<bool> _hidden;
    private readonly Action<string, Exception?> _log;
    private readonly List<PlayingSound> _playing = [];
    private bool _disposed;

    /// <param name="output">Makes the sounds.</param>
    /// <param name="ui">The UI thread, where the end of a sound is handled.</param>
    /// <param name="quiet">True while alerts are quiet: a rule, or Windows quiet hours.</param>
    /// <param name="hidden">True while the notch is hidden, for example by a full-screen app.</param>
    /// <param name="log">Warnings, with the exception if any.</param>
    public AlertSoundPlayer(ISoundOutput output, SynchronizationContext ui, Func<bool> quiet, Func<bool> hidden, Action<string, Exception?> log)
    {
        _output = output;
        _ui = ui;
        _quiet = quiet;
        _hidden = hidden;
        _log = log;
    }

    /// <summary>True while a file or bundled sound plays. The Windows sound is too short to count.</summary>
    public bool IsPlaying => _playing.Count > 0;

    /// <summary>The file and bundled sounds that play right now.</summary>
    public IReadOnlyList<PlayingSound> Playing => _playing;

    /// <summary>Raised when <see cref="IsPlaying"/> changes.</summary>
    public event EventHandler? PlayingChanged;

    /// <summary>Raised once for every file or bundled sound that started, when it ends, fails or is stopped.</summary>
    public event EventHandler<PlayingSound>? Ended;

    /// <summary>
    /// Plays <paramref name="sound"/> once, when it may play now. Returns the file or bundled sound to
    /// follow, or null when it did not start or is the Windows sound.
    /// </summary>
    /// <param name="sound">What to play.</param>
    /// <param name="priority">Critical plays on time, even in full screen or quiet hours.</param>
    /// <param name="alert">The alert it plays for, so <see cref="StopFor"/> can find it.</param>
    public PlayingSound? Play(AlertSound sound, AlertPriority priority, ShownAlert? alert = null)
    {
        ArgumentNullException.ThrowIfNull(sound);
        if (_disposed)
            return null;
        if (priority != AlertPriority.Critical && !AlertSounds.MayPlay(priority, _quiet(), _hidden()))
            return null;
        if (IsPlaying && priority != AlertPriority.Critical)
            return null;

        if (sound.Kind == AlertSoundKind.Windows)
        {
            PlayNotification();
            return null;
        }

        var playing = new PlayingSound(sound, priority, alert);
        _playing.Add(playing);
        try
        {
            playing.Handle = _output.Start(sound, error => _ui.Post(_ => OnEnded(playing, error), null));
        }
        catch (Exception ex)
        {
            OnEnded(playing, ex);
            return null;
        }
        if (_playing.Count == 1 && !playing.IsEnded)
            PlayingChanged?.Invoke(this, EventArgs.Empty);
        return playing.IsEnded ? null : playing;
    }

    /// <summary>Stops every sound now, the Windows sound included.</summary>
    public void Stop() => Stop(_playing.ToList());

    /// <summary>Stops these sounds, and the Windows sound unless told not to. A sound that started since is left playing.</summary>
    public void Stop(IEnumerable<PlayingSound> sounds, bool notification = true)
    {
        ArgumentNullException.ThrowIfNull(sounds);
        try
        {
            if (notification)
                _output.StopNotification();
        }
        catch (Exception ex)
        {
            _log("The Windows sound could not be stopped.", ex);
        }
        foreach (var playing in sounds.ToList())
            StopOne(playing);
    }

    /// <summary>Stops the sounds that play for one alert, for example when its source dismissed it.</summary>
    public void StopFor(string source, string key)
    {
        foreach (var playing in _playing.Where(p => p.IsFor(source, key)).ToList())
            StopOne(playing);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Stop();
        _disposed = true;
    }

    private void StopOne(PlayingSound playing)
    {
        if (playing.IsEnded)
            return;
        try
        {
            playing.Handle?.Stop();
        }
        catch (Exception ex)
        {
            _log("An alert sound could not be stopped.", ex);
        }
        // Done now, so the Stop button goes at once. The end the output reports later is ignored.
        Finish(playing);
    }

    private void OnEnded(PlayingSound playing, Exception? error)
    {
        if (playing.IsEnded)
            return;
        if (error is not null)
        {
            // A missing file or codec: the Windows sound still lets the user know.
            _log("An alert sound could not play, so the Windows sound played instead.", error);
            if (!_disposed)
                PlayNotification();
        }
        Finish(playing);
    }

    private void Finish(PlayingSound playing)
    {
        if (playing.IsEnded)
            return;
        playing.IsEnded = true;
        var wasPlaying = IsPlaying;
        _playing.Remove(playing);
        Ended?.Invoke(this, playing);
        if (wasPlaying && !IsPlaying)
            PlayingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PlayNotification()
    {
        try
        {
            _output.PlayNotification();
        }
        catch (Exception ex)
        {
            _log("The Windows sound could not play.", ex);
        }
    }
}
