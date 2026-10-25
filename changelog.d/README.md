# Changelog fragments

Each branch adds its changelog entries here, in its own file. Branches then never edit `CHANGELOG.md` at the same time, so they do not conflict.

## Adding a fragment

1. Create `changelog.d/<branch>.md`. Use the branch name without its prefix, for example `spine.md` for `feat/spine`.
2. Use the headings from [Keep a Changelog](https://keepachangelog.com/en/1.1.0/): Added, Changed, Deprecated, Removed, Fixed and Security. Leave out the ones you do not need.
3. Write one line per change, for users, not for developers.
4. Commit the fragment with your change.

Example:

```markdown
### Added

- Weather widget with the forecast from MET Malaysia.

### Fixed

- The clock shows the right time after the PC wakes from sleep.
```

## Writing rules

- Plain, short sentences in British English.
- No em dashes and no semicolons.
- Say what changed for the user. Leave out class names unless the entry is for contributors.

## Before a release

The maintainer folds every fragment into the Unreleased section of `CHANGELOG.md`, merges entries under the same headings and deletes the fragments. This README stays.
