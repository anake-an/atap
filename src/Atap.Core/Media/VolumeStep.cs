namespace Atap.Core.Media;

/// <summary>
/// The volume after a step up or down, in whole percent. Each step is <see cref="Step"/> points, as with the
/// Windows volume keys, and an odd level snaps to the step grid first: 37 goes up to 38 and down to 36.
/// </summary>
public static class VolumeStep
{
    public const int Step = 2;

    /// <param name="percent">The volume now, 0 to 100.</param>
    /// <param name="steps">How many steps, positive for louder.</param>
    public static int Next(int percent, int steps)
    {
        var now = Math.Clamp(percent, 0, 100);
        if (steps == 0)
            return now;
        var start = steps > 0 ? now / Step * Step : (now + Step - 1) / Step * Step;
        return Math.Clamp(start + steps * Step, 0, 100);
    }

    /// <summary>Whole percent from Windows' 0 to 1 volume scalar.</summary>
    public static int ToPercent(float scalar) => (int)Math.Round(Math.Clamp(scalar, 0f, 1f) * 100, MidpointRounding.AwayFromZero);
}
