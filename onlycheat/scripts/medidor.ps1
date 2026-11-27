# Medidor: abre o jogo na GPU e mostra, numa janela ao lado dele, tudo que a correção de renderização precisa.
#   powershell -ExecutionPolicy Bypass -File scripts\medidor.ps1 [-Exe <victory.exe>] [-Segundos 20] [-Label nome]
#        [-Env "NOME=valor;NOME2=valor"] [-Manter]
# - Sempre na GPU real (regra do projeto). Um jogo por vez: lock logs\gpu.lock (o mesmo do auto-run e do jogar).
# - Aperta Enter (botão A do pad substituto) até a cena 3D começar, mais rápido que o auto-run, para encurtar menus.
# - Na cena (>= 800 draws/frame) mede os primeiros -Segundos e tira prints aos 2 s, na metade e no fim.
# - fps em cima com fonte 2x; progresso = (2 x fps/30 + texturas + shaders + pipelines) / 5.
# - Metas na janela: >= 20 fps (objetivo 30), 0 draws descartados, 0 shaders sem tradução, 0 métodos de pipeline
#   sem implementação, 0 texturas esperando ou puladas.
# - Relatório em logs\medidor-<label>-<data>\relatorio.md e relatorio.json; log completo em victory-err.txt.
# - Fecha o jogo ao fim da medição, a não ser com -Manter.
param(
    [string]$Exe = "",
    [int]$Segundos = 20,
    [string]$Label = "base",
    [string]$Env = "",
    [int]$MaxCarregar = 600,
    # Segundos de cena esperando a fila de pipelines zerar antes de medir (0: mede logo).
    [int]$AquecerMax = 600,
    # Qual console o runtime apresenta ao jogo: series-s renderiza em resolução menor.
    [ValidateSet("series-x", "series-s")][string]$Console = "series-x",
    # Régua de imagem (build diag): frames do mundo capturados da placa, linha do tempo e métricas
    # (scripts\compara-frames.ps1). -Referencia <nome>: compara com logs\referencia\<nome>;
    # -SalvarReferencia <nome>: guarda os frames desta execução como essa referência.
    [switch]$Capturar,
    [string]$Referencia = "",
    [string]$SalvarReferencia = "",
    [switch]$Manter,
    # Teste rápido: se os prints dos primeiros 8 s de cena (2, 5 e 8 s) saem todos aberrados (miolo chapado:
    # contraste abaixo de -ContrasteMin, que é tela preta, cinza ou de uma cor só), a execução para ali, sem
    # esperar os -Segundos. O jogo é fechado e o relatório diz "aberrado".
    [switch]$PararSeAberrado,
    [double]$ContrasteMin = 3,
    # Contraste acima disto também é aberração: imagem binarizada (céu branco estourado e o resto preto, exposição
    # quebrada; só branco e preto puros dão ~127). Imagens normais medidas: 14-98.
    [double]$ContrasteMax = 110
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if ($Referencia -or $SalvarReferencia) { $Capturar = $true }
if (-not $Exe) {
    # Só o build diag salva frames.
    $Exe = if ($Capturar) { "$root\runtime-rs-fxceless\target-diag\diag\victory.exe" } else { "$root\runtime-rs-fxceless\target-luz\release\victory.exe" }
}
$game = "D:\torrents\game\game_scarlett_master.exe"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$dir = "$root\logs\medidor-$Label-$stamp"
New-Item -ItemType Directory -Force $dir | Out-Null

$lockPath = "$root\logs\gpu.lock"
$lock = $null
$deadline = (Get-Date).AddMinutes(30)
while (-not $lock) {
    try { $lock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { if ((Get-Date) -gt $deadline) { throw "GPU ocupada há 30 min (logs\gpu.lock)" }; Start-Sleep -Seconds 3 }
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class MedWin {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO i);
    public static uint IdleMs() { var i = new LASTINPUTINFO(); i.cbSize = 8; GetLastInputInfo(ref i); return (uint)Environment.TickCount - i.dwTime; }
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[void][MedWin]::SetThreadDpiAwarenessContext([IntPtr]-4)

Remove-Item Env:\VICTORY_D3D12_WARP, Env:\VICTORY_WARP -ErrorAction SilentlyContinue
$env:VICTORY_GAME_DIR = "D:\torrents\game"
# Base nova (2026-11-25): runtime-rs-fxceless = fxceless/victory-fork f85101b, sem as chaves da base antiga
# (VICTORY_FILE_CACHE, VICTORY_AUTO_START, VICTORY_CHUNK_UPLOAD, VICTORY_CONSOLE_PIPELINES*: não existem nele;
# a base antiga está na tag git base-antiga-20261125). Só chaves do fork entram aqui, depois de medidas.
$defaults = [ordered]@{
    # Chaves que o autor do fork recomenda, medidas aqui (40 s do começo da cena, cache quente, a253706):
    # 1,43 -> 2,02 fps; preparo 439 -> 152 ms, placa 43 -> 15, volta 137 -> 49, texturas 354 -> 54
    # (logs\medidor-a253-padrao-* x logs\medidor-a253-chaves-*; docs\NOTES.md, 2026-11-25).
    VICTORY_ASYNC_SUBMIT = "1"; VICTORY_QUEUE2 = "1"; VICTORY_INDIRECT_WINDOW = "1"; VICTORY_TEXTURE_NO_WAIT = "1"
    # Trazidos da base antiga a pedido do usuário (2026-11-25), sobre o fork:
    # arquivos que o jogo abre sem cache (NO_BUFFERING) passam pelo cache do Windows (lá: até a cena 68-96 -> 43-49 s);
    VICTORY_FILE_CACHE = "1"
    # O método 0x218 (VICTORY_CONSOLE_PIPELINES=1) fica desligado: com o tradutor do fork, 109 dos 271 conferidos na
    # base antiga são entregues e a imagem sai posterizada e escura (logs\medidor-a253-218-miniz-*; sem ele, mesmo
    # build, a imagem fica certa: logs\medidor-a253-trad-sem218-*). Lista nova: scripts\limpa-218.ps1.
    VICTORY_CONSOLE_PIPELINES_ALLOW = "$PSScriptRoot\pipelines-218-ok.txt"
    VICTORY_CACHE_KEY = "medidor"
    VICTORY_LOG = "victory_gpu::queue=info,victory_gpu::host=info,victory_gpu::pipeline=info,victory_gpu::device=info,victory=info"
}
foreach ($k in $defaults.Keys) { if (-not (Test-Path "Env:\$k")) { Set-Item "Env:\$k" $defaults[$k] } }
# Regra do usuário: o ray tracing nunca liga (2026-11-25 e 2026-11-26). A chave é recusada aqui.
if ($Env -match 'VICTORY_BVH_BUILDER|VICTORY_RAYS') { throw "ray tracing proibido (VICTORY_BVH_BUILDER/VICTORY_RAYS): o usuário não quer nunca" }
Remove-Item Env:\VICTORY_BVH_BUILDER -ErrorAction SilentlyContinue
foreach ($kv in ($Env -split ';' | Where-Object { $_ -match '=' })) {
    $k, $v = $kv -split '=', 2
    Set-Item "Env:\$($k.Trim())" $v.Trim()
}
if ($Capturar) {
    New-Item -ItemType Directory -Force "$dir\frames" | Out-Null
    $env:VICTORY_CAPTURE = "$dir\frames"; $env:VICTORY_CAPTURE_WORLD = "1"
}
Get-Process victory -ErrorAction SilentlyContinue | Stop-Process -Force
$errLog = "$dir\victory-err.txt"
$p = Start-Process -FilePath $Exe -ArgumentList @("run", $game, "--execute", "--console", $Console) -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput "$dir\victory-out.txt" -RedirectStandardError $errLog
Start-Process powershell -WindowStyle Hidden -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "$PSScriptRoot\janela.ps1")

# Estado lido do log, incremental.
$st = [ordered]@{
    fase = "abrindo"; inicio = Get-Date; cenaEm = $null; cenaVista = $null; aquecimento = 0; fim = ""
    frame = $null; amostras = New-Object System.Collections.Generic.List[object]
    semTraducao = @{}; traduzidos = @{}; recusados = 0; metodos = @{}; feitos218 = 0; avisos = @{}; texturas = $null; nulas = 0; dstorage = 0
    memPico = 0.0; prints = New-Object System.Collections.Generic.List[object]
}
$pos = 0L
function Read-NewLog {
    if (-not (Test-Path $errLog)) { return }
    $fs = [IO.File]::Open($errLog, 'Open', 'Read', 'ReadWrite')
    try {
        if ($fs.Length -le $script:pos) { return }
        [void]$fs.Seek($script:pos, 'Begin')
        $sr = New-Object IO.StreamReader($fs)
        $text = $sr.ReadToEnd()
        $last = $text.LastIndexOf("`n")
        if ($last -lt 0) { return }
        $script:pos += [Text.Encoding]::UTF8.GetByteCount($text.Substring(0, $last + 1))
        foreach ($line in $text.Substring(0, $last).Split("`n")) { Read-Line $line }
    } finally { $fs.Dispose() }
}
function Read-Line([string]$l) {
    # A base nova (fxceless/victory-fork) escreve o log com cores ANSI mesmo redirecionado: sem tirá-las,
    # "hash=", "slot=" etc. não casam (shaders e métodos ficavam sem contar).
    $l = $l -replace "\x1b\[[0-9;]*m", ''
    if ($l -match '([\d.]+) fps,\s([\d.]+) ms/frame \(([\d.]+) preparing: copies ([\d.]+) ms [\d.]+ MB, textures ([\d.]+) ms [\d.]+ MB(?:, bindless plans [\d.]+ ms)?; ([\d.]+) on the GPU; ([\d.]+) copying back [\d.]+ MB(?:; bindless records [\d.]+ ms)?\), (\d+) draws/frame, dropped (\d+) without a host pipeline \((\d+) being made\) and (\d+) indirect') {
        $f = [ordered]@{ fps = [double]$Matches[1]; ms = [double]$Matches[2]; preparo = [double]$Matches[3]; copias = [double]$Matches[4]
            texturasMs = [double]$Matches[5]; placa = [double]$Matches[6]; volta = [double]$Matches[7]; draws = [int]$Matches[8]
            descartados = [int]$Matches[9]; fazendo = [int]$Matches[10]; indiretos = [int]$Matches[11] }
        $script:st.frame = $f
        if ($script:st.fase -eq "cena") { $script:st.amostras.Add([pscustomobject]$f) }
        if ($l -match 'textures picked by index: (\d+) uploaded so far \((\d+) MB\), (\d+) waiting, (\d+) passed over unmapped') {
            $script:st.texturas = [ordered]@{ enviadas = [int]$Matches[1]; mb = [int]$Matches[2]; esperando = [int]$Matches[3]; puladas = [int]$Matches[4] }
        }
        if ($l -match 'buffer views left null as the host would refuse them: (\d+)') { $script:st.nulas = [int]$Matches[1] }
        return
    }
    if ($l -match 'shader translated.*hash=(\w+)') { $script:st.traduzidos[$Matches[1]] = 1; return }
    if ($l -match '0x218 compute pipelines.*made=(\d+)') { $script:st.feitos218 = [int]$Matches[1]; return }
    if ($l -notmatch '\b(WARN|ERROR)\b') { return }
    if ($l -match 'does not translate.*stage="(\w+)" hash=(\w+) error=(.*)$') {
        $why = $Matches[3] -replace 'converter shader\.dxil v1: ', '' -replace 'at 0x[0-9a-f]+', 'at X'
        $script:st.semTraducao[$Matches[2]] = "$($Matches[1]): $why"
        return
    }
    if ($l -match '0x80070057|pipeline could not be made|pipeline refused') { $script:st.recusados++ }
    if ($l -match 'creation method that is not implemented.*slot=(0x[0-9a-f]+) calls=(\d+)') {
        $script:st.metodos[$Matches[1]] = [math]::Max([int]$Matches[2], [int]($script:st.metodos[$Matches[1]]))
        return
    }
    if ($l -match 'physical pages could not be written') { $script:st.dstorage++ }
    $key = ($l -replace '^\S+\s+', '' -replace '0x[0-9a-fA-F]+', '0x_' -replace '\b\d+(\.\d+)?\b', 'N')
    if ($key.Length -gt 160) { $key = $key.Substring(0, 160) }
    $script:st.avisos[$key] = 1 + [int]$script:st.avisos[$key]
}

function Get-Brightness([string]$file) { (Get-ImageStats $file).brilho }
# Brilho médio da área do jogo (sem a barra de título) e, no miolo (metade do meio na altura, 80% na largura:
# sem as faixas pretas nem as legendas), brilho e contraste (desvio padrão do brilho). Miolo chapado (contraste
# abaixo de ~3) é tela preta, cinza ou de uma cor só; imagem normal dá 14-90 (calibrado em 2026-11-25).
function Get-ImageStats([string]$file) {
    $b = [System.Drawing.Bitmap]::FromFile($file); $s = 0; $n = 0
    for ($y = 40; $y -lt $b.Height; $y += 8) { for ($x = 8; $x -lt $b.Width; $x += 8) { $c = $b.GetPixel($x, $y); $s += 0.2126 * $c.R + 0.7152 * $c.G + 0.0722 * $c.B; $n++ } }
    $cs = 0; $cq = 0; $cn = 0
    for ($y = [int]($b.Height * 0.25); $y -lt [int]($b.Height * 0.75); $y += 6) {
        for ($x = [int]($b.Width * 0.1); $x -lt [int]($b.Width * 0.9); $x += 6) { $c = $b.GetPixel($x, $y); $l = 0.2126 * $c.R + 0.7152 * $c.G + 0.0722 * $c.B; $cs += $l; $cq += $l * $l; $cn++ } }
    $b.Dispose(); $n = [math]::Max($n, 1); $cn = [math]::Max($cn, 1); $cm = $cs / $cn
    [pscustomobject]@{ brilho = [math]::Round($s / $n, 1); centro = [math]::Round($cm, 1); contraste = [math]::Round([math]::Sqrt([math]::Max($cq / $cn - $cm * $cm, 0)), 1) }
}
function Save-Shot($h, [string]$name) {
    $r = New-Object MedWin+RECT
    if (-not [MedWin]::GetWindowRect($h, [ref]$r)) { return }
    $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
    if ($w -le 0 -or $hh -le 0) { return }
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    # A própria janela do jogo (PW_RENDERFULLCONTENT), mesmo com outra janela por cima: copiar a região da tela
    # pegava o que estivesse na frente (um Explorer por cima virou "imagem chapada" e parou a rodada de 09:25).
    $dc = $g.GetHdc(); $ok = [MedWin]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc)
    if (-not $ok) { $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size) }
    $file = "$dir\$name.png"; $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
    $is = Get-ImageStats $file
    $script:st.prints.Add([pscustomobject]@{ nome = $name; brilho = $is.brilho; centro = $is.centro; contraste = $is.contraste })
}

