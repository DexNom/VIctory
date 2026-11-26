# Starts the title with the settings this fork is played with on a 16 GB PC:
# Ryzen 5 5600G, 16 GB, GeForce RTX 3060 Ti 8 GB, Windows 10, 720p, Series S.
#
#   scripts\play-16gb.ps1 -Exe <path of your own copy's executable>
#   scripts\play-16gb.ps1 -Exe <...> -Build release
#   scripts\play-16gb.ps1 -Exe <...> -More @{VICTORY_PANEL='1'}
#
# It is scripts\play.ps1's profile -- the one the fork is measured with -- plus
# the things a 16 GB box needs: the title's own output mode (720p), the
# pipeline library kept between runs, and no VICTORY_TEXTURE_BUDGET_MB (that
# switch replaces the VRAM check instead of adding to it, and the world goes
# without textures). The five renderer ports (VICTORY_PORT_*) and
# VICTORY_FRAME_INTERVAL_MS=16.667 are NOT here: on this machine they took the
# frame from 636 ms to 4,392 ms and the process died at 141 s (run 14).
param(
    [Parameter(Mandatory)][string]$Exe,
    [ValidateSet('diag', 'release')][string]$Build = 'diag',
    [hashtable]$More = @{}
)
$settings = @{
    # The fork's own faster ways, exactly as play.ps1 sets them.
    VICTORY_TEXTURE_NO_WAIT = '1'; VICTORY_RELAY = '2'; VICTORY_WIDE_BACK = '1'; VICTORY_CARD_REHASH_RUNS = '1'
    VICTORY_JOIN_COPIES = '1'; VICTORY_INDIRECT_WINDOW = '1'; VICTORY_RELAY_COPY = '1'; VICTORY_CARD_STEPS = '2'
    VICTORY_INDIRECT_BATCH = '2'; VICTORY_MAP_THREADS = '1'; VICTORY_TITLE_PRIORITIES = '0'; VICTORY_CARD_AWAKE = '1'
    VICTORY_SHOW_AFTER = '1'; VICTORY_AMBIENT = '0.8'; VICTORY_D3D12_DRED = 'light'
    VICTORY_THIN = '1'; VICTORY_JOIN_PARTS = '8'; VICTORY_UPLOAD_AHEAD = '0'
    # The 8 GB card, named so the choice never falls on the integrated one.
    VICTORY_D3D12_ADAPTER = 'NVIDIA'
    # 720p e o menor modo que o titulo tem. A escala 1.0 desenha 1280x720
    # inteiro: o upscaler dele mesmo (6) a 0.5 aplica nitidez e a imagem fica
    # dura; a placa fica 78% ociosa, entao desenhar 720p custa quase nada.
    VICTORY_OUTPUT = '1280x720'
    VICTORY_UPSCALER = '6'
    VICTORY_RENDER_SCALE = '1.0'
    # The 223 MB library of host pipelines: without it every run compiles
    # ~1,200 pipelines at about one a second, and each costs driver memory.
    VICTORY_PIPELINE_CACHE = '1'
    VICTORY_PIPELINE_WORKERS = '10'
    VICTORY_CARD_ROOM_FOR_TARGETS_MB = '512'
    VICTORY_TEXTURE_ROOM_PERCENT = '95'
    VICTORY_MEMORY_CARD_MB = '2048'
    VICTORY_CARD_WINDOWS_MB = '512'
    VICTORY_CARD_BUFFERS_MB = '256'
    VICTORY_KEEP_SNAPSHOTS_MB = '32'
    # Duas texturas com a mesma descricao, mudando so o "filler", viravam dois
    # recursos e duas copias (8.438 delas na run 15).
    VICTORY_PLACED_FILLER = '1'
    # Com 32 GiB ou menos: limita o prefetch quando falta RAM e solta scratch
    # e staging parados. O knob desenhado para esta maquina.
    VICTORY_RESIDENCY = '1'
    # The mouse looks while the right button is held (the fork's own port).
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
