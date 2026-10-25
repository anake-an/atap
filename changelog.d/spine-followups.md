### Changed

- The curved shoulders where the notch meets the screen edge are larger, to match the website.
- The notch grows and shrinks with a softer spring. Its content fades in just after the shape starts to change, instead of popping in.
- In the top left and top right positions, the notch sits a little further from the side of the screen, so its larger shoulders stay on the screen.

### Fixed

- Repeating updates keep their normal pace after the Windows clock is set back. Before, they could wait as long as the clock moved back.
- Atap now notices when the Windows clock changes and when the PC wakes from sleep.
- A widget that takes too long to start is told to stop at once, instead of running on in the background for up to 10 more seconds.
- A stopped widget no longer keeps being told about command changes, and its memory is freed.
- Turning a feature off while it is still starting no longer breaks it. It stops once it has started.
- A settings file written by a newer Atap, for example one synced from another PC, is no longer downgraded when you change a setting.
- Uninstalling now also takes atap out of PATH when the PATH entry was written with a variable such as %LOCALAPPDATA%.
