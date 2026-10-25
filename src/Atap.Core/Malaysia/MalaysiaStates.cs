namespace Atap.Core.Malaysia;

/// <summary>A Malaysian state or federal territory.</summary>
/// <param name="Code">Three-letter code stored in settings, for example "SGR".</param>
/// <param name="NameEn">Name shown in English.</param>
/// <param name="NameMs">Name shown in Bahasa Melayu.</param>
/// <param name="ForecastLocationId">The state's location id in the data.gov.my forecast, for example "St008".</param>
public sealed record MalaysiaState(string Code, string NameEn, string NameMs, string ForecastLocationId, bool IsFederalTerritory = false)
{
    /// <summary>The name for a UI language ("en" or "ms"). Anything else gets English.</summary>
    public string Name(string language) =>
        string.Equals(language, "ms", StringComparison.OrdinalIgnoreCase) ? NameMs : NameEn;
}

/// <summary>The 13 states and 3 federal territories, in the usual Malaysian order.</summary>
public static class MalaysiaStates
{
    public static IReadOnlyList<MalaysiaState> All { get; } =
    [
        new("JHR", "Johor", "Johor", "St013"),
        new("KDH", "Kedah", "Kedah", "St002"),
        new("KTN", "Kelantan", "Kelantan", "St005"),
        new("MLK", "Melaka", "Melaka", "St012"),
        new("NSN", "Negeri Sembilan", "Negeri Sembilan", "St011"),
        new("PHG", "Pahang", "Pahang", "St007"),
        new("PRK", "Perak", "Perak", "St004"),
        new("PLS", "Perlis", "Perlis", "St001"),
        new("PNG", "Penang", "Pulau Pinang", "St003"),
        new("SBH", "Sabah", "Sabah", "St502"),
        new("SWK", "Sarawak", "Sarawak", "St501"),
        new("SGR", "Selangor", "Selangor", "St008"),
        new("TRG", "Terengganu", "Terengganu", "St006"),
        new("KUL", "Kuala Lumpur", "Kuala Lumpur", "St009", IsFederalTerritory: true),
        new("LBN", "Labuan", "Labuan", "St503", IsFederalTerritory: true),
        new("PJY", "Putrajaya", "Putrajaya", "St010", IsFederalTerritory: true),
    ];

    private static readonly Dictionary<string, MalaysiaState> ByCode =
        All.ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>The state for a code in any letter case, or null.</summary>
    public static MalaysiaState? Find(string? code) =>
        code is not null && ByCode.TryGetValue(code.Trim(), out var state) ? state : null;

    public static bool IsValidCode(string? code) => Find(code) is not null;
}
