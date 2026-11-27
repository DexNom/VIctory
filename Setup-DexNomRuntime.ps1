[CmdletBinding()]
param(
    [string] $GamePath,
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'DexNom Runtime'),
    [string] $ShortcutDirectory = [Environment]::GetFolderPath('Programs')
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT' -or ![Environment]::Is64BitOperatingSystem) {
    throw 'DexNom RayTime requires 64-bit Windows.'
}
$InstallDir = [IO.Path]::GetFullPath($InstallDir)
for ($parent = [IO.DirectoryInfo]::new($InstallDir); $parent; $parent = $parent.Parent) {
    if (Test-Path -LiteralPath (Join-Path $parent.FullName '.victory-root')) {
        throw 'Choose an install directory outside the source checkout so the launcher can write saves and shader caches.'
    }
}

$config = Join-Path $InstallDir 'game-path.txt'
$binaries = 'victory-play.exe', 'victory-warm.exe', 'victory.exe'
# Both launchers require victory.exe beside them. Remove it first and publish
# or restore it last, so a stopped setup cannot launch a mixed build.
$publishOrder = 'victory-play.exe', 'victory-warm.exe', 'game-path.txt', 'victory.exe'
$transaction = Join-Path $InstallDir '.dexnom-setup-transaction'
$backup = Join-Path $transaction 'backup'
$pending = Join-Path $transaction 'pending'

