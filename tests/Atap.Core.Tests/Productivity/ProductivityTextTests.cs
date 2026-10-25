using System.Text.Json;
using Atap.Core.Productivity;
using Atap.Core.Productivity.Student;

namespace Atap.Core.Tests.Productivity;

public class ProductivityTextTests
{
    [Fact]
    public void CleanLine_ReplacesControlsRemovesDirectionOverridesAndTrims()
    {
        var rightToLeftOverride = (char)0x202E;

        var clean = ProductivityText.CleanLine($"  Hantar\tlaporan\r\nBab 3{rightToLeftOverride}  ", 100);

        Assert.Equal("Hantar laporan  Bab 3", clean);
    }

    [Fact]
    public void CleanLine_CutsWithoutSplittingASurrogatePair()
    {
        var clean = ProductivityText.CleanLine("ab\U0001F600", 3);

        Assert.Equal("ab", clean);
    }

    [Fact]
    public void CleanText_KeepsLineBreaksAndTabs()
    {
        var leftToRightIsolate = (char)0x2066;

        var clean = ProductivityText.CleanText($"Baris 1\r\n\tBaris 2\0{leftToRightIsolate}", 100);

        Assert.Equal("Baris 1\r\n\tBaris 2", clean);
    }

    [Fact]
    public void CleanLine_NullGivesEmpty()
    {
        Assert.Equal(string.Empty, ProductivityText.CleanLine(null, 10));
    }

    [Theory]
    [InlineData("csc584-lec", true)]
    [InlineData("a_1", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("kelas#1", false)]
    [InlineData("12345678901234567890123456789012345678901", false)]
    public void ItemIds_IsValid(string id, bool expected)
    {
        Assert.Equal(expected, ItemIds.IsValid(id));
    }

    [Fact]
    public void ItemIds_New_IsValidAndDifferent()
    {
        var first = ItemIds.New();
        var second = ItemIds.New();

        Assert.True(ItemIds.IsValid(first));
        Assert.Equal(12, first.Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void HourMinuteConverter_WritesHoursAndMinutes()
    {
        var json = JsonSerializer.Serialize(
            new ClassSession { Id = "a", Code = "CSC584", Start = new TimeOnly(8, 0), End = new TimeOnly(9, 30, 15) },
            StudentJsonContext.Default.ClassSession);

        Assert.Contains("\"start\": \"08:00\"", json);
        Assert.Contains("\"end\": \"09:30:15\"", json);
    }

    [Theory]
    [InlineData("\"7:05\"", 7, 5, 0)]
    [InlineData("\"07:05\"", 7, 5, 0)]
    [InlineData("\"07:05:30\"", 7, 5, 30)]
    public void HourMinuteConverter_ReadsCommonForms(string value, int hour, int minute, int second)
    {
        var session = JsonSerializer.Deserialize($$"""{ "start": {{value}} }""", StudentJsonContext.Default.ClassSession)!;

        Assert.Equal(new TimeOnly(hour, minute, second), session.Start);
    }

    [Theory]
    [InlineData("\"25:00\"")]
    [InlineData("\"pagi\"")]
    [InlineData("800")]
    public void HourMinuteConverter_RejectsOtherValues(string value)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize($$"""{ "start": {{value}} }""", StudentJsonContext.Default.ClassSession));
    }
}
