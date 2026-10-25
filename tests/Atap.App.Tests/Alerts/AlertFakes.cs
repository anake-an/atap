using Atap.Core.Alerts;
using Atap.Core.Notch;
using Atap.Features.Alerts;

namespace Atap.Tests.Alerts;

/// <summary>Records what would play and plays nothing. Each started sound waits until the test ends it.</summary>
internal sealed class FakeSoundOutput : ISoundOutput
{
    public int Notifications { get; private set; }

    public int NotificationStops { get; private set; }

    public List<FakeSound> Started { get; } = [];

    /// <summary>The next Start fails, like a missing file or codec.</summary>
    public bool FailNext { get; set; }

    public IEnumerable<FakeSound> Playing => Started.Where(s => !s.HasEnded);

    public void PlayNotification() => Notifications++;

    public void StopNotification() => NotificationStops++;

    public ISoundHandle Start(AlertSound sound, Action<Exception?> ended)
    {
        var fake = new FakeSound(sound, ended);
        Started.Add(fake);
        if (FailNext)
        {
            FailNext = false;
            fake.Fail(new FileNotFoundException("No such sound.", sound.Value));
        }
        return fake;
    }
}

internal sealed class FakeSound(AlertSound sound, Action<Exception?> ended) : ISoundHandle
{
    public AlertSound Sound { get; } = sound;

    public bool Stopped { get; private set; }

    public bool HasEnded { get; private set; }

    /// <summary>The sound reached its end.</summary>
    public void Finish() => End(null);

    public void Fail(Exception error) => End(error);

    public void Stop()
    {
        Stopped = true;
        End(null);
    }

    private void End(Exception? error)
    {
        if (HasEnded)
            return;
        HasEnded = true;
        ended(error);
    }
}

internal sealed class FakeQuietHours : IQuietHours
{
    public bool On { get; set; }

    public int Asked { get; private set; }

    public bool IsOn()
    {
        Asked++;
        return On;
    }
}

/// <summary>Full screen that the test turns on and off.</summary>
internal sealed class FakeForeground : IForegroundState
{
    private FullScreenKind _fullScreen;

    public FullScreenKind FullScreen
    {
        get => _fullScreen;
        set
        {
            _fullScreen = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? ProcessName => null;

    public event EventHandler? Changed;

    public IDisposable WantProcessNames() => new Release();

    private sealed class Release : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

/// <summary>Runs posted work at once, so the end of a sound is handled before the call returns.</summary>
internal sealed class InlineContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => d(state);
}
