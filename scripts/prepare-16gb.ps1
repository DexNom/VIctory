# Compiles everything the game needs before a run, so that neither the shader
# translator nor the driver's compiler works in the middle of a frame.
#
#   scripts\prepare-16gb.ps1 -Exe <the build's victory.exe>
#   scripts\prepare-16gb.ps1 -Exe <...> -Shaders <shaders.rpf>   (also the shader cache)
#
# Two steps, once per change of the translator or the driver:
#   1. `victory shaders`: every shader in the archives translated into the
#      cache (minutes). Without it the run translates them as it meets them.
#   2. `victory warm --used`: the host pipelines of the ones earlier frames
#      drew with, made and let go, so the driver's cache on disk has them.
#
# The profile is scripts\profile-16gb.ps1, the same one play-16gb.ps1 uses: the
# key of a host pipeline includes the host's settings, and the key of a shader
# includes the translator's, so preparing with another profile makes work the
# game never asks for.
param(
    [Parameter(Mandatory)][string]$Exe,
    [string[]]$Shaders = @(),
    [hashtable]$More = @{}
)
$settings = & (Join-Path $PSScriptRoot 'profile-16gb.ps1') -More $More
$Exe = (Resolve-Path $Exe).Path
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

$cache = $env:VICTORY_CACHE_DIR
if ([string]::IsNullOrEmpty($cache)) {
    $cache = Join-Path $env:LOCALAPPDATA 'VIctory\cache'
}
$book = Join-Path $cache 'pipelines\known-pipelines.bin'

# Left over settings in this window go; where the files are stays.
$paths = 'VICTORY_CONFIG', 'VICTORY_GAME_DIR', 'VICTORY_CACHE_DIR', 'VICTORY_DATA_DIR'
$inherited = @(Get-ChildItem Env:VICTORY_* | Where-Object { $_.Name -notin $paths })
foreach ($entry in $inherited) { Remove-Item "Env:$($entry.Name)" }
foreach ($k in $settings.Keys) { Set-Item "Env:$k" $settings[$k] }

$jobs = [Math]::Max(1, [Environment]::ProcessorCount * 2 / 3)
Push-Location $root
try {
    if ($Shaders.Count -gt 0) {
        $began = Get-Date
        & $Exe shaders @Shaders --jobs $jobs
        $code = $LASTEXITCODE
        $took = [int]((Get-Date) - $began).TotalSeconds
        Write-Host "shaders terminou em ${took}s (exit $code)"
        if ($code -ne 0) { exit $code }
    }
    if (-not (Test-Path $book)) {
        Write-Host "Sem livro de pipelines: rode uma sessao com VICTORY_PIPELINE_BOOK=1 primeiro."
        Write-Host "  `$env:VICTORY_PIPELINE_BOOK='1'; scripts\play-16gb.ps1 -Exe <exe>"
        exit 2
    }
    $began = Get-Date
    & $Exe warm --used --jobs $jobs
    $code = $LASTEXITCODE
    $took = [int]((Get-Date) - $began).TotalSeconds
    Write-Host "warm terminou em ${took}s (exit $code)"
} finally {
    Pop-Location
}
exit $code
