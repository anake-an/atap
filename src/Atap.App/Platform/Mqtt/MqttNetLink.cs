using System.Net;
using System.Net.Sockets;
using Atap.Core.Net;
using MQTTnet;

namespace Atap.Platform.Mqtt;

/// <summary>
/// An <see cref="IMqttLink"/> on MQTTnet. Without TLS it connects only to a private address, checked after DNS,
/// as plain http does, so a password never crosses the internet in the clear. With TLS the broker's certificate
/// must check out for its name. Keepalive is 60 seconds. Nothing here is logged.
/// </summary>
internal sealed class MqttNetLink : IMqttLink
{
    public static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GoodbyeTimeout = TimeSpan.FromSeconds(2);

    private readonly MqttClientFactory _factory = new();
    private readonly IMqttClient _client;

    public MqttNetLink()
    {
        _client = _factory.CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
    }

    public event MqttMessageHandler? MessageReceived;

    public event Action? ConnectionLost;

    public async Task ConnectAsync(MqttBrokerKey broker, string? password, CancellationToken cancellationToken)
    {
        var host = broker.Tls ? broker.Host : await PrivateAddressAsync(broker.Host, cancellationToken).ConfigureAwait(false);
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(host, broker.Port)
            .WithClientId("atap-" + Guid.NewGuid().ToString("N")[..12])
            .WithCleanSession()
            .WithKeepAlivePeriod(KeepAlive)
            .WithTimeout(ConnectTimeout);
        if (broker.User is { } user)
            builder = builder.WithCredentials(user, password ?? string.Empty);
        if (broker.Tls)
            builder = builder.WithTlsOptions(tls => tls.UseTls());

        // MQTTnet 5 returns a refusal in the result instead of throwing.
        var result = await _client.ConnectAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
        if (IsSignInRefused(result.ResultCode))
            throw new MqttLinkException(MqttProblem.SignInRefused);
        if (result.ResultCode != MqttClientConnectResultCode.Success)
            throw new MqttLinkException(MqttProblem.Unreachable);
    }

    public Task SubscribeAsync(string filter, CancellationToken cancellationToken) =>
        _client.SubscribeAsync(_factory.CreateSubscribeOptionsBuilder().WithTopicFilter(filter).Build(), cancellationToken);

    public Task UnsubscribeAsync(string filter, CancellationToken cancellationToken) =>
        _client.UnsubscribeAsync(_factory.CreateUnsubscribeOptionsBuilder().WithTopicFilter(filter).Build(), cancellationToken);

    public async Task DisconnectAsync()
    {
        try
        {
            if (!_client.IsConnected)
                return;
            using var timeout = new CancellationTokenSource(GoodbyeTimeout);
            await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), timeout.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The broker is gone or slow. Dispose closes the socket anyway.
        }
    }

    public void Dispose()
    {
        _client.ApplicationMessageReceivedAsync -= OnMessageAsync;
        _client.DisconnectedAsync -= OnDisconnectedAsync;
        _client.Dispose();
    }

    private static bool IsSignInRefused(MqttClientConnectResultCode code) =>
        code is MqttClientConnectResultCode.BadUserNameOrPassword or MqttClientConnectResultCode.NotAuthorized;

    /// <summary>The first private address for <paramref name="host"/>. Throws when there is none.</summary>
    private static async Task<string> PrivateAddressAsync(string host, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException e)
        {
            throw new MqttLinkException(MqttProblem.Unreachable, e);
        }

        var address = Array.Find(addresses, AddressPolicy.IsPrivate)
            ?? throw new MqttLinkException(addresses.Length > 0 ? MqttProblem.NeedsTls : MqttProblem.Unreachable);
        return address.ToString();
    }

    private Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        MessageReceived?.Invoke(e.ApplicationMessage.Topic, e.ApplicationMessage.Payload);
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        if (e.ClientWasConnected)
            ConnectionLost?.Invoke();
        return Task.CompletedTask;
    }
}
