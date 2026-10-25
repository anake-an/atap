namespace Atap.Core.Notch;

/// <summary>What is in front, from the foreground window event. Never polled.</summary>
public interface IForegroundState
{
    /// <summary>A full-screen app is in front. The notch hides while it is not <see cref="FullScreenKind.None"/>.</summary>
    FullScreenKind FullScreen { get; }

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
