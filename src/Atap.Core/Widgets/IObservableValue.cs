namespace Atap.Core.Widgets;

/// <summary>A value that raises <see cref="Changed"/> when it changes.</summary>
public interface IObservableValue<out T>
{
    T Value { get; }

    event EventHandler? Changed;
}

/// <summary>Simple settable <see cref="IObservableValue{T}"/>. Not thread-safe; use it from one thread.</summary>
public sealed class ObservableValue<T>(T initial) : IObservableValue<T>
{
    private T _value = initial;

    public T Value
    {
        get => _value;
        set
        {
            if (EqualityComparer<T>.Default.Equals(_value, value))
                return;
            _value = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Changed;
}
