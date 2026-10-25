using System.Net;
using System.Net.Sockets;

namespace Atap.Core.Net;

/// <summary>
/// Which addresses plain http may reach: only ones on this PC or the home or office network, such as a
/// Home Assistant box. The check runs on the addresses DNS returned, right before connecting, so a public
/// name that points somewhere private (or the other way round) is judged by where it really goes.
/// </summary>
public static class AddressPolicy
{
    /// <summary>
    /// True for loopback, 10/8, 172.16/12, 192.168/16, 169.254/16, 100.64/10 (carrier and Tailscale),
    /// ::1, fc00::/7 and fe80::/10. IPv4 inside IPv6 is checked as IPv4.
    /// </summary>
    public static bool IsPrivate(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> b = stackalloc byte[4];
            if (!address.TryWriteBytes(b, out _))
                return false;
            return b[0] == 127
                || b[0] == 10
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] is >= 64 and <= 127);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IsLoopback(address))
                return true;
            Span<byte> b = stackalloc byte[16];
            if (!address.TryWriteBytes(b, out _))
                return false;
            return (b[0] & 0xFE) == 0xFC                     // fc00::/7, unique local
                || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80);   // fe80::/10, link local
        }

        return false;
    }

    /// <summary>
    /// True when the URL names this PC or a private address directly ("localhost" or an IP literal), so it
    /// can work while Windows reports no internet. A name that only resolves to a private address is not
    /// known here, before DNS.
    /// </summary>
    public static bool IsLocalTarget(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri)
            return false;
        if (string.Equals(url.IdnHost, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(url.DnsSafeHost.Trim('[', ']'), out var address) && IsPrivate(address);
    }

    /// <summary>
    /// The connect step of the real handler. Plain http connects only to private addresses, and never
    /// through a proxy, which would hide where the request really goes. https connects as usual.
    /// </summary>
    internal static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endPoint = context.DnsEndPoint;
        var target = context.InitialRequestMessage.RequestUri;
        var plainHttp = target is not null && string.Equals(target.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

        IPAddress[] addresses = IPAddress.TryParse(endPoint.Host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken).ConfigureAwait(false);

        if (plainHttp)
        {
            // Through a proxy the endpoint is the proxy, not the target.
            if (!SameHost(endPoint.Host, target!))
                throw new PlainHttpRefusedException();
            addresses = PrivateOnly(addresses);
            if (addresses.Length == 0)
                throw new PlainHttpRefusedException();
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endPoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>The addresses plain http may connect to, in DNS order.</summary>
    internal static IPAddress[] PrivateOnly(IEnumerable<IPAddress> addresses) => addresses.Where(IsPrivate).ToArray();

    private static bool SameHost(string endPointHost, Uri target) =>
        string.Equals(endPointHost.Trim('[', ']'), target.IdnHost.Trim('[', ']'), StringComparison.OrdinalIgnoreCase)
        || string.Equals(endPointHost.Trim('[', ']'), target.DnsSafeHost.Trim('[', ']'), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Plain http was about to reach a public address, or go through a proxy. Nothing was sent.</summary>
internal sealed class PlainHttpRefusedException() : IOException("Plain http may only reach a private address.");
