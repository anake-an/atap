using System.Globalization;
using Atap.Core.Settings;
using Atap.Core.Widgets;

namespace Atap.Core.Tests.Settings;

public class SettingValidatorTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-MY");

    public static TheoryData<SettingType, object> ValidSamples => new()
    {
        { SettingType.Bool, true },
        { SettingType.Int, 5 },
        { SettingType.String, "hello" },
        { SettingType.Choice, "b" },
        { SettingType.Double, 1.5 },
        { SettingType.Time, "07:30" },
        { SettingType.Duration, 90 },
        { SettingType.Url, "https://example.com/feed" },
        { SettingType.Secret, "s3cret" },
        { SettingType.Folder, @"C:\Music" },
        { SettingType.File, @"C:\Music\song.mp3" },
        { SettingType.Hotkey, "Ctrl+Alt+K" },
        { SettingType.TimeZone, "UTC" },
        { SettingType.StringList, new[] { "one", "two" } },
        { SettingType.Custom, "anything" },
    };

    [Fact]
    public void EverySettingType_HasASample()
    {
        var covered = ValidSamples.Select(row => (SettingType)row[0]).ToHashSet();
        Assert.Equal(Enum.GetValues<SettingType>().ToHashSet(), covered);
    }

    [Theory]
    [MemberData(nameof(ValidSamples))]
    public void EverySettingType_AcceptsAValidValue(SettingType type, object value)
    {
        var check = SettingValidator.Check(Define(type), value);

        Assert.True(check.IsValid, $"{type} refused {value}: {check.MessageKey}");
        Assert.Null(check.MessageKey);
    }

    [Theory]
    [InlineData(SettingType.Bool, "yes")]
    [InlineData(SettingType.Int, "five")]
    [InlineData(SettingType.Choice, "z")]
    [InlineData(SettingType.Time, 730)]
    [InlineData(SettingType.Url, "example.com")]
    [InlineData(SettingType.Folder, "Music")]
    [InlineData(SettingType.Hotkey, "K")]
    [InlineData(SettingType.TimeZone, "Nowhere/Atlantis")]
    [InlineData(SettingType.StringList, "not a list")]
    public void EverySettingType_RefusesAWrongValue_WithAMessageKey(SettingType type, object value)
    {
        var check = SettingValidator.Check(Define(type), value);

        Assert.False(check.IsValid);
        Assert.StartsWith("settingsform.error.", check.MessageKey);
    }

    [Fact]
    public void Null_MeansBackToTheDefault()
    {
        foreach (var type in Enum.GetValues<SettingType>())
            Assert.Equal(SettingCheck.Valid(null), SettingValidator.Check(Define(type), null));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://192.168.1.10:8080/api")]
    [InlineData("  https://example.com/path?q=1  ")]
    public void Url_AcceptsAbsoluteHttpAndHttps(string url)
    {
        var check = SettingValidator.Check(Define(SettingType.Url), url);

        Assert.True(check.IsValid);
        Assert.Equal(url.Trim(), check.Value);
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("https://user:pass@example.com/feed")]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("/relative/path")]
    [InlineData("example.com")]
    [InlineData("javascript:alert(1)")]
    public void Url_RefusesEverythingElse(string url)
    {
        Assert.Equal(SettingCheck.Invalid(SettingValidator.UrlKey), SettingValidator.Check(Define(SettingType.Url), url));
    }

    [Fact]
    public void BlankUrl_IsNoneUnlessRequired()
    {
        Assert.Equal(SettingCheck.Valid(string.Empty), SettingValidator.Check(Define(SettingType.Url), " "));
        Assert.Equal(SettingCheck.Invalid(SettingValidator.RequiredKey), SettingValidator.Check(Define(SettingType.Url) with { Required = true }, " "));
    }

    [Theory]
    [InlineData("07:30", "07:30")]
    [InlineData("7:30", "07:30")]
    [InlineData("23:59", "23:59")]
    [InlineData("00:00", "00:00")]
    public void Time_IsStoredAsHHmm(string typed, string stored)
    {
        Assert.Equal(SettingCheck.Valid(stored), SettingValidator.Check(Define(SettingType.Time), typed));
    }

    [Theory]
    [InlineData("24:00")]
    [InlineData("7.30")]
    [InlineData("07:30:15")]
    [InlineData("half past seven")]
    public void Time_RefusesAnythingButHHmm(string typed)
    {
        Assert.Equal(SettingCheck.Invalid(SettingValidator.TimeKey), SettingValidator.Check(Define(SettingType.Time), typed));
    }

    [Fact]
    public void Time_FromTimeOnly()
    {
        Assert.Equal(SettingCheck.Valid("18:05"), SettingValidator.Check(Define(SettingType.Time), new TimeOnly(18, 5)));
    }

    [Theory]
    [InlineData(50, 10)]
    [InlineData(-3, 1)]
    [InlineData(4, 4)]
    public void Int_IsClampedToMinAndMax(int value, int stored)
    {
        var definition = Define(SettingType.Int) with { Min = 1, Max = 10 };

        Assert.Equal(SettingCheck.Valid(stored), SettingValidator.Check(definition, value));
    }

    [Fact]
    public void Int_RefusesFractions()
    {
        Assert.Equal(SettingCheck.Invalid(SettingValidator.WholeNumberKey), SettingValidator.Check(Define(SettingType.Int), 2.5));
    }

    [Theory]
    [InlineData("12", 10)]
    [InlineData(" 3 ", 3)]
    [InlineData("0", 1)]
    public void IntText_IsParsedAndClamped(string typed, int stored)
    {
        var definition = Define(SettingType.Int) with { Min = 1, Max = 10 };

        Assert.Equal(SettingCheck.Valid(stored), SettingValidator.CheckText(definition, typed, English));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("1e400")]
    public void IntText_RefusesWhatIsNotAWholeNumber(string typed)
    {
        Assert.Equal(SettingCheck.Invalid(SettingValidator.WholeNumberKey), SettingValidator.CheckText(Define(SettingType.Int), typed, English));
    }

    [Fact]
    public void BlankNumberText_IsTheDefaultUnlessRequired()
    {
        Assert.Equal(SettingCheck.Valid(null), SettingValidator.CheckText(Define(SettingType.Int), "", English));
        Assert.Equal(SettingCheck.Invalid(SettingValidator.RequiredKey), SettingValidator.CheckText(Define(SettingType.Int) with { Required = true }, "", English));
    }

    [Fact]
    public void DoubleText_IsParsedAndClamped()
    {
        var definition = Define(SettingType.Double) with { Min = 0, Max = 2 };

        Assert.Equal(SettingCheck.Valid(1.5), SettingValidator.CheckText(definition, "1.5", English));
        Assert.Equal(SettingCheck.Valid(2.0), SettingValidator.CheckText(definition, "9", English));
        Assert.Equal(SettingCheck.Invalid(SettingValidator.NumberKey), SettingValidator.CheckText(definition, "lots", English));
        Assert.Equal(SettingCheck.Invalid(SettingValidator.NumberKey), SettingValidator.Check(definition, double.NaN));
    }

    [Fact]
    public void Duration_IsWholeSecondsNeverNegative()
    {
        var definition = Define(SettingType.Duration);

        Assert.Equal(SettingCheck.Valid(90L), SettingValidator.Check(definition, TimeSpan.FromSeconds(90)));
        Assert.Equal(SettingCheck.Valid(0L), SettingValidator.Check(definition, -5));
        Assert.Equal(SettingCheck.Valid(600L), SettingValidator.Check(definition with { Max = 600 }, 3600));
        Assert.Equal(SettingCheck.Invalid(SettingValidator.WholeNumberKey), SettingValidator.Check(definition, 1.5));
    }

    [Fact]
    public void Choice_MustBeOneOfTheChoices()
    {
        var definition = Define(SettingType.Choice);

        Assert.Equal(SettingCheck.Valid("a"), SettingValidator.Check(definition, "a"));
        Assert.Equal(SettingCheck.Invalid(SettingValidator.ChoiceKey), SettingValidator.Check(definition, "A"));
    }

    [Fact]
    public void List_HoldsAtMostFiftyItemsByDefault()
    {
        var definition = Define(SettingType.StringList);
        var fifty = Enumerable.Range(1, 50).Select(i => $"item {i}").ToList();

        Assert.True(SettingValidator.Check(definition, fifty).IsValid);
        Assert.Equal(SettingCheck.Invalid(SettingValidator.TooManyKey), SettingValidator.Check(definition, fifty.Append("one more").ToList()));
    }

    [Fact]
    public void List_FollowsMaxItems_AndDropsBlankItems()
    {
        var definition = Define(SettingType.StringList) with { MaxItems = 2 };

        var check = SettingValidator.Check(definition, new[] { " a ", "", "  ", "b" });

        Assert.True(check.IsValid);
        Assert.Equal(new[] { "a", "b" }, Assert.IsAssignableFrom<IEnumerable<string>>(check.Value));
        Assert.Equal(SettingCheck.Invalid(SettingValidator.TooManyKey), SettingValidator.Check(definition, new[] { "a", "b", "c" }));
    }

    [Fact]
    public void ListText_IsOneItemPerLine()
    {
        var check = SettingValidator.CheckText(Define(SettingType.StringList), "one\r\ntwo\n\nthree", English);

        Assert.Equal(new[] { "one", "two", "three" }, Assert.IsAssignableFrom<IEnumerable<string>>(check.Value));
    }

    [Theory]
    [InlineData("ctrl + alt + k", "Ctrl+Alt+K")]
    [InlineData("Alt+Ctrl+Space", "Ctrl+Alt+Space")]
    [InlineData("Shift+F5", "Shift+F5")]
    [InlineData("", "")]
    public void Hotkey_IsStoredInItsCanonicalForm(string typed, string stored)
    {
        Assert.Equal(SettingCheck.Valid(stored), SettingValidator.Check(Define(SettingType.Hotkey), typed));
    }

    [Theory]
    [InlineData("K", SettingValidator.HotkeyModifierKey)]
    [InlineData("Shift+A", SettingValidator.HotkeyShiftKey)]
    [InlineData("Ctrl+Banana", SettingValidator.HotkeyKey)]
    [InlineData("Ctrl+A+B", SettingValidator.HotkeyKey)]
    public void Hotkey_SaysWhatIsWrong(string typed, string messageKey)
    {
        Assert.Equal(SettingCheck.Invalid(messageKey), SettingValidator.Check(Define(SettingType.Hotkey), typed));
    }

    [Fact]
    public void TimeZone_MustBeKnownToThisPc()
    {
        var definition = Define(SettingType.TimeZone);

        Assert.True(SettingValidator.Check(definition, TimeZoneInfo.Local.Id).IsValid);
        Assert.Equal(SettingCheck.Valid(TimeZoneInfo.Utc.Id), SettingValidator.Check(definition, TimeZoneInfo.Utc));
        Assert.Equal(SettingCheck.Invalid(SettingValidator.TimeZoneKey), SettingValidator.Check(definition, "Mars Standard Time"));
    }

    [Theory]
    [InlineData(@"C:\Music")]
    [InlineData(@"\\nas\share\music")]
    [InlineData(@"""C:\With Quotes""")]
    public void Path_MustBeFull(string path)
    {
        Assert.True(SettingValidator.Check(Define(SettingType.Folder), path).IsValid);
    }

    [Theory]
    [InlineData(@"Music")]
    [InlineData(@"..\up")]
    [InlineData(@"C:relative")]
    public void Path_RefusesRelativePaths(string path)
    {
        Assert.Equal(SettingCheck.Invalid(SettingValidator.PathKey), SettingValidator.Check(Define(SettingType.File), path));
    }

    [Fact]
    public void Text_FollowsRequiredAndMaxLength()
    {
        var definition = Define(SettingType.String) with { Required = true, MaxLength = 5 };

        Assert.Equal(SettingCheck.Invalid(SettingValidator.RequiredKey), SettingValidator.Check(definition, "   "));
        Assert.Equal(SettingCheck.Invalid(SettingValidator.TooLongKey), SettingValidator.Check(definition, "too long"));
        Assert.Equal(SettingCheck.Valid("short"), SettingValidator.Check(definition, "short"));
    }

    [Fact]
    public void Secret_FollowsMaxLength()
    {
        Assert.Equal(SettingCheck.Invalid(SettingValidator.TooLongKey), SettingValidator.Check(Define(SettingType.Secret) with { MaxLength = 3 }, "four"));
    }

    private static SettingDefinition Define(SettingType type) => new(
        "test.value",
        "test.label",
        type,
        null,
        type == SettingType.Choice ? ["a", "b", "c"] : null);
}
