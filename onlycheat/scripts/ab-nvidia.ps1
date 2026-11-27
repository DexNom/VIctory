# A/B de chaves na base NVIDIA (2026-11-27): rodadas alternadas "sem" e "com", cada uma abrindo o jogo pelo medidor,
# medindo -Segundos de cena sem ninguem mexer e fechando. A base sao as chaves do start-nvidia.bat (opcao 1, lidas do
# proprio .bat com NVIDIA_SO_CHAVES=1); -Chave vem por ultimo e vale sobre a base.
# Ex.: scripts\ab-nvidia.ps1 -Chave "VICTORY_THIN=0;VICTORY_ASYNC_SUBMIT=1" -Nome async
param([int]$Pares = 2, [Parameter(Mandatory)][string]$Chave, [Parameter(Mandatory)][string]$Nome, [int]$Segundos = 40, [string]$Opcao = "1")
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
if ($Chave -match 'BVH_BUILDER|VICTORY_RAYS') { throw "ray tracing proibido" }
$env:NVIDIA_SO_CHAVES = "1"
$base = (& cmd /c "`"$root\start-nvidia.bat`" $Opcao" | Select-Object -Last 1).Trim()
Remove-Item Env:NVIDIA_SO_CHAVES
if ($base -notmatch 'VICTORY_DLSS=1') { throw "chaves do start-nvidia.bat nao lidas: $base" }
$console = if ($Opcao -in '3', '4') { 'series-x' } else { 'series-s' }
# O que o .bat faz antes do medidor: dados do runtime no D:, GPU real.
$env:LOCALAPPDATA = "$root\.data\nvidia-local"
Remove-Item Env:VICTORY_D3D12_WARP, Env:VICTORY_WARP, Env:VICTORY_BVH_BUILDER -ErrorAction SilentlyContinue
$exe = "$root\runtime-rs-nvidia\target\release\victory.exe"
for ($i = 1; $i -le $Pares; $i++) {
    foreach ($com in @($false, $true)) {
        $env = if ($com) { "$base;$Chave" } else { $base }
        $label = if ($com) { "abn-$Nome-com-$i" } else { "abn-$Nome-sem-$i" }
        & powershell -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\medidor.ps1" -Exe $exe -Label $label -Segundos $Segundos -Console $console -MaxCarregar 1800 -AquecerMax 600 -Env $env | Out-Null
        $d = Get-ChildItem "$root\logs" -Directory -Filter "medidor-$label-*" | Sort-Object LastWriteTime | Select-Object -Last 1
        $r = Get-Content "$($d.FullName)\relatorio.md" -Encoding utf8
        $txt = (($r | Select-String 'fps mediana|uso m|draws/frame').Line) -join ' | '
        Write-Output "$label | $txt"
    }
}
