# Security policy

## Supported versions

Only the latest release gets security fixes. Please update before you report a problem.

| Version | Supported |
| --- | --- |
| Latest release | Yes |
| Older releases | No |

## Reporting a vulnerability

Report it privately through GitHub Security Advisories:

1. Open the **Security** tab of [github.com/anake-an/atap](https://github.com/anake-an/atap/security).
2. Choose **Report a vulnerability**.
3. Fill in the form.

Do not report vulnerabilities in public issues, pull requests or discussions.

Please include:

- What you found and where.
- Steps to reproduce it.
- The Atap version and Windows version.
- The impact, as far as you can tell.

## What to expect

Atap has one maintainer, so these are goals and not guarantees.

- I aim to reply within 7 days.
- I aim to confirm whether the report is valid within 14 days.
- For a valid report, I aim to ship a fix in a new release within 90 days, and sooner for serious problems.
- The advisory credits you unless you prefer not to be named.

Please keep the details private until a fix is released. If a report needs more time, I will tell you why.

## Scope

Atap has no account system and no telemetry. [PRIVACY.md](PRIVACY.md) lists every host it talks to. All of these are in scope:

- The update path through GitHub Releases.
- The HTTP client: the host allow list, redirects, the size and time limits, and the download cache.
- The local API: the named pipe, `atap push` and the `atap` command-line tool.
- Secrets saved with Windows data protection.
- settings.json, layout.json and widget data files, including files edited by hand.
- The way Atap reads media, battery, Wi-Fi and location information from Windows.
- Sound files and anything else Atap opens from a path you give it.

## Not in scope

- Problems that need an attacker who already runs code as you on your PC, unless it lets them do more than your account already can, such as reaching Atap from the network or from another account.
- Missing code signing. Releases are not signed yet.
- Findings in packages Atap uses, unless Atap uses them in a way that makes the problem reachable. Report those to the package.
