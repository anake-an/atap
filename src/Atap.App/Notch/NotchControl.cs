using System.Windows.Input;
using Atap.Core.Notch;

namespace Atap.Notch;

/// <summary>How features change the notch. No feature edits the notch window itself.</summary>
internal interface INotchControl
{
    /// <summary>Turns a flag on or off for one owner. The flag is on while any owner has it on.</summary>
    void SetFlag(NotchFlag flag, string ownerId, bool on);

    /// <summary>True while any owner has the flag on, for example Hide set by a rule.</summary>
    bool IsSet(NotchFlag flag);

    /// <summary>Gives a surface a view model to show, or null to take it back.</summary>
    void SetSurfaceContent(NotchSurface surface, object? viewModel, string ownerId);

    /// <summary>What a feature gave the surface to show, or null.</summary>
    object? GetSurfaceContent(NotchSurface surface);

    /// <summary>Offers notch input to <paramref name="handler"/>. Dispose to stop.</summary>
    IDisposable AddInputHandler(INotchInputHandler handler);

    /// <summary>Sets the text size and motion for every notch. The last owner to set it wins.</summary>
    void SetLook(NotchLook look, string ownerId);

    /// <summary>The text size and motion in use, or <see cref="NotchLook.Default"/>.</summary>
    NotchLook Look { get; }

    /// <summary>Raised on the UI thread when a flag turns on or off, or surface content changes.</summary>
    event EventHandler? Changed;
}

/// <summary>How the notch is drawn for the person using it.</summary>
/// <param name="TextScale">1 for the usual size. Everything in the notch grows with it, icons too.</param>
/// <param name="LessMotion">The notch changes size and content at once, without the spring and the fade.</param>
internal readonly record struct NotchLook(double TextScale, bool LessMotion)
{
    public static NotchLook Default { get; } = new(1, false);
}

internal enum NotchFlag
{
    Hide,
    Slim,

    /// <summary>With <see cref="Slim"/>: only a hairline at the screen edge instead of a thin bar.</summary>
    Tucked,
    QuietAlerts,
    Peek,
    Editing,
    PanelOpen,

    /// <summary>The notch changes size and content at once, as with Less motion. Battery saver sets it.</summary>
    LessMotion,
}

internal enum NotchInputKind
{
    /// <summary>A left click on the notch that no button took.</summary>
    Click,
    MiddleClick,

    /// <summary>The vertical wheel. Delta is positive away from the user.</summary>
    Wheel,
    HorizontalWheel,
    Key,

    /// <summary>The open panel lost the keyboard to another window, for example a click on another app.</summary>
    Dismiss,
}

/// <param name="Kind">What happened.</param>
/// <param name="Surface">What the notch showed at the time.</param>
internal sealed record NotchInput(NotchInputKind Kind, NotchSurface Surface)
{
    /// <summary>Wheel distance in multiples of 120 per notch.</summary>
    public int Delta { get; init; }

    public Key Key { get; init; } = Key.None;

    public ModifierKeys Modifiers { get; init; }
}

internal interface INotchInputHandler
{
    /// <summary>True when the input was used. Later handlers then do not see it.</summary>
    bool TryHandle(NotchInput input);
}

/// <summary>Holds the flags, surface content and input handlers that features set. Use it on the UI thread.</summary>
internal sealed class NotchControl : INotchControl
{
    private readonly Dictionary<NotchFlag, HashSet<string>> _flags = [];
    private readonly Dictionary<NotchSurface, (object ViewModel, string OwnerId)> _content = [];
    private readonly List<INotchInputHandler> _handlers = [];
    private (NotchLook Look, string OwnerId)? _look;

    public event EventHandler? Changed;

    public bool IsSet(NotchFlag flag) => _flags.TryGetValue(flag, out var owners) && owners.Count > 0;

    public object? GetSurfaceContent(NotchSurface surface) =>
        _content.TryGetValue(surface, out var content) ? content.ViewModel : null;

    /// <summary>Handlers that take input right now.</summary>
    public int InputHandlerCount => _handlers.Count;

    public void SetFlag(NotchFlag flag, string ownerId, bool on)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var wasSet = IsSet(flag);
        if (!_flags.TryGetValue(flag, out var owners))
        {
            owners = new HashSet<string>(StringComparer.Ordinal);
            _flags[flag] = owners;
        }
        if (on)
            owners.Add(ownerId);
        else
            owners.Remove(ownerId);
        if (IsSet(flag) != wasSet)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetSurfaceContent(NotchSurface surface, object? viewModel, string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (viewModel is null)
        {
            // Only the owner can take its content back.
            if (_content.TryGetValue(surface, out var current) && current.OwnerId == ownerId && _content.Remove(surface))
                Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
        _content[surface] = (viewModel, ownerId);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public NotchLook Look => _look?.Look ?? NotchLook.Default;

    public void SetLook(NotchLook look, string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var before = Look;
        _look = (look, ownerId);
        if (look != before)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public IDisposable AddInputHandler(INotchInputHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Add(handler);
        return new Registration(this, handler);
    }

    /// <summary>Offers input to the handlers, first registered first. True when one took it.</summary>
    public bool Offer(NotchInput input)
    {
        foreach (var handler in _handlers.ToList())
        {
            if (handler.TryHandle(input))
                return true;
        }
        return false;
    }

    /// <summary>Clears every flag and surface an owner set, for example when a feature stops.</summary>
    public void ClearOwner(string ownerId)
    {
        var changed = false;
        foreach (var owners in _flags.Values)
            changed |= owners.Remove(ownerId);
        foreach (var surface in _content.Where(c => c.Value.OwnerId == ownerId).Select(c => c.Key).ToList())
            changed |= _content.Remove(surface);
        if (_look is { } look && look.OwnerId == ownerId)
        {
            _look = null;
            changed |= look.Look != NotchLook.Default;
        }
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Registration(NotchControl owner, INotchInputHandler handler) : IDisposable
    {
        public void Dispose() => owner._handlers.Remove(handler);
    }
}
