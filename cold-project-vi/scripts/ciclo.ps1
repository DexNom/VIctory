#requires -Version 5.1
# Teste em ciclo (Ciclo.cmd na raiz): sempre que o VIctory nao esta a correr, espera 20 s e
# corre outra vez o teste automatico (jogar.ps1 -Minutos), que recompila primeiro (as melhorias
# novas entram sozinhas), joga sozinho e fecha. Tecla H nesta janela: para tudo, fechando o
# jogo como o botao X. So pode haver um ciclo aberto.
param([int] $Minutos = 6, [int] $Espera = 20)
$ErrorActionPreference = 'Stop'
$criado = $false
$unico = New-Object System.Threading.Mutex($true, 'Local\VIctoryCiclo', [ref]$criado)
if (!$criado) { Write-Host 'Ja ha um ciclo do VIctory aberto. Esta janela fecha-se.'; Start-Sleep -Seconds 5; exit 1 }

function Test-Parar {
    while ([Console]::KeyAvailable) {
        if ([Console]::ReadKey($true).Key -eq [ConsoleKey]::H) { return $true }
    }
    return $false
}
function Stop-Jogo {
    Get-Process -Name victory -ErrorAction SilentlyContinue | ForEach-Object { [void]$_.CloseMainWindow() }
    $ate = (Get-Date).AddMinutes(2)
    while ((Get-Date) -lt $ate -and (Get-Process -Name victory -ErrorAction SilentlyContinue)) { Start-Sleep -Seconds 2 }
    Get-Process -Name victory -ErrorAction SilentlyContinue | Stop-Process -Force
}

# Ecra ligado e PC acordado so enquanto este ciclo esta aberto (pedido deste processo; as
# definicoes de energia nao mudam e o Windows volta ao normal quando o ciclo termina).
Add-Type -Namespace VIctory -Name Energia -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);'
$acordado = { [void][VIctory.Energia]::SetThreadExecutionState([uint32]2147483651) }   # CONTINUOUS | SYSTEM | DISPLAY
& $acordado

$jogar = Join-Path $PSScriptRoot 'jogar.ps1'
$logs = Join-Path $env:USERPROFILE '.codex/visualizations/2026/11/22/01a2020d-43d5-7ec3-a9e8-d56142dd6f82/victory-xtx-run/logs'

# Estado ao vivo nesta janela (lido do log da sessao; nao usa o Claude): FPS, ms por frame,
# desenhos, RAM livre e tempo da volta.
function Show-Estado([datetime] $inicio) {
    $log = Get-ChildItem -LiteralPath $logs -Filter 'jogo-*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $linha = ''
    if ($log) {
        try {
            $fs = [IO.File]::Open($log.FullName, 'Open', 'Read', 'ReadWrite')
            $fs.Seek([Math]::Max(0, $fs.Length - 400000), 'Begin') | Out-Null
            $texto = (New-Object IO.StreamReader($fs)).ReadToEnd(); $fs.Dispose()
            $m = [regex]::Matches($texto, 'host: ([0-9.]+) fps, ([0-9.]+) ms/frame.*?; ([0-9.]+) on the GPU.*?(\d+) draws/frame')
            if ($m.Count -gt 0) {
                $u = $m[$m.Count - 1].Groups
                $linha = "{0,5} FPS  {1,7} ms/frame  placa {2,6} ms  {3,5} desenhos" -f $u[1].Value, $u[2].Value, $u[3].Value, $u[4].Value
            } else { $linha = 'a arrancar (menus / intro)' }
            if ($texto -match 'device removed') { $linha += '  [PLACA PERDEU-SE]' }
        } catch { $linha = '(log ocupado)' }
    }
    $livre = [int]((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1KB)
    $min = [int]((Get-Date) - $inicio).TotalMinutes
    Write-Host ("[{0:HH:mm:ss}] volta {1}, {2} min | {3} | RAM livre {4} MB" -f (Get-Date), $volta, $min, $linha, $livre)
}
$ps = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$volta = 0
try {
    Write-Host "Ciclo do VIctory: um teste de $Minutos min sempre que o jogo nao esta a correr. Carrega em H para parar."
    while ($true) {
        & $acordado
        if (Test-Parar) { break }
        if (Get-Process -Name victory -ErrorAction SilentlyContinue) {
            Start-Sleep -Seconds 1
            continue
        }
        Write-Host "A espera $Espera s antes do proximo teste (H para parar)..."
        $parar = $false
        for ($i = 0; $i -lt $Espera; $i++) { if (Test-Parar) { $parar = $true; break }; Start-Sleep -Seconds 1 }
        if ($parar) { break }
        if (Get-Process -Name victory -ErrorAction SilentlyContinue) { continue }
        $volta++
        # So as capturas das ultimas 6 voltas ficam.
        $capturas = Join-Path $env:USERPROFILE '.codex/visualizations/2026/11/22/01a2020d-43d5-7ec3-a9e8-d56142dd6f82/victory-xtx-run/capturas'
        if (Test-Path -LiteralPath $capturas) {
            Get-ChildItem -LiteralPath $capturas -Directory | Sort-Object Name -Descending | Select-Object -Skip 5 |
                ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
        }
        Write-Host "`n=== Teste $volta ($(Get-Date -Format HH:mm)) ==="
        $filho = Start-Process -FilePath $ps -NoNewWindow -PassThru -ArgumentList @(
            '-NoLogo', '-NoProfile', '-ExecutionPolicy', 'RemoteSigned', '-File', "`"$jogar`"", '-Minutos', "$Minutos")
        $inicio = Get-Date
        $proximo = (Get-Date).AddSeconds(30)
        while (!$filho.HasExited) {
            if (Test-Parar) { $parar = $true; break }
            Start-Sleep -Seconds 1
            if ((Get-Date) -ge $proximo) {
                $proximo = (Get-Date).AddSeconds(15)
                Show-Estado $inicio
            }
        }
        if ($parar) {
            Write-Host 'H: a fechar o jogo e a parar...'
            Stop-Jogo
            if (!$filho.HasExited) { Stop-Process -Id $filho.Id -Force -ErrorAction SilentlyContinue }
            break
        }
    }
} finally {
    [void][VIctory.Energia]::SetThreadExecutionState([uint32]2147483648)   # CONTINUOUS: volta ao normal
    $unico.ReleaseMutex(); $unico.Dispose()
    Write-Host 'Ciclo parado.'
}
