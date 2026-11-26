# Starts the title with the settings this fork is played with on a 16 GB PC:
# Ryzen 5 5600G, 16 GB, GeForce RTX 3060 Ti 8 GB, Windows 10, 1080p, Series S.
#
#   scripts\play-16gb.ps1 -Exe <path of your own copy's executable>
#   scripts\play-16gb.ps1 -Exe <...> -Build release
#   scripts\play-16gb.ps1 -Exe <...> -More @{VICTORY_PANEL='1'}
#
# The 64 GB settings in play.ps1 do not fit here: the title takes 20 to 30 GB
# of private bytes, so on 16 GB every hundred megabytes the host keeps is a
# page of the title's own memory on the disk. What this profile changes:
#
#   - the pipeline library is off (VICTORY_PIPELINE_CACHE=0): its 80 to 110 MB
#     mapped file and its save buffer are not worth a frame of the load;
#   - the card's copies are capped at 2 GB for textures, targets and buffers,
#     so the driver does not spill 6 to 7 GB of them into the PC's memory,
#     which is where the shared-with-the-card budget goes on a 16 GB box;
#   - uploads ahead of the lists are off: they are a copy of every texture
#     the title fills, and the PC's memory is the scarce thing here;
#   - the pipeline workers are 6, one a core, so the title's own threads
#     keep their cores while the world loads.
param(
    [Parameter(Mandatory)][string]$Exe,
    [ValidateSet('diag', 'release')][string]$Build = 'diag',
    [hashtable]$More = @{}
)
$settings = @{
    # The fork's own faster ways, as play.ps1 sets them.
    VICTORY_TEXTURE_NO_WAIT = '1'; VICTORY_RELAY = '2'; VICTORY_WIDE_BACK = '1'; VICTORY_CARD_REHASH_RUNS = '1'
    VICTORY_JOIN_COPIES = '1'; VICTORY_INDIRECT_WINDOW = '1'; VICTORY_RELAY_COPY = '1'; VICTORY_CARD_STEPS = '2'
    VICTORY_INDIRECT_BATCH = '2'; VICTORY_MAP_THREADS = '1'; VICTORY_TITLE_PRIORITIES = '0'; VICTORY_CARD_AWAKE = '1'
    VICTORY_SHOW_AFTER = '1'; VICTORY_AMBIENT = '0.8'; VICTORY_D3D12_DRED = 'light'
    VICTORY_THIN = '1'; VICTORY_JOIN_PARTS = '8'
    # The 8 GB card, named so the choice never falls on the integrated one.
    VICTORY_D3D12_ADAPTER = 'NVIDIA'
    # The title reads the console's display mode, which is 3840x2160 unless
    # told otherwise, and renders 2560x1440 (FidelityFX's 2/3) into a 1080p
    # window: 4x the targets, 4x the copies, 4x the memory, for pixels that
    # are thrown away. 1920x1080 makes it render 1280x720 and upscale to the
    # window. (VICTORY_DISPLAY_MODE changes nothing: the title matches its
    # own mode list, not the output's.)
    VICTORY_OUTPUT = '1920x1080'
    # 16 GB of PC memory: everything the host keeps, kept small. The card has
    # room (7 GB, 5 used at 4K), so the host's copies go there rather than
    # into the PC's memory, which the driver counts as shared with the card.
    VICTORY_PIPELINE_CACHE = '0'
    VICTORY_PIPELINE_WORKERS = '6'
    VICTORY_UPLOAD_AHEAD = '0'
    VICTORY_TEXTURE_BUDGET_MB = '3072'
    VICTORY_CARD_ROOM_FOR_TARGETS_MB = '1536'
    VICTORY_MEMORY_CARD_MB = '4096'
    VICTORY_CARD_BUFFERS_MB = '1024'
    VICTORY_CARD_WINDOWS_MB = '512'
    VICTORY_KEEP_SNAPSHOTS_MB = '64'
    # The mouse looks and shoots on the stand-in pad (the fork's own port).
    VICTORY_MOUSE_LOOK = '1'
    VICTORY_MOUSE_SENSITIVITY = '0.05'
}
foreach ($k in $More.Keys) { $settings[$k] = $More[$k] }

$victory = Join-Path $PSScriptRoot "..\target\$Build\victory.exe"
if (-not (Test-Path $victory)) { Write-Host "No $victory. Build it first (README.md)."; exit 1 }
if (-not (Test-Path $Exe)) { Write-Host "No $Exe."; exit 1 }
# (Other settings left over in this window go; where your files are stays.)
$paths = 'VICTORY_CONFIG', 'VICTORY_GAME_DIR', 'VICTORY_CACHE_DIR', 'VICTORY_DATA_DIR', 'VICTORY_FIDELITYFX_DIR'
$inherited = @(Get-ChildItem Env:VICTORY_* | Where-Object { $_.Name -notin $paths })
foreach ($entry in $inherited) { Remove-Item "Env:$($entry.Name)" }
foreach ($k in $settings.Keys) { Set-Item "Env:$k" $settings[$k] }
$Exe = (Resolve-Path $Exe).Path
Push-Location (Join-Path $PSScriptRoot '..')
try { & $victory run $Exe --execute --console series-s } finally { Pop-Location }
