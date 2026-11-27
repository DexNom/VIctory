# Bug reports players sent from the launcher ("Send to edupode"), read from the Cloudflare D1 database with
# your own wrangler login. Nobody else can read them: the Worker has no route that gives them out, and the
# database answers only your Cloudflare account (npx wrangler login, once).
#   scripts\get-reports.ps1                     lists the latest 30
#   scripts\get-reports.ps1 -Id VI-MFYC-RFA8    lists that player's reports and saves them all
#   scripts\get-reports.ps1 -Get 12             saves report 12
#   scripts\get-reports.ps1 -Delete 12          deletes report 12
# Saved reports go to reports\ in the repository (ignored by git) and the folder opens.
param([int]$Get = 0, [int]$Delete = 0, [int]$Last = 30, [string]$Id = '')
$ErrorActionPreference = 'Stop'
$worker = Join-Path $PSScriptRoot '..\server\hardware-worker'
$dir = Join-Path $PSScriptRoot '..\reports'

function D1([string]$sql) {
    Push-Location $worker
    try { $out = npx --yes wrangler d1 execute vi-hardware --remote --json --command $sql 2>$null | Out-String }
    finally { Pop-Location }
    $at = $out.IndexOf('[')
    if ($at -lt 0) { throw "wrangler gave no answer (logged in? npx wrangler login):`n$out" }
    return (ConvertFrom-Json $out.Substring($at))[0].results
}

function Save([int]$n) {
    $row = D1 "SELECT n, vi_id, kind, hex(zip) AS z FROM reports WHERE n = $n"
    if (-not $row) { throw "No report $n." }
    $hex = $row[0].z
    $bytes = New-Object byte[] ($hex.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = [Convert]::ToByte($hex.Substring($i * 2, 2), 16) }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $file = Join-Path $dir ("report-{0}-{1}-{2}.zip" -f $row[0].n, $row[0].vi_id, $row[0].kind)
    [IO.File]::WriteAllBytes($file, $bytes)
    "Saved $file"
    return $file
}

if ($Delete -gt 0) { D1 "DELETE FROM reports WHERE n = $Delete" | Out-Null; "Report $Delete deleted."; return }
if ($Get -gt 0) { $f = Save $Get; Start-Process explorer.exe "/select,`"$((Resolve-Path $f[-1]).Path)`""; return }
if ($Id) {
    $Id = $Id.Trim().ToUpper()
    if ($Id -notmatch '^VI-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$') { throw "Not a launcher ID: $Id (VI-XXXX-XXXX)" }
    $rows = D1 "SELECT n, created, vi_id, kind, bytes FROM reports WHERE vi_id = '$Id' ORDER BY n DESC"
    if (-not $rows) { "No reports from $Id."; return }
    $rows | Format-Table -AutoSize
    foreach ($r in $rows) { Save $r.n | Out-Null }
    Start-Process explorer.exe (Resolve-Path $dir).Path
    return
}
D1 "SELECT n, created, vi_id, kind, bytes FROM reports ORDER BY n DESC LIMIT $Last" | Format-Table -AutoSize
