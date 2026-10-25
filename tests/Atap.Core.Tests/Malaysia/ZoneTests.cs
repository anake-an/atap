using System.Text.RegularExpressions;
using Atap.Core.Malaysia;

namespace Atap.Core.Tests.Malaysia;

public class ZoneTests
{
    [Fact]
    public void States_AreSixteenWithUniqueThreeLetterCodes()
    {
        Assert.Equal(16, MalaysiaStates.All.Count);
        Assert.Equal(16, MalaysiaStates.All.Select(s => s.Code).Distinct().Count());
        Assert.All(MalaysiaStates.All, s => Assert.Matches("^[A-Z]{3}$", s.Code));
        Assert.Equal(3, MalaysiaStates.All.Count(s => s.IsFederalTerritory));
    }

    [Fact]
    public void States_HaveNamesAndUniqueForecastIds()
    {
        Assert.All(MalaysiaStates.All, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.NameEn));
            Assert.False(string.IsNullOrWhiteSpace(s.NameMs));
            Assert.Matches(@"^St\d{3}$", s.ForecastLocationId);
        });
        Assert.Equal(16, MalaysiaStates.All.Select(s => s.ForecastLocationId).Distinct().Count());
    }

    [Fact]
    public void States_FindIgnoresCaseAndRejectsUnknownCodes()
    {
        Assert.Equal("SGR", MalaysiaStates.Find(" sgr ")?.Code);
        Assert.Null(MalaysiaStates.Find("XYZ"));
        Assert.Null(MalaysiaStates.Find(null));
        Assert.False(MalaysiaStates.IsValidCode("Selangor"));
    }

    [Fact]
    public void States_NameFollowsTheLanguage()
    {
        var penang = MalaysiaStates.Find("PNG")!;

        Assert.Equal("Penang", penang.Name("en"));
        Assert.Equal("Pulau Pinang", penang.Name("ms"));
        Assert.Equal("Penang", penang.Name("fr"));
    }

    [Fact]
    public void Zones_CodesAreUniqueAndMatchThePattern()
    {
        var codes = PrayerZones.All.Select(z => z.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Matches(new Regex(PrayerZones.CodePattern), code));
        Assert.Equal(@"^[A-Z]{3}\d{2}$", PrayerZones.CodePattern);
    }

    [Fact]
    public void Zones_MatchTheRecordedJakimList()
    {
        // The 60 codes listed by e-Solat in October 2026.
        string[] recorded =
        [
            "JHR01", "JHR02", "JHR03", "JHR04", "KDH01", "KDH02", "KDH03", "KDH04", "KDH05", "KDH06", "KDH07",
            "KTN01", "KTN02", "MLK01", "NGS01", "NGS02", "NGS03", "PHG01", "PHG02", "PHG03", "PHG04", "PHG05",
            "PHG06", "PHG07", "PRK01", "PRK02", "PRK03", "PRK04", "PRK05", "PRK06", "PRK07", "PLS01", "PNG01",
            "SBH01", "SBH02", "SBH03", "SBH04", "SBH05", "SBH06", "SBH07", "SBH08", "SBH09", "SWK01", "SWK02",
            "SWK03", "SWK04", "SWK05", "SWK06", "SWK07", "SWK08", "SWK09", "SGR01", "SGR02", "SGR03", "TRG01",
            "TRG02", "TRG03", "TRG04", "WLY01", "WLY02",
        ];

        Assert.Equal(recorded, PrayerZones.All.Select(z => z.Code));
    }

    [Fact]
    public void Zones_BelongToKnownStatesAndCoverEveryState()
    {
        Assert.All(PrayerZones.All, z =>
        {
            Assert.NotEmpty(z.StateCodes);
            Assert.All(z.StateCodes, code => Assert.True(MalaysiaStates.IsValidCode(code), code));
            Assert.NotEmpty(z.Areas);
        });
        Assert.All(MalaysiaStates.All, s => Assert.NotEmpty(PrayerZones.ForState(s.Code)));
    }

    [Fact]
    public void Zones_ForState()
    {
        Assert.Equal(["SGR01", "SGR02", "SGR03"], PrayerZones.ForState("sgr").Select(z => z.Code));
        Assert.Equal(["WLY01"], PrayerZones.ForState("KUL").Select(z => z.Code));
        Assert.Equal(["WLY01"], PrayerZones.ForState("PJY").Select(z => z.Code));
        Assert.Equal(["WLY02"], PrayerZones.ForState("LBN").Select(z => z.Code));
        Assert.Equal(["NGS01", "NGS02", "NGS03"], PrayerZones.ForState("NSN").Select(z => z.Code));
        Assert.Empty(PrayerZones.ForState("XYZ"));
        Assert.Empty(PrayerZones.ForState(null));
    }

    [Fact]
    public void Zones_FindAndAreasText()
    {
        var zone = PrayerZones.Find("sgr01")!;

        Assert.Equal("SGR01", zone.Code);
        Assert.Equal("Gombak, Petaling, Sepang, Hulu Langat, Hulu Selangor, Shah Alam", zone.AreasText);
        Assert.Null(PrayerZones.Find("SGR04"));
        Assert.False(PrayerZones.IsValidCode("SGR01&month=1"));
    }

    [Fact]
    public void Centroids_CoverEveryZoneThatIsNotSpecial()
    {
        var withCentroids = DistrictCentroids.All.Select(c => c.ZoneCode).ToHashSet();

        Assert.All(PrayerZones.All, z => Assert.Equal(!z.IsSpecial, withCentroids.Contains(z.Code)));
        Assert.Equal(["KDH07", "PRK07", "SBH06", "SWK09"], PrayerZones.All.Where(z => z.IsSpecial).Select(z => z.Code));
    }

    [Fact]
    public void Centroids_AreValidPointsInsideMalaysia()
    {
        Assert.All(DistrictCentroids.All, c =>
        {
            Assert.True(PrayerZones.IsValidCode(c.ZoneCode), c.Name);
            Assert.InRange(c.Point.Latitude, 0.8, 7.5);
            Assert.InRange(c.Point.Longitude, 99.5, 119.5);
        });
        Assert.Equal(DistrictCentroids.All.Count, DistrictCentroids.All.Select(c => c.Name).Distinct().Count());
    }

    [Theory]
    [InlineData(3.1579, 101.7116, "WLY01")] // KLCC
    [InlineData(2.9430, 101.6990, "WLY01")] // Putrajaya
    [InlineData(3.0650, 101.4850, "SGR01")] // Shah Alam
    [InlineData(3.0440, 101.4460, "SGR03")] // Klang
    [InlineData(3.3500, 101.2500, "SGR02")] // Kuala Selangor
    [InlineData(5.4145, 100.3292, "PNG01")] // George Town
    [InlineData(1.4655, 103.7578, "JHR02")] // Johor Bahru
    [InlineData(6.3500, 99.8000, "KDH06")] // Langkawi
    [InlineData(4.5975, 101.0901, "PRK02")] // Ipoh
    [InlineData(5.9788, 116.0753, "SBH07")] // Kota Kinabalu
    [InlineData(1.5533, 110.3592, "SWK08")] // Kuching
    [InlineData(4.4700, 101.3800, "PHG06")] // Cameron Highlands
    [InlineData(5.2831, 115.2308, "WLY02")] // Labuan
    [InlineData(6.0750, 116.5583, "SBH07")] // Gunung Kinabalu summit, a special zone, matches Ranau
    public void FindNearest_PicksTheZoneOfTheNearestDistrict(double latitude, double longitude, string zone)
    {
        var match = ZoneLocator.FindNearest(new GeoPoint(latitude, longitude));

        Assert.NotNull(match);
        Assert.Equal(zone, match.Zone.Code);
        Assert.False(match.IsAbroad);
        Assert.True(match.DistanceKm < 25, $"{match.Nearest.Name} is {match.DistanceKm:0.0} km away");
    }

    [Theory]
    [InlineData(51.5074, -0.1278)] // London
    [InlineData(21.4225, 39.8262)] // Makkah
    [InlineData(35.6762, 139.6503)] // Tokyo
    public void FindNearest_FarAway_IsAbroad(double latitude, double longitude)
    {
        var match = ZoneLocator.FindNearest(new GeoPoint(latitude, longitude));

        Assert.NotNull(match);
        Assert.True(match.IsAbroad);
    }

    [Theory]
    [InlineData(double.NaN, 101.0)]
    [InlineData(3.0, double.PositiveInfinity)]
    [InlineData(91.0, 101.0)]
    [InlineData(3.0, 181.0)]
    public void FindNearest_InvalidPoint_IsNull(double latitude, double longitude)
    {
        Assert.Null(ZoneLocator.FindNearest(new GeoPoint(latitude, longitude)));
    }

    [Fact]
    public void GeoPoint_DistanceKualaLumpurToGeorgeTown()
    {
        var distance = new GeoPoint(3.139, 101.687).DistanceKmTo(new GeoPoint(5.414, 100.329));

        Assert.InRange(distance, 290, 300);
        Assert.Equal(0, new GeoPoint(3.139, 101.687).DistanceKmTo(new GeoPoint(3.139, 101.687)), 6);
    }
}
