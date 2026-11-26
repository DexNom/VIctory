# Monta o GTA6.exe unico: launcher + runtime + Agility SDK + docs.
# Uso: .\build.ps1 [-Out <pasta>] [-Release <pasta do victory.exe>]
param(
    [string]$Out = "C:\GTA6-PC",
    [string]$Release = ""
)
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
if ($Release -eq "") {
    $Release = Join-Path $root "target\release"
}
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

# O Agility SDK 1.620: o d3d12.dll do Windows 10 nao tem o que o titulo pede,
# e o runtime declara D3D12SDKVersion/D3D12SDKPath para carregar esta copia.
$d3d = Join-Path $PSScriptRoot "D3D12\D3D12Core.dll"
if (-not (Test-Path $d3d)) {
    $d3d = Join-Path $Out "D3D12\D3D12Core.dll"
}

$files = @(
    "$Release\victory.exe",
    $d3d,
    "$root\CONTROLES.md",
    "$root\OTIMIZACOES.md"
)
foreach ($file in $files) {
    if (-not (Test-Path $file)) { throw "Falta $file" }
}

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$target = Join-Path $Out "GTA6.exe"

& $csc /nologo /target:winexe /codepage:65001 /out:"$target" `
    /reference:System.dll /reference:System.Core.dll `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    "/resource:$Release\victory.exe,victory.exe" `
    "/resource:$d3d,D3D12Core.dll" `
    "/resource:$root\CONTROLES.md,CONTROLES.md" `
    "/resource:$root\OTIMIZACOES.md,OTIMIZACOES.md" `
    "$PSScriptRoot\GTA6.cs"

if ($LASTEXITCODE -ne 0) { throw "csc falhou com codigo $LASTEXITCODE" }
Write-Host "GTA6.exe -> $target ($([math]::Round((Get-Item $target).Length / 1MB, 1)) MB)"
