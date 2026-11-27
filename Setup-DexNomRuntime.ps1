[CmdletBinding()]
param(
    [string] $GamePath,
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'DexNom Runtime'),
    [string] $ShortcutDirectory = [Environment]::GetFolderPath('Programs'),
    [switch] $SkipGpuCheck
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT' -or ![Environment]::Is64BitOperatingSystem) {
    throw 'DexNom RayTime requires 64-bit Windows.'
}
if (!(Get-Command rustup -ErrorAction SilentlyContinue) -or !(Get-Command cargo -ErrorAction SilentlyContinue)) {
    throw 'Install Rust for Windows from https://rustup.rs, then reopen Setup.cmd.'
}

# The game needs mesh shaders and Shader Model 6.6. Check before the long build so an unsupported GPU is found early.
# This only warns; -SkipGpuCheck skips it, and a non-interactive run continues after the warning.
if (!$SkipGpuCheck) {
    try {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class VictoryGpuInfo { public string Name; public bool Software; public bool Device; public bool Mesh; public int Model; }
public static class VictoryGpuProbe {
    [DllImport("d3d12.dll")] static extern int D3D12CreateDevice(IntPtr a, int fl, ref Guid iid, out IntPtr dev);
    [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr f);
    delegate int EnumD(IntPtr s, uint i, out IntPtr a);
    delegate int DescD(IntPtr s, IntPtr d);
    delegate int CheckD(IntPtr s, int f, IntPtr d, int n);
    delegate uint RelD(IntPtr s);
    static IntPtr Slot(IntPtr o, int i) { return Marshal.ReadIntPtr(Marshal.ReadIntPtr(o), i * IntPtr.Size); }
    static void Rel(IntPtr o) { if (o != IntPtr.Zero) ((RelD)Marshal.GetDelegateForFunctionPointer(Slot(o, 2), typeof(RelD)))(o); }
    static int Check(IntPtr dev, int feature, IntPtr p, int n) {
        return ((CheckD)Marshal.GetDelegateForFunctionPointer(Slot(dev, 13), typeof(CheckD)))(dev, feature, p, n);
    }
    public static List<VictoryGpuInfo> Run() {
        List<VictoryGpuInfo> list = new List<VictoryGpuInfo>();
        Guid iidF = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"), iidD = new Guid("189819f1-1db6-4b57-be54-1821339b85f7");
        IntPtr f; if (CreateDXGIFactory1(ref iidF, out f) < 0) return list;
        EnumD en = (EnumD)Marshal.GetDelegateForFunctionPointer(Slot(f, 12), typeof(EnumD));
        IntPtr buf = Marshal.AllocHGlobal(512);
        for (uint i = 0; ; i++) {
            IntPtr ad; if (en(f, i, out ad) < 0) break;
            VictoryGpuInfo g = new VictoryGpuInfo();
            if (((DescD)Marshal.GetDelegateForFunctionPointer(Slot(ad, 10), typeof(DescD)))(ad, buf) >= 0) {
                g.Name = Marshal.PtrToStringUni(buf, 128).TrimEnd('\0');
                g.Software = (Marshal.ReadInt32(buf, 304) & 2) != 0;
            }
            IntPtr dev;
            g.Device = D3D12CreateDevice(ad, 0xb000, ref iidD, out dev) >= 0 && dev != IntPtr.Zero;
            if (g.Device) {
                IntPtr p = Marshal.AllocHGlobal(8);
                for (int sm = 0x6A; sm >= 0x60; sm--) {
                    Marshal.WriteInt32(p, sm);
                    if (Check(dev, 7, p, 4) >= 0) { g.Model = Marshal.ReadInt32(p); break; }
                }
                Marshal.WriteInt64(p, 0);
                if (Check(dev, 32, p, 8) >= 0) g.Mesh = Marshal.ReadInt32(p) != 0;
                Marshal.FreeHGlobal(p);
                Rel(dev);
            }
            Rel(ad);
            list.Add(g);
        }
        Marshal.FreeHGlobal(buf);
        Rel(f);
        return list;
    }
}
'@
        $gpus = @([VictoryGpuProbe]::Run() | Where-Object { !$_.Software })
        if ($gpus.Count -gt 0 -and !($gpus | Where-Object { $_.Device -and $_.Mesh -and $_.Model -ge 0x66 })) {
            Write-Warning 'No GPU in this PC reports mesh shaders and Shader Model 6.6, which the game needs:'
            foreach ($gpu in $gpus) {
                $model = '{0}.{1}' -f (($gpu.Model -shr 4) -band 0xF), ($gpu.Model -band 0xF)
                Write-Warning ('  {0}: mesh shaders {1}, Shader Model {2}' -f $gpu.Name, $(if ($gpu.Mesh) { 'yes' } else { 'no' }), $(if ($gpu.Device) { $model } else { 'unavailable' }))
            }
            Write-Warning 'The game will most likely not start on this GPU. Use -SkipGpuCheck to build anyway.'
            if ([Environment]::UserInteractive -and ![Console]::IsInputRedirected) {
                if ((Read-Host 'Continue with the build anyway? (y/N)') -notmatch '^[Yy]') {
                    Write-Host 'Setup cancelled.'
                    return
                }
            }
        }
    } catch {
        Write-Verbose "GPU check skipped: $($_.Exception.Message)"
    }
}

