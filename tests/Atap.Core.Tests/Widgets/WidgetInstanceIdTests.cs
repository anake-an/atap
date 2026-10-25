using Atap.Core.Widgets;

namespace Atap.Core.Tests.Widgets;

public class WidgetInstanceIdTests
{
    [Theory]
    [InlineData("media", "media", null)]
    [InlineData("json-url#nas", "json-url", "nas")]
    [InlineData("waktu-solat", "waktu-solat", null)]
    [InlineData("json-url#0", "json-url", "0")]
    [InlineData("ab", "ab", null)]
    public void TryParse_AcceptsValidIds(string value, string typeId, string? name)
    {
        Assert.True(WidgetInstanceId.TryParse(value, out var id));
        Assert.Equal(typeId, id.TypeId);
        Assert.Equal(name, id.Name);
        Assert.Equal(value, id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")] // type ids have at least 2 characters
    [InlineData("Media")]
    [InlineData("1media")]
    [InlineData("-media")]
    [InlineData("media ")]
    [InlineData("media\n")]
    [InlineData("json_url")]
    [InlineData("json-url:nas")] // ':' is not legal in Windows file names
    [InlineData("json-url#")]
    [InlineData("json-url#-nas")]
    [InlineData("json-url#NAS")]
    [InlineData("json-url#a#b")]
    [InlineData("#nas")]
    [InlineData("abcdefghijabcdefghijabcdefghijabc")] // 33 characters
    public void TryParse_RejectsInvalidIds(string value)
    {
        Assert.False(WidgetInstanceId.TryParse(value, out _));
    }

    [Fact]
    public void TryParse_Null_IsRejected()
    {
        Assert.False(WidgetInstanceId.TryParse(null, out _));
    }

    [Fact]
    public void TypeIds_AreAtMost32Characters()
    {
        Assert.True(WidgetInstanceId.IsValidTypeId(new string('a', 32)));
        Assert.False(WidgetInstanceId.IsValidTypeId(new string('a', 33)));
        Assert.True(WidgetInstanceId.IsValidName(new string('a', 32)));
        Assert.False(WidgetInstanceId.IsValidName(new string('a', 33)));
    }

    [Theory]
    [InlineData("media")]
    [InlineData("lyrics")]
    [InlineData("volume-overlay")]
    [InlineData("brightness-overlay")]
    [InlineData("audio-output")]
    [InlineData("audio-mixer")]
    [InlineData("focus-timer")]
    [InlineData("todo")]
    [InlineData("notes")]
    [InlineData("next-meeting")]
    [InlineData("file-shelf")]
    [InlineData("clipboard")]
    [InlineData("stopwatch")]
    [InlineData("alarm")]
    [InlineData("world-clock")]
    [InlineData("water")]
    [InlineData("next-class")]
    [InlineData("assignments")]
    [InlineData("clock")]
    [InlineData("battery")]
    [InlineData("wifi")]
    [InlineData("bluetooth")]
    [InlineData("privacy-dot")]
    [InlineData("keyboard")]
    [InlineData("quick-toggles")]
    [InlineData("system-monitor")]
    [InlineData("weather")]
    [InlineData("json-url")]
    [InlineData("script")]
    [InlineData("home-assistant")]
    [InlineData("waktu-solat")]
    [InlineData("hijri")]
    [InlineData("rain-alerts")]
    [InlineData("holidays")]
    [InlineData("push")]
    [InlineData("missed-alerts")]
    public void PlannedTypeIds_AreValid(string typeId)
    {
        Assert.True(WidgetInstanceId.IsValidTypeId(typeId));
    }
}
