# Posts a release's announcement in the Discord announcements channel through a webhook.
#   scripts\post-announcement.ps1 -Tag v0.3.2-edupode            posts docs\ANNOUNCEMENT.md
#   scripts\post-announcement.ps1 -Tag v0.3.2-edupode -DryRun    only prints it
# {tag} becomes the tag and {version} the installer's version (the tag without 'v' and '-edupode').
# The webhook URL is a secret (anyone holding it can post in the channel): it lives in
# %USERPROFILE%\.vi-release\discord-webhook.txt beside the release key, never in the repository.
# sign-release.ps1 calls this once a release is published.
param([Parameter(Mandatory = $true)][string]$Tag, [switch]$DryRun)
$ErrorActionPreference = 'Stop'
$file = Join-Path $PSScriptRoot '..\docs\ANNOUNCEMENT.md'
if (-not (Test-Path $file)) { throw "No announcement at $file." }
$version = $Tag.TrimStart('v') -replace '-.*$', ''
$text = [IO.File]::ReadAllText($file).Replace('{tag}', $Tag).Replace('{version}', $version).Trim()
# Every announcement ends with docs\ANNOUNCEMENT-FOOTER.md, before the @everyone line.
$footerFile = Join-Path $PSScriptRoot '..\docs\ANNOUNCEMENT-FOOTER.md'
if (Test-Path $footerFile) {
    $footer = [IO.File]::ReadAllText($footerFile).Trim()
    if ($text.EndsWith('@everyone')) { $text = $text.Substring(0, $text.Length - 9).TrimEnd() + "`n`n" + $footer + "`n`n@everyone" } else { $text = $text + "`n`n" + $footer }
}
if ($text.Length -gt 2000) { throw "The announcement is $($text.Length) characters; Discord takes 2000." }
if ($DryRun) { $text; "($($text.Length) characters)"; return }
$hookFile = Join-Path $env:USERPROFILE '.vi-release\discord-webhook.txt'
if (-not (Test-Path $hookFile)) { Write-Warning "No webhook at ${hookFile}: the announcement was not posted."; return }
$hook = [IO.File]::ReadAllText($hookFile).Trim()
if ($hook -notmatch '^https://(discord|discordapp)\.com/api/webhooks/\d+/[\w-]+$') { throw "${hookFile} does not hold a Discord webhook URL." }
$body = @{ content = $text; username = "Julia's Launcher"; allowed_mentions = @{ parse = @("everyone") } } | ConvertTo-Json -Depth 3
Invoke-RestMethod -Uri $hook -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) | Out-Null
"Announcement posted on Discord."
