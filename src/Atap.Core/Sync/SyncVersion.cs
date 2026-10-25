using System.Globalization;

namespace Atap.Core.Sync;

/// <summary>
/// When a unit last changed and on which PC. Versions are totally ordered: the newer stamp wins,
/// and a tie goes to the higher device id, so every PC picks the same winner.
/// </summary>
/// <param name="Stamp">UTC time of the change, to the millisecond.</param>
/// <param name="Device">Device id of the PC that made the change, a lowercase GUID.</param>
public readonly record struct SyncVersion(DateTimeOffset Stamp, string Device) : IComparable<SyncVersion>
{
    private const string StampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private static readonly string[] UtcStampFormats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];

    private static readonly string[] OffsetStampFormats = ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"];

    /// <summary>
    /// Stamp given to this PC's units when the user picks the OneDrive copy, so any real stamp beats
    /// them. It is never written to a bundle. The merge that first writes such a unit stamps it with
    /// the time of that pass.
    /// </summary>
    public static DateTimeOffset InitialStamp { get; } = DateTimeOffset.UnixEpoch;

    /// <summary>The latest stamp that survives a round trip as text: <c>9999-12-31T23:59:59.999Z</c>.</summary>
    public static DateTimeOffset LatestStamp { get; } = Truncate(DateTimeOffset.MaxValue);

    public int CompareTo(SyncVersion other)
    {
        var byStamp = Stamp.UtcTicks.CompareTo(other.Stamp.UtcTicks);
        return byStamp != 0 ? byStamp : string.CompareOrdinal(Device, other.Device);
    }

    public static bool operator >(SyncVersion left, SyncVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(SyncVersion left, SyncVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(SyncVersion left, SyncVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(SyncVersion left, SyncVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Formats a stamp as UTC ISO 8601 with milliseconds, such as <c>2026-10-25T08:30:00.000Z</c>.</summary>
    public static string FormatStamp(DateTimeOffset stamp) =>
        Truncate(stamp).UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture);

    /// <summary>Parses an ISO 8601 stamp with <c>Z</c> or an offset. Other text, such as a local date, fails.</summary>
    public static bool TryParseStamp(string? text, out DateTimeOffset stamp)
    {
        stamp = default;
        if (string.IsNullOrEmpty(text) || text.Length > 40)
            return false;
        if (DateTimeOffset.TryParseExact(text, UtcStampFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            || DateTimeOffset.TryParseExact(text, OffsetStampFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            stamp = Truncate(parsed);
            return true;
        }
        return false;
    }

    /// <summary>Normalizes a device id to a lowercase GUID. False when it is not a GUID.</summary>
    public static bool TryNormalizeDevice(string? text, out string device)
    {
        device = string.Empty;
        if (string.IsNullOrEmpty(text) || text.Length > 68 || !Guid.TryParse(text, out var guid))
            return false;
        device = guid.ToString("D");
        return true;
    }

    /// <summary>
    /// The stamp for a local change made at <paramref name="now"/>: <paramref name="now"/> in UTC to the
    /// millisecond, but always later than <paramref name="previous"/>, so a change made after a
    /// remote one wins even when this PC's clock is behind.
    /// </summary>
    /// <remarks>
    /// There is no stamp after <see cref="LatestStamp"/>, so the result stays at it. That stamp reads
    /// back the same from the file, so the bundle settles instead of being rewritten on every pass.
    /// </remarks>
    public static DateTimeOffset NextStamp(DateTimeOffset now, DateTimeOffset? previous)
    {
        var stamp = Truncate(now);
        if (previous is { } last && stamp <= last)
            stamp = Truncate(last) >= LatestStamp ? LatestStamp : Truncate(last).AddMilliseconds(1);
        return stamp;
    }

    /// <summary>Converts to UTC and drops everything below the millisecond, so a stamp survives a round trip as text.</summary>
    public static DateTimeOffset Truncate(DateTimeOffset stamp)
    {
        var ticks = stamp.UtcTicks;
        return new DateTimeOffset(ticks - ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
    }

    public override string ToString() => $"{FormatStamp(Stamp)} {Device}";
}
