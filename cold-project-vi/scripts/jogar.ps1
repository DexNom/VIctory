#requires -Version 5.1
# Um comando para jogar esta build num PC de 32 GB (Jogar.cmd na raiz):
# compila se preciso, usa uma pasta de corrida propria (cache, saves, logs),
# carrega em Enter no seletor, vigia a RAM e mostra um resumo no fim.
#   Jogar.cmd                  as definicoes rapidas deste fork
#   Jogar.cmd -Seguro          sem as mais novas (o primeiro a tentar se algo falhar)
#   Jogar.cmd -RayTracing      luz indireta da propria build (mais lenta)
#   Jogar.cmd -SemEnter        sem o Enter automatico
#   Jogar.cmd -Maximo          build release sem diagnosticos e log so com avisos (sem Enter automatico)
#   Jogar.cmd -Com1c8          pipelines substitutos (vazios) no metodo 0x1c8: so para medir; deixa a imagem preta
#   Jogar.cmd -SlotsDinamicos 393216   descritores dinamicos na NVIDIA (padrao 262144; o resto fica para o jogo)
#   Jogar.cmd -Minutos 5       teste sozinho: Enter automatico e o jogo fecha-se ao fim de 5 min
#   Jogar.cmd -Minutos 6 -TeclasTeste '150-153:W,165-167:D'   movimento so dentro do jogo
#   Jogar.cmd -Nosso           as nossas definicoes (1080p, buffers na placa...) em vez das do cgbj
#   Jogar.cmd -SeriesS         simula a Series S (menos memoria e mais FPS; intro preta e graficos piores: usa o 0x1c8)
#   Jogar.cmd -MemoriaNormal   sem o perfil de pouca memoria (feito para 64 GB / 24 GB de VRAM)
param([switch] $Seguro, [switch] $RayTracing, [switch] $SemEnter, [switch] $MemoriaNormal, [switch] $SeriesS, [switch] $Maximo, [switch] $Com1c8, [switch] $Nosso, [int] $SlotsDinamicos = 0, [int] $Minutos = 0,
      [string] $PastaJogo = (Join-Path $env:USERPROFILE 'Downloads\dump private build'),
      [string] $PastaCorrida = (Join-Path $env:USERPROFILE '.codex\visualizations\2026\11\22\01a2020d-43d5-7ec3-a9e8-d56142dd6f82\victory-xtx-run'),
      # Abaixo disto (MiB livres) em 3 amostras seguidas, o jogo e fechado para o PC nao bloquear.
      [int] $RamMinimaMiB = 700, [string] $TeclasTeste)
$ErrorActionPreference = 'Stop'
if ($TeclasTeste -and ($Minutos -le 0 -or $Maximo)) { throw '-TeclasTeste requer um teste temporizado com a build de diagnostico.' }
# Uma sessao de cada vez: um segundo Jogar.cmd (ou o ciclo) nao abre outra janela do jogo.
$criado = $false
$sessao = New-Object System.Threading.Mutex($true, 'Local\VIctoryJogar', [ref]$criado)
if (!$criado) { Write-Host 'Ja ha uma sessao do VIctory a correr. Esta janela fecha-se.'; Start-Sleep -Seconds 5; exit 1 }
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $PastaJogo 'game_scarlett_master.exe'
if (!(Test-Path -LiteralPath $exe)) { throw "Nao encontrei $exe" }
foreach ($d in 'cache','data','logs') { [void][IO.Directory]::CreateDirectory((Join-Path $PastaCorrida $d)) }
$toml = Join-Path $PastaCorrida 'victory.toml'
[IO.File]::WriteAllText($toml, "[paths]`ngame_dir = '$PastaJogo'`ncache_dir = '$PastaCorrida\cache'`ndata_dir = '$PastaCorrida\data'`ntest_titles = []`n")
$env:VICTORY_CONFIG = $toml

# Uma preparacao de pipelines (victory warm) ou outra sessao ainda a correr prende o executavel.
if (Get-Process -Name victory -ErrorAction SilentlyContinue) {
    Write-Host 'Ainda ha uma sessao ou a preparacao de pipelines a correr; espero que acabe...'
    while (Get-Process -Name victory -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 5 }
}
$build = if ($Maximo -or (!$Nosso -and $Minutos -eq 0)) { 'release' } else { 'diag' }
Push-Location -LiteralPath $repo
try {
    if ($build -eq 'release') { & cargo build --locked --release -p victory }
    else { & cargo build --locked --profile diag -p victory --features diagnostics }
    if ($LASTEXITCODE -ne 0) { throw 'A compilacao falhou.' }
} finally { Pop-Location }

