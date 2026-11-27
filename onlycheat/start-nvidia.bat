@echo off
rem Abre o jogo pelo VIctory-NVIDIA (runtime-rs-nvidia, trustno1-coder/VIctory-NVIDIA d12eb1b, base foxhound) com DLSS e
rem geracao de quadros (NVIDIA Streamline 2.14.16, baixado em 2026-11-27 com o OK do usuario para
rem runtime-rs-nvidia\nvidia-sdk, fora do git), PELO MEDIDOR do onlycheat (janela de medicao ao lado e posicionamento
rem da tela, como o start-coolfox.bat; pedido do usuario 2026-11-27 14:2x). Logs em logs\medidor-jogo-nvidia-*.
rem No jogo: F1 abre o menu do autor (DLSS, geracao 2x-4x), F3 mostra fps e tempos. O "SAVE AND RESTART" do F1 nao
rem funciona aqui (o medidor abre o jogo, nao o lancador do autor): mudou algo no F1, feche e abra este .bat de novo.
rem Chaves: as do runtime-rs-nvidia\scripts\play-foxhound.ps1 do autor (THIN=1 fica: a geracao de quadros precisa
rem dele), com o DLSS e a geracao, mais as medidas no cool_fox (2026-11-26, scripts\ab-bar.ps1, ver start-coolfox.bat):
rem BAR 3072 (preparo 56 -> 38-44 ms), CARD_AWAKE=0 (CPU 9-24 pontos menor; o autor usa 1), SUBMIT_FRAMES=2.
rem VICTORY_ASYNC_SUBMIT=0: o medidor liga 1 por padrao; o autor nao usa.
rem Tudo no D: (pedido do usuario): saves em .data\nvidia (copia dos saves do cool_fox na primeira vez), cache de
rem shaders em .cache\nvidia, dados do runtime em .data\nvidia-local (LOCALAPPDATA trocado so para este processo).
rem Regras do projeto: GPU real, sem ray tracing (o medidor recusa VICTORY_BVH_BUILDER; nao ligar no F1).
rem Nao e a base de retorno: a base estavel segue em base-20261127\start-base.bat.
setlocal EnableExtensions
cd /d "%~dp0"
set "GAME=D:\torrents\game\game_scarlett_master.exe"
set "RT=%~dp0runtime-rs-nvidia"
set "VICTORY=%RT%\target\release\victory.exe"
set "SL=%RT%\nvidia-sdk\Streamline\bin\x64"
if not exist "%GAME%" (echo Build nao encontrada: %GAME% & goto :fail)
if not exist "%VICTORY%" (echo VIctory-NVIDIA nao compilado: %VICTORY% & goto :fail)
if not exist "%SL%\sl.interposer.dll" (echo Sem os arquivos da NVIDIA em %SL%: rode runtime-rs-nvidia\Get-NVIDIA-DLSS.cmd & goto :fail)
if not exist "%~dp0.data\nvidia" (
  echo Primeira vez: copiando os saves de .data\victory para .data\nvidia ...
  robocopy "%~dp0.data\victory" "%~dp0.data\nvidia" /E /NFL /NDL /NJH /NJS >nul
)
if not exist "%~dp0.cache\nvidia" mkdir "%~dp0.cache\nvidia"
if not exist "%~dp0.data\nvidia-local" mkdir "%~dp0.data\nvidia-local"
set "LOCALAPPDATA=%~dp0.data\nvidia-local"
set "VICTORY_D3D12_WARP="
set "VICTORY_WARP="
set "VICTORY_BVH_BUILDER="

