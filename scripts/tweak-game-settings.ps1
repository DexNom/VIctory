<#
.SYNOPSIS
    Ajusta o settings.xml do próprio título (não as variáveis VICTORY_*).

.DESCRIPTION
    O jogo guarda as configurações gráficas em
    %LOCALAPPDATA%\victory\data\<title-id>\storage\settings\settings.xml.
    Esse arquivo tem alavancas que o runtime não tem: sombra por ray tracing,
    resolução da UI, VSync, limite de quadros, LOD de geometria, SSR da água.

    Perfis:

      balanced (padrão)  corta o trabalho de pixel caro, mantém toda textura
                         presente e o mip bias do título (-1.5): menos nítido,
                         nada sumido.
      maxfps             mais agressivo: LOD de textura em 2.0, resolução de
                         saída em 640x480, culling maior. Some detalhe, não
                         textura.
      sharp              desfaz os cortes de LOD/mip, mantém só os cortes de
                         pixel (RT, grama, SSR, motion blur).
      restore            devolve o backup mais recente.

.PARAMETER Profile
    balanced | maxfps | sharp | restore

.PARAMETER Set
    Sobrescreve um ajuste: -Set advancedGraphics.lodScale=1.0

.PARAMETER DryRun
    Mostra o que mudaria, sem gravar.

.PARAMETER Path
    Caminho do settings.xml. Por padrão o primeiro sob %LOCALAPPDATA%\victory\data.

.EXAMPLE
    .\tweak-game-settings.ps1 -DryRun
    .\tweak-game-settings.ps1 -Profile balanced
    .\tweak-game-settings.ps1 -Profile maxfps -Force
#>
[CmdletBinding()]
param(
    [ValidateSet('balanced', 'maxfps', 'sharp', 'restore')]
    [string]$Profile = 'balanced',
    [string[]]$Set = @(),
    [switch]$DryRun,
    [switch]$Force,
    [string]$Path
)

$ErrorActionPreference = 'Stop'

function Find-Settings {
    $root = Join-Path $env:LOCALAPPDATA 'victory\data'
    if (-not (Test-Path $root)) { return $null }
    $found = Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'storage\settings\settings.xml' } |
        Where-Object { Test-Path $_ } |
        Sort-Object { (Get-Item $_).LastWriteTime } -Descending
    return ($found | Select-Object -First 1)
}

# Os cortes de pixel: tudo aqui é cosmético e caro. Nenhum deles tira textura.
$pixelCuts = [ordered]@{
    'video.frameRateLimit'                              = '0'
    'video.vSync'                                       = '0'
    'graphics.desiredFPS'                               = '120'
    'advancedGraphics.motionBlur'                       = 'false'
    'advancedGraphics.rtDynamicShadows'                 = 'false'
    'advancedGraphics.shadowParticleShadows'            = 'false'
    'advancedGraphics.ssroSampleCount'                  = '1'
    'advancedGraphics.waterReflectionSSR'               = 'false'
    'advancedGraphics.waterReflectionResX'              = '384'
    'advancedGraphics.waterReflectionResY'              = '192'
}

# O que faz a imagem ficar menos nítida sem sumir com nada.
$textureSoften = [ordered]@{
    'advancedGraphics.manualMipBias'                    = '-1.500000'
    'advancedGraphics.texLodScale_Global'               = '1.000000'
    'advancedGraphics.texLodScale_Buildings'            = '1.000000'
    'advancedGraphics.texLodScale_Objects'              = '1.000000'
    'advancedGraphics.texLodScale_HD'                   = '1.000000'
    'advancedGraphics.sharpenEnabled'                   = 'true'
    'advancedGraphics.fsrSharpen'                       = '0.200000'
    'advancedGraphics.maxTexUpgradesPerFrame'           = '5'
}

# Monta um perfil juntando camadas; a última vence. (O `+` de dicionários
# ordenados do PowerShell estoura com chave repetida, e os perfis repetem
# chaves de propósito.)
function New-Profile {
    $out = [ordered]@{}
    foreach ($layer in $args) {
        if ($null -eq $layer) { continue }
        foreach ($k in $layer.Keys) { $out[$k] = $layer[$k] }
    }
    return $out
}

