namespace Atap.Notch;

/// <summary>
/// Which notch window takes the keyboard when the panel opens. With a notch on every monitor the panel opens on
/// all of them, but only one can have the keyboard: the one the pointer was last on, or else the first to ask.
/// </summary>
internal sealed class NotchFocus
{
    private object? _pointed;

    public void PointerEntered(object window) => _pointed = window;

    public void WindowClosed(object window)
    {
        if (ReferenceEquals(_pointed, window))
            _pointed = null;
    }

    /// <summary>True when <paramref name="window"/> should take the keyboard.</summary>
    public bool TryClaim(object window)
    {
        _pointed ??= window;
        return ReferenceEquals(_pointed, window);
    }
}
