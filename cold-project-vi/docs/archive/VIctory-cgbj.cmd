@echo off
rem O launcher do cgbj tal e qual (Play-VIctory.cmd pareto-residency, sem alteracoes), com:
rem  - a build deste codigo que ja tem cache quente (shaders traduzidos e pipelines feitos):
rem    a release comecava com a cache vazia e ficava a 1 FPS a traduzir durante o jogo;
rem  - a pasta do jogo, a cache e os saves deste PC;
rem  - um log em victory-xtx-run\logs\cgbj-*.log (linhas de FPS e avisos).
setlocal
set "RUN=%USERPROFILE%\.codex\visualizations\2026\11\22\01a2020d-43d5-7ec3-a9e8-d56142dd6f82\victory-xtx-run"
set "VICTORY_GAME_DIR=%USERPROFILE%\Downloads\dump private build"
set "VICTORY_CACHE_DIR=%RUN%\cache"
set "VICTORY_DATA_DIR=%RUN%\data"
set "VICTORY_LOG=warn,victory_gpu::host=info,victory_gpu::copystat=info"
rem Para esta placa (RTX 5070 Ti portatil, 12 GB; o launcher dele assume uma RTX 5090 com 32 GB):
rem 1080p em vez da TV 4K que o jogo julga ter, e ate 1 GB de buffers do jogo na placa (estavel depois de carregar a zona: 17 FPS, 2026-11-26 09:00).
rem Medido aqui: de ~3 para ~9 FPS. O launcher dele so poe as suas definicoes quando nao existem.
set "VICTORY_DISPLAY_MODE=1920x1080"
set "VICTORY_CARD_BUFFERS_MB=1024"
rem Regista os pipelines que o jogo cria; ao fechar, sao preparados para a proxima sessao
rem (sem isto, caras e objetos de cenas novas ficam por desenhar enquanto o driver os compila).
set "VICTORY_PIPELINE_BOOK=1"
rem Mais VRAM para texturas: 4 blocos de texturas feitos a frente em vez de 12 (512 MB a mais numa
rem placa de 12 GB cheia). Os blocos a frente protegem de perdas da placa: se a placa se perder, volta a 12.
set "VICTORY_RIPE_BLOCKS=4"
rem Texturas a velocidade normal enquanto houver 1 GB de RAM livre (o cgbj travava-as abaixo de 3 GB
rem e so as soltava aos 5 GB: com 32 GB o jogo ficava sempre travado, a ~2 GB livres).
set "VICTORY_PRESSURE_FREE_MB=1024"
set "VICTORY_PRESSURE_CLEAR_MB=2048"
rem A protecao de RAM antiga deste ramo (texturas a 1/4 abaixo de 4 GB livres) somava-se a do cgbj e
rem travava as texturas (log 2026-11-26 09:44): desligada, fica so a do cgbj (acima).
set "VICTORY_TEXTURE_RAM_GUARD=0"
rem Salas menos escuras: sem ray tracing, a luz indireta e uma estimativa feita da luz no ecra
rem (VICTORY_AMBIENT: parte dela que se usa). O cgbj usa 0.8; aqui 1.6. So visual.
set "VICTORY_AMBIENT=1.6"
rem "VIctory-cgbj.cmd rt": com o ray tracing do jogo (luz indireta: salas menos escuras; mais lento).
if /i "%~1"=="rt" set "VICTORY_BVH_BUILDER=1"
tasklist /FI "IMAGENAME eq victory.exe" | find /I "victory.exe" >nul && (
  echo Ja ha um VIctory a correr. Fecha-o primeiro ^(ou carrega em H no ciclo^).
  pause
  exit /b 1
)
if exist "%~dp0target\diag\victory.exe" copy /y "%~dp0target\diag\victory.exe" "%~dp0victory-3d-rtx-pareto-residency.exe" >nul
for /f %%t in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss"') do set "STAMP=%%t"
set "LOG=%RUN%\logs\cgbj-%STAMP%.log"
echo A jogar com o launcher do cgbj. Log: %LOG%
call "%~dp0Play-VIctory.cmd" pareto-residency > "%LOG%" 2>&1
echo Jogo fechado. A preparar os pipelines desta sessao para a proxima (pode levar uns minutos)...
"%~dp0victory-3d-rtx-pareto-residency.exe" warm --used >> "%LOG%" 2>&1
echo Terminado. Log: %LOG%
exit /b %errorlevel%
