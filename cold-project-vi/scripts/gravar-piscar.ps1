#requires -Version 5.1
param(
    [ValidateRange(1, 30)][int] $Segundos = 8,
    [ValidateRange(0, 60)][int] $Atraso = 5,
    [string] $Pasta = (Join-Path ([Environment]::GetFolderPath('MyVideos')) 'VIctory')
)
$ErrorActionPreference = 'Stop'
$ffmpeg = (Get-Command ffmpeg.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
if ($Atraso) { Start-Sleep -Seconds $Atraso }
$janelas = @(Get-Process -Name victory -ErrorAction SilentlyContinue | Where-Object {
    $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -like 'Americas | VIctory*'
})
if ($janelas.Count -ne 1) { throw 'Abre uma janela de jogo do VIctory antes de gravar.' }

$destino = [IO.Directory]::CreateDirectory($Pasta).FullName
$nome = 'piscar-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + $janelas[0].Id
$video = Join-Path $destino "$nome.mkv"
# O filtro seleciona apenas a janela do jogo e reduz a imagem antes de a copiar para a CPU.
$filtro = 'gfxcapture=window_exe=^victory.exe$:window_title=^Americas:width=640:height=360:resize_mode=scale_aspect:max_framerate=30:capture_cursor=0,hwdownload,format=bgra,format=yuv420p[v]'
$argumentos = @('-nostdin', '-hide_banner', '-loglevel', 'error', '-abort_on', 'empty_output', '-n', '-filter_complex', $filtro,
    '-map', '[v]', '-an', '-t', "$Segundos", '-fps_mode', 'vfr', '-threads', '1',
    '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '17', "$nome.mkv")
$inicio = New-Object System.Diagnostics.ProcessStartInfo
$inicio.FileName = $ffmpeg
$inicio.WorkingDirectory = $destino
$inicio.Arguments = $argumentos -join ' '
$inicio.UseShellExecute = $false
$inicio.CreateNoWindow = $true
$inicio.RedirectStandardError = $true
$gravacao = [Diagnostics.Process]::Start($inicio)
try {
    $erros = $gravacao.StandardError.ReadToEndAsync()
    if (!$gravacao.WaitForExit(($Segundos + 15) * 1000)) {
        $gravacao.Kill()
        $gravacao.WaitForExit()
        throw "A janela deixou de enviar imagens. A gravacao pode estar incompleta. $video"
    }
    if ($gravacao.ExitCode -ne 0 -or !(Test-Path -LiteralPath $video -PathType Leaf)) {
        throw "Falhou a captura da janela. $($erros.GetAwaiter().GetResult())"
    }
    Write-Output $video
} finally {
    $gravacao.Dispose()
}
