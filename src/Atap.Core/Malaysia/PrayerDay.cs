namespace Atap.Core.Malaysia;

/// <summary>The daily times in e-Solat order. The enum order is also the order within a day.</summary>
public enum Prayer
{
    Imsak,
    Subuh,
    Syuruk,
    Dhuha,
    Zohor,
    Asar,
    Maghrib,
    Isyak,
}

public static class Prayers
{
    /// <summary>The five obligatory prayers. "Next prayer" means the next of these.</summary>
    public static IReadOnlyList<Prayer> Obligatory { get; } =
        [Prayer.Subuh, Prayer.Zohor, Prayer.Asar, Prayer.Maghrib, Prayer.Isyak];

    /// <summary>The times listed on the expanded card.</summary>
    public static IReadOnlyList<Prayer> Shown { get; } =
        [Prayer.Imsak, Prayer.Subuh, Prayer.Syuruk, Prayer.Zohor, Prayer.Asar, Prayer.Maghrib, Prayer.Isyak];

    public static bool IsObligatory(this Prayer prayer) =>
        prayer is Prayer.Subuh or Prayer.Zohor or Prayer.Asar or Prayer.Maghrib or Prayer.Isyak;
}

/// <summary>Where a schedule came from. Only <see cref="Calculated"/> is labelled approximate.</summary>
public enum PrayerSource
{
    /// <summary>www.e-solat.gov.my, the official JAKIM source.</summary>
    Esolat,

    /// <summary>api.waktusolat.app, which republishes e-Solat data.</summary>
    Mirror,

    /// <summary>Computed on the PC with JAKIM parameters, for use outside Malaysia.</summary>
    Calculated,
}

/// <summary>One day of prayer times. Times carry their UTC offset, +08:00 for every JAKIM zone.</summary>
/// <param name="Hijri">The official Hijri date from e-Solat, or null when the source has none.</param>
public sealed record PrayerDay(
    DateOnly Date,
    HijriDate? Hijri,
    DateTimeOffset Imsak,
    DateTimeOffset Subuh,
    DateTimeOffset Syuruk,
    DateTimeOffset Dhuha,
    DateTimeOffset Zohor,
    DateTimeOffset Asar,
    DateTimeOffset Maghrib,
    DateTimeOffset Isyak)
{
    public DateTimeOffset TimeOf(Prayer prayer) => prayer switch
    {
        Prayer.Imsak => Imsak,
        Prayer.Subuh => Subuh,
        Prayer.Syuruk => Syuruk,
        Prayer.Dhuha => Dhuha,
        Prayer.Zohor => Zohor,
        Prayer.Asar => Asar,
        Prayer.Maghrib => Maghrib,
        Prayer.Isyak => Isyak,
        _ => throw new ArgumentOutOfRangeException(nameof(prayer), prayer, null),
    };

    /// <summary>True when every time follows the one before it, which any real day does.</summary>
    public bool IsInOrder =>
        Imsak < Subuh && Subuh < Syuruk && Syuruk < Dhuha && Dhuha < Zohor
        && Zohor < Asar && Asar < Maghrib && Maghrib < Isyak;

    /// <summary>The local date of an instant, in this day's UTC offset.</summary>
    internal DateOnly LocalDateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(Subuh.Offset).DateTime);
}

/// <summary>A prayer time on a given day.</summary>
public readonly record struct PrayerTime(Prayer Prayer, DateTimeOffset Time, DateOnly Date);
