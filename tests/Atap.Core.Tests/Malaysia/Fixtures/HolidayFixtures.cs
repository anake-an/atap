namespace Atap.Core.Tests.Malaysia;

/// <summary>
/// Holiday files in the bundled schema 1 format. There is no live source to record, so these
/// were written for the tests. They are not a data source for the app.
/// </summary>
internal static class HolidayFixtures
{
    public const string Year2026 = """
        {
          "schema": 1,
          "year": 2026,
          "holidays": [
            { "date": "2026-01-01", "name": { "en": "New Year's Day", "ms": "Tahun Baharu" }, "states": ["MLK", "NSN", "PHG", "PRK", "PNG", "SBH", "SWK", "SGR", "KUL", "LBN", "PJY"] },
            { "date": "2026-02-01", "name": { "en": "Thaipusam", "ms": "Thaipusam" }, "states": ["JHR", "NSN", "PNG", "PRK", "SGR", "KUL", "PJY"] },
            { "date": "2026-02-01", "name": { "en": "Federal Territory Day", "ms": "Hari Wilayah Persekutuan" }, "states": ["KUL", "LBN", "PJY"] },
            { "date": "2026-02-17", "name": { "en": "Chinese New Year", "ms": "Tahun Baharu Cina" } },
            { "date": "2026-02-18", "name": { "en": "Chinese New Year (second day)", "ms": "Tahun Baharu Cina (hari kedua)" }, "states": ["JHR", "KDH", "MLK", "NSN", "PHG", "PRK", "PLS", "PNG", "SBH", "SWK", "SGR", "KUL", "LBN", "PJY"] },
            { "date": "2026-03-21", "name": { "en": "Hari Raya Aidilfitri", "ms": "Hari Raya Aidilfitri" }, "tentative": true },
            { "date": "2026-03-22", "name": { "en": "Hari Raya Aidilfitri (second day)", "ms": "Hari Raya Aidilfitri (hari kedua)" }, "tentative": true },
            { "date": "2026-05-01", "name": { "en": "Labour Day", "ms": "Hari Pekerja" }, "tentative": false },
            { "date": "2026-08-31", "name": { "en": "National Day", "ms": "Hari Kebangsaan" } },
            { "date": "2026-09-16", "name": { "en": "Malaysia Day", "ms": "Hari Malaysia" } },
            { "date": "2026-12-25", "name": { "en": "Christmas Day", "ms": "Hari Krismas" } },
            { "date": "2026-11-08", "name": { "en": "Deepavali", "ms": "Deepavali" }, "states": ["JHR", "KDH", "KTN", "MLK", "NSN", "PHG", "PRK", "PLS", "PNG", "SBH", "SGR", "TRG", "KUL", "LBN", "PJY"] }
          ]
        }
        """;

    public const string Year2027 = """
        {
          "schema": 1,
          "year": 2027,
          "holidays": [
            { "date": "2027-01-01", "name": { "en": "New Year's Day", "ms": "Tahun Baharu" }, "states": ["MLK", "NSN", "PHG", "PRK", "PNG", "SBH", "SWK", "SGR", "KUL", "LBN", "PJY"] },
            { "date": "2027-02-06", "name": { "en": "Chinese New Year", "ms": "Tahun Baharu Cina" } }
          ]
        }
        """;

    public const string Year2028 = """
        {
          "schema": 1,
          "year": 2028,
          "holidays": [
            { "date": "2028-01-26", "name": { "en": "Chinese New Year", "ms": "Tahun Baharu Cina" } }
          ]
        }
        """;
}
