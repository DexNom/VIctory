# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

> Status: applied for. Until the certificate is granted, releases are not yet Authenticode-signed; every
> update is still signed with the release key the launcher checks ([`launcher/Updater.cs`](../launcher/Updater.cs)).

## What is signed

The programs this repository builds, and only those:

- `Julia's Launcher.exe` (the launcher, [`launcher/`](../launcher), C#, GPL-3.0)
- `victory.exe` (the VIctory runtime, [`crates/`](../crates) and [`apps/`](../apps), Rust, GPL-3.0)
- `VI-Setup-<version>.exe` (the installer, [`installer/VI.iss`](../installer/VI.iss), Inno Setup)

Every one of them is built from this repository's source by GitHub Actions
([`.github/workflows/release.yml`](../.github/workflows/release.yml)) when a version tag is pushed, never on
a person's PC. Each file gets a GitHub provenance attestation and a SHA-256 in `SHA256SUMS.txt`. Third-party
files that the release carries (`nvsmooth30-version.dll`, MIT, built from
[ItsAdeline/NVSmooth30](https://github.com/ItsAdeline/NVSmooth30)) are not signed by us. The game is not part
of this project: players use their own dump.

## Team and roles

| Role | Who | What they do |
|---|---|---|
| Committers and reviewers | [felicienradio](https://github.com/felicienradio), [edupode](https://github.com/edupode) | Write and review the code. Changes from anyone else come as pull requests that one of them reviews before merging. |
| Approvers | [edupode](https://github.com/edupode), [felicienradio](https://github.com/felicienradio) | Approve each signing request in SignPath, for a release built from a version tag on `main`. |

Repository permissions: [collaborators of felicienradio/cold-project-vi](https://github.com/felicienradio/cold-project-vi/graphs/contributors).
Every team member uses multi-factor authentication on GitHub and SignPath.

## Privacy

This program will not transfer any information to other networked systems unless specifically requested by
the user or the person installing or operating it. In detail:

- **Updates:** the launcher asks GitHub for the newest release (public release list, no personal data).
- **Hardware sharing** (off until the player turns it on): graphics card, processor, RAM, motherboard model,
  Windows version, disk type, laptop or not, and a hardware ID. At most once a day.
- **Game logs** (asked once, off unless the player says yes): when the game closes, a summary and the end of
  that game's log, with the user name taken out of every path.
- **Send errors:** only when the player presses the button.

All three go to a Cloudflare Worker ([`server/hardware-worker`](../server/hardware-worker)) that only takes data
in; only the maintainers can read it. IP addresses are never stored. Both switches are on the launcher's
Hardware & ID page.

## System changes

The installer lists the Windows settings it can change and applies them only if that box is ticked: Best
performance power mode, virtual memory managed by Windows, the high-performance graphics card for VIctory, and
the NVIDIA driver profile (Smooth Motion, Resizable BAR) on NVIDIA cards. The launcher changes them only when the
player presses the button for each. Nothing changes antivirus settings. Uninstall from Windows' Apps & features.