$profiles = @{
    balanced = New-Profile $pixelCuts $textureSoften ([ordered]@{
        'video.uiResolution.width'                      = '1280'
        'video.uiResolution.height'                     = '720'
        'video.resolutionFullscreen.width'              = '1280'
        'video.resolutionFullscreen.height'             = '720'
        'graphics.anisotropicFiltering'                 = '0'
        'advancedGraphics.decorationResolutionShift'    = '1'
        'advancedGraphics.grassLod'                     = '0.400000'
        'advancedGraphics.lodScale'                     = '0.800000'
        'advancedGraphics.smallCullThreshold'           = '1.500000'
    })
    maxfps = New-Profile $pixelCuts $textureSoften ([ordered]@{
        'video.uiResolution.width'                      = '640'
        'video.uiResolution.height'                     = '480'
        'video.resolutionFullscreen.width'              = '640'
        'video.resolutionFullscreen.height'             = '480'
        'graphics.anisotropicFiltering'                 = '0'
        'advancedGraphics.decorationResolutionShift'    = '2'
        'advancedGraphics.grassLod'                     = '0.000000'
        'advancedGraphics.lodScale'                     = '0.600000'
        'advancedGraphics.smallCullThreshold'           = '2.000000'
        'advancedGraphics.texLodScale_Global'           = '2.000000'
        'advancedGraphics.texLodScale_Buildings'        = '2.000000'
        'advancedGraphics.texLodScale_Objects'          = '2.000000'
        'advancedGraphics.texLodScale_HD'               = '2.000000'
        'advancedGraphics.ssroSampleCount'              = '0'
        'advancedGraphics.waterReflectionResX'          = '256'
        'advancedGraphics.waterReflectionResY'          = '128'
    })
    sharp = New-Profile $pixelCuts ([ordered]@{
        'video.uiResolution.width'                      = '1920'
        'video.uiResolution.height'                     = '1080'
        'video.resolutionFullscreen.width'              = '1280'
        'video.resolutionFullscreen.height'             = '720'
        'graphics.anisotropicFiltering'                 = '4'
        'advancedGraphics.decorationResolutionShift'    = '1'
        'advancedGraphics.grassLod'                     = '0.800000'
        'advancedGraphics.lodScale'                     = '1.000000'
        'advancedGraphics.smallCullThreshold'           = '1.000000'
        'advancedGraphics.maxTexUpgradesPerFrame'       = '5'
        'advancedGraphics.manualMipBias'                = '0.000000'
        'advancedGraphics.texLodScale_Global'           = '1.000000'
        'advancedGraphics.texLodScale_Buildings'        = '1.000000'
        'advancedGraphics.texLodScale_Objects'          = '1.000000'
        'advancedGraphics.texLodScale_HD'               = '1.000000'
        'advancedGraphics.sharpenEnabled'               = 'true'
        'advancedGraphics.fsrSharpen'                   = '0.500000'
    })
}

if (-not $Path) { $Path = Find-Settings }
if (-not $Path -or -not (Test-Path $Path)) {
    Write-Error "settings.xml não encontrado. Rode o jogo uma vez para ele criar o arquivo, ou passe -Path."
    exit 1
}
$Path = (Resolve-Path $Path).Path

if ($Profile -eq 'restore') {
    $backup = Get-ChildItem -Path (Split-Path $Path) -Filter ((Split-Path $Path -Leaf) + '.bak-*') -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    if (-not $backup) { Write-Error "nenhum backup ao lado de $Path"; exit 1 }
    if (-not $DryRun) { Copy-Item $backup.FullName $Path -Force }
    Write-Output "restaurado $($backup.Name) -> $Path"
    exit 0
}

$running = @(Get-Process victory, GTA6 -ErrorAction SilentlyContinue)
if ($running.Count -gt 0 -and -not $Force) {
    Write-Error ("o jogo está rodando (pid " + (($running | ForEach-Object { $_.Id }) -join ', ') + "); feche antes. O título regrava o settings.xml ao sair. Use -Force para ignorar.")
    exit 2
}

$text = Get-Content -Path $Path -Raw -Encoding UTF8
$changes = [System.Collections.Generic.List[string]]::new()

# Cada elemento vira <tag value="..." />. O primeiro <tag ...> no arquivo é o
# único dessa folha: os nomes do RAGE não se repetem entre <graphics>,
# <advancedGraphics> e <video>.
foreach ($key in $profiles[$Profile].Keys) {
    $parts = $key.Split('.')
    $tag = $parts[-1]
    $want = $profiles[$Profile][$key]
    $m = [regex]::Match($text, "<$tag\s+value=""([^""]*)""")
    if (-not $m.Success) {
        Write-Warning "settings.xml não tem <$tag>; pulando $key"
        continue
    }
    $old = $m.Groups[1].Value
    if ($old -ne $want) {
        $text = $text.Substring(0, $m.Groups[1].Index) + $want + $text.Substring($m.Groups[1].Index + $m.Groups[1].Length)
        $changes.Add("$key`: $old -> $want")
    }
}

foreach ($item in $Set) {
    $key, $sep, $value = $item.Partition('=')
    if (-not $sep) { Write-Error "mau -Set '$item' (use caminho=valor)"; exit 1 }
    $tag = $key.Split('.')[-1]
    $m = [regex]::Match($text, "<$tag\s+value=""([^""]*)""")
    if (-not $m.Success) { Write-Error "settings.xml não tem <$tag>"; exit 1 }
    $old = $m.Groups[1].Value
    if ($old -ne $value) {
        $text = $text.Substring(0, $m.Groups[1].Index) + $value + $text.Substring($m.Groups[1].Index + $m.Groups[1].Length)
        $changes.Add("$key`: $old -> $value")
    }
}

if ($changes.Count -eq 0) { Write-Output "já está assim; nada a fazer"; exit 0 }
$changes | ForEach-Object { Write-Output $_ }

if ($DryRun) { Write-Output "dry run: $($changes.Count) mudanças, nada gravado"; exit 0 }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = "$Path.bak-$stamp"
Copy-Item -Path $Path -Destination $backup -Force
[System.IO.File]::WriteAllText($Path, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "backup  $backup"
Write-Output "gravado $Path ($($changes.Count) mudanças, perfil $Profile)"
