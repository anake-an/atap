namespace Atap.Core.Lyrics;

/// <summary>
/// What the player last reported: a song position, the moment it was true, and whether playback is moving.
/// The position at any later moment follows from it, so nothing has to poll the player.
/// </summary>
/// <param name="Position">Song position at <paramref name="SampledAt"/>. A negative value counts as zero.</param>
/// <param name="SampledAt">When <paramref name="Position"/> was true.</param>
/// <param name="IsPlaying">False while paused or stopped. The position then stays where it is.</param>
/// <param name="Rate">Playback speed. See <see cref="EffectiveRate"/>.</param>
public readonly record struct PlaybackSnapshot(TimeSpan Position, DateTimeOffset SampledAt, bool IsPlaying, double Rate = 1)
{
    private const double MinRate = 1.0 / 16;
    private const double MaxRate = 16;

    /// <summary>
    /// The speed used for the math: <see cref="Rate"/> kept between 1/16 and 16, or 1 when it is not a positive number.
    /// Players often report 0 or nothing when they mean normal speed.
    /// </summary>
    public double EffectiveRate => double.IsFinite(Rate) && Rate > 0 ? Math.Clamp(Rate, MinRate, MaxRate) : 1;

    /// <summary>
    /// The song position at <paramref name="now"/>. A <paramref name="now"/> before <see cref="SampledAt"/>
    /// gives <see cref="Position"/>, so a clock that steps back never rewinds the lyrics.
    /// </summary>
    public TimeSpan PositionAt(DateTimeOffset now)
    {
        var start = Position < TimeSpan.Zero ? TimeSpan.Zero : Position;
        if (!IsPlaying || now <= SampledAt)
            return start;

        var ticks = start.Ticks + (now - SampledAt).Ticks * EffectiveRate;
        return ticks >= TimeSpan.MaxValue.Ticks ? TimeSpan.MaxValue : TimeSpan.FromTicks((long)ticks);
    }

    /// <summary>
    /// When playback reaches <paramref name="target"/>, seen from <paramref name="now"/>. A target already passed
    /// gives <paramref name="now"/>. Null while paused, or when the moment is out of range.
    /// The result is rounded up to the next tick, so a timer armed for it never fires before the target.
    /// </summary>
    public DateTimeOffset? WhenReaches(TimeSpan target, DateTimeOffset now)
    {
        if (!IsPlaying)
            return null;

        var position = PositionAt(now);
        if (target <= position)
            return now;

        var ticks = Math.Ceiling((target - position).Ticks / EffectiveRate);
        var room = DateTimeOffset.MaxValue.UtcTicks - now.UtcTicks;
        return ticks >= room ? null : now.AddTicks((long)ticks);
    }
}
