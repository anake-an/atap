### Added

- Rules for automatic profiles, kept under `rules` in `settings.json`. A rule can match the app in front, a time window on chosen days, the exact Wi-Fi name or the power state. Time windows can cross midnight. Power can be on battery, plugged in, Windows energy saver or battery below a level.
- A matching rule can switch the profile, hide the notch, turn on slim mode or quiet alerts. The first matching rule with a profile picks it. Without one, the profile picked by hand is used, then the Default profile. Hide, slim and quiet from every matching rule are combined.
- A rule never applies if it names a missing profile, has no conditions or uses a condition this version cannot read. Unknown fields and condition kinds stay in the file when it is saved.
- Battery saver modes: on battery (the default), always, never, below a battery level while on battery, or follow Windows energy saver.
