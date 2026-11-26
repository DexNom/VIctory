# Starts the title with the settings this build is played with on a 16 GB PC:
# Ryzen 5 5600G, 16 GB, GeForce RTX 3060 Ti 8 GB, Windows 10, 720p, Series S.
#
#   scripts\play-16gb.ps1 -Exe <path of your own copy's executable>
#   scripts\play-16gb.ps1 -Exe <...> -Build release
#   scripts\play-16gb.ps1 -Exe <...> -More @{VICTORY_PANEL='1'}
#
# The profile lives in scripts\profile-16gb.ps1: prepare-16gb.ps1 compiles the
# pipelines with the same one.
param(
    [Parameter(Mandatory)][string]$Exe,
    [ValidateSet('diag', 'release')][string]$Build = 'diag',
    [hashtable]$More = @{}
)
$settings = & (Join-Path $PSScriptRoot 'profile-16gb.ps1') -More $More
$victory = Join-Path $PSScriptRoot "..\target\$Build\victory.exe"
if (-not (Test-Path $victory)) { Write-Host "No $victory. Build it first (README.md)."; exit 1 }
if (-not (Test-Path $Exe)) { Write-Host "No $Exe."; exit 1 }
# (Other settings left over in this window go; where your files are stays.)
$paths = 'VICTORY_CONFIG', 'VICTORY_GAME_DIR', 'VICTORY_CACHE_DIR', 'VICTORY_DATA_DIR'
$inherited = @(Get-ChildItem Env:VICTORY_* | Where-Object { $_.Name -notin $paths })
foreach ($entry in $inherited) { Remove-Item "Env:$($entry.Name)" }
foreach ($k in $settings.Keys) { Set-Item "Env:$k" $settings[$k] }
$Exe = (Resolve-Path $Exe).Path
Push-Location (Join-Path $PSScriptRoot '..')
try { & $victory run $Exe --execute --console series-s } finally { Pop-Location }
