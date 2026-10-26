using Atap.Core.Shared;

namespace Atap.Core.Lyrics;

/// <summary>
/// The lyric line playing now, shared so one widget can show what another found. The lyrics widget publishes it,
/// and the music widget reads it for the small notch. Neither knows the other: both only lease this.
/// A reader that shows the line calls <see cref="Want"/>, so the publisher keeps the line moving while it is hidden.
/// Use it on the UI thread only.
/// </summary>
public sealed class LyricLine : ISharedService<LyricLine>
{
    private int _wanted;

    private LyricLine()
    {
    }

    /// <summary>The line playing now. Empty when there is none, such as between songs or without synced lyrics.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>True while a reader shows the line.</summary>
    public bool IsWanted => _wanted > 0;

    /// <summary>Raised when <see cref="Text"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when <see cref="IsWanted"/> changes.</summary>
    public event EventHandler? WantedChanged;

    public static LyricLine Create(SharedServiceContext context) => new();

    /// <summary>Sets the line playing now. Null or blank clears it.</summary>
    public void Publish(string? text)
    {
        text = string.IsNullOrWhiteSpace(text) ? string.Empty : text;
        if (text == Text)
            return;
        Text = text;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Says the line is on screen. Dispose the result when it is not. Disposing twice does nothing.</summary>
    public IDisposable Want()
    {
        _wanted++;
        if (_wanted == 1)
            WantedChanged?.Invoke(this, EventArgs.Empty);
        return new Hold(this);
    }

    public void Dispose()
    {
        Changed = null;
        WantedChanged = null;
    }

    private void Release()
    {
        _wanted--;
        if (_wanted == 0)
            WantedChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Hold(LyricLine owner) : IDisposable
    {
        private LyricLine? _owner = owner;

        public void Dispose()
        {
            _owner?.Release();
            _owner = null;
        }
    }
}
