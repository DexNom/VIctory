# Espera a janela do VIctory aparecer e põe ela na posição e no tamanho salvos.
#   powershell -ExecutionPolicy Bypass -File scripts\janela.ps1            aplica o salvo
#   powershell -ExecutionPolicy Bypass -File scripts\janela.ps1 -Salvar    salva a posição/tamanho atuais
param([switch]$Salvar, [int]$EsperaSeg = 120)
$ErrorActionPreference = "Stop"
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WinPos {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
}
"@
# Coordenadas em pixels reais (per-monitor DPI aware v2).
[void][WinPos]::SetThreadDpiAwarenessContext([IntPtr]-4)
$cfg = Join-Path $PSScriptRoot "janela.json"

function Get-VictoryWindow {
    Get-Process victory -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
}

if ($Salvar) {
    $p = Get-VictoryWindow
    if (-not $p) { throw "victory sem janela" }
    $r = New-Object WinPos+RECT
    [void][WinPos]::GetWindowRect($p.MainWindowHandle, [ref]$r)
    $o = [ordered]@{ x = $r.L; y = $r.T; largura = $r.R - $r.L; altura = $r.B - $r.T }
    $o | ConvertTo-Json | Set-Content $cfg -Encoding utf8
    "salvo: $($o.x),$($o.y) $($o.largura)x$($o.altura)"
    return
}

if (-not (Test-Path $cfg)) { "sem janela.json; nada a aplicar"; return }
$c = Get-Content $cfg -Raw | ConvertFrom-Json
$limite = (Get-Date).AddSeconds($EsperaSeg)
$p = $null
while (-not ($p = Get-VictoryWindow) -and (Get-Date) -lt $limite) { Start-Sleep -Milliseconds 250 }
if (-not $p) { "janela do victory não apareceu em ${EsperaSeg}s"; return }
# SWP_NOZORDER (0x4) | SWP_NOACTIVATE (0x10)
[void][WinPos]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, $c.x, $c.y, $c.largura, $c.altura, 0x14)
"janela em $($c.x),$($c.y) $($c.largura)x$($c.altura)"
