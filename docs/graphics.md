# Graphics modes

## Start menu shortcuts

| Shortcut | Graphics profile |
| --- | --- |
| **DexNom RayTime** | Game's display path with the dynamic ray-tracing structure builder off by default |
| **DexNom RayTime (Experimental RT)** | Ray tracing enabled with the game's display path |
| **DexNom RayTime (FSR Quality 4K)** | Series X mode with ray tracing, 2560 × 1440 render resolution, and 3840 × 2160 FSR Quality output |

The shortcuts share the same local saves. See [Benchmarks](../BENCHMARK.md)
for the Radeon test results and remaining rendering issues.

## Radeon FSR Quality 4K

This mode replaces the game's TAA upscaler with FSR and is available
for the supported Series X retail game build.

> [!IMPORTANT]
> FSR needs a separately supplied PC FidelityFX provider. The baseline
> used version 4.1.2. Put the provider and its dependencies in the
> runtime's `fidelityfx` folder, or set `VICTORY_FIDELITYFX_DIR` to
> the folder containing `amd_fidelityfx_upscaler_dx12.dll`.

Once the provider is in place, open **DexNom RayTime (FSR Quality 4K)**
from Start.

<details>
<summary>PowerShell launch command for the same profile</summary>

Run this after a default installation.

~~~powershell
$launcher = Join-Path $env:LOCALAPPDATA 'DexNom Runtime\victory-play.exe'
& $launcher --rt --console series-x --local-gpu-memory --fsr-quality --sparse-textures --no-upload-ahead --capture-readbacks --rt-write-stats
~~~

</details>

The launcher also accepts `--fsr-balanced` and `--fsr-performance` for
4K output in Series X mode. Select one FSR preset per run.

## Experimental NVIDIA

> [!NOTE]
> This fork has yet to establish an NVIDIA baseline for these profiles.

Start with the game's display path and ray tracing off.

~~~powershell
$env:VICTORY_FSR_4K = '0'
$env:VICTORY_AMBIENT_FALLBACK = '1'
$launcher = Join-Path $env:LOCALAPPDATA 'DexNom Runtime\victory-play.exe'
& $launcher --no-rt --console series-x
~~~

`--no-rt` skips acceleration-structure building, traversal, and lighting
resolve. Supported material shaders get a diffuse ambient fill based on
their base color. Some interiors can still look darker. Baked lighting
or a probe-based indirect-light fallback remains open work. [RT pass selection](../crates/victory-gpu/src/pipeline/ray_tracing.rs) · [Ambient fill](../crates/victory-shader/src/emit/mod.rs)

To try partial ray tracing, replace `--no-rt` with `--rt-static-bvh`.
Keep FSR disabled. This mode traces the prebuilt world structure and
skips rebuilding it for dynamic geometry. Traversal and lighting resolve
can still be expensive, and moving objects may have incomplete shadows
or reflections. Use the same scene and resolution for both runs and
record the GPU, driver, frame times, and lighting differences. The run log
records the requested ray-tracing mode. [Ray-tracing implementation](fork-ray-tracing.md) · [Open the latest log](troubleshooting.md#open-the-latest-log) · [Report a test result](CONTRIBUTING.md#report-a-bug-or-benchmark)
