using System.Buffers;

namespace Atap.Platform.Mqtt;

/// <summary>
/// Where an MQTT broker is and who signs in. Instances with the same key share one connection.
/// The password is not part of the key and never appears in it.
/// </summary>
/// <param name="Host">The broker's name or address, in lower case.</param>
/// <param name="Port">1883 for plain MQTT and 8883 for TLS, unless the user picked another.</param>
/// <param name="Tls">Connect over TLS and check the broker's certificate.</param>
/// <param name="User">The user name, or null to connect without signing in.</param>
internal sealed record MqttBrokerKey(string Host, int Port, bool Tls, string? User)
{
    public const int PlainPort = 1883;
    public const int TlsPort = 8883;

    /// <summary>Trims and lower-cases the host. A port of 0 picks the usual port for <paramref name="tls"/>.</summary>
    public static MqttBrokerKey Create(string host, int port, bool tls, string? user) => new(
        host.Trim().ToLowerInvariant(),
        port is > 0 and <= 65535 ? port : tls ? TlsPort : PlainPort,
        tls,
        string.IsNullOrEmpty(user) ? null : user);

    /// <summary>Host and port only, so the user name stays out of anything that prints the key.</summary>
    public override string ToString() => $"{Host}:{Port}";
}

/// <summary>A message as it arrives. The payload is valid only during the call.</summary>
internal delegate void MqttMessageHandler(string topic, ReadOnlySequence<byte> payload);

/// <summary>
/// One MQTT client session, so <see cref="MqttConnection"/> can be tested without a broker.
/// A link is connected once. After it fails or disconnects, the connection makes a new one.
/// </summary>
internal interface IMqttLink : IDisposable
{
    /// <summary>Raised on any thread.</summary>
    event MqttMessageHandler? MessageReceived;

    /// <summary>Raised on any thread when an open session drops.</summary>
    event Action? ConnectionLost;

    /// <summary>Throws when the broker cannot be reached or refuses the sign in.</summary>
    Task ConnectAsync(MqttBrokerKey broker, string? password, CancellationToken cancellationToken);

    Task SubscribeAsync(string filter, CancellationToken cancellationToken);

    Task UnsubscribeAsync(string filter, CancellationToken cancellationToken);

    /// <summary>Says goodbye to the broker. Never throws.</summary>
    Task DisconnectAsync();
}