# Janela do medidor.
$form = New-Object System.Windows.Forms.Form
$form.Text = "Medidor de renderização"
$form.Width = 470; $form.Height = 980; $form.TopMost = $true
$form.BackColor = [System.Drawing.Color]::FromArgb(24, 24, 28)
$form.StartPosition = 'Manual'
$box = New-Object System.Windows.Forms.Label
$box.Dock = 'Fill'; $box.Font = New-Object System.Drawing.Font("Consolas", 10)
$box.ForeColor = [System.Drawing.Color]::Gainsboro
$form.Controls.Add($box)
$box.Font = New-Object System.Drawing.Font("Consolas", 9)
# Painel desenhado em cima (pedido do usuário, 2026-11-26): fps grande (fonte 2x ou mais), barras do
# progresso (fps contra 30, texturas, shaders, pipelines) e uso de CPU, RAM e VRAM em porcentagem.
$painel = New-Object System.Windows.Forms.PictureBox
$painel.Dock = 'Top'; $painel.Height = 510
$form.Controls.Add($painel)
# Contadores do Windows: CPU como o Gerenciador de Tarefas, VRAM dedicada por placa (a maior é a RTX),
# RAM pelo ComputerInfo. Total da VRAM pelo registro da placa NVIDIA (qwMemorySize).
Add-Type -AssemblyName Microsoft.VisualBasic
$res = @{ cpu = $null; gpus = @(); vramTotal = 0; ci = New-Object Microsoft.VisualBasic.Devices.ComputerInfo }
try { $res.cpu = New-Object System.Diagnostics.PerformanceCounter('Processor Information', '% Processor Utility', '_Total'); [void]$res.cpu.NextValue() } catch { }
try {
    $cat = New-Object System.Diagnostics.PerformanceCounterCategory('GPU Adapter Memory')
    $res.gpus = @($cat.GetInstanceNames() | ForEach-Object { New-Object System.Diagnostics.PerformanceCounter('GPU Adapter Memory', 'Dedicated Usage', $_) })
} catch { }
try {
    $res.vramTotal = (Get-ItemProperty 'HKLM:\SYSTEM\ControlSet001\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DriverDesc -like '*NVIDIA*' } | Select-Object -First 1).'HardwareInformation.qwMemorySize'
} catch { }
function Get-Uso {
    $u = [ordered]@{ cpu = $null; ram = $null; ramGb = $null; ramTotal = $null; vram = $null; vramGb = $null; vramTotal = $null }
    try { if ($res.cpu) { $u.cpu = [int][math]::Min(100, [math]::Round($res.cpu.NextValue())) } } catch { }
    try {
        $t = [double]$res.ci.TotalPhysicalMemory; $l = [double]$res.ci.AvailablePhysicalMemory
        $u.ram = [int][math]::Round(100 * ($t - $l) / $t); $u.ramGb = [math]::Round(($t - $l) / 1GB, 1); $u.ramTotal = [math]::Round($t / 1GB, 1)
    } catch { }
    try {
        if ($res.gpus.Count -and $res.vramTotal) {
            $usado = ($res.gpus | ForEach-Object { $_.NextValue() } | Measure-Object -Maximum).Maximum
            $u.vram = [int][math]::Round(100 * $usado / $res.vramTotal); $u.vramGb = [math]::Round($usado / 1GB, 1); $u.vramTotal = [math]::Round($res.vramTotal / 1GB, 1)
        }
    } catch { }
    $u
}
# Cores e fontes do painel.
$cor = @{
    fundo = [System.Drawing.Color]::FromArgb(24, 24, 28); cartao = [System.Drawing.Color]::FromArgb(36, 36, 44)
    trilho = [System.Drawing.Color]::FromArgb(58, 58, 70); texto = [System.Drawing.Color]::Gainsboro; fraco = [System.Drawing.Color]::FromArgb(145, 145, 160)
    bom = [System.Drawing.Color]::FromArgb(92, 200, 120); medio = [System.Drawing.Color]::FromArgb(235, 190, 80); ruim = [System.Drawing.Color]::FromArgb(235, 95, 95)
}
$fonte = @{
    fps = New-Object System.Drawing.Font("Segoe UI", 30, [System.Drawing.FontStyle]::Bold)
    sub = New-Object System.Drawing.Font("Segoe UI", 10)
    progresso = New-Object System.Drawing.Font("Segoe UI Semibold", 15); total = New-Object System.Drawing.Font("Segoe UI", 26, [System.Drawing.FontStyle]::Bold)
    meta = New-Object System.Drawing.Font("Segoe UI", 12.5); metaPct = New-Object System.Drawing.Font("Segoe UI Semibold", 12.5)
    titulo = New-Object System.Drawing.Font("Segoe UI Semibold", 10); barra = New-Object System.Drawing.Font("Segoe UI", 9.5)
}
$pinceis = @{}
function Pincel($c) { $k = $c.ToArgb(); if (-not $pinceis.ContainsKey($k)) { $pinceis[$k] = New-Object System.Drawing.SolidBrush $c }; $pinceis[$k] }
# Cor de uma barra: progresso (quanto mais, melhor) ou uso da máquina (quanto mais, pior).
function Cor-Barra($v, [bool]$bomAcima) {
    if ($null -eq $v) { return $cor.fraco }
    if ($bomAcima) { if ($v -ge 100) { $cor.bom } elseif ($v -ge 67) { $cor.medio } else { $cor.ruim } }
    else { if ($v -ge 90) { $cor.ruim } elseif ($v -ge 75) { $cor.medio } else { $cor.bom } }
}
function Desenha-Painel($f, $sm, $pc, $u, $fase) {
    $w = [math]::Max(200, $painel.Width); $h = $painel.Height
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'ClearTypeGridFit'
    $g.Clear($cor.fundo)
    $cartao = { param($y, $alt) $g.FillRectangle((Pincel $cor.cartao), 10, $y, ($w - 20), $alt) }
    # Uma barra: nome à esquerda, valor à direita, trilho embaixo. Devolve a altura usada.
    $barra = { param($y, $nome, $pct, $txt, $c, $fn, $fv, $grossura)
        $x = 24; $bw = $w - 48
        $g.DrawString($nome, $fn, (Pincel $cor.texto), $x, $y)
        $tam = $g.MeasureString($txt, $fv)
        $g.DrawString($txt, $fv, (Pincel $(if ($fv -eq $fonte.metaPct) { $c } else { $cor.fraco })), ($x + $bw - $tam.Width), $y)
        $ty = $y + [int]$g.MeasureString("Ag", $fn).Height + 1
        $g.FillRectangle((Pincel $cor.trilho), $x, $ty, $bw, $grossura)
        if ($null -ne $pct) { $g.FillRectangle((Pincel $c), $x, $ty, [int]($bw * [math]::Max(0, [math]::Min(100, [double]$pct)) / 100), $grossura) }
        ($ty - $y) + $grossura
    }
    # fps em fonte grande; a cor segue a mediana da cena (ou o frame atual antes dela): verde >= 30, amarelo >= 20.
    & $cartao 10 92
    $ref = if ($sm.fps_mediana) { [double]$sm.fps_mediana } elseif ($null -ne $f.fps) { [double]$f.fps } else { $null }
    $fpsCor = if ($null -eq $ref) { $cor.fraco } elseif ($ref -ge 30) { $cor.bom } elseif ($ref -ge 20) { $cor.medio } else { $cor.ruim }
    $agora = if ($null -ne $f.fps) { "{0:0.0}" -f [double]$f.fps } else { "--" }
    $g.DrawString("$agora fps", $fonte.fps, (Pincel $fpsCor), 18, 12)
    $linha2 = if ($sm.fps_mediana) { "mediana da cena {0:0.0} fps   ·   objetivo 30" -f [double]$sm.fps_mediana } else { "objetivo 30 fps   ·   $fase" }
    $g.DrawString($linha2, $fonte.sub, (Pincel $cor.fraco), 24, 72)
    # Progresso até o objetivo: título e total grandes; as quatro metas um pouco menores.
    $y0 = 112; $y = $y0 + 10
    & $cartao $y0 250
    $g.DrawString("Objetivo", $fonte.progresso, (Pincel $cor.texto), 22, ($y + 8))
    $tot = if ($fase -eq "cena") { "$($pc.total)%" } else { "na cena" }
    $totCor = if ($fase -eq "cena") { Cor-Barra $pc.total $true } else { $cor.fraco }
    # O total à direita, sem encostar no título: se não couber na mesma linha, vai para a linha de baixo.
    $tw = $g.MeasureString("Objetivo", $fonte.progresso).Width
    $tt = $g.MeasureString($tot, $fonte.total)
    if (22 + $tw + 16 -le $w - 20 - $tt.Width) {
        $g.DrawString($tot, $fonte.total, (Pincel $totCor), ($w - 20 - $tt.Width), ($y - 4)); $y += 52
    } else {
        $y += 40; $g.DrawString($tot, $fonte.total, (Pincel $totCor), 18, $y); $y += [int]$tt.Height + 4
    }
    foreach ($m in @(@("fps / 30  (peso 2)", $pc.fps), @("texturas carregadas", $pc.texturas), @("shaders traduzidos", $pc.shaders), @("pipelines", $pc.pipelines))) {
        $y += (& $barra $y $m[0] $m[1] "$($m[1])%" (Cor-Barra $m[1] $true) $fonte.meta $fonte.metaPct 9) + 12
    }
    # Uso da máquina.
    $y0 = 372; $y = $y0 + 8
    & $cartao $y0 124
    $g.DrawString("USO DA MÁQUINA", $fonte.titulo, (Pincel $cor.fraco), 24, $y); $y += 22
    $y += (& $barra $y "CPU" $u.cpu $(if ($null -ne $u.cpu) { "$($u.cpu)%" } else { "--" }) (Cor-Barra $u.cpu $false) $fonte.barra $fonte.barra 7) + 8
    $y += (& $barra $y "RAM" $u.ram $(if ($null -ne $u.ram) { "$($u.ram)%   $($u.ramGb) de $($u.ramTotal) GB" } else { "--" }) (Cor-Barra $u.ram $false) $fonte.barra $fonte.barra 7) + 8
    [void](& $barra $y "VRAM (RTX)" $u.vram $(if ($null -ne $u.vram) { "$($u.vram)%   $($u.vramGb) de $($u.vramTotal) GB" } else { "--" }) (Cor-Barra $u.vram $false) $fonte.barra $fonte.barra 7)
    $g.Dispose()
    $old = $painel.Image; $painel.Image = $bmp; if ($old) { $old.Dispose() }
}

$lastEnter = Get-Date 0
$janelaMovida = $false
# Uso de CPU, RAM e VRAM amostrado a cada segundo da cena (médias no relatório).
$usoCena = New-Object System.Collections.Generic.List[object]
$usoUltimo = $null
# Prints aos 2, 5 e 8 s e depois a cada 5 s até o fim: servem de régua entre execuções.
$shots = @(@(2, 5, 8) + @(for ($t = 10; $t -le $Segundos - 2; $t += 5) { $t }) + @([math]::Max($Segundos - 2, 3))) | Sort-Object -Unique
$taken = @{}
function Mark([bool]$ok) { if ($ok) { "[OK]" } else { "[  ]" } }
function Summary {
    $a = $script:st.amostras
    $ms = @($a | ForEach-Object { $_.ms } | Sort-Object)
    $med = if ($ms.Count) { $ms[[int][math]::Floor($ms.Count / 2)] } else { $null }
    $avg = { param($f) if ($a.Count) { [math]::Round(($a | Measure-Object -Property $f -Average).Average, 1) } else { $null } }
    [ordered]@{
        fps_mediana = if ($med) { [math]::Round(1000 / $med, 2) } else { $null }; ms_mediana = $med
        preparo_ms = & $avg preparo; placa_ms = & $avg placa; volta_ms = & $avg volta; texturas_ms = & $avg texturasMs
        draws = & $avg draws; descartados_por_frame = & $avg descartados; indiretos_descartados = & $avg indiretos
    }
}
# Porcentagem até o objetivo (pedido do usuário, 2026-11-26: texturas, shaders, pipelines e 30 fps,
# o fps como objetivo principal, com peso 2):
#   fps: mediana da cena (ou o frame atual antes dela) sobre 30;
#   texturas: enviadas sobre enviadas + esperando + puladas;
#   shaders: traduzidos sobre traduzidos + sem tradução;
#   pipelines: draws que tiveram pipeline na placa (1 - descartados sem pipeline / draws).
#   total = (2 x fps + texturas + shaders + pipelines) / 5.
# Saíram da média: draws descartados por indireto e os métodos 0x1c8/0x218 (do DexNom; o cool_fox
# desenha sem eles e o item ficava sempre em 0%). Continuam nas METAS da janela.
function Get-UsoMedio {
    $a = $script:usoCena
    if (-not $a -or -not $a.Count) { return $null }
    $m = { param($k) $v = @($a | ForEach-Object { $_[$k] } | Where-Object { $null -ne $_ }); if ($v.Count) { [math]::Round(($v | Measure-Object -Average).Average, 1) } else { $null } }
    [ordered]@{ cpu = & $m 'cpu'; ram = & $m 'ram'; ramGb = & $m 'ramGb'; vram = & $m 'vram'; vramGb = & $m 'vramGb'; amostras = $a.Count }
}
function Get-Progress {
    $sm = Summary; $f = $script:st.frame
    # Em double: com 0 e 1 inteiros o PowerShell escolhia Min(int, int) e arredondava (0,67 -> 100%).
    $pct = { param($x) [int][math]::Round([math]::Max([double]0, [math]::Min([double]1, [double]$x)) * 100) }
    $fps = if ($sm.fps_mediana) { $sm.fps_mediana } elseif ($f) { $f.fps } else { 0 }
    $draws = if ($sm.draws) { 1 - ($sm.descartados_por_frame + $sm.indiretos_descartados) / $sm.draws } elseif ($f -and $f.draws) { 1 - ($f.descartados + $f.indiretos) / $f.draws } else { 0 }
    $nt = $script:st.traduzidos.Count; $ns = $script:st.semTraducao.Count
    $shaders = if ($nt + $ns) { $nt / ($nt + $ns) } else { 0 }
    $calls = ($script:st.metodos.Values | Measure-Object -Sum).Sum
    $metodos = if ($calls) { $script:st.feitos218 / ($calls + $script:st.feitos218) } else { 1 }
    $tx = $script:st.texturas
    $texturas = if ($tx -and ($tx.enviadas + $tx.esperando + $tx.puladas)) { $tx.enviadas / ($tx.enviadas + $tx.esperando + $tx.puladas) } else { 0 }
    $pip = if ($sm.draws) { 1 - $sm.descartados_por_frame / $sm.draws } elseif ($f -and $f.draws) { 1 - $f.descartados / $f.draws } else { 0 }
    $r = [ordered]@{ fps = & $pct ($fps / 30); texturas = & $pct $texturas; shaders = & $pct $shaders; pipelines = & $pct $pip; draws = & $pct $draws; metodos = & $pct $metodos }
    $r.total = [int][math]::Round((2 * $r.fps + $r.texturas + $r.shaders + $r.pipelines) / 5)
    $r
}
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1000
$timer.Add_Tick({
    try {
        Read-NewLog
        $q = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
        if (-not $q) { $st.fim = "o jogo fechou (código $($p.ExitCode))"; $timer.Stop(); $form.Close(); return }
        $gb = [math]::Round($q.PrivateMemorySize64 / 1GB, 2); if ($gb -gt $st.memPico) { $st.memPico = $gb }
        $h = $q.MainWindowHandle
        # Quando a janela do jogo aparece, o jogo e o medidor (colado à direita, 4 px) ficam juntos e
        # centralizados na tela do jogo, na horizontal e na vertical (pedido do usuário, 2026-11-26).
        # Sem sair da tela. Uma vez por execução; o medidor segue o jogo (abaixo).
        if ($h -ne 0 -and -not $script:janelaMovida) {
            $r0 = New-Object MedWin+RECT
            if ([MedWin]::GetWindowRect($h, [ref]$r0) -and ($r0.Right - $r0.Left) -gt 100) {
                $tela = [System.Windows.Forms.Screen]::FromHandle($h).WorkingArea
                $gw = $r0.Right - $r0.Left; $gh = $r0.Bottom - $r0.Top
                $largura = $gw + 4 + $form.Width; $altura = $gh
                $x = [math]::Max($tela.Left, $tela.Left + [int](($tela.Width - $largura) / 2))
                $y = [math]::Max($tela.Top, $tela.Top + [int](($tela.Height - $altura) / 2))
                [void][MedWin]::SetWindowPos($h, [IntPtr]::Zero, $x, $y, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
                $script:janelaMovida = $true
            }
        }
        if ($h -ne 0) {
            $r = New-Object MedWin+RECT
            if ([MedWin]::GetWindowRect($h, [ref]$r)) {
                # Ao lado do jogo e com a mesma altura dele (pedido do usuário, 2026-11-26).
                $form.Left = $r.Right + 4; $form.Top = $r.Top
                if (($r.Bottom - $r.Top) -gt 100 -and $form.Height -ne ($r.Bottom - $r.Top)) { $form.Height = $r.Bottom - $r.Top }
            }
        }
        $el = [int]((Get-Date) - $st.inicio).TotalSeconds
        if ($st.fase -ne "cena") {
            $st.fase = "carregando"
            # A cena só conta depois que a fila de pipelines zera ("being made" 0), como um jogo que aquece
            # o cache de PSOs antes do gameplay: uma mudança no tradutor deixa ~1.500 para o driver montar
            # (até 50 s cada) e a medição pegava o aquecimento (docs\NOTES.md, 2026-11-25). Limite -AquecerMax.
            if ($st.frame -and $st.frame.draws -ge 800) {
                if (-not $st.cenaVista) { $st.cenaVista = Get-Date }
                $aquecendo = ((Get-Date) - $st.cenaVista).TotalSeconds
                if ($st.frame.fazendo -eq 0 -or $aquecendo -ge $AquecerMax) {
                    $st.fase = "cena"; $st.cenaEm = Get-Date; $st.aquecimento = [int]$aquecendo
                } else { $st.fase = "aquecendo pipelines ($($st.frame.fazendo) na fila)" }
            }
            # Enter só quando o jogo já está em primeiro plano (nunca rouba o foco de quem está digitando),
            # só nas telas que esperam um botão (quase nada desenhado) e no máximo a cada 5 s.
            # Com o usuário parado há 10 s, o jogo pode vir para a frente; mexendo, nunca.
            elseif ($h -ne 0 -and $st.frame -and $st.frame.draws -lt 50 -and ((Get-Date) - $script:lastEnter).TotalSeconds -ge 5 -and
                    ([MedWin]::GetForegroundWindow() -eq $h -or [MedWin]::IdleMs() -ge 10000)) {
                if ([MedWin]::GetForegroundWindow() -ne $h) { [void][MedWin]::SetForegroundWindow($h); Start-Sleep -Milliseconds 60 }
                [MedWin]::keybd_event(0x0D, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80
                [MedWin]::keybd_event(0x0D, 0, 2, [UIntPtr]::Zero); $script:lastEnter = Get-Date
            }
            if ($el -gt $MaxCarregar) { $st.fim = "não chegou à cena em ${MaxCarregar}s"; $timer.Stop(); $form.Close(); return }
        }
        $naCena = if ($st.cenaEm) { [int]((Get-Date) - $st.cenaEm).TotalSeconds } else { 0 }
        if ($st.cenaEm) {
            foreach ($s in $shots) { if ($naCena -ge $s -and -not $taken[$s] -and $h -ne 0) { Save-Shot $h ("cena-{0:D2}s" -f $s); $taken[$s] = $true } }
            if ($PararSeAberrado -and $taken[8] -and $st.prints.Count -ge 3) {
                $bons = @($st.prints | Where-Object { $_.contraste -ge $ContrasteMin -and $_.contraste -le $ContrasteMax })
                if ($bons.Count -eq 0) { $st.fim = "aberrado: imagem chapada ou binarizada nos primeiros 8 s de cena (contrastes $(($st.prints | ForEach-Object { $_.contraste }) -join '/')), parado cedo"; $timer.Stop(); $form.Close(); return }
            }
            if ($naCena -ge $Segundos -and -not $Manter) { $st.fim = "cena medida por ${Segundos}s"; $timer.Stop(); $form.Close(); return }
            # Com -Manter (o start-fxceless.bat): o relatório sai aos -Segundos e o jogo e a janela seguem abertos.
            if ($naCena -ge $Segundos -and $Manter -and -not $script:relatado) {
                $script:relatado = $true; $st.fim = "cena medida por ${Segundos}s (jogo seguiu aberto)"; Write-Report | Out-Null
            }
        }
        $f = $st.frame; $sm = Summary
        $tx = $st.texturas
        $metodos = ($st.metodos.Values | Measure-Object -Sum).Sum
        $pc = Get-Progress
        $barra = ('#' * [int]($pc.total / 5)).PadRight(20, '.')
        $lines = @(
            "fase: $($st.fase)   tempo: ${el}s   na cena: ${naCena}s / ${Segundos}s",
            $(if ($st.fase -eq "cena") { "PROGRESSO ATÉ O OBJETIVO: {0,3}%  [{1}]" -f $pc.total, $barra } else { "PROGRESSO ATÉ O OBJETIVO: aguardando a cena (nos menus o número enganaria)" }),
            ("  fps {0}% (peso 2)  texturas {1}%  shaders {2}%  pipelines {3}%" -f $pc.fps, $pc.texturas, $pc.shaders, $pc.pipelines),
            "memória: $gb GB (pico $($st.memPico))",
            "",
            "AGORA",
            ("  {0} fps  {1} ms/frame  {2} draws" -f $f.fps, $f.ms, $f.draws),
            ("  preparo {0}  placa {1}  volta {2} ms" -f $f.preparo, $f.placa, $f.volta),
            "",
            "METAS (média da cena)",
            ("  {0} fps >= 20 (obj. 30): {1}" -f (Mark ($sm.fps_mediana -ge 20)), $sm.fps_mediana),
            ("  {0} draws descartados/frame: {1} (+{2} indiretos)" -f (Mark ($sm.descartados_por_frame -eq 0 -and $sm.indiretos_descartados -eq 0)), $sm.descartados_por_frame, $sm.indiretos_descartados),
            ("  {0} shaders sem tradução: {1}" -f (Mark ($st.semTraducao.Count -eq 0)), $st.semTraducao.Count),
            ("  {0} chamadas de pipeline sem método: {1}" -f (Mark (-not $metodos)), [int]$metodos),
            ("  {0} pipelines recusados pela placa: {1}" -f (Mark ($st.recusados -eq 0)), $st.recusados),
            ("  {0} texturas esperando/puladas: {1}/{2}" -f (Mark ($tx -and $tx.esperando -eq 0 -and $tx.puladas -eq 0)), $tx.esperando, $tx.puladas),
            ("  {0} views nulas: {1}   dstorage falhas: {2}" -f (Mark ($st.nulas -eq 0 -and $st.dstorage -eq 0)), $st.nulas, $st.dstorage),
            "",
            "PRINTS: " + (($st.prints | ForEach-Object { "$($_.nome)=$($_.brilho)/c$($_.contraste)" }) -join "  "),
            "",
            "SEM TRADUÇÃO (motivo: quantos)"
        ) + @($st.semTraducao.Values | Group-Object | Sort-Object Count -Descending | Select-Object -First 8 | ForEach-Object { "  $($_.Count) $($_.Name)".Substring(0, [math]::Min(58, ("  $($_.Count) $($_.Name)").Length)) })
        $box.Text = $lines -join "`r`n"
        $uso = Get-Uso
        $script:usoUltimo = $uso
        if ($st.fase -eq "cena" -and $null -ne $uso.cpu) { $script:usoCena.Add($uso) }
        Desenha-Painel $f $sm $pc $uso $st.fase
        # Uma linha de estado para quem acompanha de fora.
        ("{0} | progresso {7} | {1} fps {2} ms {3} draws | descartados {4} | sem tradução {5} | sem método {6}" -f $lines[0], $f.fps, $f.ms, $f.draws, $f.descartados, $st.semTraducao.Count, [int]$metodos, $(if ($st.fase -eq "cena") { "$($pc.total)%" } else { "aguardando a cena" })) |
            Set-Content "$dir\estado.txt" -Encoding utf8
    } catch { $box.Text = "erro no medidor: $_" }
})
$form.Add_Shown({ $timer.Start() })
$script:relatado = $false
function Write-Report {
    Read-NewLog
    $sm = Summary
    $pc = Get-Progress
    $rel = [ordered]@{
        label = $Label; exe = $Exe; env = $Env; fim = $st.fim; progresso = $pc; shaders_traduzidos = $st.traduzidos.Count
        segundos_ate_cena = if ($st.cenaEm) { [int]($st.cenaEm - $st.inicio).TotalSeconds } else { $null }
        cena = $sm; memoria_pico_gb = $st.memPico; prints = $st.prints; uso_maquina = Get-UsoMedio
        shaders_sem_traducao = $st.semTraducao; pipelines_recusados = $st.recusados; chamadas_sem_metodo = $st.metodos
        texturas = $st.texturas; views_nulas = $st.nulas; dstorage_falhas = $st.dstorage
        avisos = $st.avisos.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 40 | ForEach-Object { [ordered]@{ n = $_.Value; aviso = $_.Key } }
        pasta = $dir
    }
    $rel | ConvertTo-Json -Depth 6 | Set-Content "$dir\relatorio.json" -Encoding utf8
    $md = @("# Medidor $Label ($stamp)", "", "- **progresso até o objetivo: $($pc.total)%** (fps $($pc.fps)% com peso 2, texturas $($pc.texturas)%, shaders $($pc.shaders)%, pipelines $($pc.pipelines)%; fora da média: draws $($pc.draws)%, métodos 0x1c8/0x218 $($pc.metodos)%)",
        "- fim: $($st.fim)", "- até a cena: $($rel.segundos_ate_cena) s (aquecendo pipelines: $($st.aquecimento) s)",
        "- fps mediana: $($sm.fps_mediana) (meta 20, objetivo 30); ms: $($sm.ms_mediana); preparo $($sm.preparo_ms), placa $($sm.placa_ms), volta $($sm.volta_ms)",
        $(& { $um = Get-UsoMedio; if ($um) { "- uso médio na cena: CPU $($um.cpu)%, RAM $($um.ram)% ($($um.ramGb) GB), VRAM $($um.vram)% ($($um.vramGb) GB)" } else { "- uso médio na cena: sem amostras" } }),
        "- draws/frame: $($sm.draws); descartados/frame: $($sm.descartados_por_frame); indiretos descartados: $($sm.indiretos_descartados)",
        "- shaders sem tradução: $($st.semTraducao.Count); chamadas sem método: $(($st.metodos.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', ')",
        "- pipelines recusados: $($st.recusados); texturas esperando/puladas: $($st.texturas.esperando)/$($st.texturas.puladas); views nulas: $($st.nulas); dstorage: $($st.dstorage)",
        "- prints: " + (($st.prints | ForEach-Object { "$($_.nome) brilho $($_.brilho) contraste $($_.contraste)" }) -join '; '), "", "## Shaders sem tradução", "") +
        @($st.semTraducao.GetEnumerator() | Sort-Object Value | ForEach-Object { "- $($_.Key): $($_.Value)" }) + @("", "## Avisos mais frequentes", "") +
        @($rel.avisos | ForEach-Object { "- $($_.n)x $($_.aviso)" })
    if ($Capturar -and (Test-Path "$dir\frames")) {
        $refDir = if ($Referencia) { "$root\logs\referencia\$Referencia" } else { "" }
        $cmp = & "$PSScriptRoot\compara-frames.ps1" -Frames "$dir\frames" -Referencia $refDir -Saida $dir
        $md += @("", "## Régua de imagem", "") + @($cmp | ForEach-Object { "- $_" })
        if ($SalvarReferencia) {
            $dst = "$root\logs\referencia\$SalvarReferencia"
            New-Item -ItemType Directory -Force $dst | Out-Null
            Copy-Item "$dir\frames\frame-*.png" $dst -Force
            $md += "- frames guardados como referência em $dst"
        }
    }
    $md -join "`r`n" | Set-Content "$dir\relatorio.md" -Encoding utf8
    "relatório: $dir\relatorio.md"
    $md[0..7] -join "`n"
}
try {
    [void]$form.ShowDialog()
    if (-not $st.fim -and $Manter) { $st.fim = "janela fechada" }
    Write-Report
}
finally {
    if (-not $Manter) { Get-Process -Id $p.Id -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }
    if ($lock) { $lock.Dispose() }
}
