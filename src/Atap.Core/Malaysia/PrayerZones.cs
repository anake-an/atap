namespace Atap.Core.Malaysia;

/// <summary>A JAKIM prayer time zone.</summary>
/// <param name="Code">Zone code sent to e-Solat, for example "SGR01".</param>
/// <param name="StateCodes">States the zone belongs to. Only WLY01 has two (Kuala Lumpur and Putrajaya).</param>
/// <param name="Areas">Districts and places as JAKIM lists them. Place names read the same in both languages.</param>
public sealed record PrayerZone(string Code, IReadOnlyList<string> StateCodes, IReadOnlyList<string> Areas)
{
    /// <summary>
    /// A summit or single-village zone. It can be picked by hand but is never chosen from a
    /// location, because people near it almost always live in the surrounding zone.
    /// </summary>
    public bool IsSpecial { get; init; }

    /// <summary>Areas joined for display, for example "Gombak, Petaling, Sepang".</summary>
    public string AreasText => string.Join(", ", Areas);
}

/// <summary>Every JAKIM zone, as listed by e-Solat (recorded October 2026).</summary>
public static class PrayerZones
{
    /// <summary>The shape every zone code has: three capital letters and two digits.</summary>
    public const string CodePattern = @"^[A-Z]{3}\d{2}$";