function Remove-TransactionFile([string] $Path) {
    try {
        $attributes = [IO.File]::GetAttributes($Path)
    } catch [IO.FileNotFoundException] {
        return
    }
    if ($attributes -band ([IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint)) {
        throw "Expected an ordinary transaction file at '$Path'."
    }
    if (!($attributes -band [IO.FileAttributes]::ReadOnly)) {
        [IO.File]::Delete($Path)
        return
    }

    # Windows can delete a read-only file without changing its attributes,
    # which would also change the attributes seen through any other hard link.
    if (!('DexNomRuntime.TransactionFiles' -as [type])) {
        Add-Type -Namespace DexNomRuntime -Name TransactionFiles -MemberDefinition @'
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(
        string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool SetFileInformationByHandle(
        Microsoft.Win32.SafeHandles.SafeFileHandle file, int infoClass, ref uint flags, uint size);

    public static void DeleteReadOnly(string path) {
        const uint deleteAccess = 0x00010000, writeAttributesAccess = 0x00000100;
        const uint openExisting = 3, openReparsePoint = 0x00200000;
        const int fileDispositionInfoEx = 21;
        const uint delete = 1, ignoreReadOnly = 16;
        using (var file = CreateFileW(path, deleteAccess | writeAttributesAccess, 0, IntPtr.Zero,
                                     openExisting, openReparsePoint, IntPtr.Zero)) {
            uint disposition = delete | ignoreReadOnly;
            if (file.IsInvalid || !SetFileInformationByHandle(file, fileDispositionInfoEx, ref disposition, sizeof(uint))) {
                var error = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                throw new System.IO.IOException("Could not delete '" + path + "': " + error.Message, error);
            }
        }
    }
'@
    }
    [DexNomRuntime.TransactionFiles]::DeleteReadOnly($Path)
}

function Clear-InstallTransaction {
    # Clear the marker before removing files used to distinguish commit from rollback.
    if ([IO.Directory]::Exists($pending)) { [IO.Directory]::Delete($pending) }
    foreach ($directory in $backup, $transaction) {
        if ([IO.Directory]::Exists($directory)) {
            foreach ($name in $publishOrder) { Remove-TransactionFile (Join-Path $directory $name) }
            [IO.Directory]::Delete($directory)
        }
    }
}

function Recover-InstallTransaction {
    foreach ($path in $transaction, $backup, $pending) {
        if (Test-Path -LiteralPath $path) {
            $item = Get-Item -LiteralPath $path -Force
            if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Expected an ordinary setup directory at '$path'."
            }
        }
    }
    if ([IO.Directory]::Exists($pending)) {
        # Enumeration reports an inaccessible directory instead of treating
        # its files as missing and discarding a potentially uncommitted backup.
        $stagedFiles = [IO.Directory]::GetFiles($transaction)
        $backedUpFiles = [IO.Directory]::GetFiles($backup)
        if ($stagedFiles -contains (Join-Path $transaction 'victory.exe')) {
            # A missing staged file has been published. Move it back before
            # restoring its backup so rollback can itself be interrupted and retried.
            foreach ($name in $publishOrder) {
                $staged = Join-Path $transaction $name
                $destination = Join-Path $InstallDir $name
                $original = Join-Path $backup $name
                if ($stagedFiles -notcontains $staged) { [IO.File]::Move($destination, $staged) }
                if ($backedUpFiles -contains $original) { [IO.File]::Move($original, $destination) }
            }
        } elseif (![IO.File]::Exists((Join-Path $InstallDir 'victory.exe'))) {
            throw 'The previous setup has no staged or installed runtime. The backup has been kept for recovery.'
        }
    }
    Clear-InstallTransaction
}

# Use the same lock as Play and Preload, including when resuming an interrupted setup.
$state = [IO.Directory]::CreateDirectory((Join-Path $InstallDir 'state'))
try {
    $lock = [IO.File]::Open((Join-Path $state.FullName 'launcher.lock'), 'OpenOrCreate', 'Write', 'None')
} catch [IO.IOException] {
    throw "Could not lock the install directory. Close DexNom RayTime and shader preload, or wait for the other setup to finish. $($_.Exception.Message)"
}
try {
    try {
        Recover-InstallTransaction
    } catch {
        throw "Could not recover the previous setup. Close any programs using the installation and run Setup again. Recovery files are in '$transaction'. $($_.Exception.Message)"
    }
    if (!(Get-Command rustup -ErrorAction SilentlyContinue) -or !(Get-Command cargo -ErrorAction SilentlyContinue)) {
        throw 'Install Rust for Windows from https://rustup.rs, then reopen Setup.cmd.'
    }
    $reuseSavedPath = $false
    if (!$GamePath -and (Test-Path -LiteralPath $config -PathType Leaf)) {
        $saved = [IO.File]::ReadAllText($config).Trim()
        if ($saved -and (Test-Path -LiteralPath $saved -PathType Leaf)) {
            $GamePath = $saved
            $reuseSavedPath = $true
        }
    }
    if (!$GamePath) {
        Add-Type -AssemblyName System.Windows.Forms
        $picker = [Windows.Forms.OpenFileDialog]::new()
        try {
            $picker.Title = 'Select the Series X game executable from your extracted game installation'
            $picker.Filter = 'Game executable (*.exe)|*.exe'
            if ($picker.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) {
                Write-Host 'Setup cancelled.'
                return
            }
            $GamePath = $picker.FileName
        } finally { $picker.Dispose() }
    }
    $game = Get-Item -LiteralPath $GamePath
    if ($game.PSIsContainer -or $game.Extension -ine '.exe') {
        throw 'Select the game executable in its original installation directory.'
    }

    try {
        Push-Location -LiteralPath $PSScriptRoot
        try {
            $targetDir = Join-Path $PSScriptRoot 'target'
            [IO.Directory]::CreateDirectory($targetDir) | Out-Null
            try {
                # Cargo releases its own lock before setup has copied the build.
                $buildLock = [IO.File]::Open((Join-Path $targetDir 'dexnom-setup.lock'), 'OpenOrCreate', 'Write', 'None')
            } catch [IO.IOException] {
                throw "Could not lock the build directory. Wait for the other setup using this checkout to finish. $($_.Exception.Message)"
            }
            try {
                & (Join-Path $PSScriptRoot 'Initialize-FormatTables.ps1')
                $target = 'x86_64-pc-windows-msvc'
                & cargo build --locked --release -p victory --features diagnostics --bins --target $target --target-dir $targetDir
                if ($LASTEXITCODE -ne 0) {
                    throw 'Build failed. Check the compiler output and the C++ workload and Windows SDK listed in the README.'
                }
                $release = Join-Path $targetDir "$target/release"
                & (Join-Path $release 'victory.exe') inspect $game.FullName | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'The runtime could not read the selected game executable.' }
                [IO.Directory]::CreateDirectory($backup) | Out-Null
                foreach ($name in $binaries) {
                    [IO.File]::Copy((Join-Path $release $name), (Join-Path $transaction $name))
                }
            } finally { $buildLock.Dispose() }
        } finally { Pop-Location }
        $stagedConfig = Join-Path $transaction 'game-path.txt'
        if ($reuseSavedPath) {
            [IO.File]::Copy($config, $stagedConfig)
        } else {
            [IO.File]::WriteAllText($stagedConfig, $game.FullName, [Text.UTF8Encoding]::new($false))
        }

        [IO.Directory]::CreateDirectory($pending) | Out-Null
        foreach ($name in 'victory.exe', 'victory-play.exe', 'victory-warm.exe', 'game-path.txt') {
            $destination = Join-Path $InstallDir $name
            if ([IO.File]::Exists($destination)) {
                [IO.File]::Move($destination, (Join-Path $backup $name))
            }
        }
        foreach ($name in $publishOrder) {
            [IO.File]::Move((Join-Path $transaction $name), (Join-Path $InstallDir $name))
        }
    } catch {
        $installError = $_
        try {
            Recover-InstallTransaction
        } catch {
            throw "Setup failed: $($installError.Exception.Message) Recovery could not finish: $($_.Exception.Message) Close any programs using the installation and run Setup again. Recovery files are in '$transaction'."
        }
        throw $installError
    }

    try {
        Clear-InstallTransaction
    } catch {
        Write-Warning "The runtime was installed, but setup could not remove '$transaction'. The next setup will retry cleanup. $($_.Exception.Message)"
    }

    $launcher = Join-Path $InstallDir 'victory-play.exe'
    Write-Host "Installed DexNom RayTime in $InstallDir"
    [IO.Directory]::CreateDirectory($ShortcutDirectory) | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    foreach ($entry in @(
        @{ Name = 'DexNom RayTime'; Arguments = '' }
        @{ Name = 'DexNom RayTime (Experimental RT)'; Arguments = '--rt' }
        @{ Name = 'DexNom RayTime (FSR Quality 4K)'; Arguments = '--rt --console series-x --local-gpu-memory --fsr-quality --sparse-textures --no-upload-ahead --capture-readbacks --rt-write-stats' }
    )) {
        $shortcut = $shell.CreateShortcut((Join-Path $ShortcutDirectory ($entry.Name + '.lnk')))
        $shortcut.TargetPath = $launcher
        $shortcut.Arguments = $entry.Arguments
        $shortcut.WorkingDirectory = $InstallDir
        $shortcut.Save()
    }
    Write-Host "Open DexNom RayTime from $ShortcutDirectory. The FSR Quality 4K shortcut uses the tested ray-tracing profile."
} finally { $lock.Dispose() }
