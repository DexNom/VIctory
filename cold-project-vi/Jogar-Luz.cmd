@echo off
rem VIctory "cgbj + luz NVIDIA": o ramo cgbj (pareto-residency) com a luz do fork VIctory-NVIDIA,
rem afinado para uma RTX 5070 Ti portatil (12 GB) com 32 GB de RAM. 24-26 FPS no mundo aberto (2026-11-26).
rem Guia completo: docs\LUZ-24FPS.md
rem
rem   set VICTORY_GAME_DIR=D:\Jogos\MeuDump          (pasta com game_scarlett_master.exe)
rem   Jogar-Luz.cmd preparar                         (1.a vez e depois de cada recompilacao: traduz shaders)
rem   Jogar-Luz.cmd                                  (jogar a 1080p)
rem   Jogar-Luz.cmd tudo                             (todas as otimizacoes: 720p, texturas leves, 60 FPS, blocos cheios)
rem   modos soltos: 720p/540p, texturas, cpu, 60, caras, qualidade
setlocal
if not defined VICTORY_GAME_DIR (
  echo Define VICTORY_GAME_DIR com a pasta do teu dump ^(onde esta game_scarlett_master.exe^).
  pause
  exit /b 2
)
if not exist "%VICTORY_GAME_DIR%\game_scarlett_master.exe" (
  echo Nao encontrei "%VICTORY_GAME_DIR%\game_scarlett_master.exe".
  pause
  exit /b 2
)
set "EXE=%~dp0target\diag\victory.exe"
if not exist "%EXE%" (
  echo Falta %EXE%. Compila primeiro: cargo build --locked --profile diag -p victory --features diagnostics
  pause
  exit /b 2
)
if not defined RUN set "RUN=%LOCALAPPDATA%\VIctory\luz"
if not exist "%RUN%\logs" mkdir "%RUN%\logs"
if not defined VICTORY_CACHE_DIR set "VICTORY_CACHE_DIR=%RUN%\cache"
if not defined VICTORY_DATA_DIR set "VICTORY_DATA_DIR=%RUN%\data"
if not defined VICTORY_LOG set "VICTORY_LOG=warn,victory_gpu::host=info,victory_gpu::copystat=info,victory_hle::modules::gameinput=info"
rem --- Placa de 12 GB: 1080p (o jogo julga ter uma TV 4K), 1 GB de buffers do jogo na placa ---
set "VICTORY_DISPLAY_MODE=1920x1080"
rem Painel de FPS e diagnostico (F3 esconde) e janela sem bordas sem mudar a escala do ecra (Smooth Motion).
set "VICTORY_PANEL=1"
set "VICTORY_BORDERLESS=1"
set "VICTORY_BORDERLESS_DPI=0"
set "VICTORY_CARD_BUFFERS_MB=1024"
rem Regista os pipelines usados; ao fechar sao preparados para a proxima sessao.
set "VICTORY_PIPELINE_BOOK=1"
rem 4 blocos de texturas feitos a frente em vez de 12 (512 MB a mais para texturas).
set "VICTORY_RIPE_BLOCKS=4"
rem Texturas a velocidade normal enquanto houver 1 GB de RAM livre (o cgbj travava abaixo de 3 GB).
set "VICTORY_PRESSURE_FREE_MB=1024"
set "VICTORY_PRESSURE_CLEAR_MB=2048"
set "VICTORY_TEXTURE_RAM_GUARD=0"
rem A reserva para imagens do ecra era de 3 GB (medida a 4K); a 1080p elas ocupam ~1,4-1,6 GB.
set "VICTORY_CARD_ROOM_FOR_TARGETS_MB=1792"
set "VICTORY_AMBIENT=1.6"
rem --- Luz do fork NVIDIA ---
set "VICTORY_BVH_MISS=1"
set "VICTORY_AMBIENT_AIR=1.5"
set "VICTORY_AMBIENT_MOST=2"
set "VICTORY_NAN_SCRUB=1"
set "VICTORY_DYNAMIC_SLOTS=524288"
set "VICTORY_SKIP_PIPELINES=@%~dp0scripts\rt_off_hashes.txt"
rem --- Shaders de 64 threads que a NVIDIA recusa, traduzidos com ramos da onda inteira ---
rem  24126c14... TransmissionVolumetrics_Tiled, 7099a57f... ..._LocalLights, f1f161e4... ...Submerged.
rem  Nenhum: agua certa, ~1 FPS no mundo aberto. So _Tiled: quadrados pretos na agua e ~1 FPS.
rem  _Tiled + _LocalLights: 24 FPS com quadrados. So _LocalLights: agua certa e ~26 FPS (fica este).
set "VICTORY_WAVE_WIDE_DIGESTS=7099a57fded142613b44f410463d6be6"
rem --- Definicoes do launcher do cgbj (Play-VIctory.cmd) ---
set "VICTORY_TEXTURE_NO_WAIT=1"
set "VICTORY_RELAY=2"
set "VICTORY_WIDE_BACK=1"
set "VICTORY_CARD_REHASH_RUNS=1"
set "VICTORY_JOIN_COPIES=1"
set "VICTORY_INDIRECT_WINDOW=1"
set "VICTORY_RELAY_COPY=1"
set "VICTORY_CARD_STEPS=2"
set "VICTORY_INDIRECT_BATCH=2"
set "VICTORY_MAP_THREADS=1"
set "VICTORY_CARD_AWAKE=1"
set "VICTORY_SHOW_AFTER=1"
set "VICTORY_THIN=1"
set "VICTORY_JOIN_PARTS=8"
set "VICTORY_UPLOAD_AHEAD=1"
set "VICTORY_LEVELS_ON_DEMAND=1"
set "VICTORY_CARD_BUFFERS_MIN_KB=64"
rem --- Modos (1.o ou 2.o argumento) ---
rem  720p: o mundo a 1280x720 (escala 2/3) e o upscaler temporal do proprio jogo ate 1080p: ~23-26 FPS.
rem  540p: escala 0.5, mais rapido e mais suave. (0.75 e 1.0 com o upscaler forcado param ao arrancar.)
rem  qualidade: menos geometria esticada (relay 1, sem passos na placa), ~7 FPS a menos.
for %%m in ("%~1" "%~2" "%~3" "%~4") do (
  if /i %%m=="720p" (set "VICTORY_RENDER_SCALE=0.6666667" & set "VICTORY_OWN_UPSCALER=1")
  if /i %%m=="540p" (set "VICTORY_RENDER_SCALE=0.5" & set "VICTORY_OWN_UPSCALER=1")
  if /i %%m=="qualidade" (set "VICTORY_RELAY=1" & set "VICTORY_CARD_STEPS=0")
  if /i %%m=="texturas" (set "VICTORY_TEXTURE_MAX=2048" & set "VICTORY_LEVELS_ON_DEMAND=0" & set "VICTORY_CARD_BUFFERS_MB=1536" & set "VICTORY_WINDOWS_BAR_MB=1024")
  if /i %%m=="cpu" (set "VICTORY_ASK_EARLY=2" & set "VICTORY_WIDE_THREADS=10")
  if /i %%m=="60" set "VICTORY_FPS_MAX=60"
  if /i %%m=="caras" set "VICTORY_TEXTURE_NO_WAIT=0"
  if /i %%m=="carregar" set "VICTORY_BAKE_NO_WAIT=1"
  if /i %%m=="tudo" (set "VICTORY_RENDER_SCALE=0.6666667" & set "VICTORY_OWN_UPSCALER=1" & set "VICTORY_TEXTURE_MAX=2048" & set "VICTORY_LEVELS_ON_DEMAND=0" & set "VICTORY_CARD_BUFFERS_MB=1536" & set "VICTORY_WINDOWS_BAR_MB=1024" & set "VICTORY_FPS_MAX=60" & set "VICTORY_POOL_FULLEST=1" & set "VICTORY_TEXTURE_STAGE_MB=384")
)
tasklist /FI "IMAGENAME eq victory.exe" | find /I "victory.exe" >nul && (
  echo Ja ha um VIctory a correr. Fecha-o primeiro.
  pause
  exit /b 1
)
if /i "%~1"=="preparar" goto :preparar
if /i "%~2"=="preparar" goto :preparar
for /f %%t in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss"') do set "STAMP=%%t"
set "LOG=%RUN%\logs\luz-%STAMP%.log"
echo A jogar. Log: %LOG%
"%EXE%" run "%VICTORY_GAME_DIR%\game_scarlett_master.exe" --console series-s --execute > "%LOG%" 2>&1
echo Jogo fechado. A preparar os pipelines desta sessao para a proxima (pode levar uns minutos)...
"%EXE%" warm --used >> "%LOG%" 2>&1
echo Terminado. Log: %LOG%
exit /b 0
:preparar
set "LOG=%RUN%\logs\preparar.log"
echo A traduzir os shaders do jogo (uns 2 minutos)... Log: %LOG%
"%EXE%" shaders "%VICTORY_GAME_DIR%" > "%LOG%" 2>&1
echo A preparar os pipelines das sessoes anteriores...
"%EXE%" warm --used >> "%LOG%" 2>&1
echo Terminado. Log: %LOG%
exit /b 0