set "PICK=%~1"
if not "%PICK%"=="" goto :run
echo ================================================
echo   VIctory-NVIDIA - DLSS + geracao de quadros (com o medidor)
echo ================================================
echo   1. Series S + DLSS Qualidade + geracao 2x        (o nosso: mais leve)
echo   2. Series S + DLSS Qualidade, sem geracao
echo   3. Series X 1440p + DLSS Qualidade + geracao 2x  (o do autor; aqui 7 fps, placa segura)
echo   4. Series X 1440p + DLSS Qualidade, sem geracao
echo   Q. Sair
choice /c 1234Q /n /m "Escolha 1-4 ou Q: "
if errorlevel 5 goto :end
if errorlevel 4 (set "PICK=4") else if errorlevel 3 (set "PICK=3") else if errorlevel 2 (set "PICK=2") else (set "PICK=1")
:run
rem Do play-foxhound.ps1 do autor (d12eb1b), menos CARD_AWAKE (o nosso 0 vai abaixo).
set "AUTOR=VICTORY_THIN=1;VICTORY_JOIN_PARTS=8;VICTORY_RELAY=2;VICTORY_RELAY_COPY=1;VICTORY_WIDE_BACK=1;VICTORY_CARD_REHASH_RUNS=1;VICTORY_JOIN_COPIES=1;VICTORY_CARD_STEPS=2;VICTORY_CARD_BUFFERS_MIN_KB=64;VICTORY_INDIRECT_WINDOW=1;VICTORY_INDIRECT_BATCH=2;VICTORY_UPLOAD_AHEAD=1;VICTORY_SHOW_AFTER=1;VICTORY_MAP_THREADS=1;VICTORY_WC_NO_BACK=1;VICTORY_WC_NO_BACK_MIN_KB=1024;VICTORY_SOFT_VIEWS=copies;VICTORY_TEXTURE_POOL=1;VICTORY_RIPE_BLOCKS=0;VICTORY_TEXTURE_NO_WAIT=1;VICTORY_TEXTURE_CATCHUP=0;VICTORY_TEXTURE_STAGE_MS=16;VICTORY_LEVELS_ON_DEMAND=0;VICTORY_PRELUDE_UPLOADS=1;VICTORY_SHOW_NEW_LATER=1;VICTORY_POOL_SMALL=0;VICTORY_TABLED_REREAD=1;VICTORY_QUEUE_HOST_TEXTURES=0;VICTORY_HEAP_SYNC_FIRST=1;VICTORY_SYNC_IN_PLACE=1;VICTORY_SPANS_QUICK=1;VICTORY_SYNC_FINE=1;VICTORY_HEAP_SYNC_AT_SUBMIT=1;VICTORY_AMBIENT=0.8;VICTORY_AMBIENT_AIR=1.5;VICTORY_AMBIENT_SPECULAR=1;VICTORY_EARLY_DEPTH=1;VICTORY_LEAVE_OUT=02384a60fda6f2d0,731a39171b568298;VICTORY_PIPELINE_BOOK=1;VICTORY_FRAME_INTERVAL_US=1000"
rem DLSS (o autor: 80%% da memoria da placa para texturas com a geracao ligada, 86 com ela pronta e desligada).
set "DLSS=VICTORY_DLSS=1;VICTORY_DLSS_MODE=quality;VICTORY_STREAMLINE_DIR=%SL%;VICTORY_FRAMEGEN=1"
if "%PICK%"=="1" set "DLSS=%DLSS%;VICTORY_FRAMEGEN_X=2;VICTORY_TEXTURE_ROOM_PERCENT=80;VICTORY_MENU_FRAMEGEN=2"
if "%PICK%"=="3" set "DLSS=%DLSS%;VICTORY_FRAMEGEN_X=2;VICTORY_TEXTURE_ROOM_PERCENT=80;VICTORY_MENU_FRAMEGEN=2"
if "%PICK%"=="2" set "DLSS=%DLSS%;VICTORY_FRAMEGEN_X=0;VICTORY_TEXTURE_ROOM_PERCENT=86;VICTORY_MENU_FRAMEGEN=0"
if "%PICK%"=="4" set "DLSS=%DLSS%;VICTORY_FRAMEGEN_X=0;VICTORY_TEXTURE_ROOM_PERCENT=86;VICTORY_MENU_FRAMEGEN=0"
set "CONSOLE=series-s"
if "%PICK%"=="3" set "CONSOLE=series-x"
if "%PICK%"=="4" set "CONSOLE=series-x"
if "%CONSOLE%"=="series-x" set "DLSS=%DLSS%;VICTORY_NATIVE_1440P=1;VICTORY_MENU_RES=1440"
set "NOSSO=VICTORY_WINDOWS_BAR_MB=3072;VICTORY_CARD_AWAKE=0;VICTORY_SUBMIT_FRAMES=2;VICTORY_ASYNC_SUBMIT=0;VICTORY_DATA_DIR=%~dp0.data\nvidia;VICTORY_CACHE_DIR=%~dp0.cache\nvidia"
set "MENU=VICTORY_MENU_CONSOLE=%CONSOLE%;VICTORY_MENU_DLSS=quality;VICTORY_MENU_FPS=1000;VICTORY_MENU_FILE=%RT%\victory-settings.txt"
set "LOGS=VICTORY_LOG=victory_gpu::queue=info,victory_gpu::host=info,victory_gpu::pipeline=info,victory_gpu::device=info,victory_gpu::streamline=info,victory_hle::modules::fidelityfx=info,victory=info"
set "TUDO=%AUTOR%;%DLSS%;%NOSSO%;%MENU%;%LOGS%"
rem NVIDIA_EXTRA: chaves a mais para um teste (por ultimo, valem sobre as de cima).
if defined NVIDIA_EXTRA set "TUDO=%TUDO%;%NVIDIA_EXTRA%"
rem NVIDIA_SO_CHAVES=1: so escreve as chaves e sai (scripts\ab-nvidia.ps1 le daqui).
if defined NVIDIA_SO_CHAVES (echo %TUDO%& exit /b 0)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\medidor.ps1" -Exe "%VICTORY%" -Label jogo-nvidia -Segundos 40 -Console %CONSOLE% -MaxCarregar 1800 -AquecerMax 1800 -Manter -Env "%TUDO%"
set "RC=%ERRORLEVEL%"
echo Fim (codigo %RC%). Relatorio e logs em logs\medidor-jogo-nvidia-*
:end
pause
exit /b 0
:fail
pause
exit /b 1
