using Atap.Core.Malaysia;

namespace Atap.Core.Tests.Malaysia;

public class MalaysiaKeysTests
{
    [Fact]
    public void TypeIds_AreTheFixedIds()
    {
        Assert.Equal("waktu-solat", MalaysiaKeys.WaktuSolatTypeId);
        Assert.Equal("hijri", MalaysiaKeys.HijriTypeId);
        Assert.Equal("rain-alerts", MalaysiaKeys.RainAlertsTypeId);
        Assert.Equal("holidays", MalaysiaKeys.HolidaysTypeId);
        string[] ids = [MalaysiaKeys.WaktuSolatTypeId, MalaysiaKeys.HijriTypeId, MalaysiaKeys.RainAlertsTypeId, MalaysiaKeys.HolidaysTypeId];
        Assert.All(ids, id => Assert.Matches("^[a-z][a-z0-9-]{1,31}$", id));
    }

    [Fact]
    public void SettingKeys_StartWithTheAreaName()
    {
        string[] keys = [MalaysiaKeys.State, MalaysiaKeys.Zone, MalaysiaKeys.HijriOffset, MalaysiaKeys.UseLocation];

        Assert.All(keys, key => Assert.StartsWith("malaysia.", key, StringComparison.Ordinal));
        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.Equal("malaysia.state", MalaysiaKeys.State);
    }

    [Fact]
    public void Timings_FollowThePlan()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), MalaysiaKeys.EsolatMinInterval);
        Assert.Equal(TimeSpan.FromSeconds(15), MalaysiaKeys.DataGovMinInterval);
        Assert.Equal(25, MalaysiaKeys.PrefetchFromDay);
        Assert.Equal(TimeSpan.FromHours(3), MalaysiaKeys.ForecastMaxAge);
        Assert.Equal(TimeSpan.FromMinutes(20), MalaysiaKeys.WarningsMaxAge);
    }

    [Fact]
    public void Urls_EsolatMonth()
    {
        var url = MalaysiaUrls.EsolatMonth("sgr01", 11);

        Assert.Equal("https://www.e-solat.gov.my/index.php?r=esolatApi/TakwimSolat&period=month&zone=SGR01&month=11", url.AbsoluteUri);
        Assert.Equal(MalaysiaKeys.EsolatHost, url.Host);
    }

    [Fact]
    public void Urls_MirrorMonth()
    {
        var url = MalaysiaUrls.MirrorMonth("WLY01", 2027, 1);

        Assert.Equal("https://api.waktusolat.app/v2/solat/WLY01?year=2027&month=1", url.AbsoluteUri);
        Assert.Equal(MalaysiaKeys.MirrorHost, url.Host);
    }

    [Fact]
    public void Urls_ForecastAndWarnings()
    {
        Assert.Equal("https://api.data.gov.my/weather/forecast/?filter=Ds055@location__location_id", MalaysiaUrls.Forecast("Ds055").AbsoluteUri);
        Assert.Equal("https://api.data.gov.my/weather/warning/", MalaysiaUrls.Warnings.AbsoluteUri);
        Assert.Equal(MalaysiaKeys.DataGovHost, MalaysiaUrls.Warnings.Host);
        Assert.Equal("St008", MalaysiaStates.Find("SGR")!.ForecastLocationId);
    }

    [Theory]
    [InlineData("XXX99")]
    [InlineData("SGR01&month=1")]
    [InlineData("")]
    public void Urls_UnknownZone_Throws(string zone)
    {
        Assert.Throws<ArgumentException>(() => MalaysiaUrls.EsolatMonth(zone, 1));
        Assert.Throws<ArgumentException>(() => MalaysiaUrls.MirrorMonth(zone, 2026, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Urls_BadMonth_Throws(int month)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MalaysiaUrls.EsolatMonth("SGR01", month));
        Assert.Throws<ArgumentOutOfRangeException>(() => MalaysiaUrls.MirrorMonth("SGR01", 2026, month));
    }

    [Theory]
    [InlineData("Ds055&limit=1")]
    [InlineData("../x")]
    [InlineData("DS055")]
    [InlineData("")]
    public void Urls_BadLocationId_Throws(string locationId)
    {
        Assert.Throws<ArgumentException>(() => MalaysiaUrls.Forecast(locationId));
    }

    [Fact]
    public void MalaysiaTime_DateOfUsesUtcPlusEight()
    {
        Assert.Equal(new DateOnly(2026, 10, 26), MalaysiaTime.DateOf(new DateTimeOffset(2026, 10, 25, 16, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 10, 25), MalaysiaTime.DateOf(new DateTimeOffset(2026, 10, 25, 15, 59, 0, TimeSpan.Zero)));
        Assert.Equal(Myt.At(2026, 10, 25, 5, 49), MalaysiaTime.At(new DateOnly(2026, 10, 25), new TimeOnly(5, 49)));
    }
}
