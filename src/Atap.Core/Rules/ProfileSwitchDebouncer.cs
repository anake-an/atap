using Atap.Core.Scheduling;

namespace Atap.Core.Rules;

/// <summary>
/// Switches the profile only after the rules have pointed at it for <see cref="Delay"/>, so tabbing through
/// windows does not flip the layout. A new result re-arms one one-shot timer. While the layout editor is open
/// nothing switches. A result that is still waiting is applied when the editor closes.
/// </summary>
public sealed class ProfileSwitchDebouncer : IDisposable
{
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(1.5);

    private readonly IScheduler _scheduler;
    private readonly TimeProvider _time;
    private readonly Action<string> _apply;
    private string? _pending;
    private bool _editing;
    private IDisposable? _timer;

    public ProfileSwitchDebouncer(IScheduler scheduler, TimeProvider time, string current, Action<string> apply)
    {
        _scheduler = scheduler;
        _time = time;
        _apply = apply;
        Current = current;
    }

    /// <summary>The profile last applied.</summary>
    public string Current { get; private set; }

    /// <summary>The profile waiting for its 1.5 s, or for the editor to close. Null when none is.</summary>
    public string? Pending => _pending;

    public void Propose(string profileId)
    {
        if (profileId == _pending)
            return;
        _timer?.Dispose();
        _timer = null;
        if (profileId == Current)
        {
            _pending = null;
            return;
        }
        _pending = profileId;
        _timer = _scheduler.At(_time.GetUtcNow() + Delay, Fire);
    }

    /// <summary>The layout editor opened or closed.</summary>
    public void SetEditing(bool editing)
    {
        if (_editing == editing)
            return;
        _editing = editing;
        if (!editing && _pending is not null && _timer is null)
            Fire();
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _pending = null;
    }

    private void Fire()
    {
        _timer?.Dispose();
        _timer = null;
        if (_editing || _pending is not { } next)
            return;
        _pending = null;
        Current = next;
        _apply(next);
    }
}
