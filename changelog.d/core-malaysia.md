### Added

- Malaysia data logic in the core library, ready for the prayer time, Hijri, weather, rain alert and holiday widgets. Nothing new shows in the app yet.
- Readers for e-Solat and its mirror at api.waktusolat.app. A status other than "OK!", a changed format, or times for another zone or month are refused. A widget can then fall back to the mirror and then to its cache.
- Prayer schedule with the next and current prayer, across midnight and the end of the month. A schedule without today's times counts as out of date.
- All 60 JAKIM zones and the 16 states and federal territories. District centres match a location to its zone on the PC, and nothing is sent anywhere.
- Prayer times calculated on the PC for use outside Malaysia, marked as approximate. In tests they come within 2 minutes of 67 recorded e-Solat days for SGR01 and WLY01.
- Hijri dates from e-Solat with Malaysian month names in English and Bahasa Melayu. A fallback date takes an offset of up to 2 days either way.
- Reader for the bundled public holiday files, with state filters and tentative dates. It says clearly when the data runs out.
- Readers for the data.gov.my forecast and the MET Malaysia warnings, including the states each land warning names. A warning that cannot be read is skipped, so the others still show.
