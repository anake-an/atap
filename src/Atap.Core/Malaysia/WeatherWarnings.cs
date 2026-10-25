using System.Collections;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Atap.Core.Malaysia;

/// <summary>The kind of a MET Malaysia warning, read from its title.</summary>
public enum WarningKind
{
    Other,

    /// <summary>"No Advisory" or "Tiada Nasihat", sent when there is nothing to report.</summary>
    NoAdvisory,
    Thunderstorm,
    Rain,
    TropicalCyclone,

    /// <summary>Strong winds and rough seas. These name sea areas, not states.</summary>
    WindAndSea,
}

/// <summary>A MET Malaysia warning, with its English and Malay text as sent.</summary>
/// <param name="Id">A stable id built from the issue time, title and validity, for one alert per warning.</param>
/// <param name="States">States named in a land warning (thunderstorm or rain). Empty for the other kinds.</param>
public sealed record WeatherWarning(
    string Id,
    WarningKind Kind,
    DateTimeOffset Issued,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    string TitleEn,
    string TitleMs,
    string HeadingEn,
    string HeadingMs,
    string TextEn,
    string TextMs,
    string? InstructionEn,
    string? InstructionMs,
    IReadOnlyList<string> States)
{
    public string Title(string language) => IsMalay(language) ? TitleMs : TitleEn;

    public string Heading(string language) => IsMalay(language) ? HeadingMs : HeadingEn;

    public string Text(string language) => IsMalay(language) ? TextMs : TextEn;

    public string? Instruction(string language) => IsMalay(language) ? InstructionMs : InstructionEn;

    /// <summary>True for thunderstorm and rain warnings, the ones rain alerts show.</summary>
    public bool IsLandWarning => Kind is WarningKind.Thunderstorm or WarningKind.Rain;

    /// <summary>True from <see cref="ValidFrom"/> until <see cref="ValidTo"/>. A missing bound is open.</summary>
    public bool IsActive(DateTimeOffset now) =>
        (ValidFrom is not { } from || now >= from) && (ValidTo is not { } to || now < to);

    public bool AffectsState(string? stateCode) =>
        MalaysiaStates.Find(stateCode) is { } state && States.Contains(state.Code, StringComparer.Ordinal);

    private static bool IsMalay(string language) => string.Equals(language, "ms", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The warnings read from one response, in response order. An entry that cannot be read, for
/// example a new kind of warning without an issue time, is skipped so it cannot hide the others.
/// </summary>
public sealed class WarningList : IReadOnlyList<WeatherWarning>
{
    private readonly IReadOnlyList<WeatherWarning> _warnings;

    public WarningList(IReadOnlyList<WeatherWarning> warnings, int skipped = 0, ParseError skipError = ParseError.None, string? skipDetail = null)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        ArgumentOutOfRangeException.ThrowIfNegative(skipped);
        _warnings = warnings;
        Skipped = skipped;
        SkipError = skipError;
        SkipDetail = skipDetail;
    }

    /// <summary>How many entries were skipped as unreadable. The App logs this when it is not 0.</summary>
    public int Skipped { get; }

    /// <summary>Why the first skipped entry was refused, or <see cref="ParseError.None"/>.</summary>
    public ParseError SkipError { get; }

    /// <summary>The field or value behind <see cref="SkipError"/>, for the log.</summary>
    public string? SkipDetail { get; }

    public int Count => _warnings.Count;

    public WeatherWarning this[int index] => _warnings[index];

    public IEnumerator<WeatherWarning> GetEnumerator() => _warnings.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Parses <c>https://api.data.gov.my/weather/warning/</c>, an array of
/// <c>{"warning_issue":{"issued":"...","title_bm":"...","title_en":"..."},"valid_from":"...","valid_to":"...","text_en":"...","text_bm":"...",...}</c>.
/// Times have no offset and are Malaysian time.
/// </summary>
public static partial class WarningParser
{
    private static readonly string[] LocalFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mm",
    ];

    private static readonly (string Code, Regex Pattern)[] StateNames =
    [
        ("JHR", Words("Johor")),
        ("KDH", Words("Kedah")),
        ("KTN", Words("Kelantan")),
        ("MLK", Words("Melaka", "Malacca")),
        ("NSN", Words("Negeri Sembilan", "N. Sembilan")),
        ("PHG", Words("Pahang")),
        ("PRK", Words("Perak")),
        ("PLS", Words("Perlis")),
        ("PNG", Words("Pulau Pinang", "Penang")),
        ("SBH", Words("Sabah")),
        ("SWK", Words("Sarawak")),
        ("SGR", Words("Selangor")),
        ("TRG", Words("Terengganu")),
        ("KUL", Words("Kuala Lumpur")),
        ("LBN", Words("Labuan")),
        ("PJY", Words("Putrajaya")),
    ];

    /// <summary>
    /// Every readable warning in the response. An empty array is a valid answer with no warnings.
    /// Unreadable entries are skipped and counted in <see cref="WarningList.Skipped"/>. The parse
    /// fails only when the response is not an array, or when it has entries and none can be read.
    /// </summary>
    public static ParseResult<WarningList> Parse(string? json) => MalaysiaJson.Run(json, Read);

    /// <summary>Reads the kind from the English or Malay title.</summary>
    public static WarningKind KindOf(string? titleEn, string? titleMs)
    {
        var text = $"{titleEn} {titleMs}".ToLowerInvariant();
        if (text.Contains("no advisory", StringComparison.Ordinal) || text.Contains("tiada nasihat", StringComparison.Ordinal))
            return WarningKind.NoAdvisory;
        if (text.Contains("thunderstorm", StringComparison.Ordinal) || text.Contains("ribut petir", StringComparison.Ordinal))
            return WarningKind.Thunderstorm;
        if (text.Contains("rain", StringComparison.Ordinal) || text.Contains("hujan", StringComparison.Ordinal))
            return WarningKind.Rain;
        if (text.Contains("cyclone", StringComparison.Ordinal) || text.Contains("siklon", StringComparison.Ordinal))
            return WarningKind.TropicalCyclone;
        if (text.Contains("wind", StringComparison.Ordinal) || text.Contains("angin", StringComparison.Ordinal)
            || text.Contains("sea", StringComparison.Ordinal) || text.Contains("laut", StringComparison.Ordinal))
            return WarningKind.WindAndSea;
        return WarningKind.Other;
    }

    /// <summary>State codes named in a warning text, in table order.</summary>
    public static IReadOnlyList<string> StatesIn(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        return StateNames.Where(s => s.Pattern.IsMatch(text)).Select(s => s.Code).ToList();
    }

    private static WarningList Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            throw MalaysiaJson.Fail(ParseError.MissingField, "[]");

        var warnings = new List<WeatherWarning>();
        var skipped = 0;
        MalaysiaParseException? firstSkip = null;
        foreach (var entry in root.EnumerateArray())
        {
            try
            {
                warnings.Add(ReadWarning(entry));
            }
            catch (Exception ex) when (ex is MalaysiaParseException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
            {
                // One bad entry must not cost the rain alerts every other warning.
                skipped++;
                firstSkip ??= ex as MalaysiaParseException ?? MalaysiaJson.Fail(ParseError.BadValue, ex.GetType().Name);
            }
        }

        // Entries that are all unreadable mean the format changed. Say so instead of "no warnings".
        if (warnings.Count == 0 && firstSkip is not null)
            throw MalaysiaJson.Fail(firstSkip.Error, firstSkip.Detail);
        return new WarningList(warnings, skipped, firstSkip?.Error ?? ParseError.None, firstSkip?.Detail);
    }

    private static WeatherWarning ReadWarning(JsonElement entry)
    {
        var issue = MalaysiaJson.Object(entry, "warning_issue");
        var issuedText = MalaysiaJson.String(issue, "issued");
        var issued = TryParseTime(issuedText) ?? throw MalaysiaJson.Fail(ParseError.BadValue, "issued");
        var titleEn = MalaysiaJson.OptionalString(issue, "title_en")?.Trim() ?? string.Empty;
        var titleMs = MalaysiaJson.OptionalString(issue, "title_bm")?.Trim() ?? string.Empty;
        if (titleEn.Length == 0 && titleMs.Length == 0)
            throw MalaysiaJson.Fail(ParseError.MissingField, "title_en");

        var validFromText = MalaysiaJson.OptionalString(entry, "valid_from");
        var validToText = MalaysiaJson.OptionalString(entry, "valid_to");
        var validFrom = OptionalTime(validFromText, "valid_from");
        var validTo = OptionalTime(validToText, "valid_to");

        var headingEn = MalaysiaJson.OptionalString(entry, "heading_en")?.Trim() ?? string.Empty;
        var headingMs = MalaysiaJson.OptionalString(entry, "heading_bm")?.Trim() ?? string.Empty;
        var textEn = MalaysiaJson.OptionalString(entry, "text_en")?.Trim() ?? string.Empty;
        var textMs = MalaysiaJson.OptionalString(entry, "text_bm")?.Trim() ?? string.Empty;
        var kind = KindOf(titleEn, titleMs);

        // Sea warnings name waters off a state, which says nothing about rain on land.
        var states = kind is WarningKind.Thunderstorm or WarningKind.Rain
            ? StatesIn(textMs).Union(StatesIn(textEn)).OrderBy(Order).ToList()
            : [];

        return new WeatherWarning(
            IdOf(issuedText, titleEn, validFromText, validToText, headingEn),
            kind,
            issued,
            validFrom,
            validTo,
            titleEn.Length > 0 ? titleEn : titleMs,
            titleMs.Length > 0 ? titleMs : titleEn,
            headingEn,
            headingMs,
            textEn,
            textMs,
            Blank(MalaysiaJson.OptionalString(entry, "instruction_en")),
            Blank(MalaysiaJson.OptionalString(entry, "instruction_bm")),
            states);
    }

    private static DateTimeOffset? OptionalTime(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return TryParseTime(text) ?? throw MalaysiaJson.Fail(ParseError.BadValue, field);
    }

    private static DateTimeOffset? TryParseTime(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (DateTime.TryParseExact(trimmed, LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return local.Year is >= 2000 and <= 2100 ? new DateTimeOffset(local, MalaysiaTime.Offset) : null;
        // Only a time that ends in Z or +hh:mm or -hh:mm is taken as written. Anything else
        // would be guessed as UTC and land 8 hours off.
        if (OffsetTimePattern().IsMatch(trimmed)
            && DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset)
            && withOffset.Year is >= 2000 and <= 2100)
            return withOffset.ToOffset(MalaysiaTime.Offset);
        return null;
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d{1,7})?)?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex OffsetTimePattern();

    private static string IdOf(params string?[] parts)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    private static int Order(string code)
    {
        for (var i = 0; i < MalaysiaStates.All.Count; i++)
        {
            if (MalaysiaStates.All[i].Code == code)
                return i;
        }
        return int.MaxValue;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static Regex Words(params string[] names) =>
        new($@"\b(?:{string.Join('|', names.Select(Regex.Escape))})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
