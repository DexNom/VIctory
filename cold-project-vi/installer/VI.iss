; Julia's Launcher installer (Inno Setup 6). Built by .github\workflows\release.yml from the files in dist\:
;   iscc /DAppVersion=0.3.2 installer\VI.iss
; Asks for administrator rights once, installs outside Windows' protected folders (C:\Games\VI by
; default, writable by the player so updates install themselves), runs the Windows settings in the
; background (Julia's Launcher.exe --admin-setup: power, virtual memory (no antivirus
; changes), the high-performance graphics card, Smooth Motion), then opens the launcher without
; administrator rights, which finds the game and prepares the shaders.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId=VI-Launcher-edupode
AppName=Julia's Launcher
AppVersion={#AppVersion}
AppPublisher=edupode and felicienradio (open source)
AppPublisherURL=https://github.com/felicienradio/cold-project-vi/tree/main
AppSupportURL=https://discord.gg/648WEXCAsZ
DefaultDirName={sd}\Games\VI
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=VI-Setup-{#AppVersion}
SetupIconFile=..\launcher\icon.ico
UninstallDisplayIcon={app}\Julia's Launcher.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "pt"; MessagesFile: "compiler:Languages\Portuguese.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Dirs]
; The player may write here: updates replace the programs while the launcher runs without admin.
Name: "{app}"; Permissions: users-modify

[Files]
Source: "..\dist\Julia's Launcher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\victory.exe"; DestDir: "{app}"; Flags: ignoreversion
; The D3D12 Agility SDK (Microsoft's redistributable): Windows 10 needs it to run the engine.
Source: "..\dist\D3D12Core.dll"; DestDir: "{app}\D3D12"; Flags: ignoreversion
Source: "..\dist\launcher-source.zip"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\SHA256SUMS.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\scripts\rt_off_hashes.txt"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"
Source: "..\README.md"; DestDir: "{app}"
Source: "..\README.pt.md"; DestDir: "{app}"

[Tasks]
; Shown and asked before anything in Windows changes (each can be undone in Windows' settings).
Name: "windows"; Description: "Set up Windows for the game: Best performance power mode, virtual memory managed by Windows, the high-performance graphics card for VIctory, and the NVIDIA driver profile (Smooth Motion, Resizable BAR) on NVIDIA cards"

[Icons]
Name: "{autodesktop}\Julia's Launcher"; Filename: "{app}\Julia's Launcher.exe"
Name: "{autoprograms}\Julia's Launcher"; Filename: "{app}\Julia's Launcher.exe"

[Run]
Filename: "{app}\Julia's Launcher.exe"; Parameters: "--admin-setup ""{app}"""; StatusMsg: "Setting up Windows for the game (power, virtual memory, graphics card)..."; Tasks: windows; Flags: waituntilterminated runhidden
Filename: "{app}\Julia's Launcher.exe"; Description: "Open Julia's Launcher"; Flags: postinstall nowait runasoriginaluser

[UninstallDelete]
Type: filesandordirs; Name: "{app}\tools"
Type: files; Name: "{app}\Julia's Launcher.old.exe"
