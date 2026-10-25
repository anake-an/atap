namespace Atap.Core.Notch;

/// <summary>
/// Turns the sideways wheel into page steps for the panel. A mouse tilt sends 120 per notch, a touchpad
/// sends many small amounts, so the amounts add up and one step happens per 120. After a step the rest
/// of that swipe is ignored for a moment, so one swipe turns one page. A pause starts a fresh count.
/// </summary>
public sealed class WheelPager
{
    public const int StepDelta = 120;

    /// <summary>How long after a step the same swipe is ignored.</summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(350);

    /// <summary>A pause this long between wheel amounts starts a fresh count.</summary>
    public static readonly TimeSpan ResetAfter = TimeSpan.FromMilliseconds(300);

    private int _total;
    private TimeSpan? _last;
    private TimeSpan? _stepped;

    /// <summary>Adds a wheel amount at a point in time. Returns +1 for the next page, -1 for the previous one, or 0.</summary>
    /// <param name="delta">Positive to the right.</param>
    /// <param name="now">Any steady clock, such as the time since start.</param>
    public int Add(int delta, TimeSpan now)
    {
        if (_last is { } last && now - last >= ResetAfter)
            _total = 0;
        _last = now;

        if (_stepped is { } stepped && now - stepped < SettleTime)
            return 0;

        _total += delta;
        if (Math.Abs(_total) < StepDelta)
            return 0;

        var step = Math.Sign(_total);
        _total = 0;
        _stepped = now;
        return step;
    }
}
