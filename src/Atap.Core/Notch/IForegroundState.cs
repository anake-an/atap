using Atap.Core.Placement;

namespace Atap.Core.Notch;

/// <summary>What is in front, from the foreground window event. Never polled.</summary>
public interface IForegroundState
{
    /// <summary>
    /// What Windows reports for the whole PC. The notch hides for <see cref="FullScreenKind.Game"/> and
    /// <see cref="FullScreenKind.Presentation"/>. Alerts wait while it is not <see cref="FullScreenKind.None"/>.
    /// </summary>
    FullScreenKind FullScreen { get; }

    /// <summary>
    /// The screen that the front window covers completely, in full screen or as a borderless window, in physical
    /// pixels. Null while no window covers a whole screen. The notch on that screen turns slim.
    /// </summary>
    PixelRect? CoveredScreen { get; }

    /// <summary>The foreground exe name without a path, only while <see cref="WantProcessNames"/> is held.</summary>
    string? ProcessName { get; }

    /// <summary>Starts reading the foreground process name. Dispose to stop.</summary>
    IDisposable WantProcessNames();

    /// <summary>Raised on the UI thread.</summary>
    event EventHandler? Changed;
}

/// <summary>Why Windows considers the user busy with a full-screen app.</summary>
public enum FullScreenKind
{
    None,

    /// <summary>A full-screen app or video.</summary>
    FullScreen,

    /// <summary>A Direct3D game in exclusive full screen.</summary>
    Game,

    /// <summary>Presentation mode.</summary>
    Presentation,
}

/// <summary>Whether a window fills a whole screen, pure.</summary>
public static class ScreenCover
{
    /// <summary>
    /// True when <paramref name="window"/> reaches every edge of <paramref name="screen"/>. A maximized window
    /// stops at the taskbar, so it does not count. Full screen and borderless windows do, even when they reach
    /// a little past the edges.
    /// </summary>
    public static bool Covers(PixelRect window, PixelRect screen) =>
        screen.Width > 0 && screen.Height > 0
        && window.Left <= screen.Left && window.Top <= screen.Top
        && window.Right >= screen.Right && window.Bottom >= screen.Bottom;
}
