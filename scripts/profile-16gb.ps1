# The 16 GB profile, in one place. play-16gb.ps1 and prepare-16gb.ps1 both read
# it, because a host pipeline made with another profile is one the game does not
# use: the key includes the host's settings.
#
#   $settings = & "$PSScriptRoot\profile-16gb.ps1" -More @{VICTORY_PANEL='1'}
#
# The values and why each is here: OTIMIZACOES.md.
#
# Measured on: Ryzen 5 5600G (6 cores, 12 threads, DDR4-3200), 16 GB,
# RTX 3060 Ti 8 GB (DXGI budget ~7.1 GB), Windows 10 19045, 1280x720.
param(
    [hashtable]$More = @{}
)
$settings = @{
    # The fork's own faster ways, exactly as play.ps1 sets them.
    # RELAY=2 drew a wall in the wrong place on the fork's own docs; 1 is the
    # value every measured run here used (cf-11 onwards).
    VICTORY_TEXTURE_NO_WAIT = '1'; VICTORY_RELAY = '1'; VICTORY_WIDE_BACK = '1'; VICTORY_CARD_REHASH_RUNS = '1'
    VICTORY_JOIN_COPIES = '1'; VICTORY_INDIRECT_WINDOW = '1'; VICTORY_RELAY_COPY = '1'; VICTORY_CARD_STEPS = '2'
    # TITLE_PRIORITIES=1 (cf-13): with 0 the title's threads stay at IDLE
    # (-15) and `xmem::lift_thread` (which only lifts -2..0) never raises
    # them; with the default they are BELOW_NORMAL (-1) and go to
    # ABOVE_NORMAL. The queue waiting "for their memory" fell from 22,096 to
    # about 1,000 with no cost in frame time.
    VICTORY_INDIRECT_BATCH = '2'; VICTORY_MAP_THREADS = '1'; VICTORY_TITLE_PRIORITIES = '1'; VICTORY_CARD_AWAKE = '1'
    VICTORY_SHOW_AFTER = '1'; VICTORY_AMBIENT = '1.6'; VICTORY_D3D12_DRED = 'light'
    # O teto da nevoa de ar: 4 (o padrao calibrado) lava a imagem em interior e
    # a deixa com cara de massinha. 2 e o valor que o launcher da Julia usa.
    VICTORY_AMBIENT_MOST = '2'
    # A nevoa marrom de interior que a luz do ceu dos probes poe: 1.5 e o valor
    # que o proprio fork usa no perfil pareto-residency.
    VICTORY_AMBIENT_AIR = '1.5'
    VICTORY_THIN = '1'; VICTORY_JOIN_PARTS = '8'; VICTORY_UPLOAD_AHEAD = '1'
    # Renderer Port 5 (zero-copy CBV): the constant buffer is read as bytes in
    # place, without the copy it made per draw. On six cores and ~41,000 list
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
    # ---- threads: 6 cores, 12 hardware threads -------------------------------
    # The fork's defaults are sized for a 24-thread 12900K. Here they put ~30
    # runnable threads on 12, and the submitting thread (the frame's wall) was
    # preempted: it waited 550 ms of a 1,070 ms frame at presents.
    VICTORY_THIN_THREADS = '6'          # was 8
    VICTORY_PIPELINE_WORKERS = '4'      # was 10
    VICTORY_LAND_THREADS = '2'          # was 3 (cores / 4)
    VICTORY_UNDER_THREADS = '3'         # was 6
    # 5, not 3: the view heap pass was 30.9 ms on one thread and the closing
    # wait of the lists 12.5 ms. soak15 (97.3 ms) and soak16 (90.0 ms) against
    # the 115.5 ms of soak3: -22% on the frame. Records stay at 4 - soak14 put
    # them at 3 beside the views at 5 and the two fought (140.7 ms).
    VICTORY_VIEW_THREADS = '5'          # was 3 (was 4 views + 6 records)
    # ---- card: 8 GB, DXGI budget ~7.1 GB ------------------------------------
    # Nivel de textura: desligado. A reducao por mip bias removia o device na
    # tela de carregamento (0x887A0001, chamada invalida no caminho de copia),
    # entao toda textura sobe inteira, como antes da opcao existir.
    # At 95 % the queue filled the card to its line and every render target or
    # buffer copy the host made after that was refused: textures were evicted
    # and uploaded again (15,462 waiting, 5,655 MB uploaded). 86 % leaves ~1 GB.
    # 90 % with the smaller card diet below (cf-12/cf-13): the budget was never
    # the trigger in any run after this, and the pool of card buffers is what
    # took the write sweep of shaders from 66.6 ms/658 MB to 3.2 ms/30 MB.
    VICTORY_TEXTURE_ROOM_PERCENT = '90'
    VICTORY_CARD_ROOM_FOR_TARGETS_MB = '768'
    # 128, not the default 3,072: the room held for targets that do not exist
    # yet is what the texture queue lacks on 8 GB.
    VICTORY_MEMORY_CARD_MB = '128'
    VICTORY_CARD_WINDOWS_MB = '256'
    # 1024 MB of card buffers, and `CARD_BUFFERS_MIN_MB=1` so the pool may fill
    # to its line: with the pool at 192 MB the write sweep thrashed (cf-4).
    #
    # The number comes from the budget sweep in
    # WASDeKeys/victory-low-end-pc `RESULTS.md` (a 6 GB card, the same
    # cool_fox tree): 1024 carries the whole gain and 768 gives it back.
    #
    #   CARD_BUFFERS_MB  CARD_WINDOWS_MB  median ms/frame
    #   1024             256              91.2, 90.2, 93.5
    #   1024             768              92-98
    #   1280             512              106.1
    #   768              1024             273.7   <- what this file had
    #   640              1152             274.6
    #
    # Below the line the title's buffer copies no longer fit on the card and
    # every one of them is read back over the bus: that is the 3x, not a
    # texture setting. `CARD_WINDOWS_MB` is 256 rather than 0: their sweep has
    # 256 at 91 and 0 at 93.6, inside the noise, and 256 keeps the vertex and
    # index windows on the card.
    #
    # Here (RTX 3060 Ti 8 GB) the pair measures neutral: 768/96 gave 116.7 ms
    # at 3,748 draws (31.1 us a draw, soak1) and 1024/256 gave 130.7 ms at
    # 4,096 draws (31.9 us a draw, soak2) - a 2.5% spread, inside the scene's
    # own variation. The sweep above is a controlled A/B and this is not, so
    # the sweep's number stays and the local figure is recorded, not claimed.
    VICTORY_CARD_BUFFERS_MB = '1024'
    VICTORY_CARD_BUFFERS_MIN_MB = '1'
    # Snapshots: 339 MB a frame copied and compared whole to find the ~11 MB
    # that changed was 55 ms of a 134 ms frame (run cf-3). With the relay
    # (VICTORY_RELAY=1) the copies come back on a thread of their own, and
    # keeping the snapshots turns the relay off (`shadow::relay`/`keeps`):
    # GPU use went from 22-68 % to 85-100 % with KEEP_SNAPSHOTS=0 (cf-11).
    VICTORY_KEEP_SNAPSHOTS = '0'
    # ---- PC memory: 16 GB, and the title commits 22 GB ----------------------
    # The list allocators keep the most their commands ever took, and 512 idle
    # lists kept theirs for the whole run (9 GB on the author's machine). Trim
    # the ones nobody takes after 10 s, and keep fewer.
    VICTORY_LIST_IDLE_TRIM_S = '10'
    VICTORY_LIST_IDLE_MOST = '192'
    # Blocks of textures made ahead of their first piece (64 MiB each).
    VICTORY_RIPE_BLOCKS = '4'
    # The title's constant ring is reused, so FILL_TITLE=1's two tests
    # (`written_since_taken`, `!landed_lately`) never pass and 22-24 ms a
    # frame went waiting for an 80-byte fill. =2 lets only the size decide
    # (up to 64 KiB); those waits disappeared in cf-18.
    VICTORY_FILL_TITLE = '2'
    # A recheck of a texture picked by index reads 48 KiB of its memory for a
    # fingerprint. Thousands due a frame read hundreds of MB (35 ms of a frame,
    # the third part of the heap pass) to find the few the title changed after
    # their upload. Past 48 MB the rest wait for the next frame: they are the
    # oldest of the queue, and the uploads are bounded by the staging bytes.
    VICTORY_RECHECK_MB = '48'
    # A read buffer grows to the largest texture its thread read and stays.
    VICTORY_READ_KEEP_MB = '64'
    # Duas texturas com a mesma descricao, mudando so o "filler", viravam dois
    # recursos e duas copias (8.438 delas na run 15).
    VICTORY_PLACED_FILLER = '1'
    # A missing pipeline makes its draw vanish; one second of waiting a frame
    # costs less than a character without a body in a cutscene.
    VICTORY_PIPELINE_WAIT_MS = '1000'
    # The mouse looks while the right button is held (the fork's own port).
    VICTORY_MOUSE_LOOK = '1'
    VICTORY_MOUSE_SENSITIVITY = '0.05'
    # Janela sem borda do tamanho do monitor. Alt+Enter ou F11 alternam dentro
    # do jogo (a swap chain mantem o tamanho e estica; nenhum modo muda).
    VICTORY_FULLSCREEN = '0'
}
foreach ($k in $More.Keys) { $settings[$k] = $More[$k] }
return $settings
