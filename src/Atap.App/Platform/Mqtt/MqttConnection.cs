using System.Buffers;
using Atap.Core.Custom;
using Atap.Core.Scheduling;

namespace Atap.Platform.Mqtt;

/// <summary>How a broker connection is doing.</summary>
internal enum MqttState
{
    Connecting,
    Connected,

    /// <summary>The last try failed or the session dropped. The next try waits out the backoff.</summary>
    Retrying,
}

/// <summary>Why the last try failed.</summary>
internal enum MqttProblem
{
    None,

    /// <summary>No answer, a network error, or a TLS certificate that did not check out.</summary>
    Unreachable,

    /// <summary>The broker turned down the user name or password.</summary>
    SignInRefused,

    /// <summary>The broker is on a public address and TLS is off, so nothing was sent.</summary>
    NeedsTls,
}

/// <summary>Thrown by an <see cref="IMqttLink"/> when it knows why a connect failed.</summary>
internal sealed class MqttLinkException(MqttProblem problem, Exception? inner = null)
    : System.IO.IOException("The MQTT broker could not be used.", inner)
{
    public MqttProblem Problem { get; } = problem;
}

/// <summary>A connection's state as its subscribers see it.</summary>
internal readonly record struct MqttStatus(MqttState State, MqttProblem Problem = MqttProblem.None);

/// <summary>
/// One widget's interest in one topic filter on one broker. Messages and state changes arrive on the UI thread.
/// Dispose it to let go. The connection closes when its last subscription ends.
/// </summary>
internal sealed class MqttSubscription : IDisposable
{
    private readonly Action<string, byte[]> _onMessage;
    private readonly Action<MqttStatus> _onStatus;
    private Action<MqttSubscription>? _release;

    internal MqttSubscription(string filter, Action<string, byte[]> onMessage, Action<MqttStatus> onStatus, Action<MqttSubscription> release)
    {
        Filter = filter;
        _onMessage = onMessage;
        _onStatus = onStatus;
        _release = release;
    }

    public string Filter { get; }

    public bool IsReleased => _release is null;

    public void Dispose()
    {
        var release = _release;
        _release = null;
        release?.Invoke(this);
    }

    internal void Deliver(string topic, byte[] payload)
    {
        if (_release is not null)
            _onMessage(topic, payload);
    }

    internal void Notify(MqttStatus status)
    {
        if (_release is not null)
            _onStatus(status);
    }
}

/// <summary>
/// One session with one broker, shared by every subscription that names it. Connects on the first subscription
/// and closes on the last. After a failure or a drop it tries again after 5 seconds, doubling up to 5 minutes, and
/// a good connect starts the count again. Payloads over <see cref="MaxPayloadBytes"/> are dropped before they are
/// copied. Use it on the UI thread. Link events from other threads are posted there.
/// </summary>
internal sealed class MqttConnection : IDisposable
{
    public static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan LongestRetry = TimeSpan.FromMinutes(5);
    public const int MaxPayloadBytes = MqttMapping.MaxPayloadBytes;

    /// <summary>A retry missed during sleep runs once on wake.</summary>
    private static readonly ScheduleOptions RetryOptions = new(Missed: MissedRun.RunOnce);

    private readonly MqttBrokerKey _broker;
    private readonly string? _password;
    private readonly Func<IMqttLink> _newLink;
    private readonly SynchronizationContext _ui;
    private readonly IScheduler _scheduler;
    private readonly TimeProvider _time;
    private readonly List<MqttSubscription> _subscriptions = [];

    /// <summary>Filters the current link has subscribed to.</summary>
    private readonly HashSet<string> _subscribed = new(StringComparer.Ordinal);

    private IMqttLink? _link;
    private CancellationTokenSource? _attempt;
    private IDisposable? _retry;
    private int _failures;
    private bool _disposed;

    public MqttConnection(MqttBrokerKey broker, string? password, Func<IMqttLink> newLink, SynchronizationContext ui, IScheduler scheduler, TimeProvider time)
    {
        _broker = broker;
        _password = password;
        _newLink = newLink;
        _ui = ui;
        _scheduler = scheduler;
        _time = time;
    }

    public MqttBrokerKey Broker => _broker;

    public MqttStatus Status { get; private set; } = new(MqttState.Connecting);

    public int SubscriptionCount => _subscriptions.Count;

    /// <summary>When the next try is due while <see cref="MqttState.Retrying"/>, otherwise null.</summary>
    public DateTimeOffset? RetryAt { get; private set; }

    /// <summary>
    /// The wait after <paramref name="failures"/> failures in a row: 5 s after the first, doubling up to 5 minutes.
    /// </summary>
    public static TimeSpan RetryDelay(int failures)
    {
        var doublings = Math.Clamp(failures - 1, 0, 16);
        var seconds = FirstRetry.TotalSeconds * Math.Pow(2, doublings);
        return TimeSpan.FromSeconds(Math.Min(seconds, LongestRetry.TotalSeconds));
    }

