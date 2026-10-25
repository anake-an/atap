# Releasing Atap

Atap uses [Semantic Versioning](https://semver.org/). The version comes from git tags through MinVer. Tags use the prefix `v`, for example `v0.1.0`.

The release workflow is `.github/workflows/release.yml`. It runs when a tag that matches `v*.*.*` is pushed. It then checks the tag against strict SemVer: `vX.Y.Z` or `vX.Y.Z-prerelease`, with no leading zeros. Any other tag fails. It also fails when the tagged commit is not on `main`.

## Steps

1. Check that `main` is green on CI.
2. Fold every fragment in `changelog.d/` into the Unreleased section of `CHANGELOG.md`, as [changelog.d/README.md](../changelog.d/README.md) says, and delete the fragments. Then move the items under `## [Unreleased]` into a new section. Leave an empty Unreleased section. The heading must be exactly `## [X.Y.Z] - YYYY-MM-DD`: the version without the `v`, in square brackets, then a space, a dash, a space and the date. The workflow takes the release notes from that section. It fails when it cannot find the heading or the section is empty.
3. Update the compare links at the bottom of `CHANGELOG.md`.
4. Commit to `main`.

   ```
   git add CHANGELOG.md
   git commit -m "chore: release vX.Y.Z"
   ```

5. Tag the commit and push the tag.

   ```
   git tag vX.Y.Z
   git push origin main
   git push origin vX.Y.Z
   ```

6. The workflow then does this:
   1. Restores packages from nuget.org in locked mode. It uses no package cache, so nothing from other workflow runs ends up in the release.
   2. Builds and runs the tests.
   3. Publishes `win-x64` and `win-arm64` self-contained, each with the `atap` command-line tool in `cli\atap.exe`, built with NativeAOT. CI publishes the tool the same way on every pull request, so a missing C++ toolchain shows up there first.
   4. Signs Atap's own files, once signing is set up. See [Code signing](#code-signing).
   5. Runs `vpk download github` once per channel to fetch the previous release, so it can make delta updates.
   6. Runs `vpk pack` once per channel with pack id `Atap`, main exe `Atap.exe` and the icon `src/Atap.App/Assets/atap.ico`. The release notes are taken from that version's section in `CHANGELOG.md`.
   7. Signs the two installers, once signing is set up. Then it checks both installers, a full package per channel and both `atap.exe` are there, and stops before the draft if one is missing.
   8. Runs `vpk upload github` for the `win` channel, which creates a **draft** release. Then it runs it for `win-arm64` with `--merge`, which adds the Arm64 files to the same draft.
   9. For a stable version, builds the Microsoft Store bundle as a workflow artifact. See [Microsoft Store](#microsoft-store).
7. Check the draft (see below), then publish it. Copies of Atap from the installer or the portable zip update themselves from published releases.

## x64 and Arm64

Each architecture is a Velopack channel with its own release feed. A copy updates from the channel it was installed from.

| PC | Channel | Installer | Portable zip | Feed |
|---|---|---|---|---|
| x64 | `win` | `Atap-win-Setup.exe` | `Atap-win-Portable.zip` | `releases.win.json` |
| Arm64 | `win-arm64` | `Atap-win-arm64-Setup.exe` | `Atap-win-arm64-Portable.zip` | `releases.win-arm64.json` |

x64 stays on the default channel `win` for good. Another channel name would rename the installer and break the download link.

A copy installed by Scoop (anything under `scoop\apps\`, or under the folders in `SCOOP` and `SCOOP_GLOBAL`) never updates itself. Scoop owns those files, and Settings says to run `scoop update atap`.

Arm64 is built on the x64 runner, because the workflow does not use Arm runners. If you have an Arm PC, test the Arm64 installer on it before you publish. The first Arm64 release has no delta package, because there is no earlier Arm64 release.

## Prereleases

Add a suffix after a dash: `v1.0.0-preview.1`. The workflow marks any version containing `-` as a prerelease on GitHub. Prerelease sections in `CHANGELOG.md` use the same heading format, for example `## [0.1.0-preview.1] - YYYY-MM-DD`.

A copy that runs a prerelease checks prereleases too, so it moves to the next preview or to a newer stable release, whichever is newest. A copy that runs a stable release only gets stable releases.

## Check the draft release

- The tag and version are right.
- The notes match the changelog section.
- The assets are there for both channels: the installer, the portable zip, the full package, a delta package (not for the first release of a channel) and the release feed file. The `win` channel also has `RELEASES`.
- The prerelease box matches the version. It is ticked only when the version has a `-`.
- A fresh install of the installer runs, and the menu header shows the new version.
- The installer and the installed app show the Atap icon.
- The installer is named `Atap-win-Setup.exe`. Keep that name in every release. The website download button and the README link to `https://github.com/anake-an/atap/releases/latest/download/Atap-win-Setup.exe`, which always serves the newest published stable release. Drafts and prereleases are skipped.

## If the workflow fails

- If the fix needs a code change, delete the tag first, then commit the fix to `main` and tag again:

  ```
  git push --delete origin vX.Y.Z
  git tag -d vX.Y.Z
  ```

- If the run got as far as creating the draft release, delete that draft on the Releases page before you re-run or tag again. `vpk upload` stops when the tag already has a release. This is true even when only the `win-arm64` upload failed.
- A failed Store job does not stop the draft release. Fix the cause, then re-run that job.

## Hotfix flow

The workflow only releases commits that are on `main`, so a tag on a hotfix branch fails. A hotfix ships from `main` together with everything else merged since the last release.

1. Check what is on `main` since the last release: `git log vX.Y.Z..main`.
2. If any of it is not ready to ship, revert it on `main` first. Merge it again after the hotfix release.
3. Branch from `main`, fix the bug, add a CHANGELOG entry and open a pull request.
4. After the merge, follow the steps above with a patch version, for example `v0.1.1`.

## Microsoft Store

The Store build has no Velopack, because the Store handles updates. Build it with the MSBuild property `AtapChannel=Store`, which defines the `STORE` constant:

```
dotnet build -p:AtapChannel=Store
```

For each stable release, the `store` job of the release workflow does this:

1. Publishes the Store build for `win-x64` and `win-arm64`.
2. Runs `packaging/msix/build.ps1`. It adds `packaging/msix/AppxManifest.xml` and the logos from `assets/brand/store`. Then it packs one MSIX per architecture with MakeAppx from the Windows SDK and bundles them into `Atap_X.Y.Z.0_x64_arm64.msixbundle`.
3. Keeps the bundle as the workflow artifact `store-package` for 30 days. It is not attached to the GitHub release.

The package version is the release version with `.0` added. Prereleases get no Store package, because MSIX versions have no prerelease part. The bundle is not signed. The Store signs it when you submit it.

### Set up the Store listing once

The Store name is "Atap Notch", because "Atap" was taken. The manifest uses it as the display name. The app itself, `Atap.exe` and the Velopack pack id stay Atap.

1. The name "Atap Notch" is reserved in Partner Center.
2. The identity from Partner Center > Product management > Product identity is already in `packaging/msix/AppxManifest.xml`: name `Novafortress.AtapNotch`, publisher `CN=E5DA39E3-8B84-4AA4-A3C6-B4CFB4221E11`, publisher display name `Novafortress`. Store ID `9N8WMBX8188D`.
3. These values are public. If they ever change, change them in the manifest and in Partner Center together. The workflow still skips the Store package if a placeholder ever comes back.
4. On the first submission, Partner Center asks why Atap needs the `runFullTrust` capability. Say that Atap is a desktop app.

The package declares English and Bahasa Melayu, so Partner Center offers a Store listing in both.

### Each stable release

1. Open the release workflow run and download the `store-package` artifact. Unzip it.
2. In Partner Center, start a new submission and upload the `.msixbundle` under Packages.
3. Submit it.

The script can also run on your own PC with the Windows SDK installed. The comment at the top of `packaging/msix/build.ps1` shows how.

## Code signing

Releases are not signed yet. Windows SmartScreen warns about an unsigned installer until enough people have downloaded it.

The release workflow has signing steps for SignPath. They run only when the repository secret `SIGNPATH_API_TOKEN` exists. Without it they are skipped and the release is built as before.

Velopack needs signing at two points, so each release makes two signing requests:

1. Atap's own files for both runtimes, before `vpk pack`. These are `Atap.exe`, `Atap.dll` and `Atap.Core.dll`. The artifact configuration is `packaging/signpath/app-files.xml`.
2. The two installers, after `vpk pack`. The artifact configuration is `packaging/signpath/installers.xml`.

An approver accepts each request in SignPath. The workflow waits up to 20 minutes for each one.

### Apply to SignPath Foundation

SignPath Foundation signs open source projects for free. Apply after the repo goes public at v1.0.0, because the project must already be released. Its terms ask for this:

- An OSI-approved licence for all of the code, with no commercial dual licence. Atap is MIT.
- A released project, in the form that will be signed. v1.0.0 on GitHub Releases is that.
- Multi-factor authentication for every team member, on GitHub and on SignPath.
- Manual approval of every release before it is signed.
- A "Code signing policy" page on the website, linked from the home page and the download page. It says that signing is free, done by SignPath.io with a certificate from SignPath Foundation. It names the team roles (authors, reviewers and approvers) and who has them. It has a privacy statement.
- Only Atap's own files are signed. Packages from other authors stay as they ship them.
- The product name and version in the signed files are checked by SignPath. The product name is `Atap`.

Then set it up:

1. Apply on [signpath.org](https://signpath.org/apply).
2. Once the project is accepted, install the SignPath GitHub App on the repo. Link the GitHub trusted build system to the SignPath project.
3. Create the artifact configurations `app-files` and `installers` from the files in `packaging/signpath`. Ask SignPath whether the Velopack `Setup.exe` may be signed. If not, delete the three installer signing steps from the workflow.
4. Add the repository secret `SIGNPATH_API_TOKEN` with the API token of a SignPath CI user.
5. Add the repository variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG` and `SIGNPATH_SIGNING_POLICY_SLUG` with the values from SignPath.

The secret name `SIGNPATH_API_TOKEN` comes from the SignPath docs. The three variable names and the two artifact configuration slugs are placeholders. Change them in the workflow if SignPath gives other names.

### Paid fallback: Certum

If SignPath Foundation says no, buy a Certum Open Source Code Signing certificate. It is made for open source developers and has a yearly fee. Certum keeps the key on a smart card or in its cloud signing service, so the SignPath steps in the workflow would be replaced. `vpk pack` can sign with `signtool.exe` through `--signParams`.

## Repository settings for a public release

Check these before and after the repository goes public:

- Enable CodeQL code scanning.
- Add branch protection on `main`, with CI required.
- Enable private vulnerability reporting, so [SECURITY.md](../SECURITY.md) works as written.
- Enable GitHub Discussions, so [SUPPORT.md](../SUPPORT.md) works as written.
- Apply to SignPath Foundation for code signing. See [Code signing](#code-signing).