$InstallDir = [IO.Path]::GetFullPath($InstallDir)
for ($parent = [IO.DirectoryInfo]::new($InstallDir); $parent; $parent = $parent.Parent) {
    if (Test-Path -LiteralPath (Join-Path $parent.FullName '.victory-root')) {
        throw 'Choose an install directory outside the source checkout so the launcher can write saves and shader caches.'
    }
}
$config = Join-Path $InstallDir 'game-path.txt'
if (!$GamePath -and (Test-Path -LiteralPath $config -PathType Leaf)) {
    $saved = [IO.File]::ReadAllText($config).Trim()
    if ($saved -and (Test-Path -LiteralPath $saved -PathType Leaf)) { $GamePath = $saved }
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

Push-Location -LiteralPath $PSScriptRoot
try {
    & (Join-Path $PSScriptRoot 'Initialize-FormatTables.ps1')
    $targetDir = Join-Path $PSScriptRoot 'target'
    $target = 'x86_64-pc-windows-msvc'
    & cargo build --locked --release -p victory --features diagnostics --bins --target $target --target-dir $targetDir
    if ($LASTEXITCODE -ne 0) {
        throw 'Build failed. Check the compiler output and the C++ workload and Windows SDK listed in the README.'
    }
    $release = Join-Path $targetDir "$target/release"
    & (Join-Path $release 'victory.exe') inspect $game.FullName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'The runtime could not read the selected game executable.' }
} finally { Pop-Location }

# Hold the same lock as Play and Preload during an install or update.
$state = [IO.Directory]::CreateDirectory((Join-Path $InstallDir 'state'))
try {
    $lock = [IO.File]::Open((Join-Path $state.FullName 'launcher.lock'), 'OpenOrCreate', 'Write', 'None')
} catch [IO.IOException] {
    throw "Could not lock the install directory. Close DexNom RayTime and shader preload before updating. $($_.Exception.Message)"
}
try {
    foreach ($name in 'victory.exe','victory-play.exe','victory-warm.exe') {
        $destination = Join-Path $InstallDir $name
        $temp = Join-Path $InstallDir ([IO.Path]::GetRandomFileName())
        try {
            [IO.File]::Copy((Join-Path $release $name), $temp)
            if ([IO.File]::Exists($destination)) {
                [IO.File]::Replace($temp, $destination, [System.Management.Automation.Language.NullString]::Value)
            } else {
                [IO.File]::Move($temp, $destination)
            }
        } finally { if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) } }
    }
    [IO.File]::WriteAllText($config, $game.FullName, [Text.UTF8Encoding]::new($false))
} finally { $lock.Dispose() }

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