    public void Add(MqttSubscription subscription)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _subscriptions.Add(subscription);
        subscription.Notify(Status);

        if (_link is null && _retry is null)
            _ = ConnectAsync();
        else if (_link is { } link && Status.State == MqttState.Connected && _subscribed.Add(subscription.Filter))
            _ = SubscribeAsync(link, subscription.Filter);
    }

    public void Remove(MqttSubscription subscription)
    {
        if (!_subscriptions.Remove(subscription))
            return;
        var filter = subscription.Filter;
        if (_subscriptions.Exists(s => s.Filter == filter))
            return;
        if (_subscribed.Remove(filter) && _link is { } link && Status.State == MqttState.Connected)
            _ = UnsubscribeAsync(link, filter);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _retry?.Dispose();
        _retry = null;
        RetryAt = null;
        _subscriptions.Clear();
        _subscribed.Clear();
        var link = _link;
        var wasConnected = Status.State == MqttState.Connected;
        _link = null;
        _attempt?.Cancel();
        if (link is not null)
            _ = CloseAsync(link, wasConnected);
    }

    private async Task ConnectAsync()
    {
        _retry = null;
        RetryAt = null;
        if (_disposed || _subscriptions.Count == 0)
            return;

        var link = _newLink();
        var attempt = new CancellationTokenSource();
        _link = link;
        _attempt = attempt;
        _subscribed.Clear();
        link.MessageReceived += (topic, payload) => OnMessage(link, topic, payload);
        link.ConnectionLost += () => _ui.Post(_ => OnLost(link), null);
        SetStatus(new MqttStatus(MqttState.Connecting));

        try
        {
            await link.ConnectAsync(_broker, _password, attempt.Token);
            // Subscriptions added during the awaits are picked up by the loop.
            while (link == _link && _subscriptions.Find(s => !_subscribed.Contains(s.Filter)) is { } missing)
            {
                await link.SubscribeAsync(missing.Filter, attempt.Token);
                _subscribed.Add(missing.Filter);
            }
            if (link != _link)
                return;
            _failures = 0;
            SetStatus(new MqttStatus(MqttState.Connected));
        }
        catch (Exception e) when (link == _link)
        {
            Fail(link, e is MqttLinkException known ? known.Problem : MqttProblem.Unreachable);
        }
        catch (Exception)
        {
            // Closed or replaced while connecting. The new state is already set.
        }
        finally
        {
            if (_attempt == attempt)
                _attempt = null;
            attempt.Dispose();
        }
    }

    private void Fail(IMqttLink link, MqttProblem problem)
    {
        _link = null;
        _subscribed.Clear();
        _attempt?.Cancel();
        _ = CloseAsync(link, goodbye: false);
        if (_disposed)
            return;

        _failures++;
        var due = _time.GetUtcNow() + RetryDelay(_failures);
        RetryAt = due;
        _retry = _scheduler.At(due, () => _ = ConnectAsync(), RetryOptions);
        SetStatus(new MqttStatus(MqttState.Retrying, problem));
    }

    private void OnLost(IMqttLink link)
    {
        if (link != _link || Status.State != MqttState.Connected)
            return;
        Fail(link, MqttProblem.Unreachable);
    }

    /// <summary>Runs on the link's thread. Large payloads stop here, before they are copied.</summary>
    private void OnMessage(IMqttLink link, string topic, ReadOnlySequence<byte> payload)
    {
        if (payload.Length > MaxPayloadBytes)
            return;
        var bytes = payload.ToArray();
        _ui.Post(_ => Deliver(link, topic, bytes), null);
    }

    private void Deliver(IMqttLink link, string topic, byte[] payload)
    {
        if (link != _link)
            return;
        foreach (var subscription in _subscriptions.ToArray())
        {
            if (MqttTopic.Matches(subscription.Filter, topic))
                subscription.Deliver(topic, payload);
        }
    }

    private void SetStatus(MqttStatus status)
    {
        Status = status;
        foreach (var subscription in _subscriptions.ToArray())
            subscription.Notify(status);
    }

    private async Task SubscribeAsync(IMqttLink link, string filter)
    {
        try
        {
            await link.SubscribeAsync(filter, CancellationToken.None);
        }
        catch (Exception) when (link == _link)
        {
            Fail(link, MqttProblem.Unreachable);
        }
        catch (Exception)
        {
            // The link is gone. The next one subscribes again.
        }
    }

    private static async Task UnsubscribeAsync(IMqttLink link, string filter)
    {
        try
        {
            await link.UnsubscribeAsync(filter, CancellationToken.None);
        }
        catch (Exception)
        {
            // Messages for the filter are ignored anyway, as no subscription matches.
        }
    }

    private static async Task CloseAsync(IMqttLink link, bool goodbye)
    {
        try
        {
            if (goodbye)
                await link.DisconnectAsync();
        }
        finally
        {
            try
            {
                link.Dispose();
            }
            catch (Exception)
            {
                // A client that fails to close has nothing more to give.
            }
        }
    }
}
