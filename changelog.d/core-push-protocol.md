### Added

- Core protocol for the local API. It reads and writes the `ping`, `push`, `end` and `alert` messages. Bad input gives an error code and never a crash.
- Text in pushes and alerts is cleaned before it is shown. Control characters and bidi controls are removed. Line breaks and tabs become single spaces.
- Push limits. An id has 1 to 32 lower-case letters, digits or hyphens. A title has at most 48 characters and a subtitle at most 64. Progress runs from 0 to 100. A push lasts from 10 seconds to 24 hours, and 15 minutes by default. A message is at most 8 KB. Icons and accents come from fixed lists.
- Live activity list for pushes. It holds at most 5 activities. A push with a known id updates that activity. Each activity is removed when its time runs out.
