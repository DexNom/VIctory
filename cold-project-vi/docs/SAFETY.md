# Is Julia's Launcher safe?

Yes. And you do not have to take our word for it: everything below can be checked by anyone.

## Why Windows warns you

- **SmartScreen ("Windows protected your PC")** shows for programs that are new and not *code-signed*. A code-signing certificate costs money every year, and a free open source project usually starts without one. The warning says "unknown publisher", not "virus". Press **More info > Run anyway**.
- **Defender or another antivirus** sometimes flags a new launcher by its *behaviour*, not by a known virus. Launchers before 0.3.6 ran hidden PowerShell commands to set the page file, and added a Windows Defender exception for building the engine. That is also what malware does, so antivirus programs got suspicious. **Since 0.3.6 the launcher does neither**: it sets power and virtual memory itself, and it never changes antivirus settings.

## Check it yourself

1. **The code is public.** Everything the launcher and the engine do is in this repository: [`launcher/`](../launcher) (C#, one folder) and [`crates/`](../crates) (Rust).
2. **GitHub builds the files, not a person's PC.** Every release is built by the [Release workflow](../.github/workflows/release.yml) on GitHub's servers, from the commit the release names. The build log is public on the [Actions page](https://github.com/felicienradio/cold-project-vi/actions/workflows/release.yml).
3. **Each file has a provenance attestation.** It proves that the file came from that workflow run in this repository. With the [GitHub CLI](https://cli.github.com):
   ```
   gh attestation verify "Julia's Launcher.exe" -R felicienradio/cold-project-vi
   gh attestation verify victory.exe -R felicienradio/cold-project-vi
   ```
4. **Compare the SHA-256.** Each release page has `SHA256SUMS.txt`. In PowerShell:
   ```
   Get-FileHash "Julia's Launcher.exe" -Algorithm SHA256
   ```
   The hash must match the line for that file.
5. **Scan it.** Upload the file to [VirusTotal](https://www.virustotal.com) and look at the results.
6. **Updates are signed.** The launcher installs an update only when its list of files is signed by the release key and every file's SHA-256 matches ([`launcher/Updater.cs`](../launcher/Updater.cs)). A file changed on the way is refused.

## What the launcher changes on your PC, all of it

- **Its own folder** and `%LOCALAPPDATA%\VIctory` (settings, shader cache, logs, saves).
- **Windows power mode** set to Best performance (`powercfg`), when you press Set it or run the installer.
- **Virtual memory** set to "managed by Windows" (only if you agree; Windows asks for administrator rights).
- **Graphics preference** of `victory.exe` set to the high-performance card (Windows > Graphics settings).
- **NVIDIA driver profile** of `victory.exe`: Smooth Motion and Resizable BAR (only on NVIDIA cards).

Nothing else: no services, no start-up entries, no browser changes, no antivirus changes.

## What it sends, and only if you say yes

- **Hardware sharing:** graphics card, processor, RAM, motherboard model, Windows version, disk type, laptop or not, and your VI ID. At most once a day.
- **Game logs:** when the game closes, a summary and the end of that game's log, with your user name taken out of every path.
- **Send errors:** only when you press the button.

Your IP address, your files and your saves are never sent. Both switches are on Hardware & ID.

## Still flagged?

Send the file to Microsoft as a false positive: [microsoft.com/wdsi/filesubmission](https://www.microsoft.com/wdsi/filesubmission) (choose "Incorrectly detected as malware"). It usually clears within a few days for everyone. Tell us on Discord which antivirus flagged it, and which file.

Fun fact: the deadly virus was removed from the game in 0.3.6. Like Herobrine, it was never there.
