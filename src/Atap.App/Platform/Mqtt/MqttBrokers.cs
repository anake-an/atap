using Atap.Core.Scheduling;
using Atap.Core.Shared;

namespace Atap.Platform.Mqtt;

/// <summary>
/// The MQTT connections of every Home Assistant widget, one per broker. Instances that name the same host, port,
/// TLS setting and user share one <see cref="MqttConnection"/>. A connection opens with its first subscription and
/// closes with its last. Use it on the UI thread.
/// </summary>
internal sealed class MqttBrokers : ISharedService<MqttBrokers>
{
    private readonly SynchronizationContext _ui;
    private readonly IScheduler _scheduler;
    private readonly TimeProvider _time;
    private readonly Func<IMqttLink> _newLink;
    private readonly Dictionary<MqttBrokerKey, MqttConnection> _connections = [];
    private bool _disposed;

    internal MqttBrokers(SynchronizationContext ui, IScheduler scheduler, TimeProvider time, Func<IMqttLink> newLink)
    {
        _ui = ui;
        _scheduler = scheduler;
        _time = time;
        _newLink = newLink;
    }

    /// <summary>Open connections, one per broker.</summary>
    public int ConnectionCount => _connections.Count;

    // MQTTnet loads only when the first link is made, not when the widget catalog is read.
    public static MqttBrokers Create(SharedServiceContext context) =>
        new(context.Ui, context.Scheduler, context.Time, static () => new MqttNetLink());

    /// <summary>The connection for <paramref name="broker"/>, if one is open.</summary>
    public MqttConnection? Find(MqttBrokerKey broker) => _connections.GetValueOrDefault(broker);

    /// <summary>
    /// Listens to <paramref name="filter"/> on <paramref name="broker"/>. The password is used only when this call
    /// opens the connection. <paramref name="onStatus"/> is called at once with the current state.
    /// </summary>
    public MqttSubscription Subscribe(MqttBrokerKey broker, string? password, string filter, Action<string, byte[]> onMessage, Action<MqttStatus> onStatus)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_connections.TryGetValue(broker, out var connection))
        {
            connection = new MqttConnection(broker, password, _newLink, _ui, _scheduler, _time);
            _connections[broker] = connection;
        }

        var subscription = new MqttSubscription(filter, onMessage, onStatus, s => Release(connection, s));
        connection.Add(subscription);
        return subscription;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var connection in _connections.Values)
            connection.Dispose();
        _connections.Clear();
    }

    private void Release(MqttConnection connection, MqttSubscription subscription)
    {
        connection.Remove(subscription);
        if (connection.SubscriptionCount > 0)
            return;
        if (_connections.TryGetValue(connection.Broker, out var current) && current == connection)
            _connections.Remove(connection.Broker);
        connection.Dispose();
    }
}
