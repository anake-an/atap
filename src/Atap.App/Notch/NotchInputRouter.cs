using System.Windows.Input;
using Atap.Core.Notch;

namespace Atap.Notch;

/// <summary>
/// Turns mouse and key events on a notch window into <see cref="NotchInput"/> for the registered handlers.
/// With no handlers nothing happens, so input on the notch costs nothing until a feature asks for it.
/// </summary>
internal sealed class NotchInputRouter(NotchControl control)
{
    public bool OnWheel(int delta, NotchSurface surface) =>
        Offer(new NotchInput(NotchInputKind.Wheel, surface) { Delta = delta });

    public bool OnHorizontalWheel(int delta, NotchSurface surface) =>
        Offer(new NotchInput(NotchInputKind.HorizontalWheel, surface) { Delta = delta });

    public bool OnMiddleClick(NotchSurface surface) => Offer(new NotchInput(NotchInputKind.MiddleClick, surface));

    public bool OnClick(NotchSurface surface) => Offer(new NotchInput(NotchInputKind.Click, surface));

    public bool OnKey(Key key, ModifierKeys modifiers, NotchSurface surface) =>
        Offer(new NotchInput(NotchInputKind.Key, surface) { Key = key, Modifiers = modifiers });

    public bool OnDismiss(NotchSurface surface) => Offer(new NotchInput(NotchInputKind.Dismiss, surface));

    private bool Offer(NotchInput input) => control.InputHandlerCount > 0 && control.Offer(input);
}
