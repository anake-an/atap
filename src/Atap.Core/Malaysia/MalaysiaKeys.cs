namespace Atap.Core.Malaysia;

/// <summary>Ids, setting keys, hosts and refresh times shared by every Malaysia widget and feature.</summary>
public static class MalaysiaKeys
{
    // Widget type ids, fixed by the architecture plan.
    public const string WaktuSolatTypeId = "waktu-solat";
    public const string HijriTypeId = "hijri";
    public const string RainAlertsTypeId = "rain-alerts";
    public const string HolidaysTypeId = "holidays";

    /// <summary>Feature id of the state, zone and Hijri offset settings.</summary>
    public const string FeatureId = "malaysia";

    /// <summary>Settings section id. Strings for the section use this area name too.</summary>
    public const string SectionId = "malaysia";

    // App settings. All are synced except UseLocation, which is per PC.

    /// <summary>State code from <see cref="MalaysiaStates"/>, for example "SGR". Empty until picked.</summary>
    public const string State = "malaysia.state";

    /// <summary>JAKIM zone code from <see cref="PrayerZones"/>, for example "SGR01". Empty until picked.</summary>
    public const string Zone = "malaysia.zone";

    /// <summary>Days added to the fallback Hijri date, from -2 to +2. Official e-Solat dates are never shifted.</summary>
    public const string HijriOffset = "malaysia.hijriOffset";

    /// <summary>Use Windows Location to suggest a zone. Off by default and stored per PC.</summary>
    public const string UseLocation = "malaysia.useLocation";

    // Hosts. Each widget declares the ones it uses in its capabilities.
    public const string EsolatHost = "www.e-solat.gov.my";
    public const string MirrorHost = "api.waktusolat.app";
    public const string DataGovHost = "api.data.gov.my";

    /// <summary>Smallest gap between two requests to e-Solat or the mirror.</summary>
    public static readonly TimeSpan EsolatMinInterval = TimeSpan.FromSeconds(60);

    /// <summary>Smallest gap between two requests to data.gov.my.</summary>
    public static readonly TimeSpan DataGovMinInterval = TimeSpan.FromSeconds(15);

    /// <summary>Next month's prayer times are fetched from this day of the month on.</summary>
    public const int PrefetchFromDay = 25;

    /// <summary>How long a forecast stays fresh.</summary>
    public static readonly TimeSpan ForecastMaxAge = TimeSpan.FromHours(3);

    /// <summary>How long the warning list stays fresh.</summary>
    public static readonly TimeSpan WarningsMaxAge = TimeSpan.FromMinutes(20);
}
