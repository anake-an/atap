### Added

- Work offline, on the Privacy page. Atap stops using the internet straight away and keeps showing what it saved last time. Checking for updates is not affected.
- Saved data, on the Privacy page. It shows how much space Atap's downloads take, and clears them with one button.
- Use the Wi-Fi name, under More options on the Privacy page. It gets Atap ready for rules for each Wi-Fi network. It is off until you turn it on, and the name stays on your PC.
- Parts of the notch that go online keep a saved copy, so they still show something when you are offline or a service is down.

### Changed

- A restart no longer downloads again what Atap fetched a moment ago.
- Atap is gentle with online services. It waits longer after each failure, waits when a service asks it to, spaces out its requests and never asks for the same thing twice at once.

### Security

- Plain http only works for addresses on your own network, such as a Home Assistant box. The address is checked after the name is looked up.
- Redirects to another site are refused, so your headers and keys never follow a link somewhere else.
- Saved copies and logs never hold the web address. Files are named by a hash, and logs show only the site and the status.
