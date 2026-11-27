# Installation

## Requirements

- Windows x64 and a Direct3D 12 driver with mesh shader and Shader Model 6.6 support. The tested system uses Windows 11 and an AMD Radeon RX 7900 XTX. [Test system](../BENCHMARK.md#radeon-fsr-quality-4k-baseline)
- Rust for Windows and Visual Studio Build Tools. Select **Desktop development with C++**, including the x64 MSVC toolset and a Windows 10 or 11 SDK. [Rust installer](https://rustup.rs/) · [Microsoft C++ Build Tools](https://visualstudio.microsoft.com/visual-cpp-build-tools/)
- An Xbox Series X executable from your own extracted game installation.

> [!IMPORTANT]
> Keep the executable with the rest of the game files. Use a controller
> for the game's first-time setup.

## Set up

1. [Download the source ZIP](https://github.com/DexNom/VIctory/archive/refs/heads/main.zip) and extract it to a writable folder.
2. Double-click `Setup.cmd` and select your game executable. Setup verifies the BCPack format tables, builds the three runtime programs, and adds their launch profiles to the Windows Start menu.
3. Open **DexNom RayTime** from Start. The [graphics guide](graphics.md) explains the other shortcuts and the extra provider needed for FSR.

## Update or change the game path

Close the runtime and shader preload, extract the new source ZIP, and run
`Setup.cmd` again. Setup reuses the saved game path when it still exists
and keeps the same save directory. To select another installation, pass its
executable path from the extracted source folder.

~~~powershell
.\Setup.cmd -GamePath "D:\My Games\Series X\game.exe"
~~~

## Local files

Setup installs to `%LOCALAPPDATA%\DexNom Runtime` by default.

| File or folder | Contents |
| --- | --- |
| `game-path.txt` | Path to the selected game executable |
| `state\` | Saves and other game state |
| `cache\` | Translated shaders, pipeline caches, and other generated files |
| `logs\` | Gameplay and shader warmup logs |

The game files remain in your own installation. See [Troubleshooting](troubleshooting.md#open-the-latest-log) for the path to the latest log.

## Build manually

Run these commands from the source folder.

~~~powershell
.\Initialize-FormatTables.ps1
cargo build --locked --release -p victory --features diagnostics --bins
~~~

Copy `victory.exe`, `victory-play.exe`, and `victory-warm.exe` from
`target\release` into the same folder outside the checkout. Run
`victory-play.exe` with the full game executable path as its final argument.
The [configuration example](../victory.example.toml) documents paths and
environment variables for direct runtime use.
