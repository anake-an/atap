### Added

- Core logic for the JSON URL, script and Home Assistant widgets. Nothing shows in the app yet.
- Field paths for custom widgets: `$`, `.name`, `['name']`, `[n]`, `[*]` and `length()`. A negative index counts from the end. Any other path syntax gets a clear error that says what is wrong and where.
- Value templates for custom widgets: Number, Text, Progress, List and Status dot.
- Warning and critical thresholds with a hysteresis band, so a value close to a threshold does not flicker between levels. Each threshold gets a band sized to itself. A threshold of 0 needs its band set by hand. The critical band never reaches below the warning threshold.
- Each item in a List keeps its own band between refreshes, even when items have no labels or share a label.
- A short gap with no value, such as a sensor that is briefly unavailable, does not reset the band.
- An approval for script widgets that only works on the PC where it was given. Any change to the program, an argument or the working folder needs approval again.
- Script settings with hidden or blank-looking characters are refused. This covers text-direction marks, zero-width characters, Hangul fillers, line separators and spaces other than the normal one.
- Script paths with a folder or file name that ends in a space or dot are refused. Windows would run a different path than the one shown.
- A script that breaks these rules never counts as approved.
- MQTT topic matching and value mapping for plain and JSON payloads. An MQTT payload of "unavailable" or "unknown" from Home Assistant shows as no value.
- A value template can also treat words such as "unavailable" in a JSON field as no value. The Home Assistant REST API sends its state this way.
- Custom JSON is read up to 256 KB and 32 levels deep.
