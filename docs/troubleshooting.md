# Troubleshooting

## Setup cannot build the runtime

Check that Windows can find Rust and that Visual Studio Build Tools includes
**Desktop development with C++**, the x64 MSVC toolset, and a Windows SDK.
If setup reports an invalid BCPack table hash, see the verification and
upstream source details in the [asset policy](asset-policy.md#local-build-dependency).

If the launcher reports a missing `VCRUNTIME140.dll`, install the x64
Visual C++ Redistributable. [Microsoft download](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)

## The runtime will not start

- If the game executable moved, run `Setup.cmd` again and select it in the extracted game folder. [Change the game path](installation.md#update-or-change-the-game-path)
- If the launcher reports a state lock, close any other gameplay or shader warmup process before retrying.
- If FSR reports a missing provider, check the DLL and dependencies or try the regular **DexNom RayTime** shortcut. [FidelityFX setup](graphics.md#radeon-fsr-quality-4k)

## Open the latest log

`logs\latest.txt` contains the path to the most recent gameplay log. Once
the launcher creates that file, open the log from PowerShell.

~~~powershell
$logs = Join-Path $env:LOCALAPPDATA 'DexNom Runtime\logs'
$latestLog = (Get-Content -LiteralPath (Join-Path $logs 'latest.txt') -Raw).Trim()
notepad.exe $latestLog
~~~

Use your own runtime folder if you installed to a custom location.
Redact local paths before sharing log excerpts.

> [!TIP]
> Use `latest-warm.txt` in the same command to open the latest shader warmup log.

## Warm the shader cache

Close the game and open `victory-warm.exe` in the installed runtime folder.
Its log lists translated and refused shaders. Some graphics pipelines still
compile during play because they need the game's draw state.

## Dark materials or slow frames

The City waterfront has known black materials, and interior lighting may
be incomplete. The first frames can also be slow while pipelines compile
or textures stream. Record the scene, graphics mode, and whether the cache
was already warm when comparing results. [Known results](../BENCHMARK.md#radeon-fsr-quality-4k-baseline) · [Report a bug or benchmark](CONTRIBUTING.md#report-a-bug-or-benchmark)
