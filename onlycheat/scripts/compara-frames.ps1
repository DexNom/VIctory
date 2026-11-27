# Régua de imagem: frames que o runtime capturou da placa (VICTORY_CAPTURE + VICTORY_CAPTURE_WORLD, build diag)
# viram uma linha do tempo com métricas, e comparados com uma referência quando houver.
#   powershell -ExecutionPolicy Bypass -File scripts\compara-frames.ps1 -Frames <pasta> [-Referencia <pasta>] [-Saida <pasta>]
# - Frames do mundo: frame-<n>.png com n a partir do primeiro frame de 500 draws (o runtime grava um a cada 8).
#   O mesmo número de frame NÃO mostra o mesmo momento entre execuções (o jogo anda pelo relógio): a comparação é
#   da linha do tempo inteira, por métricas e lado a lado, não pixel a pixel.
# - Métricas por frame: brilho médio (0-255), % de pixels pretos (< 8), % de pixels estourados (> 247).
# - Saída: linha-do-tempo.png (atual em cima, referência embaixo), metricas.json, e um resumo no console.
param(
    [Parameter(Mandatory)][string]$Frames,
    [string]$Referencia = "",
    [string]$Saida = "",
    [int]$Max = 12
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
if (-not $Saida) { $Saida = Split-Path $Frames -Parent }

function Get-WorldFrames([string]$dir) {
    $log = Get-ChildItem $dir -Filter 'frame-*.png' | ForEach-Object {
        if ($_.BaseName -match '^frame-(\d+)$') { [pscustomobject]@{ n = [int64]$Matches[1]; file = $_.FullName } }
    } | Sort-Object n
    # O mundo começa no primeiro frame que não é potência de dois nem múltiplo de 16 dos menus: os do mundo vêm
    # de 8 em 8 a partir do primeiro de 500 draws; pega a maior sequência de passo 8.
    $best = @(); $cur = @()
    foreach ($f in $log) {
        if ($cur.Count -and $f.n - $cur[-1].n -eq 8) { $cur += $f } else { if ($cur.Count -gt $best.Count) { $best = $cur }; $cur = @($f) }
    }
    if ($cur.Count -gt $best.Count) { $best = $cur }
    @($best | Select-Object -First $Max)
}
function Measure-Frame([string]$file) {
    $b = [System.Drawing.Bitmap]::FromFile($file); $s = 0.0; $n = 0; $black = 0; $white = 0
    for ($y = 2; $y -lt $b.Height; $y += 4) { for ($x = 2; $x -lt $b.Width; $x += 4) {
        $c = $b.GetPixel($x, $y); $l = 0.2126 * $c.R + 0.7152 * $c.G + 0.0722 * $c.B
        $s += $l; $n++; if ($l -lt 8) { $black++ } elseif ($l -gt 247) { $white++ } } }
    $b.Dispose()
    [ordered]@{ brilho = [math]::Round($s / [math]::Max($n, 1), 1); pretos = [math]::Round(100.0 * $black / [math]::Max($n, 1), 1); estourados = [math]::Round(100.0 * $white / [math]::Max($n, 1), 1) }
}
function Get-Row($list) {
    foreach ($f in $list) { $m = Measure-Frame $f.file; [pscustomobject]@{ n = $f.n; file = $f.file; brilho = $m.brilho; pretos = $m.pretos; estourados = $m.estourados } }
}

$atual = @(Get-Row (Get-WorldFrames $Frames))
$ref = if ($Referencia -and (Test-Path $Referencia)) { @(Get-Row (Get-WorldFrames $Referencia)) } else { @() }
if (-not $atual.Count) { "nenhum frame do mundo em $Frames"; return }

# Linha do tempo: miniaturas de 320 px de largura, atual em cima, referência embaixo.
$tw = 320; $first = [System.Drawing.Image]::FromFile($atual[0].file); $th = [int]($first.Height * $tw / $first.Width); $first.Dispose()
$cols = [math]::Max($atual.Count, $ref.Count); $rows = if ($ref.Count) { 2 } else { 1 }
$label = 18
$img = New-Object System.Drawing.Bitmap ($cols * $tw), ($rows * ($th + $label))
$g = [System.Drawing.Graphics]::FromImage($img); $g.Clear([System.Drawing.Color]::Black)
$font = New-Object System.Drawing.Font("Consolas", 9); $brush = [System.Drawing.Brushes]::White
$r = 0
foreach ($row in @($atual, $ref) | Where-Object { $_.Count }) {
    for ($i = 0; $i -lt $row.Count; $i++) {
        $src = [System.Drawing.Image]::FromFile($row[$i].file)
        $g.DrawImage($src, $i * $tw, $r * ($th + $label) + $label, $tw, $th); $src.Dispose()
        $name = if ($r -eq 0) { "atual" } else { "ref" }
        $g.DrawString(("{0} #{1} b{2} p{3}% e{4}%" -f $name, $i, $row[$i].brilho, $row[$i].pretos, $row[$i].estourados), $font, $brush, $i * $tw + 2, $r * ($th + $label) + 2)
    }
    $r++
}
$g.Dispose()
$png = Join-Path $Saida "linha-do-tempo.png"; $img.Save($png, [System.Drawing.Imaging.ImageFormat]::Png); $img.Dispose()

$avg = { param($rows, $k) if ($rows.Count) { [math]::Round(($rows | Measure-Object -Property $k -Average).Average, 1) } else { $null } }
$res = [ordered]@{
    frames = $atual.Count; referencia = $ref.Count
    atual = [ordered]@{ brilho = & $avg $atual brilho; pretos = & $avg $atual pretos; estourados = & $avg $atual estourados }
    ref = [ordered]@{ brilho = & $avg $ref brilho; pretos = & $avg $ref pretos; estourados = & $avg $ref estourados }
    por_frame = $atual | ForEach-Object { [ordered]@{ n = $_.n; brilho = $_.brilho; pretos = $_.pretos; estourados = $_.estourados } }
    imagem = $png
}
$res | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Saida "metricas.json") -Encoding utf8
"linha do tempo: $png"
"atual: {0} frames, brilho {1}, pretos {2}%, estourados {3}%" -f $atual.Count, $res.atual.brilho, $res.atual.pretos, $res.atual.estourados
if ($ref.Count) { "ref:   {0} frames, brilho {1}, pretos {2}%, estourados {3}%" -f $ref.Count, $res.ref.brilho, $res.ref.pretos, $res.ref.estourados }
