# Signs and publishes a draft release that .github\workflows\release.yml built (run on edupode's PC):
#   scripts\sign-release.ps1 v0.2.1
# 1. downloads the draft's programs and checks each one's provenance (built by this repository's workflow);
# 2. writes manifest.json (version, notes, each file with its SHA-256) and manifest.sig (RSA-SHA256 with
#    the private key from scripts\new-release-key.ps1, %USERPROFILE%\.vi-release\release-key.xml);
# 3. uploads both and publishes the release. Every launcher then installs it within 5 minutes.
param([Parameter(Mandatory = $true)][string]$Tag, [switch]$Yes, [switch]$NoAnnounce)
$ErrorActionPreference = 'Stop'
$repo = 'edupode/cold-project-vi'
$key = Join-Path $env:USERPROFILE '.vi-release\release-key.xml'
if (-not (Test-Path $key)) { throw "No release key at $key (scripts\new-release-key.ps1 makes it)." }
$work = Join-Path $env:TEMP "vi-release-$Tag"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory $work | Out-Null
gh release download $Tag -R $repo -D $work -p 'Julias-Launcher.exe' -p 'victory.exe' -p 'nvsmooth30-version.dll'
foreach ($f in Get-ChildItem $work -File) {
    gh attestation verify $f.FullName -R $repo | Out-Null
    if ($LASTEXITCODE -eq 0) { continue }
    # The experimental RTX 30 add-on may be left out of an update; the launcher and VIctory may not.
    if ($f.Name -like 'nvsmooth30-*') { Write-Warning "$($f.Name): no provenance; left out of this update."; Remove-Item $f.FullName; continue }
    throw "$($f.Name): no provenance from $repo's workflow; not signing."
}
# Where each download goes beside the launcher (Updater.Allowed).
$map = @{ 'Julias-Launcher.exe' = "Julia's Launcher.exe"; 'victory.exe' = 'victory.exe'; 'nvsmooth30-version.dll' = 'tools/nvsmooth30/version.dll' }
$files = foreach ($f in Get-ChildItem $work -File) {
    [ordered]@{
        name   = $map[$f.Name]
        url    = "https://github.com/$repo/releases/download/$Tag/$($f.Name)"
        sha256 = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLower()
        size   = $f.Length
    }
}
# Plain text only ([IO.File]::ReadAllText): Get-Content's strings carry PSPath, PSDrive... and ConvertTo-Json would publish them.
$notes = if (Test-Path "$PSScriptRoot\..\docs\RELEASE-NOTES.md") { [IO.File]::ReadAllText("$PSScriptRoot\..\docs\RELEASE-NOTES.md") } else { '' }
$notesPt = if (Test-Path "$PSScriptRoot\..\docs\RELEASE-NOTES.pt.md") { [IO.File]::ReadAllText("$PSScriptRoot\..\docs\RELEASE-NOTES.pt.md") } else { '' }
$manifest = [ordered]@{ version = $Tag.TrimStart('v'); notes = $notes; notes_pt = $notesPt; files = @($files) } | ConvertTo-Json -Depth 4
$manifestPath = Join-Path $work 'manifest.json'
[IO.File]::WriteAllText($manifestPath, $manifest, (New-Object Text.UTF8Encoding $false))
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
$rsa.FromXmlString([IO.File]::ReadAllText($key))
$sig = [Convert]::ToBase64String($rsa.SignData([IO.File]::ReadAllBytes($manifestPath), 'SHA256'))
[IO.File]::WriteAllText((Join-Path $work 'manifest.sig'), $sig)
Write-Host $manifest
if (-not $Yes) {
    $answer = Read-Host "Publish $Tag to everyone (they install it within 5 minutes)? [y/N]"
    if ($answer -notmatch '^(y|yes|s|sim)$') { Write-Host 'Not published.'; exit 1 }
}
gh release upload $Tag -R $repo --clobber $manifestPath (Join-Path $work 'manifest.sig')
gh release edit $Tag -R $repo --draft=false --latest
Write-Host "Published $Tag."
# The announcement (docs\ANNOUNCEMENT.md) in the Discord channel, through the webhook kept beside the key.
if (-not $NoAnnounce) { & (Join-Path $PSScriptRoot 'post-announcement.ps1') -Tag $Tag }
