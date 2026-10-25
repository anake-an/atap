### Added

- Groundwork for synced lyrics. Atap can read LRC lyrics with offsets, word timings and several timestamps on one line. It finds the current and next line from the playback position, so the widget needs one timer per line and no polling.
- Romanisation for lyric lines. Korean Hangul follows the Revised Romanization of Korean, including sound changes between syllables. Japanese kana follow a Hepburn table. Kanji stay as they are.
- Request and response models for lyrics lookups on LRCLIB, with a pick of the best search result. It prefers the same artist and a matching song length. Synced lyrics lead only when the length matches. A song length of 0 counts as unknown.
