# The 16 GB profile, in one place. play-16gb.ps1 and prepare-16gb.ps1 both read
# it, because a host pipeline made with another profile is one the game does not
# use: the key includes the host's settings.
#
#   $settings = & "$PSScriptRoot\profile-16gb.ps1" -More @{VICTORY_PANEL='1'}
#
# The values and why each is here: OTIMIZACOES.md.
param(
    [hashtable]$More = @{}
)
$settings = @{
    # The fork's own faster ways, exactly as play.ps1 sets them.
    VICTORY_TEXTURE_NO_WAIT = '1'; VICTORY_RELAY = '2'; VICTORY_WIDE_BACK = '1'; VICTORY_CARD_REHASH_RUNS = '1'
    VICTORY_JOIN_COPIES = '1'; VICTORY_INDIRECT_WINDOW = '1'; VICTORY_RELAY_COPY = '1'; VICTORY_CARD_STEPS = '2'
    VICTORY_INDIRECT_BATCH = '2'; VICTORY_MAP_THREADS = '1'; VICTORY_TITLE_PRIORITIES = '0'; VICTORY_CARD_AWAKE = '1'
    VICTORY_SHOW_AFTER = '1'; VICTORY_AMBIENT = '0.8'; VICTORY_D3D12_DRED = 'light'
    VICTORY_THIN = '1'; VICTORY_JOIN_PARTS = '8'; VICTORY_UPLOAD_AHEAD = '1'
    # Renderer Port 5 (zero-copy CBV): the constant buffer is read as bytes in
    # place, without the copy it made per draw. On six cores and ~48,000 list
    # calls a frame the copy is the work, not the drawing.
    VICTORY_PORT_CBV = '1'
    # 720p is the smallest mode the title has. Scale 1.0 draws 1280x720 whole:
    # its own upscaler (6) at 0.5 sharpens and the picture goes hard.
    VICTORY_OUTPUT = '1280x720'
    VICTORY_UPSCALER = '6'
    VICTORY_RENDER_SCALE = '1.0'
    # The library of host pipelines, and STORE_LATER: a pipeline is handed out
    # at once and stored with the save instead of holding the frame on the disk.
    VICTORY_LIBRARY = '1'
    VICTORY_LIBRARY_STORE_LATER = '1'
    VICTORY_PIPELINE_WORKERS = '10'
    # 512 MB, not the default 3,072: on an 8 GB card with 6.8 in use, the room
    # held for targets that do not exist yet is what the texture queue lacks.
    VICTORY_CARD_ROOM_FOR_TARGETS_MB = '512'
    VICTORY_TEXTURE_ROOM_PERCENT = '95'
    VICTORY_MEMORY_CARD_MB = '2048'
    VICTORY_CARD_WINDOWS_MB = '512'
    VICTORY_CARD_BUFFERS_MB = '256'
    VICTORY_KEEP_SNAPSHOTS_MB = '32'
    # Duas texturas com a mesma descricao, mudando so o "filler", viravam dois
    # recursos e duas copias (8.438 delas na run 15).
    VICTORY_PLACED_FILLER = '1'
    # A missing pipeline makes its draw vanish; one second of waiting a frame
    # costs less than a character without a body in a cutscene.
    VICTORY_PIPELINE_WAIT_MS = '1000'
    # The mouse looks while the right button is held (the fork's own port).
    VICTORY_MOUSE_LOOK = '1'
    VICTORY_MOUSE_SENSITIVITY = '0.05'
}
foreach ($k in $More.Keys) { $settings[$k] = $More[$k] }
return $settings
