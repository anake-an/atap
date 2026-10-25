namespace Atap.Core.Input;

/// <summary>What one press of the panel hotkey asks for, step by step.</summary>
public enum PeekAction
{
    /// <summary>Still deciding, or nothing to do.</summary>
    None,

    /// <summary>A tap: open or close the panel.</summary>
    TogglePanel,

    /// <summary>A hold: show the open notch until the key goes up.</summary>
    StartPeek,

    /// <summary>The hold ended: close the peek.</summary>
    EndPeek,
}

/// <summary>
/// One press of the panel hotkey, which is both a tap and a hold. Windows reports only the press, so the app
/// checks every <see cref="CheckEvery"/> whether the key is still down and passes that in here. Released within
/// <see cref="TapLimit"/> it is a tap, held longer it is a peek until release. After <see cref="GiveUpAfter"/> it
/// stops checking, so a stuck key never keeps a timer running.
/// </summary>
public sealed class PeekGesture(TimeSpan pressedAt)
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan TapLimit = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

    private bool _peeking;

    /// <summary>True once the press is over. The app stops checking then.</summary>
    public bool IsDone { get; private set; }

    /// <param name="now">The same clock as the press.</param>
    /// <param name="held">The hotkey's key is still down.</param>
    public PeekAction Update(TimeSpan now, bool held)
    {
        if (IsDone)
            return PeekAction.None;

        var heldFor = now - pressedAt;
        if (!held || heldFor >= GiveUpAfter)
        {
            IsDone = true;
            if (_peeking)
                return PeekAction.EndPeek;
            // A key still down after the limit without peeking cannot happen, but it must not open the panel.
            return held ? PeekAction.None : PeekAction.TogglePanel;
        }

        if (!_peeking && heldFor >= TapLimit)
        {
            _peeking = true;
            return PeekAction.StartPeek;
        }
        return PeekAction.None;
    }
}
