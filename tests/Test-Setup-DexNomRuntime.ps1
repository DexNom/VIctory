[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$setup = Join-Path $PSScriptRoot '../Setup-DexNomRuntime.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('DexNom Setup tests ' + [Guid]::NewGuid())
$checkout = Join-Path $root 'source'
$release = Join-Path $checkout 'target/x86_64-pc-windows-msvc/release'
$runtime = Join-Path $release 'victory.exe'
$binaries = 'victory.exe', 'victory-play.exe', 'victory-warm.exe'
$files = $binaries + 'game-path.txt'
$originalPath = $env:PATH
$tools = Join-Path $root 'tools'
$failBuild = Join-Path $tools 'fail-build'

function Assert-Equal($Actual, $Expected, [string] $Message) {
    if ($Actual -cne $Expected) {
        throw "$Message Expected '$Expected', found '$Actual'."
    }
}

function Assert-Fails([scriptblock] $Action, [string] $MessagePattern) {
    try {
        & $Action
    } catch {
        if ($_.Exception.Message -notmatch $MessagePattern) { throw }
        return
    }
    throw "Setup unexpectedly succeeded. Expected an error matching '$MessagePattern'."
}

function Get-Fingerprints([string] $Directory, [string[]] $Names) {
    foreach ($name in $Names) {
        $path = Join-Path $Directory $name
        if ([IO.File]::Exists($path)) {
            (Get-FileHash -LiteralPath $path).Hash
        } else {
            '<missing>'
        }
    }
}

function New-InstallFixture([string] $Name) {
    $install = Join-Path $root $Name
    [IO.Directory]::CreateDirectory($install) | Out-Null
    foreach ($name in $binaries) {
        [IO.File]::WriteAllText((Join-Path $install $name), "old $name")
    }
    [IO.File]::WriteAllText((Join-Path $install 'game-path.txt'), "`r`n$oldGame`r`n", [Text.UTF8Encoding]::new($true))
    foreach ($name in $userFiles) {
        $path = Join-Path $install $name
        [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
        [IO.File]::WriteAllText($path, "user $name")
    }
    $install
}

function Invoke-Setup([string] $Install, [string] $Game) {
    $options = @{
        InstallDir = $Install
        ShortcutDirectory = Join-Path $root 'Shortcuts'
    }
    if ($Game) { $options.GamePath = $Game }
    & (Join-Path $checkout 'Setup-DexNomRuntime.ps1') @options
}

function Assert-TransactionRemoved([string] $Install) {
    Assert-Equal (Test-Path -LiteralPath (Join-Path $Install '.dexnom-setup-transaction')) $false 'Setup left transaction files.'
}

[IO.Directory]::CreateDirectory($release) | Out-Null
try {
    Copy-Item -LiteralPath $setup -Destination $checkout
    [IO.File]::WriteAllText((Join-Path $checkout 'Initialize-FormatTables.ps1'), '# Synthetic fixture')
    $oldGame = Join-Path $root 'Old title.exe'
    $newGame = Join-Path $root 'New title.exe'
    $invalidGame = Join-Path $root 'Invalid title.exe'
    foreach ($game in $oldGame, $newGame, $invalidGame) { [IO.File]::WriteAllText($game, 'synthetic input') }
    $inspector = Join-Path $root 'Inspector.cs'
    [IO.File]::WriteAllText($inspector, @'
public static class Inspector
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "build")
            return System.IO.File.Exists(System.IO.Path.Combine(
                System.AppDomain.CurrentDomain.BaseDirectory, "fail-build")) ? 17 : 0;
        return args.Length == 2 && args[0] == "inspect"
            && System.IO.File.Exists(args[1]) && !args[1].EndsWith("Invalid title.exe") ? 0 : 1;
    }
}
'@)
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    & $compiler /nologo /target:exe "/out:$runtime" $inspector
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile the synthetic inspector.' }
    [IO.Directory]::CreateDirectory($tools) | Out-Null
    foreach ($name in 'cargo.exe', 'rustup.exe') { [IO.File]::Copy($runtime, (Join-Path $tools $name)) }
    $env:PATH = "$tools;$originalPath"
    foreach ($name in 'victory-play.exe', 'victory-warm.exe') {
        [IO.File]::WriteAllText((Join-Path $release $name), "new $name")
    }
    $newBinaries = (Get-Fingerprints $release $binaries) -join ','
    $userFiles = 'victory.toml', 'state/save.dat', 'cache/shader.dat', 'logs/older.txt'

    $install = New-InstallFixture 'Locked launcher'
    $before = (Get-Fingerprints $install ($files + $userFiles)) -join ','
    $held = [IO.File]::Open((Join-Path $install 'victory-play.exe'), 'Open', 'Read', 'Read')
    try {
        Assert-Fails { Invoke-Setup $install $newGame } 'Move|Replace'
    } finally { $held.Dispose() }
    Assert-Equal ((Get-Fingerprints $install ($files + $userFiles)) -join ',') $before 'File locking changed the installed files.'
    Assert-TransactionRemoved $install

    $install = New-InstallFixture 'Missing output'
    $before = (Get-Fingerprints $install $files) -join ','
    $warmer = Join-Path $release 'victory-warm.exe'
    [IO.File]::Move($warmer, "$warmer.hold")
    try {
        Assert-Fails { Invoke-Setup $install $newGame } 'victory-warm\.exe'
    } finally { [IO.File]::Move("$warmer.hold", $warmer) }
    Assert-Equal ((Get-Fingerprints $install $files) -join ',') $before 'Incomplete build changed the installed files.'
    Assert-Fails { Invoke-Setup $install $invalidGame } 'could not read the selected game'
    Assert-Equal ((Get-Fingerprints $install $files) -join ',') $before 'Failed inspection changed the installed files.'
    Assert-TransactionRemoved $install

    $install = New-InstallFixture 'Publication failure'
    $config = Join-Path $install 'game-path.txt'
    [IO.File]::Delete($config)
    [IO.File]::Delete((Join-Path $install 'victory-warm.exe'))
    [IO.Directory]::CreateDirectory($config) | Out-Null
    $before = (Get-Fingerprints $install ($files + $userFiles)) -join ','
    Assert-Fails { Invoke-Setup $install $newGame } 'Move'
    Assert-Equal ((Get-Fingerprints $install ($files + $userFiles)) -join ',') $before 'Failed publication changed the installed files.'
    Assert-Equal ([IO.Directory]::Exists($config)) $true 'Rollback removed the existing directory.'
    Assert-TransactionRemoved $install

    $install = New-InstallFixture 'Concurrent setup or launcher'
    $before = (Get-Fingerprints $install ($files + $userFiles)) -join ','
    $held = [IO.File]::Open((Join-Path $install 'state/launcher.lock'), 'OpenOrCreate', 'Write', 'None')
    try {
        Assert-Fails { Invoke-Setup $install $newGame } 'Could not lock the install directory'
    } finally { $held.Dispose() }
    Assert-Equal ((Get-Fingerprints $install ($files + $userFiles)) -join ',') $before 'Lock contention changed the installed files.'
    Assert-TransactionRemoved $install

    $held = [IO.File]::Open((Join-Path $checkout 'target/dexnom-setup.lock'), 'OpenOrCreate', 'Write', 'None')
    try {
        Assert-Fails { Invoke-Setup $install $newGame } 'Could not lock the build directory'
    } finally { $held.Dispose() }
    Assert-Equal ((Get-Fingerprints $install ($files + $userFiles)) -join ',') $before 'Build lock contention changed the installed files.'
    Assert-TransactionRemoved $install

    foreach ($phase in 'staging', 'backup', 'publishing', 'rollback', 'committed') {
        $install = New-InstallFixture "Interrupted $phase"
        $before = (Get-Fingerprints $install ($files + $userFiles)) -join ','
        $transaction = Join-Path $install '.dexnom-setup-transaction'
        $backup = Join-Path $transaction 'backup'
        [IO.Directory]::CreateDirectory($backup) | Out-Null
        foreach ($name in $binaries) { [IO.File]::Copy((Join-Path $release $name), (Join-Path $transaction $name)) }
        [IO.File]::WriteAllText((Join-Path $transaction 'game-path.txt'), $newGame)
        if ($phase -ne 'staging') {
            [IO.Directory]::CreateDirectory((Join-Path $transaction 'pending')) | Out-Null
            foreach ($name in $files) {
                [IO.File]::Move((Join-Path $install $name), (Join-Path $backup $name))
                if ($phase -eq 'backup') { break }
            }
        }
        if ($phase -in 'publishing', 'rollback', 'committed') {
            foreach ($name in 'victory-play.exe', 'victory-warm.exe', 'game-path.txt', 'victory.exe') {
                if ($name -eq 'victory.exe' -and $phase -ne 'committed') { break }
                [IO.File]::Move((Join-Path $transaction $name), (Join-Path $install $name))
            }
        }
        if ($phase -eq 'rollback') {
            $name = 'victory-play.exe'
            [IO.File]::Move((Join-Path $install $name), (Join-Path $transaction $name))
            [IO.File]::Move((Join-Path $backup $name), (Join-Path $install $name))
            $held = [IO.File]::Open((Join-Path $install 'victory-warm.exe'), 'Open', 'Read', 'Read')
            try {
                Assert-Fails { Invoke-Setup $install } 'Could not recover the previous setup'
                Assert-Equal ([IO.File]::Exists((Join-Path $install 'victory.exe'))) $false 'A partial rollback exposed the runtime.'
                Assert-Equal ([IO.File]::Exists((Join-Path $backup 'victory.exe'))) $true 'A partial rollback lost the backup.'
            } finally { $held.Dispose() }
        } elseif ($phase -eq 'committed') {
            $before = (Get-Fingerprints $install ($files + $userFiles)) -join ','
        }
        [IO.File]::WriteAllText($failBuild, '')
        try {
            Assert-Fails { Invoke-Setup $install } 'Build failed'
        } finally { [IO.File]::Delete($failBuild) }
        Assert-Equal ((Get-Fingerprints $install ($files + $userFiles)) -join ',') $before "Recovery failed after $phase."
        Assert-TransactionRemoved $install
    }

    $install = Join-Path $root 'Fresh install'
    Invoke-Setup $install $oldGame
    Assert-Equal ((Get-Fingerprints $install $binaries) -join ',') $newBinaries 'Fresh install wrote the wrong binaries.'
    Assert-Equal ([IO.File]::ReadAllText((Join-Path $install 'game-path.txt'))) $oldGame 'Fresh install saved the wrong game path.'
    Assert-TransactionRemoved $install

    $install = New-InstallFixture 'Successful update'
    $before = (Get-Fingerprints $install ($userFiles + 'game-path.txt')) -join ','
    Invoke-Setup $install
    Assert-Equal ((Get-Fingerprints $install $binaries) -join ',') $newBinaries 'Update wrote the wrong binaries.'
    Assert-Equal ((Get-Fingerprints $install ($userFiles + 'game-path.txt')) -join ',') $before 'Update changed user data or the saved path.'
    Assert-TransactionRemoved $install
    Invoke-Setup $install $newGame
    Assert-Equal ([IO.File]::ReadAllText((Join-Path $install 'game-path.txt'))) $newGame 'Explicit game path change was not saved.'
    $shell = New-Object -ComObject WScript.Shell
    foreach ($entry in @(
        @{ Name = 'DexNom RayTime'; Arguments = '' }
        @{ Name = 'DexNom RayTime (Experimental RT)'; Arguments = '--rt' }
        @{ Name = 'DexNom RayTime (FSR Quality 4K)'; Arguments = '--rt --console series-x --local-gpu-memory --fsr-quality --sparse-textures --no-upload-ahead --capture-readbacks --rt-write-stats' }
    )) {
        $path = Join-Path $root ('Shortcuts/' + $entry.Name + '.lnk')
        Assert-Equal ([IO.File]::Exists($path)) $true "Shortcut '$($entry.Name)' is missing."
        $shortcut = $shell.CreateShortcut($path)
        Assert-Equal $shortcut.TargetPath (Join-Path $install 'victory-play.exe') "Shortcut '$($entry.Name)' has the wrong target."
        Assert-Equal $shortcut.WorkingDirectory $install "Shortcut '$($entry.Name)' has the wrong working directory."
        Assert-Equal $shortcut.Arguments $entry.Arguments "Shortcut '$($entry.Name)' has the wrong arguments."
    }
    Assert-TransactionRemoved $install

    $install = New-InstallFixture 'Read-only saved path'
    $config = Join-Path $install 'game-path.txt'
    $configAttributes = [IO.FileAttributes]::ReadOnly -bor [IO.FileAttributes]::Hidden
    [IO.File]::SetAttributes($config, $configAttributes)
    $oldWarmer = Join-Path $install 'victory-warm.exe'
    [IO.File]::SetAttributes($oldWarmer, [IO.FileAttributes]::ReadOnly)
    $before = (Get-Fingerprints $install ($userFiles + 'game-path.txt')) -join ','
    foreach ($attempt in 1, 2) {
        Invoke-Setup $install
        Assert-TransactionRemoved $install
        Assert-Equal ((Get-Fingerprints $install ($userFiles + 'game-path.txt')) -join ',') $before "Update $attempt changed saved data."
        $updatedAttributes = [IO.File]::GetAttributes($config) -band $configAttributes
        Assert-Equal $updatedAttributes $configAttributes "Update $attempt lost read-only or hidden config attributes."
        Assert-Equal ((Get-Fingerprints $install $binaries) -join ',') $newBinaries "Update $attempt wrote the wrong binaries."
    }

    $install = New-InstallFixture 'Read-only rollback'
    $config = Join-Path $install 'game-path.txt'
    [IO.File]::SetAttributes($config, $configAttributes)
    $oldWarmer = Join-Path $install 'victory-warm.exe'
    [IO.File]::SetAttributes($oldWarmer, [IO.FileAttributes]::ReadOnly)
    $runtimeConflict = Join-Path $install 'victory.exe'
    [IO.File]::Delete($runtimeConflict)
    [IO.Directory]::CreateDirectory($runtimeConflict) | Out-Null
    $before = (Get-Fingerprints $install ($files + $userFiles)) -join ','
    $newWarmer = Join-Path $release 'victory-warm.exe'
    $newWarmerAttributes = [IO.File]::GetAttributes($newWarmer)
    [IO.File]::SetAttributes($newWarmer, ($newWarmerAttributes -bor [IO.FileAttributes]::ReadOnly))
    try {
        Assert-Fails { Invoke-Setup $install $newGame } 'Move'
    } finally { [IO.File]::SetAttributes($newWarmer, $newWarmerAttributes) }
    Assert-TransactionRemoved $install
    Assert-Equal ((Get-Fingerprints $install ($files + $userFiles)) -join ',') $before 'Rollback changed files.'
    # Windows can set Archive when a file is renamed. The protection flags must survive.
    $excludeArchive = -bnot [IO.FileAttributes]::Archive
    Assert-Equal ([IO.File]::GetAttributes($config) -band $excludeArchive) $configAttributes 'Rollback changed the config protection flags.'
    Assert-Equal ([IO.File]::GetAttributes($oldWarmer) -band $excludeArchive) ([IO.FileAttributes]::ReadOnly) 'Rollback changed the warmer protection flags.'
    [IO.Directory]::Delete($runtimeConflict)
    Invoke-Setup $install
    Assert-TransactionRemoved $install

    $backup = Join-Path $install '.dexnom-setup-transaction/backup'
    [IO.Directory]::CreateDirectory($backup) | Out-Null
    $savedBackup = Join-Path $backup 'game-path.txt'
    [IO.File]::Copy($config, $savedBackup)
    $backupAttributes = [IO.File]::GetAttributes($savedBackup)
    $held = [IO.File]::Open($savedBackup, 'Open', 'Read', 'Read')
    try {
        Assert-Fails { Invoke-Setup $install } 'Could not recover the previous setup.*game-path\.txt'
        Assert-Equal ([IO.File]::GetAttributes($savedBackup)) $backupAttributes 'Blocked cleanup changed backup attributes.'
    } finally { $held.Dispose() }
    Invoke-Setup $install
    Assert-TransactionRemoved $install

    Write-Output "Installer regression tests passed on PowerShell $($PSVersionTable.PSVersion)."
} finally {
    $env:PATH = $originalPath
    # Only the synthetic fixtures in this test tree may have been marked read-only.
    foreach ($path in [IO.Directory]::GetFiles($root, '*', [IO.SearchOption]::AllDirectories)) {
        $attributes = [IO.File]::GetAttributes($path)
        if ($attributes -band [IO.FileAttributes]::ReadOnly) {
            [IO.File]::SetAttributes($path, ($attributes -band (-bnot [IO.FileAttributes]::ReadOnly)))
        }
    }
    [IO.Directory]::Delete($root, $true)
}