$stamp = [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
$log = Join-Path $PastaCorrida "logs\jogo-$stamp.log"
$mem = Join-Path $PastaCorrida "logs\memoria-$stamp.log"
# O livro de pipelines regista os que o jogo cria; 'victory warm --used' faz-los antes da proxima corrida.
$more = @{ VICTORY_PIPELINE_BOOK = '1' }
# Perfil de pouca memoria (32 GB de RAM, 12 GB de VRAM): as copias de texturas limitadas a
# 3 GB na placa, para a VRAM nao transbordar para a RAM (placa a 5 % e 1 s por frame).
# (Largar texturas apos 5 s, VICTORY_TEXTURE_KEEP=0, ficou de fora: a sessao de 2026-11-26
# 03:59 perdeu a placa aos 16 min, e o fork guarda memoria libertada contra essas perdas.)
if ($Nosso) {
    # Para este portatil (medido 2026-11-26: de ~3 para ~9 FPS no mundo, sem crash):
    # - 1080p em vez da TV 4K que o jogo julgava ter (4x menos pixeis);
    # - buffers do jogo na placa e nao na RAM do PC (os padroes de placas <= 12 GB deixavam-nos
    #   do lado do PC, lidos pelo barramento);
    # - 64 listas no ar em vez de 256, para os descritores dinamicos voltarem mais cedo.
    $more.VICTORY_DISPLAY_MODE = '1920x1080'
    $more.VICTORY_CARD_BUFFERS_MIN_MB = '1'
    $more.VICTORY_CARD_BUFFERS_MB = '1024'
    $more.VICTORY_LATE_WAIT_LISTS = '64'
    # Do launcher desta versao (cgbj, afinada numa RTX 5090 com 32 GB): niveis de texturas a pedido e o ar.
    $more.VICTORY_LEVELS_ON_DEMAND = '1'
    $more.VICTORY_AMBIENT_AIR = '1.5'
}
if ($Nosso -and !$MemoriaNormal) {
    $more.VICTORY_TEXTURE_BUDGET_MB = '3072'
}
# No teste sozinho, Enter a cada 8 s ate ao fim (a intro so avanca com Enter).
$enters = if ($Minutos -gt 0) { (20..($Minutos * 60 - 30) | Where-Object { ($_ - 20) % 8 -eq 0 } | ForEach-Object { "${_}:Enter" }) -join ',' } else { '30:Enter,45:Enter,60:Enter,90:Enter' }
if (!$SemEnter) { $more.VICTORY_KEYS = $enters }
if ($TeclasTeste) { $more.VICTORY_KEYS = (@($more.VICTORY_KEYS, $TeclasTeste) | Where-Object { $_ }) -join ',' }
# No teste sozinho, uma captura do ecra (meio tamanho) a cada 20 s, para analise; o ciclo
# guarda so as das ultimas 6 voltas.
if ($Minutos -gt 0) {
    # Como o launcher do cgbj (20 FPS na RTX 5090): Series S, que esta versao diz ter corrigido.
    $SeriesS = $true
    $more.VICTORY_CAPTURE = Join-Path $PastaCorrida "capturas\$stamp"
    $more.VICTORY_CAPTURE_EVERY_MS = '20000'
    # (VICTORY_WAVE_WIDE=1 perdeu a placa aos 1,7 min, 2026-11-26 08:22: fica de fora; esta
    # versao (cgbj) tem o seu proprio Wave32 para NVIDIA.)
}

# Vigia de RAM: so fecha o jogo quando a RAM livre fica abaixo do minimo 15 s seguidos.
$vigia = Start-Job -ArgumentList $RamMinimaMiB, $mem -ScriptBlock {
    param($minimo, $mem)
    $baixas = 0
    Start-Sleep -Seconds 10
    while (Get-Process -Name victory -ErrorAction SilentlyContinue) {
        $livre = [int]((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1KB)
        $p = Get-Process -Name victory -ErrorAction SilentlyContinue | Select-Object -First 1
        $priv = if ($p) { [int]($p.PrivateMemorySize64 / 1MB) } else { 0 }
        Add-Content -LiteralPath $mem -Value "$([DateTime]::Now.ToString('HH:mm:ss')) livre_mib=$livre privada_mib=$priv"
        if ($livre -lt $minimo) { $baixas++ } else { $baixas = 0 }
        if ($baixas -ge 3) {
            Add-Content -LiteralPath $mem -Value "FECHADO pelo vigia: RAM livre abaixo de $minimo MiB 3 vezes seguidas"
            Stop-Process -Name victory -Force
            break
        }
        Start-Sleep -Seconds 5
    }
}
Write-Host "Log: $log"
# Teste sozinho: fecha a janela do jogo ao fim de -Minutos (como o botao X), e a forca se
# nao fechar em 2 minutos. O jogo termina como quando o utilizador fecha a janela.
$relogio = $null
if ($Minutos -gt 0) {
    Write-Host "Teste automatico: o jogo fecha-se sozinho ao fim de $Minutos minutos. Podes ir fazer outra coisa."
    $relogio = Start-Job -ArgumentList $Minutos -ScriptBlock {
        param($minutos)
        $fim = (Get-Date).AddMinutes($minutos)
        Start-Sleep -Seconds 20
        while ((Get-Date) -lt $fim -and (Get-Process -Name victory -ErrorAction SilentlyContinue)) { Start-Sleep -Seconds 5 }
        Get-Process -Name victory -ErrorAction SilentlyContinue | ForEach-Object { [void]$_.CloseMainWindow() }
        $ate = (Get-Date).AddMinutes(2)
        while ((Get-Date) -lt $ate -and (Get-Process -Name victory -ErrorAction SilentlyContinue)) { Start-Sleep -Seconds 5 }
        Get-Process -Name victory -ErrorAction SilentlyContinue | Stop-Process -Force
    }
}
# Series X por defeito: na Series S o jogo desenha a intro e outras coisas com pipelines do
# metodo 0x1c8 (ainda sem shaders), e a imagem fica pior.
# Por defeito exatamente como o launcher do cgbj: Series S, as definicoes dele (play.ps1 -Cgbj) e,
# a jogar a serio, a build release. O teste em ciclo fica na build de diagnostico (Enter
# automatico e capturas so existem nela), com o log das linhas de FPS.
if (!$Nosso) { $SeriesS = $true }
$opcoes = @{ Exe = $exe; More = $more; Build = $build; Console = $(if ($SeriesS) { 'series-s' } else { 'series-x' }) }
if (!$Nosso) {
    $opcoes.Cgbj = $true
    # Como o atalho "VIctory cgbj (original)" (VIctory-cgbj.cmd): 1080p e 1 GB de buffers na placa.
    $more.VICTORY_DISPLAY_MODE = '1920x1080'
    $more.VICTORY_CARD_BUFFERS_MB = '1024'
    $more.VICTORY_AMBIENT = '1.6'
    $more.VICTORY_TEXTURE_RAM_GUARD = '0'
    # Perfil medido no ciclo da RTX 5070 Ti Laptop. -Seguro usa os limites de origem.
    if (!$Seguro) {
        # Limita a reserva de blocos livres. As texturas em uso conservam o seu espaco.
        # (2 blocos a frente e 512 MB de buffers na placa deram 1,1 FPS, 2026-11-26 18:06: os buffers do
        # jogo voltavam a ser lidos pelo barramento. Ficam 4 blocos e 1 GB.)
        $more.VICTORY_RIPE_BLOCKS = '4'
        # Dica da comunidade: VICTORY_THIN=1 desfoca a imagem toda nas RTX 50 (Blackwell, como a 5070 Ti).
        $more.VICTORY_THIN = '0'
        # A runtime verifica o orcamento da placa antes de usar o Resizable BAR.
        $more.VICTORY_WINDOWS_BAR_MB = '1024'
        $more.VICTORY_PRESSURE_FREE_MB = '1024'
        $more.VICTORY_PRESSURE_CLEAR_MB = '2048'
    }
    if ($Minutos -gt 0) { $more.VICTORY_LOG = 'warn,victory_gpu::host=info,victory_gpu::copystat=info' }
}
# Maximo: sem os contadores de diagnostico e sem as milhares de linhas de log por segundo
# (fica so a linha de FPS); o Enter automatico so existe na build de diagnostico.
if ($Maximo) {
    $more.VICTORY_LOG = 'warn,victory_gpu::host=info'
}
if ($Seguro) { $opcoes.Safe = $true }
if ($RayTracing) { $opcoes.RayTracing = $true }
$ErrorActionPreference = 'Continue'
& (Join-Path $PSScriptRoot 'play.ps1') @opcoes *>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath $log
Stop-Job $vigia -ErrorAction SilentlyContinue; Remove-Job $vigia -Force -ErrorAction SilentlyContinue
if ($relogio) { Stop-Job $relogio -ErrorAction SilentlyContinue; Remove-Job $relogio -Force -ErrorAction SilentlyContinue }

# Pipelines feitos antes da proxima corrida (driver), a partir do livro desta.
Write-Host "`nA preparar os pipelines para a proxima corrida (victory warm --used)..."
& (Join-Path $repo "target\$build\victory.exe") warm --used *>&1 | Select-Object -Last 3 | ForEach-Object { $_.ToString() }

# Resumo
$esc = [char]27
$linhas = Get-Content -LiteralPath $log | ForEach-Object { $_ -replace "$esc\[[0-9;]*m", '' }
Write-Host "`n=== Resumo ($stamp) ==="
$fps = $linhas | Where-Object { $_ -match 'victory_gpu::host: [0-9.]+ fps' }
$fps | Select-Object -Last 5 | ForEach-Object { if ($_ -match ': ([0-9.]+ fps, [0-9.]+ ms/frame).*?(\d+ draws/frame)') { "$($Matches[1])  $($Matches[2])" } }
$max = ($fps | ForEach-Object { if ($_ -match '(\d+) draws/frame') { [int]$Matches[1] } } | Measure-Object -Maximum).Maximum
Write-Host "Maximo de draws/frame: $max"
if (Test-Path -LiteralPath $mem) { Get-Content -LiteralPath $mem | Select-Object -Last 2 }
$linhas | Where-Object { $_ -match 'window closed|panicked|device removed|DEVICE_REMOVED|fatal' } | Select-Object -Last 3