    public static IReadOnlyList<PrayerZone> All { get; } =
    [
        Zone("JHR01", "JHR", "Pulau Aur", "Pulau Pemanggil"),
        Zone("JHR02", "JHR", "Johor Bahru", "Kota Tinggi", "Mersing", "Kulai"),
        Zone("JHR03", "JHR", "Kluang", "Pontian"),
        Zone("JHR04", "JHR", "Batu Pahat", "Muar", "Segamat", "Gemas Johor", "Tangkak"),

        Zone("KDH01", "KDH", "Kota Setar", "Kubang Pasu", "Pokok Sena (Daerah Kecil)"),
        Zone("KDH02", "KDH", "Kuala Muda", "Yan", "Pendang"),
        Zone("KDH03", "KDH", "Padang Terap", "Sik"),
        Zone("KDH04", "KDH", "Baling"),
        Zone("KDH05", "KDH", "Bandar Baharu", "Kulim"),
        Zone("KDH06", "KDH", "Langkawi"),
        Zone("KDH07", "KDH", "Puncak Gunung Jerai") with { IsSpecial = true },

        Zone("KTN01", "KTN", "Bachok", "Kota Bharu", "Machang", "Pasir Mas", "Pasir Puteh", "Tanah Merah", "Tumpat", "Kuala Krai", "Mukim Chiku"),
        Zone("KTN02", "KTN", "Gua Musang (Daerah Galas dan Bertam)", "Jeli", "Jajahan Kecil Lojing"),

        Zone("MLK01", "MLK", "Alor Gajah", "Jasin", "Melaka Tengah"),

        Zone("NGS01", "NSN", "Tampin", "Jempol"),
        Zone("NGS02", "NSN", "Jelebu", "Kuala Pilah", "Rembau"),
        Zone("NGS03", "NSN", "Port Dickson", "Seremban"),

        Zone("PHG01", "PHG", "Pulau Tioman"),
        Zone("PHG02", "PHG", "Kuantan", "Pekan", "Muadzam Shah"),
        Zone("PHG03", "PHG", "Jerantut", "Temerloh", "Maran", "Bera", "Chenor", "Jengka"),
        Zone("PHG04", "PHG", "Bentong", "Lipis", "Raub"),
        Zone("PHG05", "PHG", "Genting Sempah", "Janda Baik", "Bukit Tinggi"),
        Zone("PHG06", "PHG", "Cameron Highlands", "Genting Highlands", "Bukit Fraser"),
        Zone("PHG07", "PHG", "Mukim Rompin", "Mukim Endau", "Mukim Pontian"),

        Zone("PRK01", "PRK", "Tapah", "Slim River", "Tanjung Malim"),
        Zone("PRK02", "PRK", "Kuala Kangsar", "Sungai Siput", "Ipoh", "Batu Gajah", "Kampar"),
        Zone("PRK03", "PRK", "Lenggong", "Pengkalan Hulu", "Gerik"),
        Zone("PRK04", "PRK", "Temengor", "Belum"),
        Zone("PRK05", "PRK", "Kampung Gajah", "Teluk Intan", "Bagan Datuk", "Seri Iskandar", "Beruas", "Parit", "Lumut", "Sitiawan", "Pulau Pangkor"),
        Zone("PRK06", "PRK", "Selama", "Taiping", "Bagan Serai", "Parit Buntar"),
        Zone("PRK07", "PRK", "Bukit Larut") with { IsSpecial = true },

        Zone("PLS01", "PLS", "Kangar", "Arau", "Padang Besar", "Kuala Perlis"),

        Zone("PNG01", "PNG", "Timur Laut", "Barat Daya", "Seberang Perai Utara", "Seberang Perai Tengah", "Seberang Perai Selatan"),

        Zone("SBH01", "SBH", "Bahagian Sandakan (Timur)", "Bukit Garam", "Semawang", "Temanggong", "Tambisan", "Bandar Sandakan", "Sukau"),
        Zone("SBH02", "SBH", "Beluran", "Telupid", "Pinangah", "Terusan", "Kuamut", "Bahagian Sandakan (Barat)"),
        Zone("SBH03", "SBH", "Lahad Datu", "Silabukan", "Kunak", "Sahabat", "Semporna", "Tungku", "Bahagian Tawau (Timur)"),
        Zone("SBH04", "SBH", "Bandar Tawau", "Balong", "Merotai", "Kalabakan", "Bahagian Tawau (Barat)"),
        Zone("SBH05", "SBH", "Kudat", "Kota Marudu", "Pitas", "Pulau Banggi", "Bahagian Kudat"),
        Zone("SBH06", "SBH", "Gunung Kinabalu") with { IsSpecial = true },
        Zone("SBH07", "SBH", "Kota Kinabalu", "Ranau", "Kota Belud", "Tuaran", "Penampang", "Papar", "Putatan", "Bahagian Pantai Barat"),
        Zone("SBH08", "SBH", "Pensiangan", "Keningau", "Tambunan", "Nabawan", "Bahagian Pedalaman (Atas)"),
        Zone("SBH09", "SBH", "Beaufort", "Kuala Penyu", "Sipitang", "Tenom", "Long Pasia", "Membakut", "Weston", "Bahagian Pedalaman (Bawah)"),

        Zone("SWK01", "SWK", "Limbang", "Lawas", "Sundar", "Trusan"),
        Zone("SWK02", "SWK", "Miri", "Niah", "Bekenu", "Sibuti", "Marudi"),
        Zone("SWK03", "SWK", "Pandan", "Belaga", "Suai", "Tatau", "Sebauh", "Bintulu"),
        Zone("SWK04", "SWK", "Sibu", "Mukah", "Dalat", "Song", "Igan", "Oya", "Balingian", "Kanowit", "Kapit"),
        Zone("SWK05", "SWK", "Sarikei", "Matu", "Julau", "Rajang", "Daro", "Bintangor", "Belawai"),
        Zone("SWK06", "SWK", "Lubok Antu", "Sri Aman", "Roban", "Debak", "Kabong", "Lingga", "Engkilili", "Betong", "Spaoh", "Pusa", "Saratok"),
        Zone("SWK07", "SWK", "Serian", "Simunjan", "Samarahan", "Sebuyau", "Meludam"),
        Zone("SWK08", "SWK", "Kuching", "Bau", "Lundu", "Sematan"),
        Zone("SWK09", "SWK", "Kampung Patarikan") with { IsSpecial = true },

        Zone("SGR01", "SGR", "Gombak", "Petaling", "Sepang", "Hulu Langat", "Hulu Selangor", "Shah Alam"),
        Zone("SGR02", "SGR", "Kuala Selangor", "Sabak Bernam"),
        Zone("SGR03", "SGR", "Klang", "Kuala Langat"),

        Zone("TRG01", "TRG", "Kuala Terengganu", "Marang", "Kuala Nerus"),
        Zone("TRG02", "TRG", "Besut", "Setiu"),
        Zone("TRG03", "TRG", "Hulu Terengganu"),
        Zone("TRG04", "TRG", "Dungun", "Kemaman"),

        new("WLY01", ["KUL", "PJY"], ["Kuala Lumpur", "Putrajaya"]),
        new("WLY02", ["LBN"], ["Labuan"]),
    ];

    private static readonly Dictionary<string, PrayerZone> ByCode =
        All.ToDictionary(z => z.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>The zone for a code in any letter case, or null.</summary>
    public static PrayerZone? Find(string? code) =>
        code is not null && ByCode.TryGetValue(code.Trim(), out var zone) ? zone : null;

    public static bool IsValidCode(string? code) => Find(code) is not null;

    /// <summary>Zones of one state in code order, for the state then zone picker.</summary>
    public static IReadOnlyList<PrayerZone> ForState(string? stateCode) =>
        MalaysiaStates.Find(stateCode) is { } state
            ? All.Where(z => z.StateCodes.Contains(state.Code, StringComparer.Ordinal)).ToList()
            : [];

    private static PrayerZone Zone(string code, string stateCode, params string[] areas) =>
        new(code, [stateCode], areas);
}
