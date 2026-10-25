using System.Text;
using Atap.Core.LocalApi;

namespace Atap.Core.Tests.LocalApi;

public class PushFrameTests
{
    [Fact]
    public void Encode_WritesLittleEndianLengthThenUtf8()
    {
        var json = """{"op":"ping"}""";

        var frame = PushFrame.Encode(json);

        Assert.Equal(new byte[] { 13, 0, 0, 0 }, frame[..PushFrame.HeaderSize]);
        Assert.Equal(Encoding.UTF8.GetBytes(json), frame[PushFrame.HeaderSize..]);
    }

    [Fact]
    public void Encode_CountsBytesNotCharacters()
    {
        var frame = PushFrame.Encode(((char)0x20AC).ToString());

        Assert.Equal(3, PushFrame.ReadLength(frame));
        Assert.Equal(PushFrame.HeaderSize + 3, frame.Length);
    }

    [Fact]
    public void Encode_OverTheCap_Throws()
    {
        Assert.Throws<ArgumentException>(() => PushFrame.Encode(new string(' ', PushFrame.MaxPayloadBytes + 1)));
        Assert.Equal(PushFrame.MaxPayloadBytes, PushFrame.ReadLength(PushFrame.Encode(new string(' ', PushFrame.MaxPayloadBytes))));
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0 }, 0)]
    [InlineData(new byte[] { 0x00, 0x20, 0, 0 }, 8192)]
    [InlineData(new byte[] { 0x0D, 0, 0, 0, 0x7B }, 13)]
    public void ReadLength_WithinCap_ReturnsLength(byte[] header, int expected)
    {
        Assert.Equal(expected, PushFrame.ReadLength(header));
    }

    [Theory]
    [InlineData(new byte[] { 0x01, 0x20, 0, 0 })]
    [InlineData(new byte[] { 0, 0, 0, 0x80 })]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })]
    [InlineData(new byte[] { 0, 0, 0x10, 0 })]
    public void ReadLength_OverCap_ReturnsNull(byte[] header)
    {
        Assert.Null(PushFrame.ReadLength(header));
    }

    [Fact]
    public void ReadLength_ShortHeader_Throws()
    {
        Assert.Throws<ArgumentException>(() => PushFrame.ReadLength(new byte[] { 1, 0, 0 }));
    }

    [Fact]
    public void Frame_RoundTripsEveryOp()
    {
        ApiRequest[] requests =
        [
            new PingRequest(),
            new PushRequest("backup", "Backup NAS") { Progress = 64, Ttl = TimeSpan.FromMinutes(10) },
            new EndRequest("backup"),
            new AlertRequest("Backup done") { Icon = PushIcon.Check },
        ];

        foreach (var request in requests)
        {
            var frame = PushFrame.Encode(PushProtocol.Format(request));
            var length = PushFrame.ReadLength(frame);

            Assert.NotNull(length);
            var result = PushProtocol.Parse(frame.AsSpan(PushFrame.HeaderSize, length.Value));
            Assert.Equal(request, result.Request);
        }
    }

    [Fact]
    public void Frame_RoundTripsResponses()
    {
        var frame = PushFrame.Encode(PushProtocol.FormatResponse(ApiResponse.Failure(ApiErrorCode.RateLimited)));
        var length = PushFrame.ReadLength(frame)!.Value;

        Assert.True(PushProtocol.TryParseResponse(Encoding.UTF8.GetString(frame, PushFrame.HeaderSize, length), out var response));
        Assert.Equal(ApiErrorCode.RateLimited, response.Error);
    }
}
