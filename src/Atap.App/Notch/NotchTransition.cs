using System.Windows;
using Atap.Core.Placement;

namespace Atap.Notch;

/// <summary>How the notch content changed, so the view knows whether to fade it in.</summary>
public enum NotchContentChange
{
    /// <summary>Another surface, page or live widget. The new content fades in while the shape springs.</summary>
    Swapped,

    /// <summary>The same content with a part added or taken away, such as an alert banner. Only the size springs.</summary>
    Refreshed,
}

public sealed class NotchContentChangedEventArgs(NotchContentChange change) : EventArgs
{
    public static readonly NotchContentChangedEventArgs Swapped = new(NotchContentChange.Swapped);
    public static readonly NotchContentChangedEventArgs Refreshed = new(NotchContentChange.Refreshed);

    public NotchContentChange Change { get; } = change;
}

/// <summary>What one state change does, worked out before anything moves.</summary>
/// <param name="Fade">The new content fades in from the first frame.</param>
/// <param name="Spring">The size springs. Otherwise size and content change at once.</param>
internal readonly record struct NotchTransitionPlan(bool Fade, bool Spring);

/// <summary>
/// The pure rules that make a notch state change one smooth step. The new content, the new size and the window
/// room all change in the same frame. The content fades in only when it is new, never when a part is added to it.
/// The window never moves its anchored corner while the notch changes size, so nothing jumps.
/// </summary>
internal static class NotchTransition
{
    // Extra window room so the spring overshoot is not clipped.
    public const double OvershootAllowance = 0.15;

    /// <param name="change">What changed.</param>
    /// <param name="calm">Less motion, from Atap, battery saver or Windows' animation effects.</param>
    /// <param name="hasSize">False before the notch has been sized once. The first size is set at once.</param>
    public static NotchTransitionPlan Plan(NotchContentChange change, bool calm, bool hasSize) =>
        new(Fade: !calm && hasSize && change == NotchContentChange.Swapped, Spring: !calm && hasSize);

    /// <summary>
    /// The window room while the notch springs and after it settles, in DIPs. A side whose window edge would move
    /// with the size (a centred notch, or one pinned to the right or bottom) keeps the most room it has had, so the
    /// window never moves while the shape changes. Other sides grow first and shrink after the spring.
    /// </summary>
    /// <param name="kept">The room kept so far. Empty after the notch moved or hid.</param>
    public static (Size During, Size After) Room(Size kept, Size current, Size target, bool spring, NotchAnchor anchor)
    {
        var during = spring
            ? new Size(WithOvershoot(current.Width, target.Width), WithOvershoot(current.Height, target.Height))
            : target;
        var after = target;
        if (KeepsWidth(anchor))
        {
            var width = Math.Max(kept.Width, during.Width);
            during.Width = width;
            after.Width = width;
        }
        if (KeepsHeight(anchor))
        {
            var height = Math.Max(kept.Height, during.Height);
            during.Height = height;
            after.Height = height;
        }
        return (during, after);
    }

    /// <summary>
    /// The notch's margin inside the window, in DIPs, so it sits where the placement puts it even when the window
    /// room is larger. <paramref name="room"/> and <paramref name="notch"/> are the window and the notch in pixels.
    /// </summary>
    public static Thickness Margin(PixelRect room, PixelRect notch, HorizontalAlignment horizontal, VerticalAlignment vertical, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
            scale = 1;
        var (left, right) = horizontal switch
        {
            HorizontalAlignment.Left => (notch.Left - room.Left, 0),
            HorizontalAlignment.Right => (0, room.Right - notch.Right),
            _ => Centre(notch.Left + notch.Right - room.Left - room.Right),
        };
        var (top, bottom) = vertical switch
        {
            VerticalAlignment.Top => (notch.Top - room.Top, 0),
            VerticalAlignment.Bottom => (0, room.Bottom - notch.Bottom),
            _ => Centre(notch.Top + notch.Bottom - room.Top - room.Bottom),
        };
        return new Thickness(Math.Max(0, left) / scale, Math.Max(0, top) / scale, Math.Max(0, right) / scale, Math.Max(0, bottom) / scale);
    }

    // The larger of two sizes, plus room for the spring to overshoot the change between them.
    public static double WithOvershoot(double current, double target) =>
        Math.Max(current, target) + Math.Abs(target - current) * OvershootAllowance;

    // A centred element moves by half the difference of its margins, so twice the offset goes on one side.
    private static (int Start, int End) Centre(int twiceOffset) => twiceOffset >= 0 ? (twiceOffset, 0) : (0, -twiceOffset);

    private static bool KeepsWidth(NotchAnchor anchor) => anchor is NotchAnchor.TopCenter or NotchAnchor.TopRight or NotchAnchor.RightCenter;

    private static bool KeepsHeight(NotchAnchor anchor) => anchor is NotchAnchor.LeftCenter or NotchAnchor.RightCenter;
}
