# Builds a release on this PC and publishes it, signed, on felicienradio/cold-project-vi (no GitHub Actions):
#   scripts\release-local.ps1 v0.3.0            (asks before publishing)
#   scripts\release-local.ps1 v0.3.0 -Yes       (publishes without asking)
#   scripts\release-local.ps1 v0.3.0 -NoBuild   (reuses target\diag\victory.exe)
# Needs: Rust, the GitHub CLI logged in (gh auth login), Inno Setup 6, and the release key made by
# scripts\new-release-key.ps1 (%USERPROFILE%\.vi-release\release-key.xml).
# The tag must equal Updater.Version in launcher\Updater.cs. Commit and push your changes first:
# the tag is put on the current commit.
param([Parameter(Mandatory = $true)][string]$Tag, [switch]$Yes, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$repo = 'felicienradio/cold-project-vi'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$key = Join-Path $env:USERPROFILE '.vi-release\release-key.xml'
if (-not (Test-Path $key)) { throw "No release key at $key (scripts\new-release-key.ps1 makes it)." }

$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = Join-Path $env:ProgramFiles 'GitHub CLI\gh.exe' }
if (-not (Test-Path $gh)) { throw 'The GitHub CLI was not found: winget install GitHub.cli, then gh auth login.' }
$iscc = @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'), (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 was not found: winget install JRSoftware.InnoSetup.' }

$version = (Select-String -Path (Join-Path $root 'launcher\Updater.cs') -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
if ("v$version" -ne $Tag) { throw "Updater.Version ($version) is not the tag ($Tag)." }

Push-Location $root
try {
    if (git status --porcelain) { throw 'There are uncommitted changes: commit and push them first.' }
    $dist = Join-Path $root 'dist'
    Remove-Item -Recurse -Force $dist -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $dist | Out-Null
    if (Get-ChildItem $dist) { throw "dist could not be emptied (a file in it is open): close it and try again." }

    if (-not $NoBuild) {
        Write-Host 'Building victory.exe...'
        cargo build --locked --profile diag -p victory --features diagnostics
        if ($LASTEXITCODE -ne 0) { throw 'cargo build failed.' }
    }
    Copy-Item 'target\diag\victory.exe' $dist

    Write-Host 'Building the launcher...'
    cmd /c "`"$root\launcher\build-launcher.cmd`" `"$dist\Julia's Launcher.exe`""
    if ($LASTEXITCODE -ne 0) { throw 'The launcher did not build.' }

    Compress-Archive -Path 'launcher\*.cs', 'launcher\build-launcher.cmd', 'launcher\icon.ico' -DestinationPath (Join-Path $dist 'launcher-source.zip')
    Get-ChildItem $dist -File | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name } | Set-Content (Join-Path $dist 'SHA256SUMS.txt')

    Write-Host 'Building the installer...'
    & $iscc /Q "/DAppVersion=$($version -replace '-.*', '')" 'installer\VI.iss'
    if ($LASTEXITCODE -ne 0) { throw 'The installer did not build.' }

    # What the launchers download (Updater.Allowed): name beside the launcher <- file of the release.
    Copy-Item (Join-Path $dist "Julia's Launcher.exe") (Join-Path $dist 'Julias-Launcher.exe')
    $map = [ordered]@{ 'Julias-Launcher.exe' = "Julia's Launcher.exe"; 'victory.exe' = 'victory.exe' }
    $files = foreach ($name in $map.Keys) {
        $f = Get-Item (Join-Path $dist $name)
        [ordered]@{
            name   = $map[$name]
            url    = "https://github.com/$repo/releases/download/$Tag/$name"
            sha256 = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLower()
            size   = $f.Length
        }
    }
    # ReadAllText, not Get-Content: Windows PowerShell's Get-Content adds PSPath and other fields that ConvertTo-Json writes out.
    $notes = [IO.File]::ReadAllText((Join-Path $root 'docs\RELEASE-NOTES.md'))
    $notesPt = if (Test-Path 'docs\RELEASE-NOTES.pt.md') { [IO.File]::ReadAllText((Join-Path $root 'docs\RELEASE-NOTES.pt.md')) } else { '' }
    $manifest = [ordered]@{ version = $version; notes = $notes; notes_pt = $notesPt; files = @($files) } | ConvertTo-Json -Depth 4
    $manifestPath = Join-Path $dist 'manifest.json'
    [IO.File]::WriteAllText($manifestPath, $manifest, (New-Object Text.UTF8Encoding $false))
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
    $rsa.FromXmlString([IO.File]::ReadAllText($key))
    $sig = [Convert]::ToBase64String($rsa.SignData([IO.File]::ReadAllBytes($manifestPath), 'SHA256'))
    [IO.File]::WriteAllText((Join-Path $dist 'manifest.sig'), $sig)
    Write-Host $manifest

    if (-not $Yes) {
        $answer = Read-Host "Publish $Tag on $repo (every launcher installs it within 5 minutes)? [y/N]"
        if ($answer -notmatch '^(y|yes|o|oui|s|sim)$') { Write-Host "Not published. The files are in $dist."; exit 1 }
    }

    git tag -f -a $Tag -m "Julia's Launcher $Tag"
    git push --force "https://github.com/$repo.git" "refs/tags/$Tag"
    if ($LASTEXITCODE -ne 0) { throw 'The tag could not be pushed.' }
    $upload = @('Julias-Launcher.exe', 'victory.exe', 'launcher-source.zip', 'SHA256SUMS.txt', 'manifest.json', 'manifest.sig') | ForEach-Object { Join-Path $dist $_ }
    $upload += (Get-ChildItem $dist -Filter 'VI-Setup-*.exe').FullName
    & $gh release create $Tag -R $repo --verify-tag --latest --title "Julia's Launcher $Tag" --notes-file 'docs\RELEASE-NOTES.md' @upload
    if ($LASTEXITCODE -ne 0) { throw 'The release could not be made.' }
    Write-Host "Published $Tag : https://github.com/$repo/releases/tag/$Tag"
}
finally { Pop-Location }
