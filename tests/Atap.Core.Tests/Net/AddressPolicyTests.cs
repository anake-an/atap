using System.Net;
using Atap.Core.Net;

namespace Atap.Core.Tests.Net;

public sealed class AddressPolicyTests
{
    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("100.64.1.1")]
    [InlineData("fd00::1")]
    [InlineData("::1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("169.254.10.20")]
    [InlineData("100.127.255.254")]
    [InlineData("fc00::5")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:192.168.1.10")]
    public void PrivateAddresses_AreAccepted(string address)
    {
        Assert.True(AddressPolicy.IsPrivate(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("192.169.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("fec0::1")]
    [InlineData("::ffff:8.8.8.8")]
    public void PublicAddresses_AreRefused(string address)
    {
        Assert.False(AddressPolicy.IsPrivate(IPAddress.Parse(address)));
    }

    [Fact]
    public void PrivateOnly_KeepsThePrivateAddresses_InDnsOrder()
    {
        var addresses = new[] { "8.8.8.8", "192.168.1.10", "2001:4860:4860::8888", "fd00::1" }.Select(IPAddress.Parse);

        var kept = AddressPolicy.PrivateOnly(addresses);

        Assert.Equal(["192.168.1.10", "fd00::1"], kept.Select(a => a.ToString()));
    }

    [Theory]
    [InlineData("http://localhost:8123/api", true)]
    [InlineData("http://192.168.1.10/api", true)]
    [InlineData("http://[fd00::1]:8080/", true)]
    [InlineData("http://8.8.8.8/", false)]
    [InlineData("https://homeassistant.local/", false)]
    [InlineData("https://example.com/", false)]
    public void LocalTargets_AreTheOnesNamedByNumberOrLocalhost(string url, bool local)
    {
        Assert.Equal(local, AddressPolicy.IsLocalTarget(new Uri(url)));
    }
}
